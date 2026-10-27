"""Build a hash-bound Unity plan for the verified six-wall sample only."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parent
trial = root / 'NR12WallBatchPilot'
manifest = json.loads((trial / 'PilotManifest.json').read_text())
qa = json.loads((trial / 'RoundTripQA.json').read_text())
mask = json.loads((trial / 'MaskValidation.json').read_text())
samples = json.loads((trial / 'AtlasSamples.json').read_text())
comparison = json.loads((trial / 'FullSceneMaterialComparison.json').read_text())
assert len(manifest['objects']) == len(qa['objects']) == len(samples['objects']) == 6
assert qa['atlas_cross_object_overlap_samples'] == 0
assert all(r['max_coordinate_error_m'] < .003 and r['signed_volume_local_m3'] > 0 and
           r['recentered_qa']['pass'] for r in qa['objects'])
assert mask['compared_pixels'] > 0 and mask['metallic_max_error_16bit'] == mask['smoothness_max_error_16bit'] == 0
assert all(abs(r['metallic_sample'] - .015) < .002 and
           r['density_texels_per_metre_area_weighted_p10'] > 60 for r in samples['objects'])
assert comparison['visible_wall_mask_pixels'] > 100

def item(record, role):
    path = root / record['path']
    assert path.stat().st_size == record['bytes'] and hashlib.sha256(path.read_bytes()).hexdigest() == record['sha256']
    return dict(record, role=role)

assets = [item(manifest['fbx'], 'model')]
assets += [item(manifest['textures'][key], key) for key in ('basecolor', 'mask', 'normal')]
plan = {'status': 'awaiting_Unity_six_wall_trial', 'source_sha256': manifest['source_sha256'],
        'scope': 'six isolated corridor walls; one shared material atlas',
        'materials': manifest['objects'], 'assets': assets,
        'notes': ['Source scene and lighting are not changed.',
                  'Original object identities, world-position shader nodes and scene context were used during the bake.',
                  'The shared material is not a claim that Unity will combine draw calls.',
                  'No formal scene, source lights, baked lighting, interaction or collision is included.']}
(trial / 'ImportPlan.json').write_text(json.dumps(plan, ensure_ascii=False, indent=2))
print(json.dumps({'assets': len(assets), 'objects': len(plan['materials']), 'scope': plan['scope']}))
