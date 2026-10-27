"""Build a verified import plan for the two-object atlas trial only."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parent
trial = root / 'NR12AtlasPilot'
manifest = json.loads((trial / 'PilotManifest.json').read_text())
qa = json.loads((trial / 'RoundTripQA.json').read_text())
masks = json.loads((trial / 'MaskValidation.json').read_text())
rows = manifest['objects']
assert len(rows) == 3 and len(qa['objects']) == 3
assert qa['atlas_cross_object_overlap_samples'] == 0
assert all(row['max_coordinate_error_m'] < .003 and row['signed_volume_local_m3'] > 0 for row in qa['objects'])
assert masks['compared_pixels'] > 0
assert masks['metallic_max_error_16bit'] == 0 and masks['smoothness_max_error_16bit'] == 0
assert rows[0]['textures'] == rows[1]['textures'] and rows[2]['textures'] == {}

def item(record, role):
    path = root / record['path']
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    assert path.stat().st_size == record['bytes'] and actual == record['sha256'], path
    return dict(record, role=role)

assets = [item(manifest['fbx'], 'model')]
assets += [item(rows[0]['textures'][key], key) for key in ('basecolor', 'mask', 'normal')]
result = {'status': 'awaiting_Unity_atlas_trial', 'source_sha256': manifest['source_sha256'],
          'scope': 'isolated two-object material atlas and one untextured emissive diffuser',
          'materials': rows, 'assets': assets,
          'notes': ['The atlas mask stores metallic in R and one minus roughness in A.',
                    'The diffuser has no source UV and retains its warm source emission.',
                    'No formal scene, source area lights, baked lighting or switch integration is included.']}
(trial / 'ImportPlan.json').write_text(json.dumps(result, ensure_ascii=False, indent=2))
print(json.dumps({'assets': len(assets), 'objects': len(rows), 'scope': result['scope']}))
