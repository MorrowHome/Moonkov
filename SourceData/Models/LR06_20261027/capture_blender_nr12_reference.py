"""Render one low-sample source reference without saving the original blend."""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

root=Path(__file__).resolve().parent
scene=bpy.context.scene
camera_data=bpy.data.cameras.new('LR06_Temporary_Comparison_Camera')
camera=bpy.data.objects.new('LR06_Temporary_Comparison_Camera',camera_data)
scene.collection.objects.link(camera)
camera.location=Vector((1993.8,2457.6,254.2))
look=Vector((1998.4,2463.0,255.3))
camera.rotation_euler=(look-camera.location).to_track_quat('-Z','Y').to_euler()
camera_data.sensor_fit='VERTICAL'
camera_data.sensor_height=24
camera_data.lens=12/math.tan(math.radians(48)/2)
camera_data.clip_start=.05
camera_data.clip_end=100
scene.camera=camera
scene.render.engine='CYCLES'
scene.cycles.device='CPU'
scene.cycles.samples=16
scene.cycles.use_denoising=False
scene.render.threads_mode='FIXED'
scene.render.threads=4
scene.render.resolution_x=1200
scene.render.resolution_y=800
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(Path('Library/BuildValidation/LR06Pilot/BlenderReference.png').resolve())
bpy.ops.render.render(write_still=True)
result={'scope':'Blender source reference; 16 samples; actual Unity preview uses different illustrative lights',
        'camera_position':[1993.8,2457.6,254.2],'camera_target':[1998.4,2463.0,255.3],
        'vertical_fov_degrees':48,'path':scene.render.filepath,'source_saved':False}
Path('Library/BuildValidation/LR06Pilot/BlenderReference.json').write_text(json.dumps(result,indent=2))
print('@@JSON@@'+json.dumps(result),flush=True)
