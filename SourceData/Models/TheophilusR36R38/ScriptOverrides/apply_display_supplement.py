"""Apply the original four R36 and six R38 display meshes after R29-to-R35. No save/export."""
import bpy,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import supplement_common as C
assert C.ROOT.resolve()==Path(__file__).resolve().parent,'Common module belongs to another package directory; restart Blender.'

def cleanup_new(before):
    # Never purge pre-existing orphaned resources. Revisit newly created
    # dependencies after their users are removed, rather than guessing order.
    for kind in ['objects','collections']:
        seq=getattr(bpy.data,kind)
        for name in set(seq.keys())-before[kind]:
            if kind=='objects':seq.remove(seq[name],do_unlink=True)
            else:seq.remove(seq[name])
    kinds=['meshes','materials','images','node_groups','actions','texts','worlds','cameras','lights','scenes']
    while True:
        removed=False
        for kind in kinds:
            seq=getattr(bpy.data,kind)
            for name in set(seq.keys())-before[kind]:
                block=seq[name]
                if block.users==0:seq.remove(block);removed=True
        if not removed:break

def apply():
    from compat_readonly import require_511_precheck
    data=C.mapping();C.runtime_guard();C.verify_payload(data);C.verify_baseline(data)
    if C.MARKER in bpy.context.scene:
        C.verify_applied(data)
        print('R36_R38_ALREADY_APPLIED_UNCHANGED_NO_SAVE')
        return {'status':'already_applied','saved':False}
    require_511_precheck(C.ROOT)
    for kind,names in data['expected_created'].items():
        for n in names:assert n not in getattr(bpy.data,kind),'Existing/conflicting '+kind+': '+n+'; use the pre-supplement R35 working copy.'
    payload=C.ROOT/next(iter(data['payload']))
    with bpy.data.libraries.load(str(payload),link=False) as (src,dst):
        for kind,names in data['expected_created'].items():
            assert set(getattr(src,kind))==set(names),'Unexpected payload '+kind
    before=C.ids()
    try:
        with bpy.data.libraries.load(str(payload),link=False) as (src,dst):dst.collections=list(data['collections'])
        for coll in dst.collections:bpy.context.scene.collection.children.link(coll)
        for rec in data['added_objects']:
            ob=bpy.data.objects[rec['name']]
            if ob.type=='MESH':
                assert not ob.data.materials,'Geometry payload unexpectedly contains materials.'
                for n in rec['materials']:ob.data.materials.append(bpy.data.materials[n])
                ob.data.polygons.foreach_set('material_index',rec['face_material_indices'])
        bpy.context.view_layer.update()
        created={k:sorted(C.ids()[k]-before[k]) for k in before}
        assert created==data['expected_created'],'Append created unexpected datablocks.'
        rec={'mapping_sha256':C.sha(C.ROOT/C.MAPPING),'created':created,'baseline_filepath':bpy.data.filepath}
        bpy.context.scene[C.MARKER]=json.dumps(rec)
        C.verify_applied(data)
        print('R36_R38_APPLIED: 10 display meshes + 2 roots. Zero materials, textures or scene patches. No save/export.')
        return {'status':'applied','saved':False,'created':created}
    except Exception:
        if C.MARKER in bpy.context.scene:del bpy.context.scene[C.MARKER]
        cleanup_new(before);bpy.context.view_layer.update()
        assert C.ids()==before,'Failure cleanup incomplete; do not save.'
        raise

if __name__=='__main__':apply()
