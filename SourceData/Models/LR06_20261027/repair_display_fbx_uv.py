"""Repair only collapsed UV0 on two R8 materials in LR06 display FBX copies.

The original FBX, author blend and all geometry remain unchanged. Output is
written under Library first and must be verified before replacing display FBX.
Uses the installed Blender FBX parser/writer; it does not launch Blender.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import sys
import types
from pathlib import Path

base = Path(__file__).resolve().parent
project = base.parents[2]
addon = Path('/Applications/Blender.app/Contents/Resources/5.2/scripts/addons_core/io_scene_fbx')
package = types.ModuleType('lr06_uv_fbx')
package.__path__ = [str(addon)]
sys.modules[package.__name__] = package
for module_name in ('data_types', 'fbx_utils_threading', 'parse_fbx', 'encode_bin'):
    spec = importlib.util.spec_from_file_location(package.__name__ + '.' + module_name, addon / (module_name + '.py'))
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
sys.modules[package.__name__ + '.fbx_utils_threading']._MULTITHREADING_ENABLED = False
parser = sys.modules[package.__name__ + '.parse_fbx']
encoder = sys.modules[package.__name__ + '.encode_bin']
methods = {'B': 'add_bool', 'C': 'add_char', 'Z': 'add_int8', 'Y': 'add_int16', 'I': 'add_int32',
           'L': 'add_int64', 'F': 'add_float32', 'D': 'add_float64', 'R': 'add_bytes', 'S': 'add_string',
           'i': 'add_int32_array', 'l': 'add_int64_array', 'f': 'add_float32_array', 'd': 'add_float64_array',
           'b': 'add_bool_array', 'c': 'add_byte_array'}
target_materials = {'R8_OffWhite_Ceramic_Coat', 'R8_Rough_Exposed_Steel'}
source_plan = json.loads((base / 'FullPreviewStaging/UnityPlan.json').read_text())
output = project / 'Library/BuildValidation/LR06UVLighting/RepairedFBX'
output.mkdir(parents=True, exist_ok=True)


def child(node, name):
    return next(item for item in node.elems if item.id == name)


def convert(node):
    result = encoder.FBXElem(node.id)
    for kind, value in zip(node.props_type, node.props):
        getattr(result, methods[chr(kind)])(value)
    result.elems.extend(convert(item) for item in node.elems)
    return result


def compare_except_uv0(a, b, within_uv0=False):
    assert a.id == b.id and a.props_type == b.props_type and len(a.elems) == len(b.elems), a.id
    skip = within_uv0 and a.id in (b'UV', b'UVIndex')
    if not skip:
        assert a.props == b.props, a.id
    next_uv0 = within_uv0 or (a.id == b'LayerElementUV' and a.props[0] == 0)
    for old, new in zip(a.elems, b.elems):
        compare_except_uv0(old, new, next_uv0)


def sub(a, b): return tuple(a[i] - b[i] for i in range(3))
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def dot(a, b): return sum(a[i]*b[i] for i in range(3))
def normalize(v):
    length = math.sqrt(dot(v, v))
    return tuple(x / length for x in v), length


def repair(part):
    name = 'Map_%02d.fbx' % part
    expected = next(row for row in source_plan['parts'] if Path(row['path']).name == name)
    # Always start from the untouched staged export. This makes the repair
    # reproducible after the candidate has been installed in the display scene.
    source = base / expected['path']
    target = project / 'Assets/MoonEnvironment/Variants/LR06FullPreview/Models' / name
    original_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    assert original_hash == expected['sha256'], ('Display FBX differs from baseline', name)
    document, version = parser.parse(str(source))
    objects = child(document, b'Objects')
    geometry = next(item for item in objects.elems if item.id == b'Geometry' and item.props[2] == b'Mesh')
    model = next(item for item in objects.elems if item.id == b'Model')
    material_ids = {item.props[0]: item.props[1].split(b'\0\1')[0].decode('utf-8')
                    for item in objects.elems if item.id == b'Material'}
    connections = child(document, b'Connections')
    names = [material_ids[item.props[1]] for item in connections.elems if len(item.props) >= 3 and
             item.props[1] in material_ids and item.props[2] == model.props[0]]
    material_layer = next(item for item in geometry.elems if item.id == b'LayerElementMaterial')
    material_indices = child(material_layer, b'Materials').props[0]
    uv_layer = next(item for item in geometry.elems if item.id == b'LayerElementUV' and item.props[0] == 0)
    assert child(uv_layer, b'MappingInformationType').props[0] == b'ByPolygonVertex'
    assert child(uv_layer, b'ReferenceInformationType').props[0] == b'IndexToDirect'
    uv = child(uv_layer, b'UV').props[0]
    uv_indices = child(uv_layer, b'UVIndex').props[0]
    vertices = child(geometry, b'Vertices').props[0]
    polygons = child(geometry, b'PolygonVertexIndex').props[0]
    assert len(polygons) == 3*len(material_indices) == len(uv_indices)
    count, zero_geometry = {}, 0
    measured = []
    # Existing valid R8 UVs repeat roughly 5/6 times per metre. Match that
    # density while retaining tileable UVs and untouched original UV islands.
    scale = 5.0 / 6.0
    origin = (2000.0, 2000.0, 250.0)
    for triangle, material_index in enumerate(material_indices):
        material = names[material_index]
        if material not in target_materials:
            continue
        uvi = [uv_indices[3*triangle + k] for k in range(3)]
        old_uv = [(uv[2*i], uv[2*i+1]) for i in uvi]
        area = abs((old_uv[1][0]-old_uv[0][0])*(old_uv[2][1]-old_uv[0][1]) -
                   (old_uv[1][1]-old_uv[0][1])*(old_uv[2][0]-old_uv[0][0]))
        if area >= 1e-12:
            continue
        # These are genuinely collapsed coordinates, not narrow authored islands.
        assert all(abs(v) < 1e-12 for point in old_uv for v in point), (name, material, old_uv)
        indices = [x if x >= 0 else -x-1 for x in polygons[3*triangle:3*triangle+3]]
        p = [tuple(vertices[3*i+k] for k in range(3)) for i in indices]
        normal_raw = cross(sub(p[1], p[0]), sub(p[2], p[0]))
        length = math.sqrt(dot(normal_raw, normal_raw))
        if length < 1e-10:
            zero_geometry += 1
            continue
        normal = tuple(x / length for x in normal_raw)
        major = max(range(3), key=lambda i: abs(normal[i]))
        if normal[major] < 0:
            normal = tuple(-x for x in normal)
        reference = (0.0, 0.0, 1.0) if abs(normal[2]) < .95 else (0.0, 1.0, 0.0)
        tangent, _ = normalize(cross(reference, normal))
        bitangent = cross(normal, tangent)
        new_uv = [(dot(sub(point, origin), tangent)*scale, dot(sub(point, origin), bitangent)*scale) for point in p]
        first = len(uv) // 2
        for u, v in new_uv: uv.extend((u, v))
        for k in range(3): uv_indices[3*triangle+k] = first+k
        uv_area2 = abs((new_uv[1][0]-new_uv[0][0])*(new_uv[2][1]-new_uv[0][1]) -
                       (new_uv[1][1]-new_uv[0][1])*(new_uv[2][0]-new_uv[0][0]))
        measured.append(1024 * math.sqrt(uv_area2 / length))
        count[material] = count.get(material, 0) + 1
    assert count, ('No collapsed target UVs found', name)
    dest = output / name
    encoder.write(str(dest), convert(document), version)
    original, _ = parser.parse(str(source))
    written, written_version = parser.parse(str(dest))
    assert written_version == version
    compare_except_uv0(original, written)
    assert all(abs(value - 1024*scale) < .001 for value in measured)
    return {'path': str(target.relative_to(project)), 'source': str(source.relative_to(project)),
            'candidate': str(dest.relative_to(project)),
            'original_sha256': original_hash, 'candidate_sha256': hashlib.sha256(dest.read_bytes()).hexdigest(),
            'materials_repaired_triangles': count, 'zero_geometry_skipped': zero_geometry,
            'target_density_texels_per_metre_at_1024': 1024*scale,
            'other_fbx_fields_identical': True, 'source_blend_changed': False}


if __name__ == '__main__':
    cli = argparse.ArgumentParser()
    cli.add_argument('--parts', nargs='+', type=int, required=True, choices=(22, 23, 27, 29, 30, 31))
    args = cli.parse_args()
    result = {'scope': 'display-only FBX UV0 repair; no geometry or material-slot changes',
              'parts': [repair(part) for part in args.parts]}
    path = output / ('repair_' + '_'.join('%02d' % x for x in args.parts) + '.json')
    path.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(result, ensure_ascii=False))
