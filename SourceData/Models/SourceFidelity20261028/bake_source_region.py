"""Bake source nodes and actual Cycles irradiance in source-scene context, without saving the .blend."""
import bpy,bmesh,json,hashlib,math,pathlib,sys,time,numpy as np
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
from material_bake_nodes import flatten,principled,emission_channel
from freeze_object_inputs import freeze_mesh,freeze_material
PREFIX=sys.argv[sys.argv.index('--prefix')+1] if '--prefix' in sys.argv else 'NR01_V5_'
SIZE=int(sys.argv[sys.argv.index('--size')+1]) if '--size' in sys.argv else 4096
OUT=ROOT/'Bakes'/PREFIX.rstrip('_');OUT.mkdir(parents=True,exist_ok=True)
scene=bpy.context.scene;dg=bpy.context.evaluated_depsgraph_get()
source_sha=hashlib.sha256(pathlib.Path(bpy.data.filepath).read_bytes()).hexdigest()
summary=json.loads((ROOT/'SourceSummary.json').read_text());assert source_sha==summary['source_sha256']
objects=[o for o in scene.objects if o.type=='MESH' and o.name.startswith(PREFIX) and not o.hide_render]
assert objects, 'No visible source meshes for '+PREFIX
original={};rows=[];material_copies={}
for obj in objects:
 evaluated=obj.evaluated_get(dg)
 mesh=bpy.data.meshes.new_from_object(evaluated,preserve_all_data_layers=True,depsgraph=dg)
 original[obj.name]=(obj.data,[(m.show_render,m.show_viewport) for m in obj.modifiers])
 # Correct closed inward components on temporary evaluated copies only.
 bm=bmesh.new();bm.from_mesh(mesh);bm.faces.ensure_lookup_table();seen=set();reversed_parts=0;open_parts=0
 for first in bm.faces:
  if first.index in seen:continue
  pending=[first];faces=[];closed=True
  while pending:
   f=pending.pop()
   if f.index in seen:continue
   seen.add(f.index);faces.append(f)
   for edge in f.edges:
    if not edge.is_manifold:closed=False
    pending.extend(x for x in edge.link_faces if x.index not in seen)
  if not closed:open_parts+=1;continue
  vertices={v for f in faces for v in f.verts};center=sum((v.co for v in vertices),Vector())/len(vertices)
  volume=sum((f.verts[0].co-center).dot((f.verts[i].co-center).cross(f.verts[i+1].co-center))/6 for f in faces for i in range(1,len(f.verts)-1))
  if volume < -1e-9:bmesh.ops.reverse_faces(bm,faces=faces);reversed_parts+=1
 bm.to_mesh(mesh);bm.free();mesh.update()
 obj.data=mesh
 for m in obj.modifiers:m.show_render=False;m.show_viewport=False
 slots=[]
 for slot in obj.material_slots:
  source=slot.material
  if not source:raise ValueError('Missing material '+obj.name)
  if source.name not in material_copies:
   copy=source.copy();flatten(copy.node_tree);principled(copy);material_copies[source.name]=copy
  slots.append(material_copies[source.name])
 obj.data.materials.clear()
 for mat in slots:obj.data.materials.append(mat)
 old_uv=next((u.name for u in mesh.uv_layers if u.active_render),'')
 freeze_mesh(obj,obj.name,old_uv)
 uv=obj.data.uv_layers.new(name='SourceStaticBake')
 obj.data.uv_layers.active=uv
 mesh.calc_loop_triangles()
 rows.append({'object':obj.name,'source_materials':[next(k for k,v in material_copies.items() if v==m) for m in slots], 'uv_channel':len(mesh.uv_layers)-1,'triangles':len(mesh.loop_triangles),'vertices':len(mesh.vertices),'reversed_closed_parts':reversed_parts,'open_parts':open_parts,'matrix_world':[list(r) for r in obj.matrix_world],'bounds_world':[list(obj.matrix_world@Vector(c)) for c in obj.bound_box]})
