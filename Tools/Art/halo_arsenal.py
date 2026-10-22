"""Original vector assemblies. ImageMagick rasterizes editable SVGs; no baked bloom.
Nine silhouettes, restrained signal colors and open firing apertures.
"""
from pathlib import Path
import math, subprocess
ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'ArtSource/HaloArsenal';OUT=ROOT/'Assets/DollSinger/Art/HaloArsenal';ICONS=ROOT/'Assets/Resources/HaloIcons'
for directory in [SRC,OUT,ICONS]:directory.mkdir(parents=True,exist_ok=True)
PALETTE={'cicada':'#72e6de','trine':'#b4a1ff','aegis':'#80b9ff','vesper':'#b8e9ff','thorn':'#f1a4d2','sundown':'#ff967e','rondo':'#90e5be','tempest':'#85cef4','nocturne':'#c6b9f2'}
W,D,S='#eef4fa','#243448','#657d95'
def pt(r,a):
 a=math.radians(a);return 320+r*math.cos(a),320+r*math.sin(a)
def polygon(points,color):return '<polygon points="'+' '.join(f'{x:.2f},{y:.2f}' for x,y in points)+f'" fill="{color}"/>'
def band(outer,inner,start,end,color,n=32):
 return polygon([pt(outer,start+(end-start)*i/n) for i in range(n+1)]+[pt(inner,end-(end-start)*i/n) for i in range(n+1)],color)
