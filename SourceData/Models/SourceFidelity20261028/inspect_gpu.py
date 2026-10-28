"""Run with --background --factory-startup --python this_file [-- --render-probe]."""
import json
import pathlib
import sys
import time

import bpy

ROOT = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT))
from cycles_device import configure_cycles_device

if bpy.data.filepath:
    raise RuntimeError('Use --factory-startup without a source blend for this probe.')
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
report = configure_cycles_device(scene)
if '--render-probe' in sys.argv:
    scene.render.resolution_x = 64
    scene.render.resolution_y = 64
    scene.render.resolution_percentage = 100
    scene.cycles.samples = 8
    scene.cycles.use_denoising = False
    output = ROOT.parents[2] / 'LocalData/SourceFidelity20261028/WindowsGpu'
    output.mkdir(parents=True, exist_ok=True)
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(output / 'probe.png')
    started = time.monotonic()
    bpy.ops.render.render(write_still=True)
    report.update(renderSeconds=round(time.monotonic() - started, 3),
                  samples=8, resolution=[64, 64], image=str(output / 'probe.png'))
    (output / 'probe.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('MOONKOV_GPU_REPORT ' + json.dumps(report), flush=True)
