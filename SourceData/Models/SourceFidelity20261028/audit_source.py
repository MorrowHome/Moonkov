"""Read-only fidelity contract. Records used scene data without altering or saving the source."""
import bpy, hashlib, json, collections, pathlib
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parent
scene=bpy.context.scene

def value(v):
 if isinstance(v,(bool,int,float,str)) or v is None:return v
 if isinstance(v,bpy.types.ID):return {'datablock':v.name,'kind':v.bl_rna.identifier}
 try:return [value(x) for x in v]
 except TypeError:return str(v)

def graph(tree):
 nodes=[]
 for n in tree.nodes:
  r={'id':n.name,'type':n.bl_idname,'inputs':[{'id':s.identifier,'name':s.name,'value':value(s.default_value) if hasattr(s,'default_value') else None} for s in n.inputs]}
  for p in ['operation','blend_type','data_type','clamp_factor','use_clamp','invert','space','vector_type','uv_map','attribute_name','attribute_type','distribution','noise_dimensions','normalize','projection','projection_blend','extension','interpolation']:
   if hasattr(n,p):r[p]=value(getattr(n,p))
  if n.type=='TEX_IMAGE' and n.image:r['image']=n.image.name
  if n.type=='GROUP' and n.node_tree:r['node_group']=n.node_tree.name
  if n.type=='VALTORGB':r['ramp']={'interpolation':n.color_ramp.interpolation,'color_mode':n.color_ramp.color_mode,'hue_interpolation':n.color_ramp.hue_interpolation,'elements':[{'position':e.position,'color':list(e.color)} for e in n.color_ramp.elements]}
  if n.type=='CURVE_RGB':r['mapping_curves']=[{'points':[{'location':list(p.location),'handle_type':p.handle_type} for p in c.points]} for c in n.mapping.curves]
  nodes.append(r)
 return {'nodes':nodes,'links':[{'from':[l.from_node.name,l.from_socket.identifier],'to':[l.to_node.name,l.to_socket.identifier]} for l in tree.links]}

objects=[];used=set();lights=[]
for o in scene.objects:
 if o.type in ('MESH','CURVE','FONT'):
  mats=[s.material.name if s.material else None for s in o.material_slots]
  used.update(m for m in mats if m)
  objects.append({'name':o.name,'type':o.type,'data':o.data.name,'hide_render':o.hide_render,'collections':[c.name for c in o.users_collection], 'matrix_world':[list(r) for r in o.matrix_world], 'bounds':[list(o.matrix_world@Vector(c)) for c in o.bound_box], 'materials':mats,'uv_layers':[u.name for u in o.data.uv_layers] if o.type=='MESH' else [], 'property_keys':list(o.keys()),'modifiers':[{'type':m.type,'name':m.name,'show_render':m.show_render} for m in o.modifiers]})
 if o.type=='LIGHT':
  d=o.data
  lights.append({'name':o.name,'data':d.name,'type':d.type,'hide_render':o.hide_render,'collections':[c.name for c in o.users_collection],'position':list(o.matrix_world.translation),'matrix_world':[list(r) for r in o.matrix_world],'color':list(d.color),'energy':d.energy,'size':getattr(d,'size',None),'size_y':getattr(d,'size_y',None),'shape':getattr(d,'shape',None),'spread':getattr(d,'spread',None),'spot_size':getattr(d,'spot_size',None),'spot_blend':getattr(d,'spot_blend',None),'shadow_soft_size':getattr(d,'shadow_soft_size',None),'property_keys':list(o.keys()),'data_property_keys':list(d.keys()),'use_nodes':d.use_nodes,'nodes':graph(d.node_tree) if d.use_nodes and d.node_tree else None})
materials={name:graph(bpy.data.materials[name].node_tree) for name in sorted(used) if bpy.data.materials[name].node_tree}
groups={g.name:graph(g) for g in bpy.data.node_groups if g.bl_idname=='ShaderNodeTree'}
types=collections.Counter(n['type'] for m in materials.values() for n in m['nodes'])
image_names={n['image'] for m in [*materials.values(),*groups.values()] for n in m['nodes'] if 'image' in n}
images=[]
for name in sorted(image_names):
 i=bpy.data.images[name];p=pathlib.Path(bpy.path.abspath(i.filepath))
 images.append({'name':name,'source':i.source,'size':list(i.size),'colorspace':i.colorspace_settings.name,'packed':i.packed_file is not None,'filename':p.name,'external_exists':p.is_file()})
view_dependent=[name for name,m in materials.items() if any(n['type'] in ('ShaderNodeCameraData','ShaderNodeLightPath','ShaderNodeFresnel','ShaderNodeLayerWeight') for n in m['nodes'])]
report={'source_file':pathlib.Path(bpy.data.filepath).name,'source_sha256':hashlib.sha256(pathlib.Path(bpy.data.filepath).read_bytes()).hexdigest(),'blender':bpy.app.version_string,'source_saved':False,'render':{'engine':scene.render.engine,'samples':scene.cycles.samples,'denoise':scene.cycles.use_denoising,'view_transform':scene.view_settings.view_transform,'look':scene.view_settings.look,'exposure':scene.view_settings.exposure,'gamma':scene.view_settings.gamma,'diffuse_bounces':scene.cycles.diffuse_bounces,'glossy_bounces':scene.cycles.glossy_bounces,'max_bounces':scene.cycles.max_bounces},'objects':objects,'materials':materials,'node_groups':groups,'lights':lights,'images':images,'world':graph(scene.world.node_tree) if scene.world and scene.world.node_tree else None,'summary':{'object_count':len(objects),'used_materials':len(materials),'light_types':dict(collections.Counter(l['type'] for l in lights)),'node_types':dict(types),'view_dependent_materials':view_dependent,'missing_external_images':[i['name'] for i in images if not i['packed'] and not i['external_exists']]}}
local=ROOT.parents[2]/'LocalData'/'SourceFidelity20261028'
local.mkdir(parents=True,exist_ok=True)
(local/'SourceContract.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
(ROOT/'SourceSummary.json').write_text(json.dumps({k:report[k] for k in ['source_file','source_sha256','blender','source_saved','render','summary']},ensure_ascii=False,indent=2))
print('SOURCE_CONTRACT',json.dumps({'render':report['render'],'summary':report['summary']},ensure_ascii=False),flush=True)
