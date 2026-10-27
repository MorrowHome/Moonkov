"""Compare six exported walls against source shaders in the original scene."""
import bpy
import hashlib
import json
import math
import numpy as np
from mathutils import Vector
from pathlib import Path

root = Path(__file__).resolve().parent
out = root / 'NR12WallBatchPilot'
manifest = json.loads((out / 'PilotManifest.json').read_text())
source = root / 'Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend'
assert Path(bpy.data.filepath) == source and hashlib.sha256(source.read_bytes()).hexdigest() == manifest['source_sha256']
fbx = root / manifest['fbx']['path']
assert hashlib.sha256(fbx.read_bytes()).hexdigest() == manifest['fbx']['sha256']
for info in manifest['textures'].values():
    path = root / info['path']
    assert path.stat().st_size == info['bytes'] and hashlib.sha256(path.read_bytes()).hexdigest() == info['sha256']

scene = bpy.context.scene
layer = bpy.context.view_layer
source_material = bpy.data.materials['NR01_V9_Wall_Warm_Utility_Coat']
for obj in scene.objects:
    obj.pass_index = 0
for row in manifest['objects']:
    obj = bpy.data.objects[row['object']]
    assert not obj.children, ('Original wall has children; preserve the scene hierarchy', obj.name)
    obj.hide_render = True
    obj.name = 'LR06_Compare_HiddenOriginal_' + row['object']

assert bpy.ops.import_scene.fbx(filepath=str(fbx), use_custom_normals=True, use_image_search=False) == {'FINISHED'}
walls = []
for row in manifest['objects']:
    obj = bpy.data.objects[row['object']]
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    bounds = [[min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)]]
    assert max(abs(bounds[j][i] - row['source_world_bounds'][j][i]) for i in range(3) for j in range(2)) < .003
    obj.data.materials.clear()
    obj.data.materials.append(source_material)
    obj.pass_index = 1
    walls.append(obj)

camera_data = bpy.data.cameras.new('LR06_Wall_Comparison_Camera')
camera = bpy.data.objects.new('LR06_Wall_Comparison_Camera', camera_data)
scene.collection.objects.link(camera)
camera.location = Vector((1993.8, 2457.6, 254.2))
look = Vector((1998.4, 2463.0, 255.3))
camera.rotation_euler = (look - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera_data.sensor_fit = 'VERTICAL'
camera_data.sensor_height = 24
camera_data.lens = 12 / math.tan(math.radians(48) / 2)
camera_data.clip_start, camera_data.clip_end = .05, 100
scene.camera = camera
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 16
scene.cycles.use_denoising = False
scene.cycles.seed = 1911
scene.render.threads_mode = 'FIXED'
scene.render.threads = 4
scene.render.resolution_x, scene.render.resolution_y = 720, 480
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.render.image_settings.color_depth = '8'
layer.use_pass_object_index = True

# A fresh in-memory compositor preserves the raw render for the comparison.
# It also permits a mask render without changing the source file.
group = bpy.data.node_groups.new('LR06_Comparison_Compositor', 'CompositorNodeTree')
group.interface.new_socket(name='Image', in_out='OUTPUT', socket_type='NodeSocketColor')
scene.compositing_node_group = group
render_layers = group.nodes.new('CompositorNodeRLayers')
render_layers.layer = layer.name
output = group.nodes.new('NodeGroupOutput')
image_link = group.links.new(render_layers.outputs['Image'], output.inputs['Image'])


def render(label):
    path = out / ('full_scene_' + label + '.png')
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    image = bpy.data.images.load(str(path), check_existing=False)
    if label == 'mask':
        image.colorspace_settings.name = 'Non-Color'
    pixels = np.empty(720 * 480 * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    print('FULL_SCENE_RENDERED', label, flush=True)
    return pixels.reshape((-1, 4))


reference = render('source')
baked = source_material.copy()
tree = baked.node_tree
bsdf = next(node for node in tree.nodes if node.type == 'BSDF_PRINCIPLED')


def texture(tag, input_name, colorspace):
    node = tree.nodes.new('ShaderNodeTexImage')
    node.image = bpy.data.images.load(str(root / manifest['textures'][tag]['path']), check_existing=False)
    node.image.colorspace_settings.name = colorspace
    target = bsdf.inputs[input_name]
    for link in list(target.links):
        tree.links.remove(link)
    tree.links.new(node.outputs['Color'], target)


texture('basecolor', 'Base Color', 'sRGB')
texture('roughness', 'Roughness', 'Non-Color')
texture('metallic', 'Metallic', 'Non-Color')
normal = tree.nodes.new('ShaderNodeTexImage')
normal.image = bpy.data.images.load(str(root / manifest['textures']['normal']['path']), check_existing=False)
normal.image.colorspace_settings.name = 'Non-Color'
normal_map = tree.nodes.new('ShaderNodeNormalMap')
normal_map.uv_map = 'LR06_WallBatchUV'
for link in list(bsdf.inputs['Normal'].links):
    tree.links.remove(link)
tree.links.new(normal.outputs['Color'], normal_map.inputs['Color'])
tree.links.new(normal_map.outputs['Normal'], bsdf.inputs['Normal'])
for obj in walls:
    obj.data.materials.clear()
    obj.data.materials.append(baked)
atlas = render('atlas')

group.links.remove(image_link)
group.links.new(render_layers.outputs['Object Index'], output.inputs['Image'])
scene.cycles.samples = 1
scene.view_settings.view_transform = 'Standard'
scene.view_settings.exposure = 0
scene.view_settings.gamma = 1
mask = render('mask')[:, 0] > .5
assert int(mask.sum()) > 100, 'Camera does not show enough of the six test walls'
delta = np.abs(reference[mask, :3] - atlas[mask, :3])
report = {'scope': 'same corrected FBX wall geometry in original Blender scene, source nodes versus atlas; source lights, 16 samples',
          'camera_position': list(camera.location), 'camera_target': list(look),
          'samples': 16, 'resolution': [720, 480], 'visible_wall_mask_pixels': int(mask.sum()),
          'absolute_decoded_png_rgb_mean': float(delta.mean()),
          'absolute_decoded_png_rgb_p95': float(np.quantile(delta, .95)),
          'source_render': 'NR12WallBatchPilot/full_scene_source.png',
          'baked_render': 'NR12WallBatchPilot/full_scene_atlas.png',
          'mask_render': 'NR12WallBatchPilot/full_scene_mask.png',
          'limitations': ['Same corrected FBX geometry in both renders; not a geometry comparison.',
                          'Noisy 16-sample Blender comparison; not Unity or final lighting acceptance.',
                          'Original compositor is bypassed for both images. Source file is not saved.']}
(out / 'FullSceneMaterialComparison.json').write_text(json.dumps(report, indent=2))
print('@@JSON@@' + json.dumps(report), flush=True)
