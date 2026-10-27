"""Compare Unity triangle-count differences with degenerate FBX faces."""
import importlib.util
import json
import sys
import types
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parent
project = root.parents[2]
out = root / 'FullPreviewStaging'
plan = json.loads((out / 'UnityPlan.json').read_text())
unity = json.loads((project / 'Library/BuildValidation/LR06FullPreview/UnityFullPreview.json').read_text())
diffs = {Path(row['modelPath']).name: row for row in unity['triangleDifferences']}
addon = Path('/Applications/Blender.app/Contents/Resources/5.2/scripts/addons_core/io_scene_fbx')
package = types.ModuleType('lr06_triangle_parser')
package.__path__ = [str(addon)]
sys.modules[package.__name__] = package
for name in ('data_types', 'fbx_utils_threading', 'parse_fbx'):
    spec = importlib.util.spec_from_file_location('lr06_triangle_parser.' + name, addon / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
sys.modules['lr06_triangle_parser.fbx_utils_threading']._MULTITHREADING_ENABLED = False
parser = sys.modules['lr06_triangle_parser.parse_fbx']
rows = []
for part in plan['parts']:
    diff = diffs.get(Path(part['path']).name)
    if not diff:
        continue
    document, _ = parser.parse(str(root / part['path']))
    objects = next(n for n in document.elems if n.id == b'Objects')
    mesh = next(n for n in objects.elems if n.id == b'Geometry' and n.props[2] == b'Mesh')
    vertices = np.asarray(next(n for n in mesh.elems if n.id == b'Vertices').props[0], dtype=np.float64).reshape((-1, 3))
    indices = np.asarray(next(n for n in mesh.elems if n.id == b'PolygonVertexIndex').props[0], dtype=np.int64)
    indices = np.where(indices < 0, -indices - 1, indices).reshape((-1, 3))
    tri = vertices[indices]
    cross = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    twice_area_sq = np.einsum('ij,ij->i', cross, cross)
    rows.append({'part': part['path'], 'unity_removed': diff['expectedTriangles'] - diff['actualTriangles'],
                 'exact_zero_area': int(np.count_nonzero(twice_area_sq == 0)),
                 'area_under_1e_8_m2': int(np.count_nonzero(twice_area_sq < 4e-16)),
                 'area_under_1e_6_m2': int(np.count_nonzero(twice_area_sq < 4e-12))})
report = {'scope': 'FBX triangle areas versus Unity importer count differences; association is not proof of removal cause',
          'parts_with_count_differences': len(rows), 'unity_removed': sum(r['unity_removed'] for r in rows),
          'exact_zero_area': sum(r['exact_zero_area'] for r in rows),
          'area_under_1e_8_m2': sum(r['area_under_1e_8_m2'] for r in rows),
          'area_under_1e_6_m2': sum(r['area_under_1e_6_m2'] for r in rows), 'parts': rows}
(out / 'UnityTriangleDifference.json').write_text(json.dumps(report, indent=2))
print(json.dumps({k: v for k, v in report.items() if k != 'parts'}))
