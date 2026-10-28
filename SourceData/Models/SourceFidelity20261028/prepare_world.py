"""Prepare every unique visible source instance for static lighting and runtime export.
Run in the canonical source. Writes only LocalData; never saves the canonical blend.
"""
import bpy,bmesh,json,pathlib,hashlib,sys,math,time,collections,numpy as np
from mathutils import Vector,Matrix
ROOT=pathlib.Path(__file__).resolve().parent;PROJECT=ROOT.parents[2];sys.path.insert(0,str(ROOT))
from material_bake_nodes import flatten
from freeze_object_inputs import add_attribute,object_random,freeze_material
OUT=PROJECT/'LocalData/SourceFidelity20261028/World';OUT.mkdir(parents=True,exist_ok=True)
contract=json.loads((ROOT/'SourceSummary.json').read_text());assert hashlib.sha256(pathlib.Path(bpy.data.filepath).read_bytes()).hexdigest()==contract['source_sha256']
plan=json.loads((PROJECT/'LocalData/SourceFidelity20261028/BakePlan.json').read_text())
ir=json.loads((ROOT/'MaterialIR/MaterialIR.json').read_text());compiled=json.loads((ROOT/'MaterialIR/CompiledMaterials.json').read_text())
material_rows={r['name']:r for r in compiled['materials']};source_scene=bpy.context.scene;dg=bpy.context.evaluated_depsgraph_get();start=time.monotonic()
# Separate bake scene keeps source visibility and geometry intact.
checkpoint=OUT/'PreparedWorldProgress.blend';resumed=checkpoint.exists()
if resumed:
 with bpy.data.libraries.load(str(checkpoint),link=False) as (src,dst):dst.scenes=['SourceWorldStatic']
 stage=dst.scenes[0]
else:stage=bpy.data.scenes.new('SourceWorldStatic');stage.world=source_scene.world.copy()
stage.render.engine='CYCLES'
for key in ['samples','max_bounces','diffuse_bounces','glossy_bounces','transmission_bounces','transparent_max_bounces','sample_clamp_direct','sample_clamp_indirect']:
 if hasattr(source_scene.cycles,key):setattr(stage.cycles,key,getattr(source_scene.cycles,key))
stage.cycles.samples=128;stage.cycles.use_denoising=False;stage.render.threads_mode='FIXED';stage.render.threads=4
stage.view_settings.view_transform=source_scene.view_settings.view_transform;stage.view_settings.look=source_scene.view_settings.look
for obj in source_scene.objects:
 if not resumed and obj.type=='LIGHT' and not obj.hide_render:
  light=bpy.data.objects.new('SOURCE_'+obj.name,obj.data.copy());stage.collection.objects.link(light);light.matrix_world=obj.matrix_world.copy()
# Shader graphs are frozen on copies only.
materials={};refs={}
for name,row in material_rows.items():
 existing=bpy.data.materials.get(f'SourceMaterial_{row["id"]:03d}') if resumed else None
 if existing:materials[name]=existing;continue
 mat=bpy.data.materials[name].copy();mat.name=f'SourceMaterial_{row["id"]:03d}';flatten(mat.node_tree)
 for n in mat.node_tree.nodes:
  if n.type=='TEX_COORD' and n.object:
   source=n.object
   if source.name not in refs:
    ref=bpy.data.objects.new('SOURCE_COORD_'+source.name,None);stage.collection.objects.link(ref);ref.matrix_world=source.matrix_world.copy();refs[source.name]=ref
   n.object=refs[source.name]
 freeze_material(mat);materials[name]=mat

