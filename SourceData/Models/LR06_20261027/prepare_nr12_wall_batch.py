"""Bake six source walls in their full Blender scene, without saving the source."""
import bpy
import bmesh
import hashlib
import json
import sys
from pathlib import Path
from mathutils import Vector

root = Path(__file__).resolve().parent
sys.path.insert(0, str(root))
from nr12_atlas_png import pack_metal_smoothness

source = root / 'Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend'
expected_sha = 'cdb003ad58e2c081076cc874eb9f1c4fdf3c216e056321257dabcf5d6f817054'
assert Path(bpy.data.filepath) == source and hashlib.sha256(source.read_bytes()).hexdigest() == expected_sha
out = root / 'NR12WallBatchPilot'
out.mkdir(exist_ok=True)
names = [
    'NR01_V5_WEST_Full_Height_Wall',
    'NR01_V5_EAST_Full_Height_Wall',
    'NR01_V5_SOUTH_LEFT_RETURN_Structural_Screen',
    'NR01_V5_NORTH_RIGHT_RETURN_Structural_Screen',
    'NR01_V5_SOUTH_Raised_Roof_End_Fascia',
    'NR01_V5_NORTH_Raised_Roof_End_Fascia',
]
objects = [bpy.data.objects[n] for n in names]
assert len(set(names)) == len(objects) and all(o.type == 'MESH' for o in objects)
assert all(len(o.material_slots) == 1 and o.material_slots[0].material and
           o.material_slots[0].material.name == 'NR01_V9_Wall_Warm_Utility_Coat' for o in objects)
assert all(not o.data.uv_layers and o.matrix_world.to_3x3().determinant() > 0 for o in objects)
scene = bpy.context.scene
depsgraph = bpy.context.evaluated_depsgraph_get()
mat = objects[0].material_slots[0].material.copy()
rows = []


def corrected_mesh(ob):
    evaluated = ob.evaluated_get(depsgraph)
    bounds = [ob.matrix_world @ Vector(corner) for corner in evaluated.bound_box]
    mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    visited, flipped, components = set(), 0, []
    for first in bm.faces:
        if first.index in visited:
            continue
        pending, faces, closed = [first], [], True
        while pending:
            face = pending.pop()
            if face.index in visited:
                continue
            visited.add(face.index)
            faces.append(face)
            for edge in face.edges:
                if not edge.is_manifold:
                    closed = False
                pending.extend(other for other in edge.link_faces if other.index not in visited)
        verts = {v for face in faces for v in face.verts}
        center = sum((v.co for v in verts), Vector()) / len(verts)
        volume = 0.0
        for face in faces:
            for i in range(1, len(face.verts) - 1):
                a, b, c = (face.verts[j].co - center for j in (0, i, i + 1))
                volume += a.dot(b.cross(c)) / 6.0
        assert closed and abs(volume) > 1e-9, (ob.name, 'open or ambiguous evaluated component')
        if volume < 0:
            bmesh.ops.reverse_faces(bm, faces=faces)
            flipped += 1
        components.append({'faces': len(faces), 'signed_volume_before': volume, 'reversed': volume < 0})
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return mesh, [[min(p[i] for p in bounds) for i in range(3)],
                  [max(p[i] for p in bounds) for i in range(3)]], components, flipped


# Keep the original source object identities and world positions for Object
# Info Random and world-space nodes. The source file is never saved.
for obj in objects:
    matrix = obj.matrix_world.copy()
    mesh, bounds, components, flipped = corrected_mesh(obj)
    obj.parent = None
    obj.matrix_world = matrix
    obj.data = mesh
    for modifier in obj.modifiers:
        modifier.show_render = False
        modifier.show_viewport = False
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    obj.data.uv_layers.new(name='LR06_WallBatchUV')
    rows.append({'object': obj.name, 'source_world_bounds': bounds,
                 'source_material': 'NR01_V9_Wall_Warm_Utility_Coat',
                 'evaluated_closed_components': components, 'reversed_component_count': flipped})

scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 8
scene.render.threads_mode = 'FIXED'
scene.render.threads = 4
scene.render.bake.margin = 20
scene.render.bake.use_selected_to_active = False
scene.render.bake.use_clear = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.image_settings.color_depth = '16'
bpy.ops.object.select_all(action='DESELECT')
for obj in objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
assert bpy.ops.uv.smart_project(island_margin=.012) == {'FINISHED'}
bpy.ops.object.mode_set(mode='OBJECT')

