"""Run with Blender --background --factory-startup --python-exit-code 10.

Small independent scenes test mixed-membership rejection and actual direct
illumination, without opening the map or touching production assets.
"""
import bpy
import json
import pathlib
import sys
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT))
from prepared_light_transport import PreparedLightTransport, RAY_FLAGS
from cycles_device import configure_cycles_device

OUT = ROOT.parents[2] / 'LocalData/SourceFidelity20261028/TransportRegression'
OUT.mkdir(parents=True, exist_ok=True)
source = bpy.data.scenes.new('TransportSourceTest')
stage = bpy.data.scenes.new('TransportPreparedTest')
bpy.context.window.scene = source
world = bpy.data.worlds.new('TransportZeroWorld')
world.use_nodes = True
world.node_tree.nodes.get('Background').inputs['Strength'].default_value = 0
source.world = stage.world = world
material = bpy.data.materials.new('TransportWhiteDiffuse')
material.use_nodes = True
tree = material.node_tree
tree.nodes.clear()
diffuse = tree.nodes.new('ShaderNodeBsdfDiffuse')
diffuse.inputs['Color'].default_value = (1, 1, 1, 1)
output = tree.nodes.new('ShaderNodeOutputMaterial')
tree.links.new(diffuse.outputs[0], output.inputs['Surface'])
objects = []
for name, x in [('Included', -2), ('Excluded', 0), ('Outside', 2)]:
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([(x-.75, -.75, 0), (x+.75, -.75, 0), (x+.75, .75, 0), (x-.75, .75, 0)], [], [(0, 1, 2, 3)])
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh)
    source.collection.objects.link(obj)
    objects.append(obj)
receivers = bpy.data.collections.new('TestReceiverRoot')
receivers.objects.link(objects[0])
excluded = bpy.data.collections.new('TestExcludedChild')
excluded.objects.link(objects[1])
excluded.collection_objects[0].light_linking.link_state = 'INCLUDE'
receivers.children.link(excluded)
receivers.collection_children[0].light_linking.link_state = 'EXCLUDE'
data = bpy.data.lights.new('TransportArea', 'AREA')
data.energy = 100
data.shape = 'RECTANGLE'
data.size = data.size_y = 1.5
light = bpy.data.objects.new('TransportArea', data)
source.collection.objects.link(light)
light.location = (-2, 0, 3)
light.visible_camera = False
light.visible_glossy = False
light.light_linking.receiver_collection = receivers
bpy.context.view_layer.update()
transport = PreparedLightTransport(source, stage, bpy.context.view_layer)
copy = transport.copy_light(light, stage)
assert all(getattr(light, f) == getattr(copy, f) for f in RAY_FLAGS)
assert len({transport.signature(o.name) for o in objects}) == 3
try:
    transport.bind_receiver(objects[0], [objects[0].name, objects[1].name])
    raise AssertionError('Mixed receivers were silently merged')
except ValueError:
    pass
for obj in objects:
    prepared = bpy.data.objects.new('Prepared_' + obj.name, obj.data.copy())
    stage.collection.objects.link(prepared)
    transport.bind_receiver(prepared, [obj.name])
assert len(copy.light_linking.receiver_collection.all_objects) == 2
child = copy.light_linking.receiver_collection.children[0]
assert child.collection_objects[0].light_linking.link_state == 'INCLUDE'
assert copy.light_linking.receiver_collection.collection_children[0].light_linking.link_state == 'EXCLUDE'
camera_data = bpy.data.cameras.new('TransportTopCamera')
camera_data.type = 'ORTHO'
camera_data.ortho_scale = 8
camera = bpy.data.objects.new('TransportTopCamera', camera_data)
source.collection.objects.link(camera)
camera.location = (0, 0, 6)
bpy.context.view_layer.update()
second_camera = bpy.data.objects.new('PreparedTopCamera', camera_data)
stage.collection.objects.link(second_camera)
second_camera.matrix_world = camera.matrix_world.copy()

def render(scene, camera, name):
    bpy.context.window.scene = scene
    scene.camera = camera
    scene.render.engine = 'CYCLES'
    configure_cycles_device(scene)
    scene.cycles.samples = 32
    scene.cycles.use_denoising = False
    scene.cycles.use_adaptive_sampling = False
    scene.cycles.max_bounces = 1
    scene.cycles.diffuse_bounces = 1
    scene.cycles.seed = 0
    scene.render.resolution_x = 192
    scene.render.resolution_y = 64
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'OPEN_EXR'
    scene.render.image_settings.color_depth = '32'
    scene.render.filepath = str(OUT / (name + '.exr'))
    bpy.ops.render.render(write_still=True)
    image = bpy.data.images.load(scene.render.filepath, check_existing=False)
    pixels = np.empty(len(image.pixels), dtype=np.float32)
    image.pixels.foreach_get(pixels)
    values = pixels.reshape(64, 192, 4)[:, :, :3]
    return [float(values[20:44, x-12:x+12].mean()) for x in (48, 96, 144)]

original = render(source, camera, 'source')
prepared = render(stage, second_camera, 'prepared')
print('TRANSPORT_PIXEL_MEANS', original, prepared, flush=True)
assert original[0] > .001 and prepared[0] > .001
assert abs(original[0] - prepared[0]) / original[0] < .03, (original, prepared)
assert max(original[1:] + prepared[1:]) < original[0] * .01, (original, prepared)
report = {'rayFlagsEqual': True, 'mixedMembershipRejected': True,
          'nestedIncludeExcludePreserved': True, 'sourceMeans': original,
          'preparedMeans': prepared, 'mapOpened': False,
          'notValidated': ['full-map membership and atlas budget', 'production UV boundaries', 'formal scene']}
(OUT / 'Report.json').write_text(json.dumps(report, indent=2))
print('PREPARED_LIGHT_TRANSPORT_TEST_PASS', json.dumps(report), flush=True)
