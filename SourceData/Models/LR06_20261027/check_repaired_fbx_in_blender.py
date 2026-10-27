"""Read original and repaired FBX in Blender and compare geometry and slots."""
import bpy
import hashlib
import json
import struct
import sys
from pathlib import Path

base = Path(__file__).resolve().parent
project = base.parents[2]
part = int(sys.argv[sys.argv.index('--') + 1])
name = 'Map_%02d.fbx' % part
original = project / 'SourceData/Models/LR06_20261027/FullPreviewStaging' / name
candidate = project / 'Library/BuildValidation/LR06UVLighting/RepairedFBX' / name


def inspect(path):
    before = set(bpy.context.scene.objects)
    assert bpy.ops.import_scene.fbx(filepath=str(path), use_custom_normals=True) == {'FINISHED'}
    added = [obj for obj in bpy.context.scene.objects if obj not in before]
    meshes = [obj for obj in added if obj.type == 'MESH']
    assert len(meshes) == 1, (path, len(meshes))
    obj = meshes[0]
    mesh = obj.data
    mesh.calc_loop_triangles()
    digest = hashlib.sha256()
    for vertex in mesh.vertices:
        digest.update(struct.pack('<3f', *vertex.co))
    for triangle in mesh.loop_triangles:
        digest.update(struct.pack('<4I', *triangle.vertices, triangle.material_index))
    materials = [slot.material.name.split('.')[0] if slot.material else None for slot in obj.material_slots]
    return {'mesh': obj.name, 'vertices': len(mesh.vertices), 'triangles': len(mesh.loop_triangles),
            'geometry_and_material_index_sha256': digest.hexdigest(), 'material_slots': materials,
            'uv_layers': [layer.name for layer in mesh.uv_layers]}


a, b = inspect(original), inspect(candidate)
assert a['vertices'] == b['vertices'] and a['triangles'] == b['triangles']
assert a['geometry_and_material_index_sha256'] == b['geometry_and_material_index_sha256']
assert a['material_slots'] == b['material_slots'] and a['uv_layers'] == b['uv_layers']
result = {'status': 'blender_fbx_geometry_and_slots_match', 'part': part, 'original': a, 'repaired': b}
out = project / 'Library/BuildValidation/LR06UVLighting/RepairedFBX' / ('blender_check_%02d.json' % part)
out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
print('@@JSON@@' + json.dumps(result, ensure_ascii=False), flush=True)
