"""Calibrate the existing LR06 display lights using actual source fixtures."""
import json
import math
from pathlib import Path

base = Path(__file__).resolve().parent
original = json.loads((base / 'FullPreviewStaging/UnityPlan.json').read_text())
inventory = json.loads((base / 'LightConversionInventory.json').read_text())
sources = {source['name']: source for source in inventory['lights']}


def position(source):
    return [source['matrix_world'][i][3] for i in range(3)]


rows = []
for old in original['preview_light_proxies']:
    candidates = [sources[name] for name in old['source_names']]
    windows = [source for source in candidates if '_WINDOW_' in source['name']]
    source = min(windows or candidates, key=lambda s: math.dist(position(s), old['source_position_blender']))
    matrix = source['matrix_world']
    direction = [-matrix[i][2] for i in range(3)]
    if '_WINDOW_' in source['name']:
        horizontal = math.hypot(direction[0], direction[1])
        assert horizontal > .01
        p = [matrix[0][3] + 3.5 * direction[0] / horizontal,
             matrix[1][3] + 3.5 * direction[1] / horizontal, matrix[2][3] - .3]
        category, intensity, radius = 'window', 35.0, 25.0
    else:
        offset = .25 if old['group'] in ('NR01', 'W18') else 1.0
        p = [matrix[i][3] + direction[i] * offset for i in range(3)]
        if old['group'] == 'NR01': category, intensity, radius = 'corridor', 14.0, 22.0
        elif old['group'] == 'W18': category, intensity, radius = 'work_area', 18.0, 22.0
        elif old['group'] == 'LR02' or any(word in source['name'] for word in ('CORE_LEVEL', 'CASING', 'SHAFT')):
            category, intensity, radius = 'platform', 40.0, 30.0
        else: category, intensity, radius = 'entry_or_transfer', 22.0, 25.0
    rows.append({'name': old['name'], 'group': old['group'], 'category': category,
                 'source_name': source['name'], 'original_source_names': old['source_names'],
                 'original_position_blender': old['source_position_blender'],
                 'position_unity': [p[0], p[2], p[1]], 'source_color_linear': source['color'],
                 'intensity': intensity, 'range_metres': radius, 'shadows': False})

sun = sources['B_PREVIEW_SUN_NOT_GAME_LIGHT']
matrix = sun['matrix_world']
direction = [-matrix[i][2] for i in range(3)]
assert len(rows) == 31 and len({r['name'] for r in rows}) == 31
result = {'scope': 'Independent display only; source fixture positions and sun direction are approximate, not game light conversion.',
          'source_sun': {'name': sun['name'], 'direction_unity': [direction[0], direction[2], direction[1]],
                         'color_linear': sun['color'], 'unity_intensity': .8, 'soft_shadows': True},
          'point_lights': rows}
(base / 'DisplayLightCalibrationPlan.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
print('Existing proxies:', len(rows), 'source fixtures:', len({row['source_name'] for row in rows}))
for row in rows: print(row['name'], row['category'], row['source_name'], row['intensity'])
