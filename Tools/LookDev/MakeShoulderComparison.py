"""Arrange unretouched Unity Play captures and summarize measured results."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,os,statistics
import numpy as np
root=Path('Docs/Images/ShoulderRim')
font=os.environ.get('LOOKDEV_FONT','/System/Library/Fonts/Hiragino Sans GB.ttc')
def ft(n):return ImageFont.truetype(font,n)
def pair(name,left,right,title,crop=False):
    w=640;pad=24;gap=16;top=120;h=720
    canvas=Image.new('RGB',(pad*2+w*2+gap,top+h+(430 if crop else 0)+56),(19,25,36));d=ImageDraw.Draw(canvas)
    d.text((pad,18),title,font=ft(30),fill=(242,244,249))
    d.text((pad,67),'同镜头 / 同姿态 / 同光源 / 同曝光 · 原始截图排版',font=ft(21),fill=(177,190,212))
    for i,(path,label) in enumerate([(left,'本轮修改前'),(right,'肩部亮带与 Bloom')]):
        x=pad+i*(w+gap);image=Image.open(root/path).convert('RGB')
        d.text((x,top),label,font=ft(23),fill=(237,199,130));canvas.paste(image.resize((w,h),Image.Resampling.LANCZOS),(x,top+40))
        if crop:
            # Identical shoulder crop; no exposure, contrast or color adjustments.
            part=image.crop((178,402,418,538));d.text((x,top+h+50),'同区域放大',font=ft(20),fill=(183,197,216))
            canvas.paste(part.resize((w,round(w*136/240)),Image.Resampling.LANCZOS),(x,top+h+82))
    canvas.save(root/name)
pair('BeforeAfter.png','side-before-bloom.png','side-after-bloom.png','肩部轮廓亮带与柔和泛光',True)
pair('PortraitComparison.png','portrait-before.png','portrait-after.png','正常人物镜头对比')
# Native GGX / Rim ablations, with Bloom off and on in separate rows.
w=400;pad=20;gap=12;rowh=530
canvas=Image.new('RGB',(pad*2+w*4+gap*3,118+rowh*2),(19,25,36));d=ImageDraw.Draw(canvas)
d.text((pad,16),'肩部效果拆解 · 只有 Body / InnerDress 切换反射项',font=ft(27),fill=(235,240,248))
for row,(suffix,label) in enumerate([('nobloom','Bloom 关闭'),('bloom','Bloom 开启')]):
    y=76+row*rowh;d.text((pad,y),label,font=ft(21),fill=(237,199,130))
    for col,(stage,title) in enumerate([('before','修改前'),('ggx','原生 GGX'),('rim','方向性 Rim'),('after','GGX 与 Rim 组合')]):
        x=pad+col*(w+gap);d.text((x,y+30),title,font=ft(19),fill=(211,224,242))
        im=Image.open(root/f'side-{stage}-{suffix}.png');canvas.paste(im.resize((w,450),Image.Resampling.LANCZOS),(x,y+62))
canvas.save(root/'Ablation.png')
if (root/'Performance.json').exists():
    raw=json.loads((root/'Performance.json').read_text());result={}
    for stage,name in [(0,'before'),(1,'after')]:
        rows=[r for r in raw['samples'] if r['stage']==stage]
        if rows:result[name]={'samples':len(rows),**{k:statistics.median(r[k] for r in rows) for k in ['submitMs','mainMs','gpuMs','frameMs','draws','setPass','triangles']}}
    result['hdr']=raw['hdr'];result['notes']=raw['notes']
    # Explicitly measured display-image changes; not HDR radiance or illuminance.
    signals={}
    for light in ['front','shade','highroof','lamp','dark']:
        path=root/f'{light}-after.png'
        if path.exists():
            a=np.asarray(Image.open(path)).astype(float)/255
            signals[light]={'faceMean':float(a[548:570,440:514,:3].mean()),'shoulderMean':float(a[670:720,280:350,:3].mean())}
    result['displaySamples']=signals
    (root/'Summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')

# A separate Bloom toggle comparison isolates halo from the lit surface.
w=640;pad=24;gap=16
canvas=Image.new('RGB',(pad*2+w*2+gap,580),(19,25,36));d=ImageDraw.Draw(canvas)
d.text((pad,18),'相同边缘光 · 只切换 Bloom',font=ft(30),fill=(240,244,249))
for col,(file,label) in enumerate([('side-after-nobloom.png','Bloom 关闭'),('side-after-bloom.png','Bloom 开启')]):
    x=pad+col*(w+gap);d.text((x,75),label,font=ft(22),fill=(237,199,130))
    part=Image.open(root/file).crop((168,390,438,560));canvas.paste(part.resize((w,round(w*170/270)),Image.Resampling.LANCZOS),(x,118))
canvas.save(root/'BloomComparison.png')