def radial(points,angle,color):return polygon([pt(r,angle+a) for r,a in points],color)
def line(points,color,width=3):return '<polyline points="'+' '.join(f'{x:.2f},{y:.2f}' for x,y in points)+f'" fill="none" stroke="{color}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>'
def svg(body):return '<svg xmlns="http://www.w3.org/2000/svg" width="640" height="640" viewBox="0 0 640 640">'+body+'</svg>'
for code,G in PALETTE.items():
 shell=sigil=''
 if code=='cicada':
  # An open tuning fork with two swept, layered wing vanes, rather than a ring.
  for a in [0,180]:
   shell+=radial([(285,-51),(296,-24),(218,12),(177,47),(145,43),(178,1),(245,-26)],a,D)
   shell+=radial([(284,-50),(278,-30),(216,4),(166,42),(159,27),(197,-10)],a,W)
   sigil+=radial([(272,-35),(247,-17),(206,7),(180,21),(203,-6),(253,-30)],a,G)
   sigil+=line([pt(285,a-48),pt(252,a-31),pt(207,a-13)],G,4)
  shell+=polygon([(285,55),(304,55),(304,154),(289,172)],W)+polygon([(336,55),(355,55),(351,172),(336,154)],W)
  sigil+=polygon([(310,65),(330,65),(330,123),(320,137),(310,123)],G)
 elif code=='trine':
  # Three interleaved porcelain keys, each with an indexed violet channel.
  for a in [-90,30,150]:
   shell+=radial([(297,0),(271,35),(180,38),(150,3),(189,-9)],a,D)
   shell+=radial([(289,0),(265,30),(187,32),(161,4),(195,-4)],a,W)
   sigil+=radial([(274,1),(254,23),(219,26),(236,8)],a,G)
   sigil+=line([pt(211,a-1),pt(188,a+6),pt(204,a+22)],S,4)
   for i in range(3):shell+=band(288,282,a+9+i*5,a+11+i*5,G,3)
 elif code=='aegis':
  # Four massive corner bastions around an octagonal opening.
  for a in [45,135,225,315]:
   shell+=radial([(300,-18),(300,18),(265,28),(203,28),(164,14),(164,-14),(203,-28),(265,-28)],a,D)
   shell+=radial([(289,-15),(289,15),(256,23),(209,23),(179,11),(179,-11),(209,-23),(256,-23)],a,W)
   sigil+=radial([(276,-10),(276,10),(250,16),(233,16),(248,0),(233,-16),(250,-16)],a,G)
   sigil+=line([pt(207,a-18),pt(190,a-8),pt(190,a+8),pt(207,a+18)],S,4)
 elif code=='vesper':
  # A horizontal lunar caliper, with a narrow split vane and asymmetric crescent.
  shell+=band(285,234,110,250,D)+band(277,246,113,247,W)
  sigil+=band(269,253,-56,56,G)
  for a in [0,180]:
   shell+=radial([(306,-3),(280,-11),(183,-6),(159,0),(183,6),(280,11),(306,3)],a,W)
   sigil+=line([pt(288,a),pt(190,a)],G,5)
  sigil+=band(227,223,137,223,G)
  for a in [-90,90]:shell+=radial([(286,-2),(286,2),(257,2),(257,-2)],a,G)
 elif code=='thorn':
  # Five detached crystalline petals: dark roots, pale edges, pink inner veins.
  for a in [-90,-18,54,126,198]:
   shell+=radial([(305,0),(242,18),(163,15),(179,-6),(245,-15)],a,D)
   shell+=radial([(302,0),(240,11),(181,10),(203,-3),(256,-9)],a,W)
   sigil+=radial([(289,0),(236,7),(205,6),(240,1)],a,G)
   sigil+=line([pt(207,a-5),pt(241,a-9),pt(265,a-6)],G,3)
 elif code=='sundown':
  # A broken eclipse crucible and suspended ember; never an opaque central ball.
  for a in [-132,-12,108]:
   shell+=band(291,229,a,a+87,D,16)+band(281,246,a+2,a+81,W,16)
   sigil+=band(263,253,a+8,a+48,G,12)
   shell+=radial([(295,0),(307,3),(306,7),(286,8)],a+77,G)
  shell+=polygon([(271,521),(291,506),(349,506),(369,521),(351,573),(320,599),(289,573)],D)
  sigil+=polygon([(288,526),(305,516),(335,516),(352,526),(339,562),(320,579),(301,562)],G)
  sigil+=polygon([(305,526),(320,521),(335,526),(320,554)],W)
 elif code=='rondo':
  # Two mirrored jade teardrops, with an offset refrain channel.
  for a in [12,192]:
   shell+=band(281,179,a,a+127,D)+band(270,191,a+2,a+121,W)
   shell+=radial([(281,124),(191,120),(190,153),(223,153)],a,D)
   sigil+=band(257,231,a+12,a+78,G)
   sigil+=radial([(243,80),(231,83),(203,117),(215,111)],a,G)
   sigil+=line([pt(210,a+9),pt(200,a+26),pt(205,a+45)],S,4)
 elif code=='tempest':
  # Five swept turbine vanes and clear negative spaces; no full seal body.
  for a in [-90,-18,54,126,198]:
   shell+=radial([(302,6),(272,29),(190,36),(150,17),(176,1),(245,8)],a,D)
   shell+=radial([(291,9),(263,24),(192,29),(164,17),(182,8),(244,14)],a,W)
   sigil+=radial([(278,12),(254,20),(210,24),(235,19),(255,12)],a,G)
   sigil+=line([pt(188,a+15),pt(199,a+21),pt(219,a+22)],G,3)
  sigil+=band(150,145,0,315,G,42)
 else:
  # An off-centre night crescent and two quiet pendants, with a delicate second orbit.
  shell+=band(294,214,-164,35,D)+band(282,236,-158,29,W)
  sigil+=band(270,255,-148,-42,G)
  shell+=band(260,248,55,151,G)
  for a in [58,122]:
   shell+=radial([(295,0),(262,-6),(195,0),(262,6)],a,D)
   sigil+=radial([(287,0),(260,-3),(216,0),(260,3)],a,G)
  sigil+=line([pt(206,-155),pt(192,-148),pt(186,-130)],G,3)
 node=polygon([(320,304),(333,320),(320,336),(307,320)],G)+polygon([(320,310),(327,320),(320,329),(313,320)],W)
 for kind,body in [('Shell',shell),('Sigil',sigil),('Node',node)]:
  path=SRC/f'{code}-{kind}.svg';path.write_text(svg(body),encoding='utf-8')
  subprocess.run(['magick','-background','none',str(path),str(OUT/f'{code}-{kind}.png')],check=True)
 path=SRC/f'{code}-Icon.svg';path.write_text(svg(shell+sigil),encoding='utf-8')
 subprocess.run(['magick','-background','none',str(path),str(ICONS/(code.title()+'.png'))],check=True)
print('Exported nine original silhouettes, 27 layers and nine matching inventory icons.')
