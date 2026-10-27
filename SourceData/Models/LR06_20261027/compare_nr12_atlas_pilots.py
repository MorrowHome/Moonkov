"""Compare pilot UV density and isolated Unity previews; not full-map acceptance."""
import bpy
import json
import math
import numpy as np
from pathlib import Path

root = Path(__file__).resolve().parent
project = root.parents[2]
results = {}

for label, folder, fbx, resolution in (
        ('separate', 'NR12Pilot_Corrected', 'NR12_corridor_pilot.fbx', 512),
        ('atlas', 'NR12AtlasPilot', 'NR12_corridor_atlas_pilot.fbx', 1024)):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(root / folder / fbx), use_image_search=False)
    rows = []
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH' or not obj.data.uv_layers:
            continue
        obj.data.calc_loop_triangles()
        uv = obj.data.uv_layers.active.data
        samples, area_sum, uv_sum = [], 0.0, 0.0
        for triangle in obj.data.loop_triangles:
            points = [obj.matrix_world @ obj.data.vertices[v].co for v in triangle.vertices]
            area = (points[1] - points[0]).cross(points[2] - points[0]).length / 2
            uvpoints = [uv[i].uv for i in triangle.loops]
            du, dv = uvpoints[1] - uvpoints[0], uvpoints[2] - uvpoints[0]
            uvarea = abs(du.x * dv.y - du.y * dv.x) / 2
            if area <= 1e-9 or uvarea <= 1e-12:
                continue
            density = resolution * math.sqrt(uvarea / area)
            samples.append((density, area))
            area_sum += area
            uv_sum += uvarea
        samples.sort()
        def weighted(q):
            elapsed = 0
            for value, area in samples:
                elapsed += area
                if elapsed >= area_sum * q:
                    return value
            return samples[-1][0]
        rows.append({'object': obj.name, 'surface_area_m2': area_sum, 'atlas_fraction': uv_sum,
                     'density_texels_per_metre_area_weighted_p10': weighted(.1),
                     'density_texels_per_metre_area_weighted_median': weighted(.5),
                     'density_texels_per_metre_area_weighted_p90': weighted(.9)})
    results[label] = rows

comparisons = []
for shot in ('fixture', 'wall_front', 'wall_back'):
    arrays = []
    for folder in ('LR06Pilot', 'LR06AtlasPilot'):
        image = bpy.data.images.load(str(project / 'Library' / 'BuildValidation' / folder / (shot + '.png')),
                                     check_existing=False)
        image.colorspace_settings.name = 'Non-Color'
        assert tuple(image.size) == (1200, 800)
        values = np.empty(1200 * 800 * 4, dtype=np.float32)
        image.pixels.foreach_get(values)
        arrays.append(values.reshape((-1, 4))[:, :3])
    old, atlas = arrays
    foreground = np.max(np.abs(old - old[0]), axis=1) > (1 / 255)
    delta = np.abs(old[foreground] - atlas[foreground])
    comparisons.append({'shot': shot, 'foreground_pixels': int(foreground.sum()),
                        'decoded_rgb_mean_abs_difference': float(delta.mean()),
                        'decoded_rgb_p95_abs_difference': float(np.quantile(delta, .95)),
                        'decoded_rgb_max_abs_difference': float(delta.max())})

report = {'scope': 'three isolated Editor preview cameras with the same temporary lights; not source-lighting acceptance',
          'uv_density': results, 'unity_preview_comparisons': comparisons}
(root / 'NR12AtlasPilot' / 'PilotComparison.json').write_text(json.dumps(report, indent=2))
print('@@JSON@@' + json.dumps(report), flush=True)
