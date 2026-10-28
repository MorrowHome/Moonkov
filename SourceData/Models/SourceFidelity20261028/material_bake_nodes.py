"""Copy and flatten source shader groups for per-channel baking; no source group edits."""
import bpy

def _copy_node(tree, source):
 node=tree.nodes.new(source.bl_idname)
 # Dynamic socket layouts must be established before copying socket defaults.
 first=['data_type','operation','blend_type','noise_dimensions']
 properties=first+[p.identifier for p in source.bl_rna.properties if p.identifier not in first]
 excluded={'rna_type','name','parent','inputs','outputs','internal_links','dimensions','type','color_ramp'}
 for name in properties:
  p=source.bl_rna.properties.get(name)
  if not p or p.is_readonly or name in excluded or name.startswith('bl_'):continue
  try:
   v=getattr(source,name)
   if hasattr(v,'__len__') and not isinstance(v,(str,bpy.types.ID)):v=tuple(v)
   setattr(node,name,v)
  except (AttributeError,TypeError,ValueError):
   if name in {'image','node_tree','operation','data_type','blend_type','space','vector_type','uv_map','attribute_name','noise_dimensions'}:raise
 for a,b in zip(source.inputs,node.inputs):
  if hasattr(a,'default_value') and hasattr(b,'default_value'):
   v=a.default_value
   b.default_value=tuple(v) if hasattr(v,'__len__') and not isinstance(v,(str,bpy.types.ID)) else v
 if source.type=='VALTORGB':
  a=source.color_ramp;b=node.color_ramp
  b.interpolation=a.interpolation;b.color_mode=a.color_mode;b.hue_interpolation=a.hue_interpolation
  while len(b.elements)>len(a.elements):b.elements.remove(b.elements[-1])
  while len(b.elements)<len(a.elements):b.elements.new(.5)
  for x,y in zip(a.elements,b.elements):y.position=x.position;y.color=x.color
 return node

def _index(sockets,socket):
 return next(i for i,s in enumerate(sockets) if s==socket)

def _interface_socket(sockets,socket):
 found=next((s for s in sockets if s.identifier==socket.identifier),None)
 return found or sockets.get(socket.name)

def flatten(tree):
 while any(n.type=='GROUP' for n in tree.nodes):
  group=next(n for n in tree.nodes if n.type=='GROUP')
  inner=group.node_tree
  if not inner:raise ValueError('Missing shader group '+group.name)
  clones={n:_copy_node(tree,n) for n in inner.nodes if n.type not in {'GROUP_INPUT','GROUP_OUTPUT'}}
  def source(socket):
   node=socket.node
   if node.type=='GROUP_INPUT':
    parent=_interface_socket(group.inputs,socket)
    if parent is None:raise ValueError('Missing group input '+socket.name)
    return (parent.links[0].from_socket,None) if parent.is_linked else (None,getattr(parent,'default_value',None))
   return clones[node].outputs[_index(node.outputs,socket)],None
  for link in inner.links:
   output,default=source(link.from_socket)
   if link.to_node.type=='GROUP_OUTPUT':
    if not link.to_node.is_active_output:continue
    parent=_interface_socket(group.outputs,link.to_socket)
    if parent is None:raise ValueError('Missing group output '+link.to_socket.name)
    destinations=[l.to_socket for l in parent.links]
   else:destinations=[clones[link.to_node].inputs[_index(link.to_node.inputs,link.to_socket)]]
   for destination in destinations:
    if output:tree.links.new(output,destination)
    elif default is not None and hasattr(destination,'default_value'):destination.default_value=default
  tree.nodes.remove(group)
 return tree

def principled(material):
 nodes=[n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED']
 if len(nodes)!=1:raise ValueError(material.name+': needs explicit treatment for '+str(len(nodes))+' Principled surfaces')
 return nodes[0]

def emission_channel(material,channel):
 """Temporarily expose an actual linked source socket as emission, without lighting contamination."""
 tree=material.node_tree;bsdf=principled(material)
 output=next(n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output)
 target=output.inputs['Surface'];prior=[(l.from_socket,l.to_socket) for l in target.links]
 for link in list(target.links):tree.links.remove(link)
 emit=tree.nodes.new('ShaderNodeEmission')
 source=bsdf.inputs[channel]
 if source.is_linked:tree.links.new(source.links[0].from_socket,emit.inputs['Color'])
 else:
  v=source.default_value
  emit.inputs['Color'].default_value=tuple(v) if hasattr(v,'__len__') else (v,v,v,1)
 tree.links.new(emit.outputs['Emission'],target)
 def restore():
  tree.nodes.remove(emit)
  for a,b in prior:tree.links.new(a,b)
 return restore
