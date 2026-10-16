"""Apply exact R39 material + four UV coordinates to a matching R38 work copy.
Run with Blender's Scripting editor or --python. Does not save automatically.
"""
import bpy,json,sys
from pathlib import Path
P=Path(__file__).resolve().parent;sys.path.insert(0,str(P))
from r39_common import *
def apply():
 from compat_readonly import runtime_guard,require_511_precheck
 runtime_guard()
 from compat_readonly import module
 module(P)
 p=json.loads((P/'R39_Deck0176_Material_UV_Patch.json').read_text())
 ob=bpy.data.objects.get(p['target_object']);assert ob and ob.type=='MESH','Required original deck is missing'
 m=ob.data;assert m.name==p['target_mesh'] and m.users==1,'Mesh identity or ownership changed'
 if object_sig(ob)==p['after_object_signature']:
  assert mat_sig(bpy.data.materials[p['new_material']])==p['new_material_signature'],'R39 material was edited'
  verify_patch_images(p)
  print('R39 already applied; no changes.');return
 require_511_precheck(P)
 assert object_sig(ob)==p['before_object_signature'],'Target deck no longer matches R38; nothing changed'
 assert geom_sig(m)==p['target_geometry'],'Original geometry changed; nothing changed'
 lib=P/p['material_library'];assert sha(lib)==p['material_library_sha256'],'Material library hash mismatch'
 for name,h in p['textures'].items():assert sha(P/'textures'/name)==h,'Texture mismatch: '+name
 assert p['new_material'] not in bpy.data.materials,'Conflicting R39 material already exists'
 assert not any(n in bpy.data.images for n in p['new_images']),'Conflicting R39 image already exists'
 before_ids={k:set(getattr(bpy.data,k).keys()) for k in ['materials','images','node_groups']}
 touched=False
 try:
  with bpy.data.libraries.load(str(lib),link=False) as (src,dst):
   assert p['new_material'] in src.materials;dst.materials=[p['new_material']]
  mat=bpy.data.materials[p['new_material']]
  assert mat_sig(mat)==p['new_material_signature'],'Imported material verification failed'
  verify_patch_images(p)
  touched=True;restore_surface(m,p['after'])
  assert object_sig(ob)==p['after_object_signature'],'Unexpected post-apply target state'
 except Exception:
  if touched:restore_surface(m,p['before'])
  for k in ['materials','node_groups','images']:
   seq=getattr(bpy.data,k)
   for n in set(seq.keys())-before_ids[k]:
    x=seq[n];x.use_fake_user=False
    if x.users==0:seq.remove(x)
  assert object_sig(ob)==p['before_object_signature'],'Failed R39 rollback; do not save'
  assert {k:set(getattr(bpy.data,k).keys()) for k in before_ids}==before_ids,'Failed R39 resource cleanup; do not save'
  raise
 print('R39 applied: one original top polygon, one appended material slot, four UV coordinates. No automatic save.')
if __name__=='__main__':apply()
