"""Capture a spatial source-radiance field with two mirror hemispheres per probe.
Primary visibility is isolated to the tiny probe; all original materials, area
lights, emission and bounce context remain visible to its glossy rays.
"""
import bpy,pathlib,json,hashlib,numpy as np,math,time,sys
from mathutils import Vector,Matrix
ROOT=pathlib.Path(__file__).resolve().parent;PROJECT=ROOT.parents[2];WORK=PROJECT/'LocalData/SourceFidelity20261028/World';OUT=WORK/'Reflections';OUT.mkdir(parents=True,exist_ok=True)
sys.path.insert(0,str(ROOT))
from reflection_capture_contract import file_sha256,lighting_revision,capture_ready
input_blend_sha=file_sha256(bpy.data.filepath)
source=json.loads((ROOT/'SourceSummary.json').read_text());assert '--resolved' in sys.argv or input_blend_sha==source['source_sha256']
if '--resolved' in sys.argv:
 assert json.loads((WORK/'ResolvedMaterials.json').read_text())['sourceSha256']==source['source_sha256']
 manifest=json.loads((WORK/'WorldReflectionPlan.json').read_text());rows=manifest['probes'];probes=[{'p':Vector((r['position']['x'],r['position']['z'],r['position']['y']))} for r in rows]
 assert manifest['sourceSha256']==source['source_sha256'],'Reflection plan belongs to another source'
 scene=next(s for s in bpy.data.scenes if s.name.startswith('SourceWorldStatic'));bpy.context.window.scene=scene
 print('RESOLVED_WORLD_REFLECTION_PLAN',len(rows),flush=True)
