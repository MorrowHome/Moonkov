"""Preserve both global and original-object-local source AO semantics.
Global AO sees the full prepared world. Local AO keeps original instances separate.
Only AO-tagged texels update the red statistics channel; measured variance stays intact.
"""
import bpy,pathlib,json,time,numpy as np,zlib,struct
ROOT=pathlib.Path(__file__).resolve().parent;PROJECT=ROOT.parents[2];WORK=PROJECT/'LocalData/SourceFidelity20261028/World';BAKES=WORK/'Bakes'
ir=json.loads((ROOT/'MaterialIR/MaterialIR.json').read_text());compiled=json.loads((ROOT/'MaterialIR/CompiledMaterials.json').read_text());ids={r['id']:r['name'] for r in compiled['materials']}
ao_by_id={i:next(n for n in ir['materials'][name]['nodes'] if n['type']=='AMBIENT_OCCLUSION') for i,name in ids.items() if any(n['type']=='AMBIENT_OCCLUSION' for n in ir['materials'][name]['nodes'])}
local_ids={i for i,n in ao_by_id.items() if n['only_local']};global_ids=set(ao_by_id)-local_ids
world=json.loads((WORK/'PreparedWorld.json').read_text());stage=next(s for s in bpy.data.scenes if s.name.startswith('SourceWorldStatic'));bpy.context.window.scene=stage
stage.render.engine='CYCLES';stage.cycles.samples=1;stage.cycles.device='CPU';stage.render.threads_mode='FIXED';stage.render.threads=4
try:
 prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='METAL';prefs.refresh_devices()
 for d in prefs.devices:d.use=d.type=='METAL'
 if any(d.use for d in prefs.devices):stage.cycles.device='GPU'
except Exception:pass

def png(path,array):
 h,w,_=array.shape;rows=np.empty((h,w*4+1),dtype=np.uint8);rows[:,0]=0;rows[:,1:]=array[::-1].reshape(h,-1)
 def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
 path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(rows.tobytes(),6))+chunk(b'IEND',b''))

def target_image(materials,size):
 im=bpy.data.images.new('AO_Masked',size,size,alpha=True,float_buffer=True);im.colorspace_settings.name='Non-Color';targets=[]
 for mat in set(materials):
  n=mat.node_tree.nodes.new('ShaderNodeTexImage');n.image=im;mat.node_tree.nodes.active=n;targets.append((mat.node_tree,n))
 return im,targets

def emission_mask(tree,ao_socket=None):
 emit=tree.nodes.new('ShaderNodeEmission');combine=tree.nodes.new('ShaderNodeCombineXYZ');combine.inputs['X'].default_value=1;combine.inputs['Y'].default_value=1 if ao_socket else 0
 if ao_socket:tree.links.new(ao_socket,combine.inputs['X'])
 tree.links.new(combine.outputs[0],emit.inputs['Color']);return emit,combine

