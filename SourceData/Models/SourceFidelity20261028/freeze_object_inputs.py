"""Freeze object-space shader inputs into mesh attributes before merging a bake batch.

The integer seed follows Cycles' documented implementation (Apache-2.0):
https://github.com/blender/blender/blob/main/intern/cycles/blender/object.cpp
https://github.com/blender/blender/blob/main/intern/cycles/util/hash.h
No source material, mesh or node-group datablock is modified.
"""
import bpy,numpy as np

def object_random(name):
 mask=(1<<32)-1;h=0
 for byte in name.encode('utf-8'):h=(h*37+byte)&mask
 initial=(0xdeadbeef+21)&mask
 values=[(initial+h)&mask,initial,initial]
 for target,other,bits in [(2,1,14),(0,2,11),(1,0,25),(2,1,16),(0,2,4),(1,0,14),(2,1,24)]:
  rotated=((values[other]<<bits)|(values[other]>>(32-bits)))&mask
  values[target]=((values[target]^values[other])-rotated)&mask
 return float(np.float32(values[2])*np.float32(1/4294967295))

def add_attribute(mesh,name,data,kind='FLOAT_VECTOR'):
 attribute=mesh.attributes.get(name) or mesh.attributes.new(name,kind,'CORNER')
 attribute.data.foreach_set('value' if kind=='FLOAT' else 'color' if kind=='FLOAT_COLOR' else 'vector',np.asarray(data,dtype=np.float32).ravel())

def freeze_mesh(obj,source_name,original_uv=None):
 mesh=obj.data;n=len(mesh.loops)
 ids=np.empty(n,dtype=np.int32);mesh.loops.foreach_get('vertex_index',ids)
 coords=np.empty(len(mesh.vertices)*3,dtype=np.float32);mesh.vertices.foreach_get('co',coords);coords=coords.reshape(-1,3)
 local=coords[ids];minimum=coords.min(axis=0);maximum=coords.max(axis=0)
 generated=(local-minimum)/np.maximum(maximum-minimum,1e-20)
 add_attribute(mesh,'SourceLocal',local);add_attribute(mesh,'SourceGenerated',generated)
 add_attribute(mesh,'SourceRandom',np.full(n,object_random(source_name)),'FLOAT')
 add_attribute(mesh,'SourceLocation',np.tile(np.asarray(obj.matrix_world.translation),(n,1)))
 add_attribute(mesh,'SourceObjectColor',np.tile(np.asarray(obj.color),(n,1)),'FLOAT_COLOR')
 add_attribute(mesh,'SourceObjectAlpha',np.full(n,obj.color[3]),'FLOAT')
 add_attribute(mesh,'SourceObjectIndex',np.full(n,obj.pass_index),'FLOAT')
 normals=np.empty(n*3,dtype=np.float32);mesh.corner_normals.foreach_get('vector',normals);add_attribute(mesh,'SourceLocalNormal',normals)
 for uv in mesh.uv_layers:
  values=np.empty(n*2,dtype=np.float32);uv.data.foreach_get('uv',values)
  values=values.reshape(-1,2);vec=np.zeros((n,3),dtype=np.float32);vec[:,:2]=values
  add_attribute(mesh,'SourceUV_'+uv.name,vec)
 primary=original_uv if original_uv is not None else next((uv.name for uv in mesh.uv_layers if uv.active_render),None)
 values=np.zeros((n,3),dtype=np.float32)
 if primary:
  xy=np.empty(n*2,dtype=np.float32);mesh.uv_layers[primary].data.foreach_get('uv',xy);values[:,:2]=xy.reshape(-1,2)
 add_attribute(mesh,'SourcePrimaryUV',values)
 return object_random(source_name)

def freeze_material(mat):
 tree=mat.node_tree
 def replace(socket,attribute,output='Vector'):
  destinations=[l.to_socket for l in socket.links]
  if not destinations:return
  node=tree.nodes.new('ShaderNodeAttribute');node.attribute_name=attribute
  for destination in destinations:tree.links.new(node.outputs[output],destination)
 for node in list(tree.nodes):
  if node.type=='OBJECT_INFO':
   for name,attribute,output in [('Random','SourceRandom','Fac'),('Location','SourceLocation','Vector'),('Color','SourceObjectColor','Color'),('Alpha','SourceObjectAlpha','Fac'),('Object Index','SourceObjectIndex','Fac')]:
    socket=node.outputs.get(name)
    if socket:replace(socket,attribute,output)
   if any(s.is_linked for s in node.outputs):
    linked=[s.name for s in node.outputs if s.is_linked]
    raise ValueError('Unfrozen object inputs: '+str(linked))
  elif node.type=='TEX_COORD':
   for name,attribute in [('Generated','SourceGenerated'),('Normal','SourceLocalNormal'),('UV','SourcePrimaryUV')]:replace(node.outputs[name],attribute)
   if node.outputs['Object'].is_linked and not node.object:replace(node.outputs['Object'],'SourceLocal')
  elif node.type=='UVMAP' and not node.from_instancer:replace(node.outputs['UV'],'SourceUV_'+node.uv_map if node.uv_map else 'SourcePrimaryUV')
