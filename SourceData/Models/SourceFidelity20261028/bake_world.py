"""Bake all prepared atlases with the complete fixed source rig and guided denoising.
Restartable per atlas. The original blend is never opened for writing.
"""
import bpy,pathlib,json,time,sys,numpy as np,math,zlib,struct
ROOT=pathlib.Path(__file__).resolve().parent;PROJECT=ROOT.parents[2];OUT=PROJECT/'LocalData/SourceFidelity20261028/World';BAKES=OUT/'Bakes';BAKES.mkdir(parents=True,exist_ok=True)
report=json.loads((OUT/'PreparedWorld.json').read_text());scene=next(s for s in bpy.data.scenes if s.name.startswith('SourceWorldStatic'));bpy.context.window.scene=scene
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.use_denoising=False;scene.cycles.use_adaptive_sampling=False
scene.render.threads_mode='FIXED';scene.render.threads=4
sys.path.insert(0,str(ROOT))
from cycles_device import configure_cycles_device
configure_cycles_device(scene)
scene.render.bake.use_clear=True;scene.render.bake.use_selected_to_active=False;scene.render.bake.margin=12
scene.render.image_settings.file_format='OPEN_EXR';scene.render.image_settings.color_depth='16';scene.render.image_settings.color_mode='RGBA';scene.render.image_settings.exr_codec='ZIP'
start=time.monotonic();results=[]

def guide_materials(materials,mode):
 restores=[]
 for mat in materials:
  tree=mat.node_tree;output=next(n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output);prior=[(l.from_socket,l.to_socket) for l in output.inputs['Surface'].links]
  for link in list(output.inputs['Surface'].links):tree.links.remove(link)
  made=[]
  def node(kind):
   n=tree.nodes.new(kind);made.append(n);return n
  emit=node('ShaderNodeEmission')
  attribute=node('ShaderNodeAttribute');attribute.attribute_name='SourceWorldNormal' if mode=='normal' else 'SourceInstance'
  if mode=='normal':
   scale=node('ShaderNodeVectorMath');scale.operation='SCALE';scale.inputs['Scale'].default_value=.5;tree.links.new(attribute.outputs['Vector'],scale.inputs[0])
   add=node('ShaderNodeVectorMath');add.operation='ADD';add.inputs[1].default_value=(.5,.5,.5);tree.links.new(scale.outputs['Vector'],add.inputs[0]);tree.links.new(add.outputs['Vector'],emit.inputs['Color'])
  else:
   combine=node('ShaderNodeCombineXYZ')
   for axis,coefficient in zip('XYZ',[.61803398875,.41421356237,.73205080757]):
    mul=node('ShaderNodeMath');mul.operation='MULTIPLY';mul.inputs[1].default_value=coefficient;tree.links.new(attribute.outputs['Fac'],mul.inputs[0]);fraction=node('ShaderNodeMath');fraction.operation='FRACT';tree.links.new(mul.outputs[0],fraction.inputs[0]);tree.links.new(fraction.outputs[0],combine.inputs[axis])
   tree.links.new(combine.outputs['Vector'],emit.inputs['Color'])
  tree.links.new(emit.outputs[0],output.inputs['Surface'])
  restores.append((tree,made,prior))
 def restore():
  for tree,made,prior in restores:
   for n in made:tree.nodes.remove(n)
   for a,b in prior:tree.links.new(a,b)
 return restore

def bake(obj,tag,size,kind,samples,guide=None):
 image=bpy.data.images.new(tag,size,size,alpha=True,float_buffer=True);image.colorspace_settings.name='Non-Color'
 materials=list({m for m in obj.data.materials if m});restore=guide_materials(materials,guide) if guide else lambda:None
 targets=[]
 for material in materials:
  node=material.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;material.node_tree.nodes.active=node;targets.append((material.node_tree,node))
 scene.cycles.samples=samples
 try:
  kwargs={'type':kind,'uv_layer':'SourceStaticBake'}
  if kind=='DIFFUSE':kwargs['pass_filter']={'DIRECT','INDIRECT'}
  assert bpy.ops.object.bake(**kwargs)=={'FINISHED'}
 finally:
  for tree,node in targets:tree.nodes.remove(node)
  restore()
 return image

