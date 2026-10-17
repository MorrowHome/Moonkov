"""Arrange real Unity captures without retouching; summarize interleaved samples."""
import json, os, statistics
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import numpy as np

root=Path('Docs/Images/FullBody')
font=os.environ.get('LOOKDEV_FONT','/System/Library/Fonts/Hiragino Sans GB.ttc')
def ft(size): return ImageFont.truetype(font,size)
def pair(name,view,title,width=640):
    pad,gap,top=24,16,130
    h=round(width*1080/960)
    out=Image.new('RGB',(pad*2+width*2+gap,top+h+24),(19,25,36));d=ImageDraw.Draw(out)
    d.text((pad,18),title,font=ft(30),fill=(240,244,249))
    d.text((pad,65),'同镜头 / 姿态 / 灯光 / 曝光 / Bloom · 原始截图排版',font=ft(21),fill=(180,195,216))
    for i,(stage,label) in enumerate([('before','修改前：肩部版本'),('after','全身分区边缘光')]):
        x=pad+i*(width+gap);d.text((x,100),label,font=ft(21),fill=(237,199,130))
        im=Image.open(root/f'{view}-{stage}.png').convert('RGB');out.paste(im.resize((width,h),Image.Resampling.LANCZOS),(x,top))
    out.save(root/name)
pair('BeforeAfter.png','side-full','全身边缘光 · 皮肤暖润 / 发丝 / 衣料 / 饰件')
pair('PortraitComparison.png','side-portrait','人物近景 · 保留原肤色与服装配色')
pair('EyeHairComparison.png','side-eyes','眼部与发丝细节')
pair('LegComparison.png','side-legs','腿部皮肤、袜料与鞋饰')
pair('BackComparison.png','back-backview','背部皮肤、头发与衣料')
pair('StrongLightComparison.png','front-full','强光对比 · 相同主光强度 4')
pair('GameComparison.png','game','正式地图中的固定姿态对比') if (root/'game-after.png').exists() else None

# Each row uses identical camera/lighting between the two material stages.
w=360;h=405;pad=16;gap=12
out=Image.new('RGB',(pad*2+4*w+3*gap,2*h+150),(19,25,36));d=ImageDraw.Draw(out)
d.text((pad,15),'全身材质 · 遮挡与局部灯复核',font=ft(28),fill=(240,244,249))
for col,(light,label) in enumerate([('front','强光'),('highroof','8 米顶棚'),('lamp','遮挡内点光'),('dark','全暗')]):
    x=pad+col*(w+gap);d.text((x,65),label,font=ft(22),fill=(237,199,130))
    for row,stage in enumerate(['before','after']):
        y=105+row*(h+15);im=Image.open(root/f'{light}-full-{stage}.png');out.paste(im.resize((w,h),Image.Resampling.LANCZOS),(x,y))
out.save(root/'ShadowComparison.png')
raw=json.loads((root/'Performance.json').read_text());summary={'notes':raw['notes'],'hdr':raw['hdr']}
for stage,name in [(0,'before'),(1,'after')]:
    rows=[r for r in raw['samples'] if r['stage']==stage]
    summary[name]={'samples':len(rows),**{k:statistics.median(r[k] for r in rows) for k in ['submitMs','mainMs','gpuMs','frameMs','draws','setPass','triangles']}}
summary['display']={}
for view in ['side-full','front-full','highroof-full','dark-full']:
    a=np.asarray(Image.open(root/f'{view}-before.png')).astype(float)/255;b=np.asarray(Image.open(root/f'{view}-after.png')).astype(float)/255
    summary['display'][view]={'meanBefore':float(a.mean()),'meanAfter':float(b.mean()),'meanAbsoluteRgbDelta':float(abs(b-a).mean()),'whiteFractionBefore':float((a.min(2)>.98).mean()),'whiteFractionAfter':float((b.min(2)>.98).mean())}
(root/'Summary.json').write_text(json.dumps(summary,indent=2)+'\n')