else:
 plan=json.loads((PROJECT/'LocalData/SourceFidelity20261028/BakePlan.json').read_text());visible={r['source'] for r in plan['objects']};scene=bpy.context.scene;dg=bpy.context.evaluated_depsgraph_get()
 lights=[o for o in scene.objects if o.type=='LIGHT' and not o.hide_render and o.data.energy>0];sun=next(o for o in lights if o.data.type=='SUN');toward_sun=(sun.matrix_world.to_quaternion()@Vector((0,0,1))).normalized()

 def ray(position,direction,distance):
  p=Vector(position);d=Vector(direction).normalized();remaining=distance
  for _ in range(32):
   hit,location,normal,index,obj,matrix=scene.ray_cast(dg,p,d,distance=remaining)
   if not hit:return None
   traveled=(location-p).length
   if obj.original.name in visible:return location
   remaining-=traveled+.002
   if remaining<=0:return None
   p=location+d*.002
  return None

 def near_surface(p):
  return any((q-p).length<.12 for d in [(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)] if (q:=ray(p,d,.12)) is not None)

 def unobstructed(a,b):
  delta=b-a
  return delta.length<.01 or ray(a,delta,delta.length-.02) is None

 probes=[]
 def add(p,importance,label,separation):
  p=Vector(p)
  if near_surface(p):return False
  for q in probes:
   delta=p-q['p']
   if abs(delta.z)<1.5 and Vector((delta.x,delta.y)).length<separation and unobstructed(p,q['p']):return False
  probes.append({'p':p,'importance':importance,'label':label});return True

 # An exterior fallback covers every surface beyond local room volumes.
 base=Vector((2048,2048,1000));hit=ray(base,(0,0,-1),2000);fallback=Vector((2048,2048,hit.z+12 if hit else 280));probes.append({'p':fallback,'importance':0,'label':'whole_world_fallback'})
 for light in sorted(lights,key=lambda o:(o.matrix_world.translation.z,o.name)):
  if light.data.type not in {'AREA','SPOT'}:continue
  direction=(light.matrix_world.to_quaternion()@Vector((0,0,-1))).normalized();p=light.matrix_world.translation+direction*1.6
  lower=ray(p,(0,0,-1),64);upper=ray(p,(0,0,1),64)
  if lower is not None:p.z=min(lower.z+1.5,upper.z-.3 if upper is not None else lower.z+1.5)
  add(p,100,light.name,20)
 # Supplement unlit/indirect-only floors without putting probes in solid slabs.
 floor_candidates=[]
 for row in plan['objects']:
  name=row['source'].upper()
  if not any(t in name for t in ['FLOOR','DECK','WALKWAY','LANDING','PLATFORM']):continue
  if row['area_m2_conservative']<4 or 'TERRAIN' in name:continue
  obj=bpy.data.objects[row['source']];matrix=Matrix(row['matrix']);bounds=np.array([list(matrix@Vector(c)) for c in obj.evaluated_get(dg).bound_box]);span=bounds.max(axis=0)-bounds.min(axis=0)
  if span[2]>2 or min(span[0],span[1])<.5:continue
  lo=bounds.min(axis=0);hi=bounds.max(axis=0);p=Vector(((lo[0]+hi[0])*.5,(lo[1]+hi[1])*.5,hi[2]+1.5));floor_candidates.append((row['area_m2_conservative'],p,row['source']))
 for area,p,name in sorted(floor_candidates,key=lambda r:-r[0]):add(p,50,name,42)
 # Low-priority exterior cells supply the terrain and distant industrial forms.
 exterior_cells=sorted({(math.floor(r['cell'][0]/8),math.floor(r['cell'][1]/8)) for r in plan['objects'] if 'DISPLAY_TERRAIN_CUT' not in r['source']})
 for cx,cy in exterior_cells:
  p=Vector(((cx+.5)*256,(cy+.5)*256,1200));hit=ray(p,(0,0,-1),2400)
  if hit:p.z=hit.z+2.0;add(p,1,'exterior_'+str(cx)+'_'+str(cy),150)

 def xyz(v):return {'x':float(v.x),'y':float(v.z),'z':float(v.y)}
 rows=[]
 for index,probe in enumerate(probes):
  p=probe['p'];minimum=[];maximum=[]
  if probe['importance']==0:lo=Vector((-8192,-8192,-2048));hi=Vector((8192,8192,4096))
  else:
   limits=[12,12,64] if probe['importance']>=50 else [180,180,150]
   for axis,limit in enumerate(limits):
    a=Vector((0,0,0));a[axis]=1;b=-a;upper=ray(p,a,limit);lower=ray(p,b,limit)
    maximum.append(p[axis]+limit if upper is None else upper[axis]+.08);minimum.append(p[axis]-limit if lower is None else lower[axis]-.08)
   lo=Vector(minimum);hi=Vector(maximum)
  rows.append({'id':index,'position':xyz(p),'boxMin':xyz(lo),'boxMax':xyz(hi),'importance':probe['importance'],'sunVisible':ray(p,toward_sun,10000) is None,'label':probe['label'],'file':str((OUT/f'Probe_{index:03d}.exr').relative_to(PROJECT))})
 manifest={'boundsVersion':2,'positionVersion':3,'sourceSha256':source['source_sha256'],'towardSun':xyz(toward_sun),'sunColor':{'r':float(sun.data.color[0]),'g':float(sun.data.color[1]),'b':float(sun.data.color[2]),'a':1},'sunEnergy':float(sun.data.energy),'sunAngle':float(sun.data.angle),'probes':rows}
 (WORK/'WorldReflectionPlan.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2));print('WORLD_REFLECTION_PLAN',len(rows),'area_spot_sources',sum(o.data.type in {'AREA','SPOT'} for o in lights),flush=True)
verification=next((int(a.split('=',1)[1]) for a in sys.argv if a.startswith('--verify-probe=')),None)
external_images={}
for image in bpy.data.images:
 if image.source=='FILE' and image.filepath and not image.packed_file:
  image_path=pathlib.Path(bpy.path.abspath(image.filepath,library=image.library)).resolve()
  external_images[str(image_path)]=file_sha256(image_path)
profile={'captureScriptSha256':file_sha256(__file__),'contractSha256':file_sha256(ROOT/'reflection_capture_contract.py'),'externalImages':external_images,
 'blender':bpy.app.version_string,'samples':32,'resolution':1024,'hemispheres':2,'seed':0,
 'maxBounces':scene.cycles.max_bounces,'diffuseBounces':scene.cycles.diffuse_bounces,'glossyBounces':scene.cycles.glossy_bounces,
 'transmissionBounces':scene.cycles.transmission_bounces,'volumeBounces':scene.cycles.volume_bounces,'transparentBounces':scene.cycles.transparent_max_bounces}
profile['sunProjection']={k:manifest[k] for k in ['towardSun','sunAngle']}
revision=lighting_revision(source['source_sha256'],input_blend_sha,profile)
manifest['lightingRevision']=revision;manifest['inputBlendSha256']=input_blend_sha;manifest['captureProfile']=profile
if verification is None:
 revision_out=OUT/revision;revision_out.mkdir(parents=True,exist_ok=True)
 for row in rows:row['file']=str((revision_out/f"Probe_{row['id']:03d}.exr").relative_to(PROJECT))
 temporary=WORK/'WorldReflectionPlan.json.tmp';temporary.write_text(json.dumps(manifest,ensure_ascii=False,indent=2));temporary.replace(WORK/'WorldReflectionPlan.json')
if verification is not None:
 rows=[dict(rows[verification])];probes=[probes[verification]]
 target=WORK/'ReflectionVerification';target.mkdir(parents=True,exist_ok=True)
 rows[0]['file']=str((target/f'Probe_{verification:03d}.exr').relative_to(PROJECT))
# Capture only glossy-ray radiance. The source world never occludes the primary
# camera's view of the tiny probe, including when the camera crosses a room wall.
for obj in bpy.data.objects:
 if obj.type in {'MESH','CURVE','FONT','SURFACE'}:obj.visible_camera=False
radius=.05;bpy.ops.mesh.primitive_uv_sphere_add(segments=128,ring_count=64,radius=radius);probe=bpy.context.object;probe.name='Temporary_Source_Radiance_Probe';probe.visible_diffuse=False;probe.visible_glossy=False;probe.visible_transmission=False;probe.visible_shadow=False
for poly in probe.data.polygons:poly.use_smooth=True
mat=bpy.data.materials.new('Temporary_Perfect_Mirror');bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bsdf.inputs['Base Color'].default_value=(1,1,1,1);bsdf.inputs['Metallic'].default_value=1;bsdf.inputs['Roughness'].default_value=0;probe.data.materials.append(mat)
camera_data=bpy.data.cameras.new('SourceRadianceCamera');camera=bpy.data.objects.new('SourceRadianceCamera',camera_data);scene.collection.objects.link(camera);camera_data.type='ORTHO';camera_data.ortho_scale=radius*2;camera_data.clip_start=.005;camera_data.clip_end=10000;scene.camera=camera
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=False;scene.cycles.use_adaptive_sampling=False;scene.cycles.seed=0;scene.cycles.sample_clamp_direct=0;scene.cycles.sample_clamp_indirect=0;scene.render.threads_mode='AUTO';scene.cycles.device='CPU'
sys.path.insert(0,str(ROOT))
from cycles_device import configure_cycles_device
configure_cycles_device(scene)
scene.render.use_persistent_data=True
resolution=1024;scene.render.resolution_x=resolution;scene.render.resolution_y=resolution;scene.render.resolution_percentage=100;scene.render.use_compositing=False;scene.render.image_settings.file_format='OPEN_EXR';scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.color_depth='16';scene.render.image_settings.exr_codec='ZIP'
w,h=2048,1024;u,v=np.meshgrid((np.arange(w)+.5)/w,(np.arange(h)+.5)/h);phi=(u-.5)*2*np.pi;theta=(1-v)*np.pi;rx=np.sin(theta)*np.sin(phi);ry=np.sin(theta)*np.cos(phi);rz=np.cos(theta)
# Use each mirror's well-conditioned central hemisphere, never its grazing rim.
projection=[]
for incoming in [1,-1]:
 den=np.sqrt(np.maximum(2*(1-incoming*ry),1e-10));x=(incoming*rx/den+1)*.5*resolution-.5;y=(rz/den+1)*.5*resolution-.5
 x=np.clip(x,0,resolution-1);y=np.clip(y,0,resolution-1);x0=x.astype(int);y0=y.astype(int);x1=np.minimum(x0+1,resolution-1);y1=np.minimum(y0+1,resolution-1);projection.append((x0,y0,x1,y1,(x-x0)[...,None],(y-y0)[...,None]))
limit=next((int(a.split('=',1)[1]) for a in sys.argv if a.startswith('--limit-new=')),None);captured=0
start=time.monotonic()
for row,definition in zip(rows,probes):
 path=PROJECT/row['file'];stamp=path.with_suffix('.json')
 if capture_ready(PROJECT,row,source['source_sha256'],revision):
  print('REFLECTION_RESUME',row['id'],flush=True);continue
 if limit is not None and captured>=limit:break
 p=definition['p'];probe.location=p;panorama=np.empty((h,w,4),dtype=np.float32)
 for half,incoming in enumerate([1,-1]):
  camera.location=p+Vector((0,-incoming*.25,0));camera.rotation_euler=(p-camera.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/f'CurrentMirror_{half}.exr');bpy.ops.render.render(write_still=True)
  im=bpy.data.images.load(scene.render.filepath,check_existing=False);im.colorspace_settings.name='Non-Color';pixels=np.empty(resolution*resolution*4,dtype=np.float32);im.pixels.foreach_get(pixels);pixels=pixels.reshape(resolution,resolution,4);x0,y0,x1,y1,fx,fy=projection[half]
  values=(pixels[y0,x0]*(1-fx)+pixels[y0,x1]*fx)*(1-fy)+(pixels[y1,x0]*(1-fx)+pixels[y1,x1]*fx)*fy;mask=ry<=0 if half==0 else ry>0;panorama[mask]=values[mask];bpy.data.images.remove(im)
 assert np.isfinite(panorama).all(),'Non-finite source reflection'
 image=bpy.data.images.new('SourceProbeRadiance',w,h,alpha=True,float_buffer=True);image.colorspace_settings.name='Non-Color';image.pixels.foreach_set(panorama.ravel());temporary_image=path.with_name(path.stem+'.tmp.exr');image.save_render(str(temporary_image),scene=scene);bpy.data.images.remove(image);temporary_image.replace(path)
 captured+=1
 row['lightingRevision']=revision;row['inputBlendSha256']=input_blend_sha
 row['radianceSha256']=file_sha256(path)
 row['maximumRadiance']=float(panorama[:,:,:3].max());row['samples']=32;row['hemispheres']=2;row['sourceSha256']=source['source_sha256'];temporary=stamp.with_suffix('.json.tmp');temporary.write_text(json.dumps(row,ensure_ascii=False,indent=2));temporary.replace(stamp);print('WORLD_REFLECTION_CAPTURED',row['id'],'of',len(rows),'peak',round(row['maximumRadiance'],3),'elapsed',round(time.monotonic()-start,1),flush=True)
if verification is None and all(capture_ready(PROJECT,r,source['source_sha256'],revision) for r in rows):
 manifest=json.loads((WORK/'WorldReflectionPlan.json').read_text())
 (WORK/'WorldReflections.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2));print('ALL_SOURCE_REFLECTIONS_COMPLETE',len(rows),round(time.monotonic()-start,1),flush=True)
else:print('SOURCE_REFLECTION_PARTIAL' if verification is None else 'SOURCE_REFLECTION_VERIFICATION_COMPLETE',verification,round(time.monotonic()-start,1),flush=True)