# CPU denoising is compositor-only in an empty scene. Lighting rays are all
# traced by Cycles above; guides keep separate objects and normal edges apart.
def denoise(image,normal,albedo,path):
 size=image.size[0];pixels=np.empty(size*size*4,dtype=np.float32);normal.pixels.foreach_get(pixels);pixels=pixels.reshape(-1,4);pixels[:,:3]=pixels[:,:3]*2-1;normal.pixels.foreach_set(pixels.ravel());del pixels
 compositor=bpy.data.scenes.new('SourceGuideDenoise');compositor.render.engine='CYCLES';compositor.cycles.samples=1;compositor.render.threads_mode='FIXED';compositor.render.threads=4
 compositor.render.resolution_x=size;compositor.render.resolution_y=size;compositor.render.resolution_percentage=100;compositor.render.use_compositing=True
 tree=bpy.data.node_groups.new('SourceIrradianceDenoise','CompositorNodeTree');compositor.compositing_node_group=tree
 dn=tree.nodes.new('CompositorNodeDenoise')
 for socket,im in [('Image',image),('Normal',normal),('Albedo',albedo)]:
  n=tree.nodes.new('CompositorNodeImage');n.image=im;tree.links.new(n.outputs['Image'],dn.inputs[socket])
 output=tree.nodes.new('CompositorNodeOutputFile');output.directory=str(path.parent);output.file_name=path.stem;output.file_output_items.new('RGBA','');output.save_as_render=False;output.format.media_type='IMAGE';output.format.file_format='OPEN_EXR';output.format.color_depth='16';output.format.exr_codec='ZIP';tree.links.new(dn.outputs['Image'],output.inputs[0])
 camera_data=bpy.data.cameras.new('SourceDenoiseCamera');camera=bpy.data.objects.new('SourceDenoiseCamera',camera_data);compositor.collection.objects.link(camera);compositor.camera=camera
 bpy.context.window.scene=compositor
 try:bpy.ops.render.render()
 finally:bpy.context.window.scene=scene;bpy.data.scenes.remove(compositor);bpy.data.objects.remove(camera);bpy.data.cameras.remove(camera_data);bpy.data.node_groups.remove(tree)
 assert path.exists(),str(path)

