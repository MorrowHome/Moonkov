"""Capture actual Cycles glossy-ray environment, including virtual area-light reflections, plus the source AgX LUT."""
import bpy,math,pathlib,json,numpy as np
from mathutils import Vector
ROOT=pathlib.Path(__file__).resolve().parent;OUT=ROOT/'Bakes'/'Radiance';OUT.mkdir(parents=True,exist_ok=True)
scene=bpy.context.scene
source_clamps={'direct':scene.cycles.sample_clamp_direct,'indirect':scene.cycles.sample_clamp_indirect}
print('SOURCE_CLAMPS',source_clamps,flush=True)
# A mirror probe adds a delta bounce. Clamp after camera shading, never before
# storing incident radiance, or virtual area lights lose their HDR energy.
scene.cycles.sample_clamp_direct=0;scene.cycles.sample_clamp_indirect=0
# A mirror probe is used because Cycles area lights are visible to glossy rays,
# while a normal camera cubemap omits their virtual emitter surfaces.
position=Vector((1998.4,2468.0,253.4));radius=.2
bpy.ops.mesh.primitive_uv_sphere_add(segments=128,ring_count=64,radius=radius,location=position)
probe=bpy.context.object;probe.name='Temporary_Source_Mirror_Probe'
for p in probe.data.polygons:p.use_smooth=True
material=bpy.data.materials.new('Temporary_Source_Perfect_Mirror');tree=material.node_tree
bsdf=next(n for n in tree.nodes if n.type=='BSDF_PRINCIPLED')
bsdf.inputs['Base Color'].default_value=(1,1,1,1);bsdf.inputs['Metallic'].default_value=1;bsdf.inputs['Roughness'].default_value=0
probe.data.materials.append(material)
# The probe itself is only a primary-ray reflector; it must not occlude the environment.
probe.visible_diffuse=False;probe.visible_glossy=False;probe.visible_transmission=False;probe.visible_shadow=False
camera_data=bpy.data.cameras.new('Temporary_Source_Probe_Camera');camera=bpy.data.objects.new('Temporary_Source_Probe_Camera',camera_data);scene.collection.objects.link(camera)
camera.location=position+Vector((0,-1,0));camera.rotation_euler=(position-camera.location).to_track_quat('-Z','Y').to_euler()
camera_data.type='ORTHO';camera_data.ortho_scale=radius*2;camera_data.clip_start=.01;camera_data.clip_end=10000;scene.camera=camera
scene.render.engine='CYCLES';scene.cycles.samples=128;scene.cycles.use_denoising=False;scene.render.threads_mode='FIXED';scene.render.threads=4
scene.cycles.device='CPU'
try:
 prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='METAL';prefs.refresh_devices()
 for d in prefs.devices:d.use=d.type=='METAL'
 if any(d.use for d in prefs.devices):scene.cycles.device='GPU'
except Exception:pass
scene.render.resolution_x=1024;scene.render.resolution_y=1024;scene.render.resolution_percentage=100;scene.render.use_compositing=False
scene.render.image_settings.file_format='OPEN_EXR';scene.render.image_settings.color_depth='16';scene.render.image_settings.exr_codec='ZIP';scene.render.filepath=str(OUT/'corridor_mirror.exr')
bpy.ops.render.render(write_still=True)
image=bpy.data.images.load(scene.render.filepath,check_existing=False)
pixels=np.empty(1024*1024*4,dtype=np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(1024,1024,4)
# Lat-long convention: U longitude around world +Y, V latitude bottom-to-top;
# Unity cubemap import is checked against source cardinal directions separately.
w,h=2048,1024
u,v=np.meshgrid((np.arange(w)+.5)/w,(np.arange(h)+.5)/h)
phi=(u-.5)*2*np.pi;theta=(1-v)*np.pi
rx=np.sin(theta)*np.sin(phi);ry=np.sin(theta)*np.cos(phi);rz=np.cos(theta)
den=np.sqrt(np.maximum(2*(1-ry),1e-10))
x=(rx/den+1)*.5*1024-.5;y=(rz/den+1)*.5*1024-.5
x=np.clip(x,0,1023);y=np.clip(y,0,1023);x0=x.astype(int);y0=y.astype(int);x1=np.minimum(x0+1,1023);y1=np.minimum(y0+1,1023)
fx=(x-x0)[...,None];fy=(y-y0)[...,None]
result=(pixels[y0,x0]*(1-fx)+pixels[y0,x1]*fx)*(1-fy)+(pixels[y1,x0]*(1-fx)+pixels[y1,x1]*fx)*fy
panorama=bpy.data.images.new('Source_Corridor_Radiance',w,h,alpha=True,float_buffer=True);panorama.colorspace_settings.name='Non-Color';panorama.pixels.foreach_set(result.astype(np.float32).ravel());panorama.file_format='OPEN_EXR';panorama.filepath_raw=str(OUT/'corridor_radiance.exr');panorama.save()
print('SOURCE_RADIANCE',float(result[...,:3].max()),flush=True)
# Bake Blender's actual colour management, including AgX Medium High Contrast.
n=64;lo=-12;hi=16
r,g,b=np.meshgrid(np.arange(n),np.arange(n),np.arange(n),indexing='ij')
values=np.stack((r,g,b),axis=-1).astype(np.float32)/(n-1)
values=np.exp2(lo+values*(hi-lo));values[0,0,0]=0
# Unity strip LUT layout: x = blue*n + red, y = green.
lut=np.ones((n,n*n,4),dtype=np.float32)
for blue in range(n):lut[:,blue*n:(blue+1)*n,:3]=values[:,:,blue,:].transpose((1,0,2))
image=bpy.data.images.new('Source_AgX_MediumHigh_LUT',n*n,n,alpha=True,float_buffer=True)
image.colorspace_settings.name='Linear Rec.709';image.pixels.foreach_set(lut.ravel());image.use_view_as_render=True
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='16'
image.save_render(str(OUT/'source_agx_medium_high.png'),scene=scene)
(OUT/'manifest.json').write_text(json.dumps({'source_saved':False,'source_clamps':source_clamps,'probe_clamps_disabled':True,'position_blender':list(position),'radius_m':radius,'probe_projection':'glossy ray mirror sphere then lat-long','resolution':[w,h],'probe_samples':scene.cycles.samples,'maximum_scene_linear_radiance':float(result[...,:3].max()),'lut':{'size':n,'log2_domain':[lo,hi],'view':scene.view_settings.view_transform,'look':scene.view_settings.look},'limits':['Finite probe radius and cubemap parallax must be validated.','One region capture does not complete the full-map reflection field.']},indent=2))
print('RADIANCE_AND_AGX_COMPLETE',flush=True)
