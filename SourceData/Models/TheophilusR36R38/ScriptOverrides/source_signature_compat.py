import bpy,json,hashlib,array
from pathlib import Path
ROOT=Path(__file__).resolve().parent
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def mesh_hash(o):
 h=hashlib.sha256();m=o.data
 for seq,attr,n,t in [(m.vertices,'co',len(m.vertices)*3,'f'),(m.edges,'vertices',len(m.edges)*2,'i'),(m.loops,'vertex_index',len(m.loops),'i'),(m.polygons,'loop_total',len(m.polygons),'i'),(m.polygons,'material_index',len(m.polygons),'i'),(m.polygons,'use_smooth',len(m.polygons),'b')]:
  a=array.array(t,[0])*n;seq.foreach_get(attr,a);h.update(a.tobytes())
 for uv in m.uv_layers:
  h.update(uv.name.encode());a=array.array('f',[0])*(len(uv.data)*2);uv.data.foreach_get('uv',a);h.update(a.tobytes());h.update(str((uv.active_render,m.uv_layers.active_index)).encode())
 h.update(json.dumps([a.name if a else None for a in m.materials]).encode());return h.hexdigest()
def vals(x):
 out={}
 for p in x.bl_rna.properties:
  if p.identifier in {'rna_type','name'} or p.type in {'POINTER','COLLECTION'}:continue
  try:
   v=getattr(x,p.identifier);out[p.identifier]=list(v) if p.is_array else v
  except:pass
 return out

def object_sig(o):
 return {'type':o.type,'data':o.data.name if o.data else None,'matrix':[list(r) for r in o.matrix_world],'parent':o.parent.name if o.parent else None,'hide_render':o.hide_render,'hide_viewport':o.hide_viewport,'hide_set':o.hide_get(),'collections':[c.name for c in o.users_collection],'modifiers':[[m.name,vals(m)] for m in o.modifiers],'mesh_hash':mesh_hash(o) if o.type=='MESH' else None}
def nh(m):
 if not m.use_nodes:return list(m.diffuse_color)
 data=[]
 for n in m.node_tree.nodes:
  ins=[]
  for i in n.inputs:
   if hasattr(i,'default_value'):
    v=i.default_value
    if isinstance(v,(int,float,bool,str)):ins.append([i.name,v])
    elif hasattr(v,'__len__'):ins.append([i.name,list(v)])
  data.append([n.name,n.bl_idname,ins])
 return sha_bytes(json.dumps([data,[(l.from_node.name,l.from_socket.name,l.to_node.name,l.to_socket.name) for l in m.node_tree.links]],sort_keys=True))
def sha_bytes(s):return hashlib.sha256(s.encode()).hexdigest()
def semantic(v):
 # Library append changes ownership metadata; runtime timing/session IDs are not edits.
 if isinstance(v,dict):return {k:semantic(x) for k,x in v.items() if k not in {'session_uid','is_override_data','execution_time'}}
 if isinstance(v,list):return [semantic(x) for x in v]
 return v
