"""Render source and baked NR12 pilot materials under identical Blender lighting."""
import bpy
import json
import math
import numpy as np
from mathutils import Vector
from pathlib import Path

root=Path(__file__).resolve().parent
pilot=root/'NR12Pilot_Corrected'
source=root/'Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend'
data=json.loads((pilot/'PilotManifest.json').read_text())
assert bpy.ops.import_scene.fbx(filepath=str(pilot/'NR12_corridor_pilot.fbx'),use_custom_normals=True,
                                use_image_search=False)=={'FINISHED'}
source_names=tuple(o['source_material'] for o in data['objects'])
with bpy.data.libraries.load(str(source),link=False) as (available,loaded):
    assert all(n in available.materials for n in source_names)
    loaded.materials=list(source_names)
source_materials=dict(zip(source_names,loaded.materials))
assert all(source_materials.values())
for entry in data['objects']:
    obj=bpy.data.objects[entry['object']]
    obj.data.materials.clear();obj.data.materials.append(source_materials[entry['source_material']])

scene=bpy.context.scene
scene.render.engine='CYCLES'
scene.cycles.device='CPU';scene.cycles.samples=64;scene.cycles.use_denoising=False;scene.cycles.seed=1911
scene.render.threads_mode='FIXED';scene.render.threads=4
scene.render.resolution_x=1200;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.render.film_transparent=True
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
world=bpy.data.worlds.new('LR06_Pilot_Comparison_World');world.use_nodes=True
world.node_tree.nodes.get('Background').inputs['Color'].default_value=(.08,.1,.14,1)
world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.2
scene.world=world
sun_data=bpy.data.lights.new('LR06_Comparison_Sun','SUN');sun_data.energy=.65
sun=bpy.data.objects.new('LR06_Comparison_Sun',sun_data);scene.collection.objects.link(sun)
sun.rotation_euler=(math.radians(48),0,math.radians(-32))
warm_data=bpy.data.lights.new('LR06_Comparison_Warm_Point','POINT')
warm_data.energy=900;warm_data.color=(1,.68,.38);warm_data.shadow_soft_size=.15
warm=bpy.data.objects.new('LR06_Comparison_Warm_Point',warm_data);scene.collection.objects.link(warm)
warm.location=(1998.175,2462.6,255.9)
camera_data=bpy.data.cameras.new('LR06_Comparison_Camera');camera=bpy.data.objects.new('LR06_Comparison_Camera',camera_data)
scene.collection.objects.link(camera);scene.camera=camera
camera.location=Vector((1993.8,2457.6,254.2));target=Vector((1998.4,2463.0,255.3))
camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
camera_data.sensor_fit='VERTICAL';camera_data.sensor_height=24
camera_data.lens=12/math.tan(math.radians(48)/2);camera_data.clip_start=.05;camera_data.clip_end=100

def render(label):
    path=pilot/('material_compare_'+label+'.png')
    scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)
    image=bpy.data.images.load(str(path),check_existing=False)
    pixels=np.empty(1200*800*4,dtype=np.float32);image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    return pixels.reshape((-1,4)).copy()

reference=render('source')
for index,entry in enumerate(data['objects']):
    obj=bpy.data.objects[entry['object']]
    mat=bpy.data.materials.new('LR06_Compare_Baked_'+str(index))
    nodes=mat.node_tree.nodes
    bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
    bsdf.inputs['Metallic'].default_value=entry['metallic']
    if index<2:
        base=nodes.new('ShaderNodeTexImage');base.image=bpy.data.images.load(str(pilot/f'pilot_{index}_basecolor.png'))
        base.image.colorspace_settings.name='sRGB'
        rough=nodes.new('ShaderNodeTexImage');rough.image=bpy.data.images.load(str(pilot/f'pilot_{index}_roughness.png'))
        rough.image.colorspace_settings.name='Non-Color'
        normal=nodes.new('ShaderNodeTexImage');normal.image=bpy.data.images.load(str(pilot/f'pilot_{index}_normal.exr'))
        normal.image.colorspace_settings.name='Non-Color'
        normnode=nodes.new('ShaderNodeNormalMap');normnode.uv_map='LR06_BakeUV'
        mat.node_tree.links.new(base.outputs['Color'],bsdf.inputs['Base Color'])
        mat.node_tree.links.new(rough.outputs['Color'],bsdf.inputs['Roughness'])
        mat.node_tree.links.new(normal.outputs['Color'],normnode.inputs['Color'])
        mat.node_tree.links.new(normnode.outputs['Normal'],bsdf.inputs['Normal'])
    else:
        bsdf.inputs['Base Color'].default_value=entry['base_color']
        bsdf.inputs['Roughness'].default_value=entry['roughness']
        bsdf.inputs['Emission Color'].default_value=entry['emission_color']
        bsdf.inputs['Emission Strength'].default_value=entry['emission_strength']
    obj.data.materials.clear();obj.data.materials.append(mat)

baked=render('baked')
mask=(reference[:,3]>.99)&(baked[:,3]>.99)
delta=np.abs(reference[mask,:3]-baked[mask,:3])
result={'scope':'isolated Blender comparison of source nodes and baked maps, same geometry/camera/lights; not Unity acceptance',
        'samples':64,'compared_pixels':int(mask.sum()),'absolute_decoded_png_rgb_mean':float(delta.mean()),
        'absolute_decoded_png_rgb_p95':float(np.quantile(delta,.95)),
        'reference_mean_rgb':[float(v) for v in reference[mask,:3].mean(axis=0)],
        'baked_mean_rgb':[float(v) for v in baked[mask,:3].mean(axis=0)],
        'source_render':str((pilot/'material_compare_source.png').relative_to(root)),
        'baked_render':str((pilot/'material_compare_baked.png').relative_to(root))}
(pilot/'MaterialComparison.json').write_text(json.dumps(result,indent=2))
print('@@JSON@@'+json.dumps(result),flush=True)
