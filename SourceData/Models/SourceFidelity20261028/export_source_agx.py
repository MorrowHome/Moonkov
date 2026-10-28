"""Export the source OCIO display transform as a log-domain strip LUT. Never save the source blend."""
import bpy,hashlib,json,pathlib,numpy as np
ROOT=pathlib.Path(__file__).resolve().parent
source=json.loads((ROOT/'SourceSummary.json').read_text())
assert hashlib.sha256(pathlib.Path(bpy.data.filepath).read_bytes()).hexdigest()==source['source_sha256']
scene=bpy.context.scene
n=64;lo=-12;hi=16
r,g,b=np.meshgrid(np.arange(n),np.arange(n),np.arange(n),indexing='ij')
values=np.exp2(lo+np.stack((r,g,b),axis=-1).astype(np.float32)/(n-1)*(hi-lo));values[0,0,0]=0
lut=np.ones((n,n*n,4),dtype=np.float32)
for blue in range(n):lut[:,blue*n:(blue+1)*n,:3]=values[:,:,blue,:].transpose((1,0,2))
image=bpy.data.images.new('Source_OCIO_LUT',n*n,n,alpha=True,float_buffer=True)
image.colorspace_settings.name='Linear Rec.709';image.pixels.foreach_set(lut.ravel());image.use_view_as_render=True
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='16'
output=ROOT.parents[2]/'Assets/MoonEnvironment/Variants/SourceFidelity/Radiance/source_agx_medium_high.png'
# ROOT is SourceData/Models/SourceFidelity20261028; parents[2] is project root.
output.parent.mkdir(parents=True,exist_ok=True);image.save_render(str(output),scene=scene)
print(json.dumps({'source_sha256':source['source_sha256'],'size':n,'log2_domain':[lo,hi],'view':scene.view_settings.view_transform,'look':scene.view_settings.look,'output':str(output),'source_saved':False}))
