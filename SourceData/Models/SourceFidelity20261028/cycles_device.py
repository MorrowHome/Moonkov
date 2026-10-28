"""Select a real Cycles GPU; never silently start a full-world CPU job."""
import json
import os
import sys

import bpy


def configure_cycles_device(scene):
    if bpy.data.filepath and bpy.app.version[:2] < bpy.data.version[:2]:
        raise RuntimeError(
            f"Blender {bpy.app.version_string} is older than the source file "
            f"{bpy.data.version}; use the source Blender version before rendering."
        )
    requested = os.environ.get('SOURCE_CYCLES_BACKEND', 'AUTO').upper()
    if requested == 'CPU':
        scene.cycles.device = 'CPU'
        report = {'backend': 'CPU', 'devices': ['explicit CPU override']}
    else:
        candidates = (['OPTIX', 'CUDA'] if sys.platform == 'win32' else
                      ['METAL'] if sys.platform == 'darwin' else ['OPTIX', 'CUDA', 'HIP', 'ONEAPI'])
        if requested != 'AUTO':
            if requested not in {'OPTIX', 'CUDA', 'METAL', 'HIP', 'ONEAPI'}:
                raise ValueError(f'Unsupported SOURCE_CYCLES_BACKEND: {requested}')
            candidates = [requested]
        prefs = bpy.context.preferences.addons['cycles'].preferences
        failures = []
        report = None
        for backend in candidates:
            try:
                prefs.compute_device_type = backend
                prefs.refresh_devices()
                selected = [d for d in prefs.devices if d.type == backend]
                if not selected:
                    failures.append(f'{backend}: no GPU found')
                    continue
                for device in prefs.devices:
                    device.use = device.type == backend
                scene.cycles.device = 'GPU'
                report = {'backend': backend, 'devices': [d.name for d in selected]}
                break
            except (TypeError, RuntimeError) as exc:
                failures.append(f'{backend}: {exc}')
        if report is None:
            raise RuntimeError('No Cycles GPU available. ' + '; '.join(failures))
    report['blender'] = bpy.app.version_string
    print('SOURCE_CYCLES_DEVICE ' + json.dumps(report), flush=True)
    return report
