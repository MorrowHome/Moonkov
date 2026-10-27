"""Check all six wall atlas samples and measure their UV texel density."""
import bpy
import json
import math
import sys
from pathlib import Path

root = Path(__file__).resolve().parent
sys.path.insert(0, str(root))
from nr12_atlas_png import read_rgba16

out = root / 'NR12WallBatchPilot'
manifest = json.loads((out / 'PilotManifest.json').read_text())
assert bpy.ops.import_scene.fbx(filepath=str(root / manifest['fbx']['path']), use_image_search=False) == {'FINISHED'}
w, h, metal = read_rgba16(out / 'atlas_metallic.png')
bw, bh, base = read_rgba16(out / 'atlas_basecolor.png')
assert (w, h) == (bw, bh) == (2048, 2048)
rows = []
for entry in manifest['objects']:
    obj = bpy.data.objects[entry['object']]
    mesh = obj.data
    mesh.calc_loop_triangles()
    uv = mesh.uv_layers.active.data
    samples, area_sum, uv_sum = [], 0.0, 0.0
    for tri in mesh.loop_triangles:
        points = [obj.matrix_world @ mesh.vertices[v].co for v in tri.vertices]
        area = (points[1] - points[0]).cross(points[2] - points[0]).length / 2
        coords = [uv[i].uv for i in tri.loops]
        du, dv = coords[1] - coords[0], coords[2] - coords[0]
        uvarea = abs(du.x * dv.y - du.y * dv.x) / 2
        if area <= 1e-9 or uvarea <= 1e-12:
            continue
        samples.append((w * math.sqrt(uvarea / area), area, uvarea, coords))
        area_sum += area
        uv_sum += uvarea
    samples.sort(key=lambda s: s[0])
    def weighted(q):
        elapsed = 0
        for value, area, _, _ in samples:
            elapsed += area
            if elapsed >= area_sum * q:
                return value
        return samples[-1][0]
    # Sample a centroid inside a large UV triangle, not padding or its seam.
    triangle = max(samples, key=lambda s: s[2])[3]
    u = sum(point.x for point in triangle) / 3
    v = sum(point.y for point in triangle) / 3
    x = min(w - 1, max(0, int(u * w)))
    y = h - 1 - min(h - 1, max(0, int(v * h)))
    index = (y * w + x) * 4
    metallic = metal[index] / 65535
    assert abs(metallic - .015) < .002, (obj.name, metallic, u, v)
    rows.append({'object': obj.name, 'uv_sample': [u, v], 'metallic_sample': metallic,
                 'basecolor_sample_srgb': [base[index + i] / 65535 for i in range(3)],
                 'surface_area_m2': area_sum, 'atlas_fraction': uv_sum,
                 'density_texels_per_metre_area_weighted_p10': weighted(.1),
                 'density_texels_per_metre_area_weighted_median': weighted(.5)})
report = {'scope': 'six imported wall meshes; centroid samples and area-weighted texel density',
          'objects': rows}
(out / 'AtlasSamples.json').write_text(json.dumps(report, indent=2))
print('@@JSON@@' + json.dumps(report), flush=True)
