"""Pack metallic/smoothness masks for the isolated pilot, with linear channels."""
import bpy
import json
import numpy as np
from pathlib import Path

source_root=Path(__file__).resolve().parent
root=source_root/'NR12Pilot_Corrected'
data=json.loads((root/'PilotManifest.json').read_text())
rows=[]
for i,obj in enumerate(data['objects']):
    if 'roughness' not in obj['textures']:
        continue
    source=bpy.data.images.load(str(source_root/obj['textures']['roughness']['path']),check_existing=False)
    source.colorspace_settings.name='Non-Color'
    width,height=source.size
    values=np.empty(width*height*4,dtype=np.float32)
    source.pixels.foreach_get(values)
    values=values.reshape((-1,4))
    mask=np.empty_like(values)
    mask[:,0]=obj['metallic']
    mask[:,1]=1
    mask[:,2]=0
    mask[:,3]=np.clip(1-values[:,0],0,1)
    image=bpy.data.images.new(f'pilot_{i}_metallic_smoothness',width,height,alpha=True)
    image.colorspace_settings.name='Non-Color'
    image.pixels.foreach_set(mask.ravel())
    image.filepath_raw=str(root/f'pilot_{i}_metallic_smoothness.png')
    image.file_format='PNG'
    image.save()
    rows.append({'object':obj['object'],'path':str(Path(image.filepath_raw).relative_to(source_root)),'colorspace':'Non-Color',
                 'channels':{'r':'metallic','g':'unused_white','b':'unused_zero','a':'1 minus roughness'},
                 'metallic':obj['metallic'],'roughness_min':float(values[:,0].min()),
                 'roughness_max':float(values[:,0].max())})
(root/'MaskManifest.json').write_text(json.dumps(rows,indent=2))
print('@@JSON@@'+json.dumps(rows),flush=True)
