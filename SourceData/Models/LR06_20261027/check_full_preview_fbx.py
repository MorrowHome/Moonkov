"""Check all staged FBX geometry and material names without launching Blender."""
import importlib.util
import json
import sys
import types
from pathlib import Path

root = Path(__file__).resolve().parent
out = root / 'FullPreviewStaging'
plan = json.loads((out / 'UnityPlan.json').read_text())
addon = Path('/Applications/Blender.app/Contents/Resources/5.2/scripts/addons_core/io_scene_fbx')
assert addon.is_dir(), 'Installed Blender FBX parser is unavailable'
package = types.ModuleType('lr06_fbx_parser')
package.__path__ = [str(addon)]
sys.modules[package.__name__] = package
for name in ('data_types', 'fbx_utils_threading', 'parse_fbx'):
    spec = importlib.util.spec_from_file_location('lr06_fbx_parser.' + name, addon / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
sys.modules['lr06_fbx_parser.fbx_utils_threading']._MULTITHREADING_ENABLED = False
parser = sys.modules['lr06_fbx_parser.parse_fbx']
checks = []
for part in plan['parts']:
    document, version = parser.parse(str(root / part['path']))
    objects = next(node for node in document.elems if node.id == b'Objects')
    meshes = [node for node in objects.elems if node.id == b'Geometry' and node.props[2] == b'Mesh']
    assert len(meshes) == 1, (part['path'], len(meshes))
    mesh = meshes[0]
    vertices = next(node for node in mesh.elems if node.id == b'Vertices').props[0]
    indices = next(node for node in mesh.elems if node.id == b'PolygonVertexIndex').props[0]
    assert len(vertices) // 3 == part['vertices'] and len(indices) // 3 == part['triangles']
    assert len(indices) % 3 == 0 and all(i >= 0 for i in indices[0::3]) and \
        all(i >= 0 for i in indices[1::3]) and all(i < 0 for i in indices[2::3])
    assert all((i if i >= 0 else -i - 1) < len(vertices) // 3 for i in indices)
    actual_materials = {node.props[1].split(b'\x00\x01')[0].decode('utf-8')
                        for node in objects.elems if node.id == b'Material'}
    expected_materials = {name for name in part['materials'] if name}
    assert actual_materials == expected_materials, (part['path'], actual_materials ^ expected_materials)
    checks.append({'path': part['path'], 'fbx_version': version, 'vertices': len(vertices) // 3,
                   'triangles': len(indices) // 3, 'materials': len(actual_materials)})
report = {'scope': 'binary FBX structure, triangle indices and material names; not Unity visual acceptance',
          'parts': len(checks), 'triangles': sum(row['triangles'] for row in checks), 'checks': checks}
(out / 'FBXIntegrity.json').write_text(json.dumps(report, indent=2))
print(json.dumps({'parts': report['parts'], 'triangles': report['triangles']}))
