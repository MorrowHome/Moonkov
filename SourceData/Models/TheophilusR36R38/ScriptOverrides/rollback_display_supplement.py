"""Remove only this unchanged R36+R38 supplement. Preserve the prior R29-to-R35 handoff."""
import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import supplement_common as C
assert C.ROOT.resolve()==Path(__file__).resolve().parent,'Common module belongs to another package directory; restart Blender.'

def rollback():
    data=C.mapping();C.runtime_guard()
    if C.MARKER not in bpy.context.scene:
        assert not any(n in getattr(bpy.data,k) for k,names in data['expected_created'].items() for n in names),'Supplement data exists without its receipt; inspect manually.'
        print('R36_R38_ALREADY_ABSENT_NO_SAVE');return {'status':'absent','saved':False}
    C.verify_applied(data)
    blocks={getattr(bpy.data,k)[n] for k,names in data['expected_created'].items() for n in names}
    allowed=blocks|{bpy.context.scene,bpy.context.scene.collection}
    for block,users in bpy.data.user_map(subset=blocks).items():
        assert set(users)<=allowed,'Supplement has new external users: '+block.name
    for n in data['expected_created']['objects']:bpy.data.objects.remove(bpy.data.objects[n],do_unlink=True)
    for n in data['expected_created']['collections']:bpy.data.collections.remove(bpy.data.collections[n])
    for n in data['expected_created']['meshes']:
        mesh=bpy.data.meshes[n];assert mesh.users==0,'Unexpected remaining mesh user: '+n
        bpy.data.meshes.remove(mesh)
    del bpy.context.scene[C.MARKER]
    bpy.context.view_layer.update()
    print('R36_R38_ROLLED_BACK: removed only 10 supplement meshes + 2 roots. Shared materials and predecessor retained. No save.')
    return {'status':'rolled_back','saved':False,'shared_materials_deleted':False}

if __name__=='__main__':rollback()