def surface_statistics(raw,normal,albedo,denoised,path,samples):
 size=raw.size[0];count=size*size;buffer=np.empty(count*4,dtype=np.float32)
 raw.pixels.foreach_get(buffer);raw_y=buffer.reshape(-1,4)[:,:3]@np.array([.2126,.7152,.0722],dtype=np.float32)
 im=bpy.data.images.load(str(denoised),check_existing=False);im.colorspace_settings.name='Non-Color';im.pixels.foreach_get(buffer);mean_y=buffer.reshape(-1,4)[:,:3]@np.array([.2126,.7152,.0722],dtype=np.float32);bpy.data.images.remove(im)
 variance=((raw_y-mean_y)/np.maximum(mean_y,.02))**2*samples;del raw_y,mean_y
 albedo.pixels.foreach_get(buffer);rgb=np.clip(buffer.reshape(-1,4)[:,:3]*255,0,255).astype(np.uint32);keys=rgb[:,0]|(rgb[:,1]<<8)|(rgb[:,2]<<16);del rgb
 normal.pixels.foreach_get(buffer);rgb=np.clip((buffer.reshape(-1,4)[:,:3]*.5+.5)*15,0,15).astype(np.uint32);nkeys=rgb[:,0]|(rgb[:,1]<<4)|(rgb[:,2]<<8);keys^=nkeys*np.uint32(2654435761);del rgb,nkeys,buffer
 variance=variance.reshape(size,size);keys=keys.reshape(size,size)
 for radius in [1,2]:
  total=variance.copy();weight=np.ones_like(variance)
  for axis in [0,1]:
   for direction in [-1,1]:
    shift=direction*radius;neighbor=np.roll(variance,shift,axis=axis);valid=keys==np.roll(keys,shift,axis=axis)
    if axis==0:valid[:radius if shift>0 else 0]=False;valid[size-radius if shift<0 else size:]=False
    else:valid[:,:radius if shift>0 else 0]=False;valid[:,size-radius if shift<0 else size:]=False
    total+=neighbor*valid;weight+=valid
  variance=total/weight
 rgba=np.ones((size,size,4),dtype=np.uint8)*255;rgba[:,:,1]=np.rint(np.clip(np.log2(1+variance)/16,0,1)*255).astype(np.uint8);rgba[:,:,2]=0
 rows=np.empty((size,size*4+1),dtype=np.uint8);rows[:,0]=0;rows[:,1:]=rgba[::-1].reshape(size,-1)
 def chunk(kind,data):return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
 payload=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',size,size,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(rows.tobytes(),6))+chunk(b'IEND',b'')
 path.write_bytes(payload)
 cache=path.with_name(path.name.replace('SurfaceStatistics_','SourceAO_'))
 if cache.exists():
  ao=bpy.data.images.load(str(cache),check_existing=False);ao.colorspace_settings.name='Non-Color';values=np.empty(size*size*4,dtype=np.float32);ao.pixels.foreach_get(values);rgba[:,:,0]=np.rint(np.clip(values.reshape(size,size,4)[:,:,0],0,1)*255).astype(np.uint8);bpy.data.images.remove(ao)
  rows[:,1:]=rgba[::-1].reshape(size,-1);path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',size,size,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(rows.tobytes(),6))+chunk(b'IEND',b''))

for atlas in report['atlases']:
 index=atlas['id'];output=BAKES/f'Irradiance_{index:03d}.exr';stamp=BAKES/f'Irradiance_{index:03d}.json'
 if output.exists() and stamp.exists() and json.loads(stamp.read_text()).get('version')==2:results.append(json.loads(stamp.read_text()));print('WORLD_BAKE_RESUME',index,flush=True);continue
 obj=scene.objects[atlas['object']];bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
 began=time.monotonic();size=atlas['size']
 print('WORLD_BAKE_BEGIN',index,'size',size,'triangles',atlas['triangles'],flush=True)
 irradiance=bake(obj,f'Irradiance_{index:03d}',size,'DIFFUSE',16)
 print('WORLD_BAKE_RAYS_COMPLETE',index,round(time.monotonic()-began,1),flush=True)
 raw=BAKES/f'Irradiance_{index:03d}_raw.exr';irradiance.save_render(str(raw),scene=scene)
 normal=bake(obj,f'GuideNormal_{index:03d}',size,'EMIT',1,'normal');albedo=bake(obj,f'GuideObjects_{index:03d}',size,'EMIT',1,'objects')
 denoise(irradiance,normal,albedo,output)
 statistics=BAKES/f'SurfaceStatistics_{index:03d}.png';surface_statistics(irradiance,normal,albedo,output,statistics,16)
 for image in [irradiance,normal,albedo]:bpy.data.images.remove(image)
 result={'version':2,'id':index,'size':size,'samples':16,'source_sha256':report['source_sha256'],'file':str(output.relative_to(PROJECT)),'bytes':output.stat().st_size,'seconds':time.monotonic()-began,'all_fixed_source_lights':True,'statistics':str(statistics.relative_to(PROJECT)),'guided_denoise':True,'bounces':scene.cycles.max_bounces}
 stamp.write_text(json.dumps(result,indent=2));results.append(result)
 print('WORLD_BAKE_COMPLETE',index,'seconds',round(result['seconds'],1),'overall',round(time.monotonic()-start,1),flush=True)
(OUT/'BakedWorld.json').write_text(json.dumps({'source_sha256':report['source_sha256'],'version':2,'atlases':results,'device':scene.cycles.device},indent=2))
print('ALL_WORLD_IRRADIANCE_COMPLETE',len(results),round(time.monotonic()-start,1),flush=True)
