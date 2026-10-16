"""Restore exactly the selected R38 deck material/UV state. Does not save."""
import bpy,json,sys
from pathlib import Path
P=Path(__file__).resolve().parent;sys.path.insert(0,str(P))
from r39_common import *
def rollback():
 from compat_readonly import runtime_guard
 runtime_guard()
 from compat_readonly import module
 module(P)
 p=json.loads((P/'R39_Deck0176_Material_UV_Patch.json').read_text());ob=bpy.data.objects.get(p['target_object'])
 assert ob and ob.type=='MESH','Required original deck missing'
 assert ob.data.name==p['target_mesh'] and ob.data.users==1,'R39 mesh identity or ownership changed'
 if object_sig(ob)==p['before_object_signature']:
  print('Already at original R38 surface; no changes.');return
 assert object_sig(ob)==p['after_object_signature'],'Deck edited after R39; refusing to overwrite edits'
 mat=bpy.data.materials.get(p['new_material']);assert mat and mat_sig(mat)==p['new_material_signature'],'R39 material edited or missing'
 verify_patch_images(p)
 assert mat.users-int(mat.use_fake_user)==1,'R39 material is now used elsewhere; refusing automatic cleanup'
 restore_surface(ob.data,p['before']);assert object_sig(ob)==p['before_object_signature']
 mat.use_fake_user=False;bpy.data.materials.remove(mat)
 for n in p['new_images']:
  im=bpy.data.images.get(n)
  if im and im.users-int(im.use_fake_user)==0:
   im.use_fake_user=False
   bpy.data.images.remove(im)
 print('R38 material/UV restored exactly. No original material or geometry removed. No automatic save.')
if __name__=='__main__':rollback()
