"""Compare the source object's Blender Object Info Random with its FBX copy."""
import bpy
import hashlib
import json
import numpy as np
from pathlib import Path

root = Path(__file__).resolve().parent
source = root / 'Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend'
assert Path(bpy.data.filepath) == source
assert hashlib.sha256(source.read_bytes()).hexdigest() == 'cdb003ad58e2c081076cc874eb9f1c4fdf3c216e056321257dabcf5d6f817054'
name = 'NR01_V5_EAST_Full_Height_Wall'
original = bpy.data.objects[name]
depsgraph = bpy.context.evaluated_depsgraph_get()
matrix = original.matrix_world.copy()
mesh = bpy.data.meshes.new_from_object(original.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
scene = bpy.data.scenes.new('LR06_Random_Validation')
scene.collection.objects.link(original)
original.parent = None
original.matrix_world = matrix
original.data = mesh
for modifier in original.modifiers:
    modifier.show_render = False
    modifier.show_viewport = False
bpy.context.window.scene = scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 1
scene.render.threads_mode = 'FIXED'
scene.render.threads = 4
scene.render.bake.margin = 2


def bake_random(obj, create_uv):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if create_uv:
        assert not obj.data.uv_layers
        obj.data.uv_layers.new(name='TemporaryRandomValidation')
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        assert bpy.ops.uv.smart_project(island_margin=.02) == {'FINISHED'}
        bpy.ops.object.mode_set(mode='OBJECT')
    mat = bpy.data.materials.new('LR06_Random_Validation_Material')
    mat.use_nodes = True
    tree = mat.node_tree
    tree.nodes.clear()
    info = tree.nodes.new('ShaderNodeObjectInfo')
    emit = tree.nodes.new('ShaderNodeEmission')
    output = tree.nodes.new('ShaderNodeOutputMaterial')
    tree.links.new(info.outputs['Random'], emit.inputs['Color'])
    tree.links.new(emit.outputs['Emission'], output.inputs['Surface'])
    image = bpy.data.images.new('LR06_Random_Validation', 64, 64, alpha=True, float_buffer=True)
    image.colorspace_settings.name = 'Non-Color'
    target = tree.nodes.new('ShaderNodeTexImage')
    target.image = image
    tree.nodes.active = target
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    assert bpy.ops.object.bake(type='EMIT') == {'FINISHED'}
    values = np.empty(64 * 64 * 4, dtype=np.float32)
    image.pixels.foreach_get(values)
    pixels = values.reshape((-1, 4))
    # New float images have opaque black in the unused UV space. The original
    # and atlas UV layouts cover different fractions, so an image-wide mean
    # would compare UV occupancy instead of Object Info Random.
    covered = pixels[pixels[:, 0] > .00001, 0]
    assert len(covered) > 100
    return {'nonzero_pixels': len(covered), 'median': float(np.median(covered)),
            'max': float(covered.max())}


source_result = bake_random(original, True)
scene.collection.objects.unlink(original)
for objname in (name, 'NR01_V5_SOUTH_TRANSFER_Worklight_Housing', 'NR01_V5_SOUTH_TRANSFER_Worklight_Diffuser'):
    bpy.data.objects[objname].name = 'LR06_Random_Original_' + objname
assert bpy.ops.import_scene.fbx(filepath=str(root / 'NR12AtlasPilot' / 'NR12_corridor_atlas_pilot.fbx'),
                                use_image_search=False) == {'FINISHED'}
copy = bpy.data.objects[name]
assert copy.name == name
copy_result = bake_random(copy, False)
difference = abs(source_result['median'] - copy_result['median'])
report = {'scope': 'Object Info Random only; same-named source wall versus isolated FBX copy',
          'object': name, 'source': source_result, 'fbx_copy': copy_result,
          'absolute_difference': difference, 'source_file_saved': False}
(root / 'NR12AtlasPilot' / 'ObjectRandomValidation.json').write_text(json.dumps(report, indent=2))
print('@@JSON@@' + json.dumps(report), flush=True)
assert difference < .00001, report
