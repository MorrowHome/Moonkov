"""Small Blender-only regression for preserving source polygon material slots."""
import bpy
import pathlib
import sys
import json
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT))
from prepared_material_bindings import replace_materials_preserving_indices

mesh = bpy.data.objects['Cube'].data.copy()
source = [bpy.data.materials.new('OriginalGrey'), bpy.data.materials.new('OriginalWarm')]
mesh.materials.clear()
for material in source:
    mesh.materials.append(material)
expected = np.array([0, 1, 0, 1, 1, 0], dtype=np.int32)
mesh.polygons.foreach_set('material_index', expected)
uv = np.array([list(x.uv) for x in mesh.uv_layers.active.data])
vertices = np.array([list(v.co) for v in mesh.vertices])
converted = [bpy.data.materials.new('FrozenGrey'), bpy.data.materials.new('FrozenWarm')]
replace_materials_preserving_indices(mesh, converted)
actual = np.empty(len(mesh.polygons), dtype=np.int32)
mesh.polygons.foreach_get('material_index', actual)
assert np.array_equal(expected, actual)
assert list(mesh.materials) == converted
assert np.array_equal(uv, np.array([list(x.uv) for x in mesh.uv_layers.active.data]))
assert np.array_equal(vertices, np.array([list(v.co) for v in mesh.vertices]))
try:
    replace_materials_preserving_indices(mesh, converted[:1])
    raise AssertionError('Invalid slot range accepted')
except ValueError:
    pass
assert list(mesh.materials) == converted
report = {'polygonIndicesPreserved': True, 'slotOrderPreserved': True,
          'uvsAndVerticesUnchanged': True, 'invalidSlotsRejectedBeforeMutation': True,
          'expected': expected.tolist(), 'actual': actual.tolist(), 'mapOpened': False}
out = ROOT.parents[2] / 'LocalData/SourceFidelity20261028/MaterialBindingRegression.json'
out.write_text(json.dumps(report, indent=2))
print('PREPARED_MATERIAL_BINDINGS_PASS', json.dumps(report), flush=True)
