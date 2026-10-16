import bpy,json,hashlib,array
from pathlib import Path
from compat_semantics import project_values,clone_flags,sockets,socket_index,socket_name,legacy_nh
ROOT=Path(__file__).resolve().parent
TARGET='B_NORTH_TRANSFER_Deck_0176';MAT='R39_Deck0176_Industrial_Plated_Surface'
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def digest(v):return hashlib.sha256(json.dumps(v,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def vals(x):
 out={}
 for p in x.bl_rna.properties:
  if p.identifier in {'rna_type','name','session_uid','is_override_data','execution_time'} or p.type in {'POINTER','COLLECTION'}:continue
  try:
   v=getattr(x,p.identifier);out[p.identifier]=list(v) if p.is_array else v
  except:pass
 return project_values(x,out)
def geom_sig(m):
 return digest({'verts':[list(v.co) for v in m.vertices],'edges':[list(e.vertices) for e in m.edges],'polys':[[list(p.vertices),p.use_smooth] for p in m.polygons],'attributes':[(a.name,a.data_type,a.domain) for a in m.attributes if a.name != 'material_index']})
def surface(m):
 return {'slots':[x.name if x else None for x in m.materials],'face_materials':[p.material_index for p in m.polygons],'uv':{u.name:[list(p.uv) for p in u.data] for u in m.uv_layers},'active_uv_index':m.uv_layers.active_index,'uv_active_render':[u.active_render for u in m.uv_layers]}
def object_sig(o):
 return digest({'name':o.name,'type':o.type,'data':o.data.name if o.data else None,'matrix':[list(r) for r in o.matrix_world],'parent':o.parent.name if o.parent else None,'visible':[o.hide_render,o.hide_viewport,o.hide_get()],'collections':[c.name for c in o.users_collection],'modifiers':[[m.name,vals(m)] for m in o.modifiers],'geometry':geom_sig(o.data) if o.type=='MESH' else None,'surface':surface(o.data) if o.type=='MESH' else None,'properties':{k:str(o[k]) for k in o.keys()}})
def mat_sig(m):
 if not m.use_nodes:return digest(list(m.diffuse_color))
 ns=[]
 for n in m.node_tree.nodes:
  ins=[]
  for _,i,sname in sockets(n,'inputs'):
   if hasattr(i,'default_value'):
    v=i.default_value
    if isinstance(v,(int,float,bool,str)):ins.append([sname,v])
    elif hasattr(v,'__len__'):ins.append([sname,list(v)])
  ns.append([n.name,n.bl_idname,ins,vals(n),n.image.name if n.type=='TEX_IMAGE' and n.image else None])
 return digest([ns,[(l.from_node.name,socket_name(l.from_node,'outputs',l.from_socket),l.to_node.name,socket_name(l.to_node,'inputs',l.to_socket)) for l in m.node_tree.links]])
def scene_sig(s):
 return digest({'world':mat_sig(s.world),'lights':{o.name:{'matrix':[list(r) for r in o.matrix_world],'data':vals(o.data)} for o in bpy.data.objects if o.type=='LIGHT'},'camera':{'name':s.camera.name,'matrix':[list(r) for r in s.camera.matrix_world],'data':vals(s.camera.data)},'resolution':[s.render.resolution_x,s.render.resolution_y,s.render.resolution_percentage],'color':vals(s.view_settings),'samples':s.cycles.samples,'denoise':s.cycles.use_denoising,'seed':s.cycles.seed,'engine':s.render.engine})
def restore_surface(m,s):
 assert m.users==1,'R39 mesh has linked users; stop before any mutation'
 assert len(s['face_materials'])==len(m.polygons),'R39 face count differs'
 assert set(s['uv'])==set(u.name for u in m.uv_layers),'UV layer structure changed'
 assert len(s['uv_active_render'])==len(m.uv_layers),'UV render flags differ'
 assert 0<=s['active_uv_index']<len(m.uv_layers),'UV active index invalid'
 for n,uvs in s['uv'].items():assert len(uvs)==len(m.uv_layers[n].data),'UV loop count differs: '+n
 assert all(n in bpy.data.materials for n in s['slots']),'Required material slot missing'
 assert all(0<=i<len(s['slots']) for i in s['face_materials']),'Face material index invalid'
 m.materials.clear()
 for n in s['slots']:m.materials.append(bpy.data.materials[n])
 for p,i in zip(m.polygons,s['face_materials']):p.material_index=i
 assert set(s['uv'])==set(u.name for u in m.uv_layers),'UV layer structure changed'
 for n,uvs in s['uv'].items():
  for p,v in zip(m.uv_layers[n].data,uvs):p.uv=v
 m.uv_layers.active_index=s['active_uv_index']
 for u,a in zip(m.uv_layers,s['uv_active_render']):u.active_render=a

def verify_patch_images(p):
 for n,h in p['textures'].items():
  im=bpy.data.images.get(n)
  assert im and im.packed_file,'R39 packed image missing: '+n
  assert hashlib.sha256(im.packed_file.data).hexdigest()==h,'R39 packed image bytes changed: '+n
  expected='sRGB' if 'BaseColor' in n else 'Non-Color'
  assert im.colorspace_settings.name==expected,'R39 image colorspace changed: '+n

# Detect stale cached code after a script overlay. Restart Blender before preflight.
COMPAT_SOURCE_SHA256=sha(__file__)
