"""Atomically place hash-verified full-preview FBX chunks and direct textures into Assets."""
import hashlib
import json
import re
import shutil
import sys
import uuid
from pathlib import Path

root = Path(__file__).resolve().parent
project = root.parents[2]
plan = json.loads((root / 'FullPreviewStaging' / 'UnityPlan.json').read_text())
assert plan['status'] == 'awaiting_Unity_full_display_preview' and len(plan['parts']) == 65
payload = project / 'Library/BuildValidation/LR06FullPreview/ImportPayload'
destination = project / 'Assets/MoonEnvironment/Variants/LR06FullPreview'
if '--install' in sys.argv:
    assert payload.is_dir() and not destination.exists() and not destination.with_suffix('.meta').exists()
    folder_template = (project / 'Assets/MoonEnvironment/Variants/LR06WallBatchPilot/Models.meta').read_text()
    meta, count = re.subn(r'(?m)^guid: [0-9a-f]+$', 'guid: ' + uuid.uuid4().hex, folder_template, count=1)
    assert count == 1
    destination.with_suffix('.meta').write_text(meta)
    payload.rename(destination)
    print(json.dumps({'installed': str(destination.relative_to(project))}))
    sys.exit(0)
assert not payload.exists() and not destination.exists() and not destination.with_suffix('.meta').exists()
payload.mkdir(parents=True)
for folder in ('Models', 'Textures', 'Materials', 'Scenes'):
    (payload / folder).mkdir()

model_template = (project / 'Assets/MoonEnvironment/Variants/LR06WallBatchPilot/Models/NR12_wall_batch_pilot.fbx.meta').read_text()
texture_template = (project / 'Assets/MoonEnvironment/Variants/LR06AtlasPilot/Textures/atlas_basecolor.png.meta').read_text()
folder_template = (project / 'Assets/MoonEnvironment/Variants/LR06WallBatchPilot/Models.meta').read_text()


def new_meta(template):
    result, count = re.subn(r'(?m)^guid: [0-9a-f]+$', 'guid: ' + uuid.uuid4().hex, template, count=1)
    assert count == 1
    return result


def verify(source, record):
    assert source.is_file() and source.stat().st_size == record['bytes']
    assert hashlib.sha256(source.read_bytes()).hexdigest() == record['sha256']


for folder in ('Models', 'Textures', 'Materials', 'Scenes'):
    (payload / (folder + '.meta')).write_text(new_meta(folder_template))
for part in plan['parts']:
    source = root / part['path']
    verify(source, part)
    target = payload / 'Models' / source.name
    shutil.copyfile(source, target)
    verify(target, part)
    meta = new_meta(model_template).replace('    isReadable: 1', '    isReadable: 0')
    target.with_suffix(target.suffix + '.meta').write_text(meta)
for tex in plan['textures']:
    source = root / tex['path']
    verify(source, tex)
    target = payload / 'Textures' / (tex['sha256'] + '.png')
    shutil.copyfile(source, target)
    verify(target, tex)
    meta = re.sub(r'(?m)^(\s*maxTextureSize:)\s*\d+$', r'\g<1> 1024', new_meta(texture_template))
    assert tex['colorspace'] == 'sRGB'
    target.with_suffix('.png.meta').write_text(meta)
shutil.copyfile(root / 'FullPreviewStaging' / 'UnityPlan.json', payload / 'PreviewPlan.json')
shutil.copyfile(root / 'LightConversionInventory.json', payload / 'SourceLightInventory.json')
print(json.dumps({'prepared': str(payload.relative_to(project)), 'models': len(plan['parts']),
                  'textures': len(plan['textures']), 'source_lights_recorded': plan['source_light_count']}))
