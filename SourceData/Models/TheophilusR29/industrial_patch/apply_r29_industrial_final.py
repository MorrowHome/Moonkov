"""Final material/UV patch over R21+R23+compact R24. No save/render/geometry changes."""
import bpy,os,json,hashlib,struct

def _geom(me):
 a=hashlib.sha256();b=hashlib.sha256()
 for v in me.vertices:a.update(struct.pack('<3f',*v.co))
 for p in me.polygons:b.update(struct.pack('<I',len(p.vertices)));b.update(struct.pack('<%dI'%len(p.vertices),*p.vertices))
 return a.hexdigest(),b.hexdigest()
def _uvhash(uv):return hashlib.sha256(b''.join(struct.pack('<2f',*d.uv) for d in uv.data)).hexdigest()
def _float2(a,b):return tuple(struct.unpack('<2f',struct.pack('<2f',a,b)))
def _read(folder):
 M=json.load(open(os.path.join(folder,'R29_Industrial_Final_Mapping.json')));D=json.load(open(os.path.join(folder,M['patch_data_file'])));return M,D

def load_r29_industrial_materials(folder):
 """Load all seven final materials, including references needed by the separate new-geometry package."""
 M,D=_read(folder)
 # Fail on incomplete external delivery before touching Blender data.
 for t in M['textures']:
  p=os.path.join(folder,t['path']);assert os.path.isfile(p),'Missing PNG '+t['path']
  assert hashlib.sha256(open(p,'rb').read()).hexdigest()==t['sha256'],'PNG hash mismatch '+t['path']
 names=[r['name'] for r in M['materials']];missing=[]
 for n in names:
  if n in bpy.data.materials:assert bpy.data.materials[n].get('R29_final_industrial_delivery'),('Existing non-final material name collision; load this library before new geometry placeholders or resolve explicitly',n)
  else:missing.append(n)
 if missing:
  with bpy.data.libraries.load(os.path.join(folder,M['material_libraries'][0]),link=False) as (src,dst):
   assert all(n in src.materials for n in missing);dst.materials=list(missing)
  for m in dst.materials:
   assert m and m.get('R29_final_industrial_delivery')
   for node in m.node_tree.nodes:
    if node.type=='TEX_IMAGE' and node.image:
     # Blender resolves library-relative paths on append. Normalize using the explicit portable PNG record.
     rec=next(r for r in M['materials'] if r['name']==m.name)
     image_rec=next(t for t in rec['textures'] if t['texture_node']==node.name)
     node.image.filepath=os.path.abspath(os.path.join(folder,image_rec['png']))
 return {'available_materials':names,'newly_loaded':missing}

def apply_r29_industrial_final(folder):
 M,D=_read(folder);planned=[];skipped=[]
 # Entire patch preflight: compact slot order is irrelevant, only actual per-face names are read.
 for r in M['objects']:
  o=bpy.data.objects.get(r['object']);assert o and o.type=='MESH','Missing source object '+r['object'];me=o.data
  assert _geom(me)==(r['vertex_float32_sha256'],r['topology_sha256']),('Geometry mismatch',o.name)
  assert 'UVMap' in me.uv_layers and _uvhash(me.uv_layers['UVMap'])==r['UVMap_float32_sha256'],('Original UVMap mismatch',o.name)
  assert all(abs(o.matrix_world[i][j]-r['source_matrix_world'][i][j])<1e-5 for i in range(4) for j in range(4)),('Transform mismatch',o.name)
  patch=D[o.name];current=[me.materials[p.material_index].name for p in me.polygons];restored=list(current);states=[]
  for pi,old,new in patch['face_material_changes']:
   assert current[pi] in [old,new],('Source face material mismatch',o.name,pi,current[pi],old,new)
   states.append(current[pi]==new);restored[pi]=old
  assert hashlib.sha256(json.dumps(restored,separators=(',',':')).encode()).hexdigest()==r['source_face_material_names_sha256'],('Unchanged face material mismatch',o.name)
  assert all(states) or not any(states),('Partially applied object',o.name)
  if all(states):
   for name,rows in patch['new_uv_loop_rows'].items():
    layer=me.uv_layers.get(name);assert layer,('Missing final UV layer',o.name,name)
    assert all(tuple(layer.data[li].uv)==_float2(u,v) for li,u,v in rows),('Final UV values differ',o.name,name)
   skipped.append(o.name)
  else:
   for name in patch['new_uv_loop_rows']:assert name not in me.uv_layers,('Unexpected preexisting final UV layer',o.name,name)
   planned.append(r)
 lib=load_r29_industrial_materials(folder)
 for r in planned:
  o=bpy.data.objects[r['object']];old=o.data;active=old.uv_layers.active_index;render=next((u.name for u in old.uv_layers if u.active_render),'UVMap');me=old.copy();me.name=old.name+'_R29_FINAL_FINISH';o.data=me;patch=D[o.name]
  for name,rows in patch['new_uv_loop_rows'].items():
   layer=me.uv_layers.new(name=name,do_init=True)
   for li,u,v in rows:layer.data[li].uv=(u,v)
  indices={m.name:i for i,m in enumerate(me.materials)}
  for pi,source,target in patch['face_material_changes']:
   if target not in indices:indices[target]=len(me.materials);me.materials.append(bpy.data.materials[target])
   me.polygons[pi].material_index=indices[target]
  me.uv_layers.active_index=active
  for u in me.uv_layers:u.active_render=(u.name==render)
  assert _geom(me)==(r['vertex_float32_sha256'],r['topology_sha256']);assert _uvhash(me.uv_layers['UVMap'])==r['UVMap_float32_sha256']
 result={'applied_objects':[r['object'] for r in planned],'already_applied':skipped,'materials':lib,'changed_faces':sum(r['changed_faces'] for r in planned),'geometry_changed':False,'UVMap_changed':False,'source_shared_materials_changed':False,'saved':False,'rendered':False}
 print('R29_FINAL_INDUSTRIAL_APPLIED',json.dumps(result),flush=True);return result
if __name__=='__main__':apply_r29_industrial_final(os.path.dirname(os.path.abspath(__file__)))