print('REGION_PREPARED',len(objects),sum(r['triangles'] for r in rows),flush=True)
bpy.ops.object.select_all(action='DESELECT')
for o in objects:o.select_set(True)
bpy.context.view_layer.objects.active=objects[0]
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
assert bpy.ops.uv.smart_project(island_margin=.004)=={'FINISHED'}
bpy.ops.object.mode_set(mode='OBJECT')
for mat in material_copies.values():freeze_material(mat)
# One combined bake object prevents a full scene/kernel update for every source part.
# Frozen attributes preserve Object Info random, generated and local coordinate inputs.
assert bpy.ops.object.join()=={'FINISHED'}
joined=bpy.context.object;joined.name='Source_'+PREFIX.rstrip('_');objects=[joined]
joined.data.calc_loop_triangles()
render_row={'object':joined.name,'uv_channel':joined.data.uv_layers.find('SourceStaticBake'),'triangles':len(joined.data.loop_triangles),'vertices':len(joined.data.vertices),'bounds_world':[list(joined.matrix_world@Vector(c)) for c in joined.bound_box]}
scene.render.engine='CYCLES';scene.cycles.samples=4;scene.cycles.use_denoising=False
scene.render.threads_mode='FIXED';scene.render.threads=4
scene.cycles.device='CPU'
try:
 prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='METAL';prefs.refresh_devices()
 for d in prefs.devices:d.use=d.type=='METAL'
 if any(d.use for d in prefs.devices):scene.cycles.device='GPU'
except Exception as e:print('GPU_FALLBACK',str(e),flush=True)
scene.render.bake.use_selected_to_active=False;scene.render.bake.use_clear=True;scene.render.bake.margin=12
scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth='16'
files={};timings={}