def replace_normal_maps(mat):
 tree=mat.node_tree
 for n in list(tree.nodes):
  if n.type!='NORMAL_MAP' or n.space!='TANGENT':continue
  key=n.uv_map or '$ACTIVE';targets=[l.to_socket for l in n.outputs['Normal'].links]
  if not targets:continue
  def vector(operation,a=None,b=None,scale=None):
   node=tree.nodes.new('ShaderNodeVectorMath');node.operation=operation
   for index,socket in enumerate([a,b]):
    if socket is not None:tree.links.new(socket,node.inputs[index])
   if scale is not None:
    if isinstance(scale,(float,int)):node.inputs['Scale'].default_value=scale
    else:tree.links.new(scale,node.inputs['Scale'])
   return node.outputs['Vector']
  color=n.inputs['Color'];strength=n.inputs['Strength'];c=tree.nodes.new('ShaderNodeRGB')
  c.outputs[0].default_value=color.default_value
  color_source=color.links[0].from_socket if color.is_linked else c.outputs[0]
  mul=tree.nodes.new('ShaderNodeVectorMath');mul.operation='MULTIPLY';tree.links.new(color_source,mul.inputs[0]);mul.inputs[1].default_value=(2,2,2)
  sub=tree.nodes.new('ShaderNodeVectorMath');sub.operation='SUBTRACT';tree.links.new(mul.outputs['Vector'],sub.inputs[0]);sub.inputs[1].default_value=(1,1,1)
  separate=tree.nodes.new('ShaderNodeSeparateXYZ');tree.links.new(sub.outputs['Vector'],separate.inputs[0])
  strength_node=tree.nodes.new('ShaderNodeValue');strength_node.outputs[0].default_value=strength.default_value
  amount=strength.links[0].from_socket if strength.is_linked else strength_node.outputs[0]
  basis=[]
  for name in ['SourceTangent_'+key,'SourceBitangent_'+key,'SourceWorldNormal']:
   a=tree.nodes.new('ShaderNodeAttribute');a.attribute_name=name;basis.append(a.outputs['Vector'])
  components=[]
  for index,axis in enumerate(['X','Y','Z']):
   value=separate.outputs[axis]
   if index<2:
    m=tree.nodes.new('ShaderNodeMath');m.operation='MULTIPLY';tree.links.new(value,m.inputs[0]);tree.links.new(amount,m.inputs[1]);value=m.outputs[0]
   else:
    clamp=tree.nodes.new('ShaderNodeClamp');tree.links.new(amount,clamp.inputs['Value'])
    mix=tree.nodes.new('ShaderNodeMixRGB');mix.blend_type='MIX';tree.links.new(clamp.outputs[0],mix.inputs[0]);mix.inputs[1].default_value=(1,1,1,1);tree.links.new(value,mix.inputs[2]);value=mix.outputs[0]
   components.append(vector('SCALE',basis[index],scale=value))
  output=vector('NORMALIZE',vector('ADD',vector('ADD',components[0],components[1]),components[2]))
  for socket in targets:tree.links.new(output,socket)
  tree.nodes.remove(n)
for mat in materials.values():replace_normal_maps(mat)

# Surface-area budget and triangle budget both constrain each atlas. Background
# terrain is kept separate; the close architecture keeps the full material graph.
rows=plan['objects'];buckets=collections.defaultdict(list)
for row in rows:
 terrain='DISPLAY_TERRAIN_CUT' in row['source'];indoor=row['source'].startswith(('MC_','MC00','NR01','L14','LD2'))
 row['density']=.5 if terrain and 'Background4km' in row['source'] else 4 if terrain else 16 if indoor else 8
 row['terrain']=terrain;buckets[(row['density'],terrain)].append(row)
jobs=[]
for (density,terrain),bucket in sorted(buckets.items()):
 bucket.sort(key=lambda r:(r['cell'][0],r['cell'][1],r['cell'][2],r['source'],r['id']))
 current=[];area=0;tris=0;capacity=4096**2*.65/density**2
 for row in bucket:
  if current and (area+row['area_m2_conservative']>capacity or tris+row['triangles']>320000 or terrain):
   jobs.append({'id':len(jobs),'rows':current,'area':area,'triangles':tris,'density':density});current=[];area=0;tris=0
  current.append(row);area+=row['area_m2_conservative'];tris+=row['triangles']
 if current:jobs.append({'id':len(jobs),'rows':current,'area':area,'triangles':tris,'density':density})
print('WORLD_JOB_PLAN',len(jobs),sum(j['triangles'] for j in jobs),flush=True)
(OUT/'JobPlan.json').write_text(json.dumps({'source_sha256':contract['source_sha256'],'jobs':jobs},indent=2))
# Cache evaluated meshes while the original dependency graph is still active.
cache={}
prepared_ids={int(o['source_atlas_id']) for o in stage.objects if 'source_atlas_id' in o}
for number,name in enumerate(sorted({r['source'] for j in jobs if j['id'] not in prepared_ids for r in j['rows']})):
 evaluated=bpy.data.objects[name].evaluated_get(dg);cache[name]=bpy.data.meshes.new_from_object(evaluated,preserve_all_data_layers=True,depsgraph=dg)
 if number%2000==0:print('EVALUATED_MESHES',number,'seconds',round(time.monotonic()-start,1),flush=True)
