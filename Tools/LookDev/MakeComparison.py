"""Layout of untouched Unity captures; no retouching, relighting or color changes."""
from PIL import Image,ImageDraw,ImageFont
from pathlib import Path
import json, os, numpy as np
p=Path('Docs/Images/LilToonNative')
font=os.environ.get('LOOKDEV_FONT','/System/Library/Fonts/Hiragino Sans GB.ttc')
def ft(n):return ImageFont.truetype(font,n)
def make(path,columns):
 w=640;gap=16;pad=24;top=124;rowh=820
 out=Image.new('RGB',(pad*2+len(columns)*w+(len(columns)-1)*gap,top+rowh*2+48),(19,25,36));d=ImageDraw.Draw(out)
 d.text((pad,18),'lilToon · Unity Play 对比',font=ft(32),fill=(240,242,248))
 d.text((pad,65),'同镜头 / 同姿态 / 同灯光 / 同曝光与调色 · 仅切换材质',font=ft(21),fill=(175,189,211))
 for row,(light,label) in enumerate([('studio','柔和主光 · 强度 1.0'),('harsh','强光 · 强度 4.0')]):
  y=top+row*rowh
  d.text((pad,y),label,font=ft(23),fill=(237,199,130))
  for col,(stage,title) in enumerate(columns):
   x=pad+col*(w+gap)
   d.text((x,y+37),title,font=ft(22),fill=(220,229,244))
   image=Image.open(p/f'{light}-{stage}.png').convert('RGB')
   out.paste(image.resize((w,720),Image.Resampling.LANCZOS),(x,y+70))
 d.text((pad,top+rowh*2+15),'原始 Unity 截图等比例排版；未修色、未生成或绘制角色画面。',font=ft(18),fill=(161,177,199))
 out.save(p/path)
make('BeforeAfter.png',[('before','任务前材质'),('after','本次修正')])
make('ReviewComparison.png',[('before','任务前材质'),('previous','上一版材质'),('after','本次修正')])
values={}
for light in ['sun','shade','lamp','dark']:
 im=np.asarray(Image.open(p/f'{light}-after.png')).astype(float)/255
 rgb=im[548:570,440:514,:3].mean(axis=(0,1))
 values[light]=float(rgb@[.2126,.7152,.0722])
values['shadeToSun']=values['shade']/values['sun']
values['note']='Same face ROI (440,548)-(514,570), display RGB luma; not illuminance/full-character visibility. Upper ornament is lit at roof boundary. Extended live-game high-roof diagnostic remains unresolved; see Diagnostics.'
(p/'ShadowSamples.json').write_text(json.dumps(values,indent=2))
# Confirm a stronger light changes local highlights, rather than lifting cloth.
a=np.asarray(Image.open(p/'sun-after.png')).astype(float)
b=np.asarray(Image.open(p/'harsh-after.png')).astype(float)
regions={'face_base':(440,548,514,570),'gold_shoulder':(285,695,380,770),'gold_glasses':(377,490,590,556),'blue_cloth':(450,652,487,685)}
response={}
for name,(x,y,X,Y) in regions.items():
 delta=(b-a)[y:Y,x:X]
 response[name]={'meanAbsoluteRgbChangeBytes':float(abs(delta).mean()),'p99MaxChannelIncreaseBytes':float(np.percentile(delta.max(axis=2),99)),'maximumChannelIncreaseBytes':float(delta.max())}
response['conditions']='Same final material, camera/pose/grade/light direction, directional intensity 1.6 vs 4. Diffuse capped at 1, excess main light reaches native GGX. Screenshot RGB bytes are not HDR radiance.'
(p/'SpecularResponse.json').write_text(json.dumps(response,indent=2))
