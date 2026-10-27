"""Build the Unity display plan from the complete hash-checked LR06 chunks."""
import hashlib
import json
import math
from collections import Counter, defaultdict
from pathlib import Path

root = Path(__file__).resolve().parent
out = root / 'FullPreviewStaging'
export = json.loads((out / 'ExportManifest.json').read_text())
materials = json.loads((out / 'MaterialPlan.json').read_text())
source_rows = json.loads((root / 'DisplayStaging' / 'ExportManifest.json').read_text())['objects']
lights = json.loads((root / 'LightConversionInventory.json').read_text())['lights']
assert export['status'] == 'full_geometry_staged_not_yet_Unity_validated'
seen, rows = set(), []
for row in source_rows:
    key = (row['source'], tuple(tuple(v) for v in row['matrix_world']), tuple(row['materials']))
    if key not in seen:
        seen.add(key)
        rows.append(row)
assert len(rows) == export['visible_instances'] == 30212


def spatial(row):
    bounds = row['source_world_bounds']
    center = [(bounds[0][i] + bounds[1][i]) / 2 for i in range(3)]
    return tuple(math.floor(center[i] / (80 if i < 2 else 50)) for i in range(3)) + (row['name'],)


parts = []
part_by_path = {part['path']: part for part in export['parts']}
for group in sorted({row['group'] for row in rows}):
    selected = sorted((row for row in rows if row['group'] == group), key=spatial)
    for start in range(0, len(selected), 500):
        chunk = selected[start:start + 500]
        rel = 'FullPreviewStaging/' + group + '_' + str(start // 500).zfill(2) + '.fbx'
        part = dict(part_by_path[rel])
        path = root / rel
        assert part['source_names'] == [row['source'] for row in chunk]
        assert path.stat().st_size == part['bytes'] and hashlib.sha256(path.read_bytes()).hexdigest() == part['sha256']
        part['source_world_bounds'] = [[min(row['source_world_bounds'][0][i] for row in chunk) for i in range(3)],
                                       [max(row['source_world_bounds'][1][i] for row in chunk) for i in range(3)]]
        del part['source_names']
        parts.append(part)
assert len(parts) == len(export['parts']) == 65
assert sum(p['instances'] for p in parts) == len(rows)
assert sum(p['triangles'] for p in parts) == export['triangles_before_join']

unique_textures = {}
for mat in materials['materials']:
    info = mat['base_texture']
    if not info:
        continue
    path = root / info['path']
    assert path.stat().st_size == info['bytes'] and hashlib.sha256(path.read_bytes()).hexdigest() == info['sha256']
    unique_textures[info['sha256']] = info

# Keep every original light in the separately saved source inventory. Only a
# small, shadow-free set of spatial proxies is enabled for the overview scene.
candidates = [light for light in lights if not light['hide_render'] and light['type'] != 'SUN' and
              light['energy_watts'] > 0 and light['group'] in {'LR01', 'LR02', 'NR01', 'W18'} and
              not any('PREVIEW' in name or 'NOT_GAME' in name for name in light['collections'])]
selected_groups = defaultdict(list)
transfer = [row for row in candidates if row['name'] in
            {'NR01_V5_SOUTH_TRANSFER_AREA', 'NR01_V5_NORTH_TRANSFER_AREA'}]
assert len(transfer) == 2
for light in candidates:
    if light in transfer:
        continue
    pos = [light['matrix_world'][i][3] for i in range(3)]
    key = (light['group'], *(math.floor(v / 80) for v in pos))
    selected_groups[key].append(light)

by_group = defaultdict(list)
for key, members in sorted(selected_groups.items()):
    by_group[key[0]].append(members)
budgets = {'LR01': 24, 'W18': 2, 'NR01': 2, 'LR02': 2}
chosen = [[light] for light in transfer]
for group, budget in budgets.items():
    groups = by_group[group]
    if len(groups) <= budget:
        chosen.extend(groups)
    elif budget == 1:
        chosen.append(groups[len(groups) // 2])
    else:
        chosen.extend(groups[round(i * (len(groups) - 1) / (budget - 1))] for i in range(budget))
assert len(chosen) <= 32
proxies = []
for index, members in enumerate(chosen):
    weights = [max(1, light['energy_watts']) for light in members]
    total = sum(weights)
    position = [sum(weight * light['matrix_world'][axis][3] for weight, light in zip(weights, members)) / total for axis in range(3)]
    color = [sum(weight * light['color'][axis] for weight, light in zip(weights, members)) / total for axis in range(3)]
    proxies.append({'name': 'LR06_Preview_WarmProxy_' + str(index).zfill(2), 'group': members[0]['group'],
                    'source_names': [light['name'] for light in members], 'source_position_blender': position,
                    'color': color, 'unity_intensity_illustrative': min(12, 1.5 + math.log10(total + 1) * 2),
                    'unity_range_metres': 35, 'shadows': False})

plan = {'status': 'awaiting_Unity_full_display_preview', 'source_sha256': export['source_sha256'],
        'visible_instances': export['visible_instances'], 'triangles': export['triangles_before_join'],
        'groups': export['groups'], 'parts': parts, 'materials': materials['materials'],
        'material_status_counts': materials['status_counts'], 'textures': list(unique_textures.values()),
        'source_light_count': len(lights), 'preview_light_proxies': proxies,
        'limits': ['Preview only; no gameplay, collision, switching, final lightmaps or probes.',
                   'Procedural materials and textures over 1024 pixels use approximations.',
                   'Warm proxy lights are illustrative; Blender watts are not Unity intensities.',
                   'The complete source light inventory is retained separately.']}
(out / 'UnityPlan.json').write_text(json.dumps(plan, ensure_ascii=False, indent=2))
print(json.dumps({'parts': len(parts), 'instances': len(rows), 'triangles': plan['triangles'],
                  'materials': len(plan['materials']), 'textures': len(plan['textures']),
                  'warm_proxies': len(proxies)}))