tree = mat.node_tree
bsdf = next(n for n in tree.nodes if n.type == 'BSDF_PRINCIPLED')
output = next(n for n in tree.nodes if n.type == 'OUTPUT_MATERIAL' and n.is_active_output)
for socket_name, tag, is_color in [('Base Color', 'basecolor', True),
                                   ('Roughness', 'roughness', False), ('Metallic', 'metallic', False)]:
    image = bpy.data.images.new('LR06_WallBatch_' + tag, 2048, 2048, alpha=True, float_buffer=True)
    image.colorspace_settings.name = 'sRGB' if is_color else 'Non-Color'
    surface = output.inputs['Surface']
    prior = [(link.from_socket, link.to_socket) for link in list(surface.links)]
    for link in list(surface.links):
        tree.links.remove(link)
    emit = tree.nodes.new('ShaderNodeEmission')
    value = bsdf.inputs[socket_name]
    if value.is_linked:
        tree.links.new(value.links[0].from_socket, emit.inputs['Color'])
    else:
        default = value.default_value
        emit.inputs['Color'].default_value = tuple(default) if hasattr(default, '__len__') else (default, default, default, 1)
    tree.links.new(emit.outputs['Emission'], surface)
    target = tree.nodes.new('ShaderNodeTexImage')
    target.image = image
    tree.nodes.active = target
    try:
        assert bpy.ops.object.bake(type='EMIT') == {'FINISHED'}
        image.filepath_raw = str(out / ('atlas_' + tag + '.png'))
        image.file_format = 'PNG'
        image.save()
    finally:
        tree.nodes.remove(target)
        tree.nodes.remove(emit)
        for a, b in prior:
            tree.links.new(a, b)
    print('WALL_BATCH_BAKED', tag, flush=True)

normal = bpy.data.images.new('LR06_WallBatch_Normal', 2048, 2048, alpha=True, float_buffer=True)
normal.colorspace_settings.name = 'Non-Color'
target = tree.nodes.new('ShaderNodeTexImage')
target.image = normal
tree.nodes.active = target
try:
    assert bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT') == {'FINISHED'}
    normal.filepath_raw = str(out / 'atlas_normal.exr')
    normal.file_format = 'OPEN_EXR'
    normal.save()
finally:
    tree.nodes.remove(target)
print('WALL_BATCH_BAKED normal', flush=True)
pack_metal_smoothness(out)

bpy.ops.object.select_all(action='DESELECT')
for obj in objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
fbx = out / 'NR12_wall_batch_pilot.fbx'
assert bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, object_types={'MESH'},
    use_mesh_modifiers=False, bake_anim=False, add_leaf_bones=False, axis_forward='-Z', axis_up='Y',
    global_scale=1, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
    use_space_transform=True, bake_space_transform=False, use_triangles=True, path_mode='STRIP') == {'FINISHED'}


def record(path, colorspace):
    return {'path': str(path.relative_to(root)), 'colorspace': colorspace, 'bytes': path.stat().st_size,
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}

textures = {tag: record(out / filename, colorspace) for tag, filename, colorspace in [
    ('basecolor', 'atlas_basecolor.png', 'sRGB'), ('roughness', 'atlas_roughness.png', 'Non-Color'),
    ('metallic', 'atlas_metallic.png', 'Non-Color'), ('mask', 'atlas_metallic_smoothness.png', 'Non-Color'),
    ('normal', 'atlas_normal.exr', 'Non-Color')]}
report = {'status': 'six_wall_trial_not_approved_for_import', 'source_sha256': expected_sha,
          'source_file_saved': False, 'scene_context': 'original Blender scene, original object identities',
          'texture_resolution': 2048, 'objects': rows, 'textures': textures,
          'fbx': record(fbx, None),
          'limitations': ['Visual and UV density comparison, Blender round-trip QA and Unity validation are required.',
                          'No source lights, collision, formal scene or interaction is included.']}
(out / 'PilotManifest.json').write_text(json.dumps(report, ensure_ascii=False, indent=2))
print('@@JSON@@' + json.dumps({'status': report['status'], 'objects': len(rows), 'fbx': report['fbx']}, ensure_ascii=False), flush=True)