def bake(tag,kind,channels=None,size=SIZE,hdr=False,samples=4):
 started=time.monotonic();scene.cycles.samples=samples
 image=bpy.data.images.new('SourceFidelity_'+tag,size,size,alpha=True,float_buffer=True)
 image.colorspace_settings.name='sRGB' if tag=='base' else 'Non-Color'
 targets=[];restores=[];temporary=[]
 for mat in material_copies.values():
  tree=mat.node_tree
  if kind=='WORLD_NORMAL_EMIT':
   output=next(n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output)
   prior=[(l.from_socket,l.to_socket) for l in output.inputs['Surface'].links]
   for link in list(output.inputs['Surface'].links):tree.links.remove(link)
   normal=principled(mat).inputs['Normal']
   geom=None
   if normal.is_linked:source_normal=normal.links[0].from_socket
   else:
    geom=tree.nodes.new('ShaderNodeNewGeometry');source_normal=geom.outputs['Normal']
   scale=tree.nodes.new('ShaderNodeVectorMath');scale.operation='SCALE';scale.inputs['Scale'].default_value=.5
   add=tree.nodes.new('ShaderNodeVectorMath');add.operation='ADD';add.inputs[1].default_value=(.5,.5,.5)
   emit=tree.nodes.new('ShaderNodeEmission')
   tree.links.new(source_normal,scale.inputs[0]);tree.links.new(scale.outputs['Vector'],add.inputs[0]);tree.links.new(add.outputs['Vector'],emit.inputs['Color']);tree.links.new(emit.outputs['Emission'],output.inputs['Surface'])
   def restore_normal(tree=tree,scale=scale,add=add,emit=emit,geom=geom,prior=prior):
    for node in [scale,add,emit]:tree.nodes.remove(node)
    if geom:tree.nodes.remove(geom)
    for a,b in prior:tree.links.new(a,b)
   restores.append(restore_normal)
  elif channels and len(channels)==1:restores.append(emission_channel(mat,channels[0]))
  elif channels:
   output=next(n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output)
   prior=[(l.from_socket,l.to_socket) for l in output.inputs['Surface'].links]
   for link in list(output.inputs['Surface'].links):tree.links.remove(link)
   combine=tree.nodes.new('ShaderNodeCombineColor');emit=tree.nodes.new('ShaderNodeEmission');bsdf=principled(mat)
   for channel,dest in zip(channels,('Red','Green','Blue')):
    source=bsdf.inputs[channel]
    if source.is_linked:tree.links.new(source.links[0].from_socket,combine.inputs[dest])
    else:combine.inputs[dest].default_value=source.default_value
   tree.links.new(combine.outputs['Color'],emit.inputs['Color']);tree.links.new(emit.outputs['Emission'],output.inputs['Surface'])
   temporary.append((tree,combine,emit,prior))
  target=tree.nodes.new('ShaderNodeTexImage');target.image=image;tree.nodes.active=target;targets.append((tree,target))
 kwargs={'type':'EMIT' if kind=='WORLD_NORMAL_EMIT' else kind,'uv_layer':'SourceStaticBake'}
 if kind=='NORMAL':kwargs['normal_space']='WORLD'
 if kind=='DIFFUSE':kwargs['pass_filter']={'DIRECT','INDIRECT'}
 assert bpy.ops.object.bake(**kwargs)=={'FINISHED'}
 image.file_format='OPEN_EXR' if hdr else 'PNG';image.filepath_raw=str(OUT/(tag+('.exr' if hdr else '.png')))
 if hdr:scene.render.image_settings.color_depth='16';scene.render.image_settings.exr_codec='ZIP'
 image.save()
 for tree,target in targets:tree.nodes.remove(target)
 for restore in restores:restore()
 for tree,combine,emit,prior in temporary:
  tree.nodes.remove(combine);tree.nodes.remove(emit)
  for a,b in prior:tree.links.new(a,b)
 path=pathlib.Path(image.filepath_raw);files[tag]={'path':str(path.relative_to(ROOT)),'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'size':size,'colorspace':image.colorspace_settings.name}
 bpy.data.images.remove(image);timings[tag]=time.monotonic()-started
 print('REGION_BAKED',tag,round(timings[tag],2),files[tag]['bytes'],flush=True)

bake('base','EMIT',['Base Color'])
bake('surface','EMIT',['Metallic','Roughness','Coat Weight'])
bake('normal_world','WORLD_NORMAL_EMIT')
bake('irradiance','DIFFUSE',size=1024,hdr=True,samples=128)
# Export original source numeric parameters as well as every linked texture channel.
parameters={}
for name,mat in material_copies.items():
 b=principled(mat)
 parameters[name]={s.name:{'linked':s.is_linked,'value':list(s.default_value) if hasattr(s.default_value,'__len__') else s.default_value} for s in b.inputs if hasattr(s,'default_value')}
# FBX serialization uses temporary evaluated copies. No source .blend save.
bpy.ops.object.select_all(action='DESELECT')
for o in objects:o.select_set(True)
bpy.context.view_layer.objects.active=objects[0]
fbx=OUT/'source_region.fbx'
assert bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},use_mesh_modifiers=False,add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',use_triangles=True,path_mode='STRIP')=={'FINISHED'}
report={'status':'source_channels_and_static_cycles_irradiance_baked_pending_Unity_validation','source_sha256':source_sha,'source_saved':False,'scope_prefix':PREFIX,'objects':rows,'render_objects':[render_row],'materials':parameters,'files':files,'fbx':{'path':str(fbx.relative_to(ROOT)),'bytes':fbx.stat().st_size,'sha256':hashlib.sha256(fbx.read_bytes()).hexdigest()},'timings_seconds':timings,'device':scene.cycles.device,'bounces':scene.cycles.max_bounces,'static_all_source_lights':True,'limits':['Region batch is one stage of the full-map transfer, not full-map completion.','Specular environment capture and source display transform are separate stages.','No target density or geometry QA acceptance is implied by successful baking.']}
(OUT/'manifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print('REGION_COMPLETE',json.dumps({'objects':len(rows),'triangles':sum(r['triangles'] for r in rows),'timings':timings}),flush=True)
