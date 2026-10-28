"""Export reachable, flattened source shader graphs and exact packed image bytes for shader reconstruction."""
import bpy,json,pathlib,hashlib,collections,sys
ROOT=pathlib.Path(__file__).resolve().parent;sys.path.insert(0,str(ROOT))
from material_bake_nodes import flatten
summary=json.loads((ROOT/'SourceSummary.json').read_text());assert hashlib.sha256(pathlib.Path(bpy.data.filepath).read_bytes()).hexdigest()==summary['source_sha256']
out=ROOT/'MaterialIR';images_dir=out/'Images';images_dir.mkdir(parents=True,exist_ok=True)
used=sorted({slot.material.name for o in bpy.context.scene.objects for slot in o.material_slots if slot.material})
props=['operation','blend_type','data_type','noise_dimensions','noise_type','normalize','interpolation_type','clamp','use_clamp','invert','space','uv_map','attribute_name','attribute_type','distribution','projection','projection_blend','extension','interpolation','from_instancer','inside','only_local','samples','mode']
images={};materials={};counts=collections.Counter()
def val(v):
 if isinstance(v,(bool,int,float,str)) or v is None:return v
 if isinstance(v,bpy.types.ID):return {'name':v.name,'type':v.bl_rna.identifier}
 return list(v)
for name in used:
 source=bpy.data.materials[name];copy=source.copy();tree=flatten(copy.node_tree)
 outputs=[n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output]
 if len(outputs)!=1:raise ValueError('Ambiguous source output '+name)
 reachable=set()
 def visit(node):
  if node in reachable:return
  reachable.add(node)
  for socket in node.inputs:
   for link in socket.links:visit(link.from_node)
 visit(outputs[0]);nodes=[]
 for n in tree.nodes:
  if n not in reachable:continue
  row={'id':n.name,'type':n.type,'inputs':[{'id':s.identifier,'name':s.name,'type':s.type,'value':val(s.default_value) if hasattr(s,'default_value') else None} for s in n.inputs],'outputs':[{'id':s.identifier,'name':s.name,'type':s.type} for s in n.outputs]}
  for p in props:
   if hasattr(n,p):row[p]=val(getattr(n,p))
  if n.type=='TEX_COORD' and n.object:row['object_inverse_world']=[list(r) for r in n.object.matrix_world.inverted()]
  if n.type=='VALTORGB':row['ramp']={'interpolation':n.color_ramp.interpolation,'mode':n.color_ramp.color_mode,'elements':[(e.position,list(e.color)) for e in n.color_ramp.elements]}
  if n.type=='TEX_IMAGE' and n.image:
   image=n.image;row['image']=image.name
   if image.name not in images:
    path=pathlib.Path(bpy.path.abspath(image.filepath));ext=path.suffix.lower() or '.png'
    if image.packed_file:raw=bytes(image.packed_file.data)
    elif path.is_file():raw=path.read_bytes()
    else:raise ValueError('Missing image '+image.name)
    digest=hashlib.sha256(raw).hexdigest();target=images_dir/(digest+ext)
    if not target.exists():target.write_bytes(raw)
    images[image.name]={'path':str(target.relative_to(ROOT)),'sha256':digest,'bytes':len(raw),'colorspace':image.colorspace_settings.name,'size':list(image.size)}
  nodes.append(row);counts[n.type]+=1
 links=[{'from':[l.from_node.name,l.from_socket.identifier],'to':[l.to_node.name,l.to_socket.identifier]} for l in tree.links if l.from_node in reachable and l.to_node in reachable]
 materials[name]={'nodes':nodes,'links':links,'output':outputs[0].name,'surface_render_method':getattr(source,'surface_render_method',None),'use_backface_culling':source.use_backface_culling}
 bpy.data.materials.remove(copy)
result={'source_sha256':summary['source_sha256'],'source_saved':False,'materials':materials,'images':images,'node_counts':dict(counts),'noise_types':dict(collections.Counter(n.get('noise_type') for m in materials.values() for n in m['nodes'] if n['type']=='TEX_NOISE'))}
(out/'MaterialIR.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print('MATERIAL_IR',json.dumps({'materials':len(materials),'images':len(images),'nodes':result['node_counts'],'noise_types':result['noise_types']}),flush=True)
