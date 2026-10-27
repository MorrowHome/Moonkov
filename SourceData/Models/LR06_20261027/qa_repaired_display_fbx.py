"""Compare Blender asset QA before/after UV repair, then render review sheets."""
import bpy
import json
import sys
from pathlib import Path

base = Path(__file__).resolve().parent
project = base.parents[2]
part = int(sys.argv[sys.argv.index('--') + 1])
name = 'Map_%02d.fbx' % part
before_path = base / 'FullPreviewStaging' / name
after_path = project / 'Library/BuildValidation/LR06UVLighting/RepairedFBX' / name
out = project / 'Library/BuildValidation/LR06UVLighting/RepairedFBX'
sys.path.insert(0, str(Path.home() / '.codex/skills/blender-asset-qa/scripts'))
import qa_check
import review_shots


def load(path):
    before = set(bpy.context.scene.objects)
    assert bpy.ops.import_scene.fbx(filepath=str(path), use_custom_normals=True) == {'FINISHED'}
    meshes = [obj for obj in bpy.context.scene.objects if obj not in before and obj.type == 'MESH']
    assert len(meshes) == 1
    return meshes[0]


old, new = load(before_path), load(after_path)
print('QA_START', old.name, new.name, flush=True)
# The source uses repeated tileable UVs. UV overlap/oob is intentional and
# independently checked per material and triangle by the repair + Unity audit.
old_qa = qa_check.run(objects=[old.name], profile='game', uv=False)
print('QA_BASELINE_DONE', len(old_qa['errors']), len(old_qa['warnings']), flush=True)
new_qa = qa_check.run(objects=[new.name], profile='game', uv=False)
print('QA_REPAIRED_DONE', len(new_qa['errors']), len(new_qa['warnings']), flush=True)
old_metrics, new_metrics = old_qa['objects'][old.name], new_qa['objects'][new.name]
assert old_metrics == new_metrics, ('UV repair changed Blender geometry QA metrics', old_metrics, new_metrics)
assert len(old_qa['zfighting']) == len(new_qa['zfighting'])
assert len(old_qa['floating']) == len(new_qa['floating'])
visuals = []
for mode in ('random', 'wire'):
    result = review_shots.run(objects=[new.name], out=str(out / ('Map_%02d_%s.png' % (part, mode))),
                              mode=mode, res=320, views=['front', 'right', 'top', 'three_quarter'])
    visuals.append(result['sheet'])
report = {'scope': 'geometry QA before and after UV-only repair; pre-existing findings are retained',
          'part': part, 'geometry_metrics_equal': True, 'baseline': old_qa, 'repaired': new_qa, 'visuals': visuals}
(out / ('Map_%02d_QA.json' % part)).write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
print('QA_DONE', json.dumps({'part': part, 'geometry_metrics_equal': True, 'baseline_pass': old_qa['pass'],
                            'repaired_pass': new_qa['pass'], 'baseline_errors': len(old_qa['errors']),
                            'repaired_errors': len(new_qa['errors']), 'warnings': len(new_qa['warnings']), 'visuals': visuals}), flush=True)