results=[];start=time.monotonic()
for atlas in world['atlases']:
 stamp=BAKES/f'AO_{atlas["id"]:03d}.json'
 if stamp.exists():
  previous=json.loads(stamp.read_text())
  if previous.get('version')==2:results.append(previous);continue
 obj=stage.objects[atlas['object']];mesh=obj.data;slot_ids=[int(m.name.split('SourceMaterial_')[1].split('.')[0]) for m in mesh.materials]
 mesh.calc_loop_triangles();tri=np.empty(len(mesh.loop_triangles)*3,dtype=np.int32);mesh.loop_triangles.foreach_get('loops',tri);tri=tri.reshape(-1,3)
 mids=np.empty(len(tri),dtype=np.int32);mesh.loop_triangles.foreach_get('material_index',mids);material_ids=np.array(slot_ids,dtype=np.int32)[mids]
 actual=set(material_ids.tolist());global_present=actual&global_ids;local_present=actual&local_ids;size=atlas['size'];count=len(mesh.loops)
 statistics_path=BAKES/f'SurfaceStatistics_{atlas["id"]:03d}.png';statistics=bpy.data.images.load(str(statistics_path),check_existing=False);statistics.colorspace_settings.name='Non-Color';buffer=np.empty(size*size*4,dtype=np.float32);statistics.pixels.foreach_get(buffer);rgba=np.rint(np.clip(buffer.reshape(size,size,4),0,1)*255).astype(np.uint8);rgba[:,:,0]=255;bpy.data.images.remove(statistics)
 def apply(im):
  im.pixels.foreach_get(buffer);values=buffer.reshape(-1,4);mask=(values[:,1]>.5)&(values[:,3]>.5);rgba.reshape(-1,4)[mask,0]=np.rint(np.clip(values[mask,0],0,1)*255).astype(np.uint8)
 if global_present:
  bpy.context.window.scene=stage;bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
  restores=[]
  for mat in set(mesh.materials):
   mid=int(mat.name.split('SourceMaterial_')[1].split('.')[0])
   if mid not in global_present:continue
   tree=mat.node_tree;output=next(n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output);prior=[(l.from_socket,l.to_socket) for l in output.inputs['Surface'].links];ao=next(n for n in tree.nodes if n.type=='AMBIENT_OCCLUSION');old_samples=ao.samples;ao.samples=32
   for link in list(output.inputs['Surface'].links):tree.links.remove(link)
   emit,combine=emission_mask(tree,ao.outputs['AO']);tree.links.new(emit.outputs[0],output.inputs['Surface']);restores.append((tree,prior,emit,combine,ao,old_samples))
  image,targets=target_image(mesh.materials,size);stage.render.bake.use_clear=True;stage.render.bake.margin=12
  print('GLOBAL_SOURCE_AO_BEGIN',atlas['id'],flush=True)
  assert bpy.ops.object.bake(type='EMIT',uv_layer='SourceStaticBake')=={'FINISHED'};apply(image)
  for tree,n in targets:tree.nodes.remove(n)
  for tree,prior,emit,combine,ao,old_samples in restores:
   tree.nodes.remove(emit);tree.nodes.remove(combine);ao.samples=old_samples
   for a,b in prior:tree.links.new(a,b)
  bpy.data.images.remove(image)
 local_count=0
 if local_present:
  instance=np.empty(count,dtype=np.float32);mesh.attributes['SourceInstance'].data.foreach_get('value',instance);instance=instance.astype(np.int32);chosen_instances=set(instance[tri[np.isin(material_ids,list(local_present)),0]].tolist());local_count=len(chosen_instances)
  vertex_ids=np.empty(count,dtype=np.int32);mesh.loops.foreach_get('vertex_index',vertex_ids);co=np.empty(len(mesh.vertices)*3,dtype=np.float32);mesh.vertices.foreach_get('co',co);co=co.reshape(-1,3)
  normals=np.empty(count*3,dtype=np.float32);mesh.attributes['SourceWorldNormal'].data.foreach_get('vector',normals);normals=normals.reshape(-1,3);uv=np.empty(count*2,dtype=np.float32);mesh.uv_layers['SourceStaticBake'].data.foreach_get('uv',uv);uv=uv.reshape(-1,2)
  scene=bpy.data.scenes.new('OriginalObjectLocalAO');scene.render.engine='CYCLES';scene.cycles.samples=1;scene.cycles.device=stage.cycles.device;scene.render.threads_mode='FIXED';scene.render.threads=4;bpy.context.window.scene=scene
  materials={}
  for material_id in [-1]+sorted(local_present):
   mat=bpy.data.materials.new('ObjectLocalAO_'+str(material_id));tree=mat.node_tree;tree.nodes.clear();output=tree.nodes.new('ShaderNodeOutputMaterial');socket=None
   if material_id>=0:
    spec=ao_by_id[material_id];ao=tree.nodes.new('ShaderNodeAmbientOcclusion');ao.only_local=True;ao.inside=False;ao.samples=32;ao.inputs['Distance'].default_value=next(s['value'] for s in spec['inputs'] if s['id']=='Distance');socket=ao.outputs['AO']
   emit,_=emission_mask(tree,socket);tree.links.new(emit.outputs[0],output.inputs['Surface']);materials[material_id]=mat
  slots=list(materials);objects=[]
  for source_id in sorted(chosen_instances):
   chosen=np.flatnonzero(instance[tri[:,0]]==source_id);ls=tri[chosen].ravel();temp=bpy.data.meshes.new('AO_SourceInstance');temp.from_pydata(co[vertex_ids[ls]].tolist(),[],np.arange(len(ls),dtype=np.int32).reshape(-1,3).tolist())
   for material_id in slots:temp.materials.append(materials[material_id])
   temp.polygons.foreach_set('material_index',[slots.index(int(i) if i in local_present else -1) for i in material_ids[chosen]]);temp.polygons.foreach_set('use_smooth',np.ones(len(chosen),dtype=bool));temp.normals_split_custom_set(normals[ls].tolist());layer=temp.uv_layers.new(name='SourceStaticBake');layer.data.foreach_set('uv',uv[ls].ravel());layer.active_render=True
   receiver=bpy.data.objects.new('AO_Instance_'+str(source_id),temp);scene.collection.objects.link(receiver);objects.append(receiver)
  bpy.context.view_layer.update();bpy.ops.object.select_all(action='DESELECT')
  for receiver in objects:receiver.select_set(True)
  # Only-local AO is invariant under rigid translations. Pack whole original
  # instances with a gap larger than twice the largest AO ray length, then join
  # once. No AO ray can reach another instance, preserving exact local scope
  # without hundreds of full Cycles bake synchronizations.
  gap=2*max(next(s['value'] for s in ao_by_id[i]['inputs'] if s['id']=='Distance') for i in local_present)+.1
  cursor_x=cursor_y=row_height=0.0
  for receiver in objects:
   bounds=np.array([list(c) for c in receiver.bound_box]);lo=bounds.min(axis=0);span=bounds.max(axis=0)-lo
   if cursor_x+span[0]>2048:cursor_x=0;cursor_y+=row_height+gap;row_height=0
   receiver.location=(cursor_x-lo[0],cursor_y-lo[1],-lo[2]);cursor_x+=span[0]+gap;row_height=max(row_height,float(span[1]))
  bpy.context.view_layer.update();bpy.context.view_layer.objects.active=objects[0]
  if len(objects)>1:
   assert bpy.ops.object.join()=={'FINISHED'};objects=[bpy.context.object]
  scene.render.bake.use_clear=True;scene.render.bake.margin=12;image,targets=target_image(materials.values(),size)
  print('LOCAL_SOURCE_AO_BEGIN',atlas['id'],'sourceObjects',local_count,flush=True);assert bpy.ops.object.bake(type='EMIT',uv_layer='SourceStaticBake')=={'FINISHED'};apply(image)
  for tree,n in targets:tree.nodes.remove(n)
  bpy.data.images.remove(image);bpy.context.window.scene=stage;bpy.data.scenes.remove(scene)
  for receiver in objects:
   data=receiver.data;bpy.data.objects.remove(receiver,do_unlink=True);bpy.data.meshes.remove(data)
  for mat in materials.values():bpy.data.materials.remove(mat)
 png(statistics_path,rgba)
 result={'version':2,'atlas':atlas['id'],'globalMaterialIds':sorted(global_present),'localMaterialIds':sorted(local_present),'localObjects':local_count,'sourceSha256':world['source_sha256'],'samples':32};stamp.write_text(json.dumps(result));results.append(result);print('SOURCE_AO_COMPLETE',atlas['id'],'elapsed',round(time.monotonic()-start,1),flush=True)
(WORK/'ObjectLocalAO.json').write_text(json.dumps({'sourceSha256':world['source_sha256'],'atlases':results},indent=2));print('ALL_SOURCE_AO_COMPLETE',round(time.monotonic()-start,1),flush=True)
