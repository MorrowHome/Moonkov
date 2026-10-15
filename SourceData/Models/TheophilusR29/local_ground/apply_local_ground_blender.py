"""Explicit six-display-mesh material binding. Never binds a Terrain object.
This is for Blender only; it is not a Unity shader importer.
"""
from pathlib import Path
import bpy,json
O=Path(__file__).resolve().parent
D=json.loads((O/'R29_Local_Ground_Manifest.json').read_text())
def apply_local_ground_materials():
 for row in D['objects']:
  o=bpy.data.objects.get(row['object'])
  if o is None or o.type!='MESH':raise RuntimeError('Missing exact local display mesh: '+row['object'])
  if len(o.material_slots)!=len(row['materials']):raise RuntimeError('Slot count mismatch: '+row['object'])
  if not o.name.startswith('R27S_'):raise RuntimeError('Refusing nonlocal target: '+o.name)
 for row in D['objects']:
  for s in row['materials']:
   if s['reuse_existing_R24'] and not bpy.data.materials.get(s['local_default_material']):raise RuntimeError('Required existing R24 material absent: '+s['local_default_material'])
 missing=[n for n in D['library_materials'] if not bpy.data.materials.get(n)]
 if missing:
  with bpy.data.libraries.load(str(O/D['material_library']),link=False) as (src,dst):dst.materials=missing
 for row in D['objects']:
  o=bpy.data.objects[row['object']]
  for s in row['materials']:
   slot=o.material_slots[s['slot']];slot.link='OBJECT';slot.material=bpy.data.materials[s['local_default_material']]
 return {'display_meshes':6,'assigned_slots':sum(len(o['materials']) for o in D['objects']),'R28_optional_mask_applied':False,'Terrain_objects_changed':0,'geometry_UV_collision_changes':False}
