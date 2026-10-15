"""Blender-only exact display append. Requires R21+R23+R24 and final material libraries.
No save, export, render, collider/interaction update or Terrain change. Unity must use
an explicit Renderer-only replacement and established mesh export path, not execute this script.
"""
import bpy,json,hashlib,struct
from pathlib import Path

def mesh_hash(me):
 h=hashlib.sha256()
 for v in me.vertices:h.update(struct.pack('<3f',*v.co))
 for f in me.polygons:h.update(struct.pack('<I',len(f.vertices)));h.update(struct.pack('<%dI'%len(f.vertices),*f.vertices))
 return h.hexdigest()
def apply_r29_final_geometry_blender(folder=None):
 P=Path(folder or Path(__file__).resolve().parent);M=json.loads((P/'R29_Final_Geometry_Manifest.json').read_text())
 lib=P/M['geometry_library'];assert hashlib.sha256(lib.read_bytes()).hexdigest()==M['library_sha256'],'Geometry file mismatch'
 for r in M['hide_old_renderer_only']:
  o=bpy.data.objects.get(r['object']);assert o and o.type=='MESH',('Missing original source',r['object'])
  assert mesh_hash(o.data)==r['vertex_topology_float32_sha256'],('Source geometry mismatch',o.name)
 for name in M['canonical_materials']:assert name in bpy.data.materials,('Load final industrial/local-ground libraries or required baseline material before geometry',name)
 exists=[r['object'] in bpy.data.objects for r in M['objects']]
 assert all(exists) or not any(exists),'Partial/newer install detected; do not blindly append duplicates'
 if all(exists):
  for r in M['objects']:
   o=bpy.data.objects[r['object']];assert o.get('R29_local_final_geometry') and mesh_hash(o.data)==r['vertex_topology_float32_sha256'],('Unexpected existing object',r['object'])
   assert [s.material.name for s in o.material_slots]==[s['canonical_material'] for s in r['material_slots']]
  return {'already_applied':True,'mesh_count':34}
 with bpy.data.libraries.load(str(lib),link=False) as (src,dst):dst.collections=[M['collection']]
 C=dst.collections[0];bpy.context.scene.collection.children.link(C)
 for r in M['objects']:
  o=bpy.data.objects[r['object']];assert mesh_hash(o.data)==r['vertex_topology_float32_sha256']
  assert len(o.material_slots)==len(r['material_slots'])
  for s in r['material_slots']:
   slot=o.material_slots[s['slot']];assert slot.material.get('required_canonical_material')==s['canonical_material']
   slot.link='DATA';slot.material=bpy.data.materials[s['canonical_material']]
  assert all(abs(o.matrix_world[i][j]-r['matrix_world'][i][j])<1e-5 for i in range(4) for j in range(4))
 for r in M['hide_old_renderer_only']:
  o=bpy.data.objects[r['object']];o.hide_render=True;o.hide_set(True)
 return {'applied_meshes':34,'hidden_source_display':[r['object'] for r in M['hide_old_renderer_only']],'collision_or_interactions_changed':False,'saved':False,'rendered':False}
if __name__=='__main__':print(apply_r29_final_geometry_blender())
