"""Validate the saved atlas PNG channels, including their actual alpha handling."""
import json
import sys
from pathlib import Path
from nr12_atlas_png import read_rgba16


root = Path(__file__).resolve().parent / (sys.argv[1] if len(sys.argv) > 1 else 'NR12AtlasPilot')
w, h, rough = read_rgba16(root / 'atlas_roughness.png')
mw, mh, metal = read_rgba16(root / 'atlas_metallic.png')
kw, kh, mask = read_rgba16(root / 'atlas_metallic_smoothness.png')
assert (w, h) == (mw, mh) == (kw, kh)
count = metal_max = smooth_max = metal_sum = smooth_sum = 0
for i in range(0, len(mask), 4):
    if rough[i + 3] < 65000 or metal[i + 3] < 65000:
        continue
    metal_error = abs(mask[i] - metal[i])
    smooth_error = abs(mask[i + 3] - (65535 - rough[i]))
    count += 1
    metal_max, smooth_max = max(metal_max, metal_error), max(smooth_max, smooth_error)
    metal_sum += metal_error
    smooth_sum += smooth_error
report = {'dimensions': [w, h], 'compared_pixels': count,
          'metallic_max_error_16bit': metal_max, 'smoothness_max_error_16bit': smooth_max,
          'metallic_mean_error_16bit': metal_sum / count if count else None,
          'smoothness_mean_error_16bit': smooth_sum / count if count else None}
(root / 'MaskValidation.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report))
assert count and metal_max <= 2 and smooth_max <= 2, report