bpy.context.window.scene=stage
progress_path=OUT/'PreparedWorldProgress.json'
previous=json.loads(progress_path.read_text()) if resumed and progress_path.exists() else {}
report=previous.get('atlases',[]);normal_fixes=previous.get('normal_components_corrected',0);degenerate_removed=0
for job in jobs:
 if job['id'] in prepared_ids:continue
 objects=[];original_rows=[]
 for row in job['rows']:
  mesh=cache[row['source']].copy();matrix=Matrix(row['matrix']);obj=bpy.data.objects.new(f'SRC_{row["id"]:05d}',mesh);stage.collection.objects.link(obj)
  source_material_names=row['materials'];mesh.materials.clear()
  for name in source_material_names:
   if name is None:raise ValueError('Missing source material '+row['source'])
   mesh.materials.append(materials[name])
  # Stable, locally centered volume test; never measure tiny panels at km offsets.
  bm=bmesh.new();bm.from_mesh(mesh);bm.faces.ensure_lookup_table();seen=set()
  for face in bm.faces:
   if face.index in seen:continue
   pending=[face];faces=[];closed=True
   while pending:
    f=pending.pop()
    if f.index in seen:continue
    seen.add(f.index);faces.append(f)
    for edge in f.edges:
     if not edge.is_manifold:closed=False
     pending.extend(x for x in edge.link_faces if x.index not in seen)
   if not closed:continue
   vertices={v for f in faces for v in f.verts};center=sum((v.co for v in vertices),Vector())/len(vertices)
   volume=sum((f.verts[0].co-center).dot((f.verts[k].co-center).cross(f.verts[k+1].co-center))/6 for f in faces for k in range(1,len(f.verts)-1))
   if volume<-1e-9:bmesh.ops.reverse_faces(bm,faces=faces);normal_fixes+=1
  bmesh.ops.dissolve_degenerate(bm,dist=1e-7,edges=list(bm.edges));bm.to_mesh(mesh);bm.free();mesh.update()
  count=len(mesh.loops);vertex_ids=np.empty(count,dtype=np.int32);mesh.loops.foreach_get('vertex_index',vertex_ids)
  co=np.empty(len(mesh.vertices)*3,dtype=np.float32);mesh.vertices.foreach_get('co',co);co=co.reshape(-1,3)
  local=co[vertex_ids];minimum=co.min(axis=0);extent=np.maximum(co.max(axis=0)-minimum,1e-20)
  add_attribute(mesh,'SourceLocal',local);add_attribute(mesh,'SourceGenerated',(local-minimum)/extent);add_attribute(mesh,'SourceRandom',np.full(count,object_random(row['source'])),'FLOAT')
  add_attribute(mesh,'SourceInstance',np.full(count,row['id']),'FLOAT')
  uv_names=sorted({u for name in source_material_names for u in material_rows[name]['uv_names'] if u!='$ACTIVE'})
  active=next((uv.name for uv in mesh.uv_layers if uv.active_render),'')
  for name in ['$ACTIVE']+uv_names:
   uv=mesh.uv_layers.get(active if name=='$ACTIVE' else name);values=np.zeros((count,3),dtype=np.float32)
   if uv:
    xy=np.empty(count*2,dtype=np.float32);uv.data.foreach_get('uv',xy);values[:,:2]=xy.reshape(-1,2)
   add_attribute(mesh,'SourcePrimaryUV' if name=='$ACTIVE' else 'SourceUV_'+name,values)
  normal=np.empty(count*3,dtype=np.float32);mesh.corner_normals.foreach_get('vector',normal);normal=normal.reshape(-1,3);normal_matrix=np.asarray(matrix.to_3x3().inverted_safe().transposed(),dtype=np.float32);normal=normal@normal_matrix.T;normal/=np.maximum(np.linalg.norm(normal,axis=1,keepdims=True),1e-20)
  add_attribute(mesh,'SourceWorldNormal',normal)
  tangent_keys=sorted({material_rows[name]['tangent_uv'] for name in source_material_names})
  transform=np.asarray(matrix.to_3x3(),dtype=np.float32)
  for key in tangent_keys:
   name=active if key=='$ACTIVE' else key;tangent=np.zeros((count,3),dtype=np.float32);sign=np.ones(count,dtype=np.float32)
   if name and mesh.uv_layers.get(name):
    try:
     mesh.calc_tangents(uvmap=name);mesh.loops.foreach_get('tangent',tangent.ravel());mesh.loops.foreach_get('bitangent_sign',sign)
    except RuntimeError:pass
   tangent=tangent@transform.T;tangent/=np.maximum(np.linalg.norm(tangent,axis=1,keepdims=True),1e-20)
   bitangent=np.cross(normal,tangent)*(sign[:,None]*(-1 if matrix.determinant()<0 else 1))
   add_attribute(mesh,'SourceTangent_'+key,tangent);add_attribute(mesh,'SourceBitangent_'+key,bitangent)
  # Coordinates and normal-map bases are frozen. Keep only shader-required attrs
  # and one bake UV, rather than merging every author's unrelated UV channel.
  required={'sharp_face','sharp_edge','custom_normal','SourceLocal','SourceGenerated','SourceRandom','SourceInstance','SourcePrimaryUV','SourceWorldNormal'}|{'SourceUV_'+name for name in uv_names}|{'SourceTangent_'+k for k in tangent_keys}|{'SourceBitangent_'+k for k in tangent_keys}
  for name in source_material_names:required.update(material_rows[name]['attribute_layout'])
  for uv in list(mesh.uv_layers):mesh.uv_layers.remove(uv)
  for attr in list(mesh.attributes):
   if not attr.is_internal and not getattr(attr,'is_required',False) and attr.name not in required:mesh.attributes.remove(attr)
  mesh.transform(matrix)
  if matrix.determinant()<0:
   bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
  objects.append(obj);original_rows.append({'instance':row['id'],'source':row['source'],'materials':source_material_names})
 bpy.ops.object.select_all(action='DESELECT')
 for obj in objects:obj.select_set(True)
 bpy.context.view_layer.objects.active=objects[0]

 if len(objects)>1:assert bpy.ops.object.join()=={'FINISHED'}
 joined=bpy.context.object;joined.name=f'SourceAtlas_{job["id"]:03d}';uv=joined.data.uv_layers.new(name='SourceStaticBake');joined.data.uv_layers.active=uv;uv.active_render=True
 joined['source_atlas_id']=job['id'];joined['source_density']=job['density']
 side=min(4096,max(2048 if len(objects)>800 else 512,2**math.ceil(math.log2(max(1,math.sqrt(job['area']/.65)*job['density'])))))
 joined['source_bake_size']=side
 if len(job['rows'])==1 and job['rows'][0]['terrain']:
  coords=np.array([list(v.co) for v in joined.data.vertices],dtype=np.float64);lo=coords[:,:2].min(axis=0);span=coords[:,:2].max(axis=0)-lo
  ids=np.empty(len(joined.data.loops),dtype=np.int32);joined.data.loops.foreach_get('vertex_index',ids)
  uv.data.foreach_set('uv',((coords[ids,:2]-lo)/np.maximum(span,1e-20)).astype(np.float32).ravel())
 else:
  bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');assert bpy.ops.uv.smart_project(island_margin=4/side,area_weight=.5,correct_aspect=True,scale_to_bounds=True)=={'FINISHED'};bpy.ops.object.mode_set(mode='OBJECT')
 joined.data.calc_loop_triangles()
 report.append({'id':job['id'],'object':joined.name,'size':side,'density':job['density'],'area_conservative':job['area'],'triangles':len(joined.data.loop_triangles),'source_objects':original_rows})
 progress_path.write_text(json.dumps({'source_sha256':contract['source_sha256'],'normal_components_corrected':normal_fixes,'atlases':report},indent=2))
 bpy.data.libraries.write(str(checkpoint),{stage},fake_user=True,compress=True)
 print('WORLD_ATLAS_PREPARED',job['id'],'of',len(jobs),'objects',len(objects),'triangles',len(joined.data.loop_triangles),'size',side,'elapsed',round(time.monotonic()-start,1),flush=True)
 # Release original mesh cache copies after their final instance has been staged.
 remaining={r['source'] for j in jobs[job['id']+1:] for r in j['rows']}
 for name in list(cache):
  if name not in remaining:bpy.data.meshes.remove(cache.pop(name))
# A temporary project is the checkpoint for baking; it is not the canonical asset.
(OUT/'PreparedWorld.json').write_text(json.dumps({'source_sha256':contract['source_sha256'],'source_saved':False,'atlas_count':len(report),'triangles':sum(x['triangles'] for x in report),'normal_components_corrected':normal_fixes,'atlases':report},indent=2))
bpy.data.libraries.write(str(OUT/'PreparedWorld.blend'),{stage},fake_user=True,compress=True)
print('WORLD_PREPARATION_COMPLETE',len(report),round(time.monotonic()-start,1),flush=True)
