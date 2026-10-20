"""Flat filled vector masters and transparent production sprites (CairoSVG, no bloom baked in)."""
from pathlib import Path
import math,cairosvg
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/HaloArsenal';OUT=ROOT/'Assets/DollSinger/Art/HaloArsenal';ICONS=ROOT/'Assets/Resources/HaloIcons'
for p in [SRC,OUT,ICONS]:p.mkdir(parents=True,exist_ok=True)
G='#e6c779';W='#fff3d2';D='#ad8847';S='#e6e9ed'
def pt(r,a):a=math.radians(a);return (320+r*math.cos(a),320+r*math.sin(a))
def polygon(points,color=G):return '<polygon points="'+' '.join(f'{x:.3f},{y:.3f}' for x,y in points)+f'" fill="{color}"/>'
def arcband(outer,inner,a,b,color=G,n=24):return polygon([pt(outer,a+(b-a)*i/n) for i in range(n+1)]+[pt(inner,b-(b-a)*i/n) for i in range(n+1)],color)
def disc(x,y,r,color=G):return f'<circle cx="{x}" cy="{y}" r="{r}" fill="{color}"/>'
def rect(x,y,w,h,color=G):return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{color}"/>'
def svg(body):return '<svg xmlns="http://www.w3.org/2000/svg" width="640" height="640" viewBox="0 0 640 640">'+body+'</svg>'
def cut(body,hole):
 outer=ET.fromstring(body);inner=ET.fromstring(hole)
 points=inner.attrib.get('points')
 if points is None:
  x=float(inner.attrib['cx']);y=float(inner.attrib['cy']);r=float(inner.attrib['r'])
  points=' '.join(f'{x+r*math.cos(i*math.tau/96):.3f},{y+r*math.sin(i*math.tau/96):.3f}' for i in range(96))
 return f'<path d="M {outer.attrib["points"]} Z M {points} Z" fill="{outer.attrib["fill"]}" fill-rule="evenodd"/>'

for code in ['cicada','trine','aegis','vesper','thorn','sundown','rondo']:
 core='';accent=''
 if code=='cicada':
  # Two broad, swept sabre wings; deliberately asymmetric tips and a fork crown.
  core+=polygon([(62,364),(82,210),(174,88),(256,73),(185,166),(190,296),(205,388),(120,496),(104,414)],G)
  core+=polygon([(578,276),(558,430),(466,552),(384,567),(455,474),(450,344),(435,252),(520,144),(536,226)],G)
  core+=polygon([(270,60),(302,60),(302,192),(281,214),(270,179)],W)+polygon([(338,60),(370,60),(370,179),(359,214),(338,192)],W)
  accent+=polygon([(93,314),(124,200),(185,126),(159,228),(150,290),(170,345),(148,380)],W)
  accent+=polygon([(547,326),(516,440),(455,514),(481,412),(490,350),(470,295),(492,260)],W)
  accent+=polygon([(279,445),(301,445),(301,563),(284,589)],D)+polygon([(339,445),(361,445),(356,589),(339,563)],D)
 elif code=='trine':
  # Three independent interlocking plates around a triangular aperture.
  outer=[pt(302,-90+i*120) for i in range(3)];inner=[pt(170,-90+i*120) for i in range(3)]
  def mix(a,b,t):return (a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t)
  for i in range(3):
   n=(i+1)%3;p=(i-1)%3
   core+=polygon([outer[i],mix(outer[i],outer[n],.44),mix(inner[i],inner[n],.44),inner[i],mix(inner[i],inner[p],.44),mix(outer[i],outer[p],.44)],G)
   a=-90+i*120
   accent+=polygon([pt(270,a),pt(205,a-15),pt(205,a+15)],W)
   accent+=polygon([pt(204,a+24),pt(232,a+22),pt(249,a+12),pt(219,a+15)],D)
 elif code=='aegis':
  for i in range(8):
   a=i*45
   core+=arcband(276,157,a+4,a+40,W if i%2 else G,n=3)
   accent+=arcband(258,223,a+11,a+32,D if i%2 else W,n=2)
  for a in range(0,360,90):
   core+=polygon([pt(264,a-5),pt(302,a-3),pt(302,a+3),pt(264,a+5)],G)
 elif code=='vesper':
  # A single-edged moon bow and two long filled blades around a broad empty diamond.
  outer=polygon([(320,26),(533,320),(320,614),(107,320)],W)
  hole=polygon([(320,174),(426,320),(320,466),(214,320)],'black')
  core=cut(outer,hole)
  core+=arcband(275,222,108,253,G,n=24)
  accent+=polygon([(320,47),(485,320),(451,320),(320,123)],G)+polygon([(320,593),(155,320),(189,320),(320,517)],G)
  accent+=polygon([(93,288),(143,320),(93,352),(65,320)],G)
 elif code=='thorn':
  # Five separate broad knife petals, not a connected star-shaped ring.
  for i in range(5):
   a=-90+i*72
   core+=polygon([pt(137,a),pt(175,a-23),pt(210,a-27),pt(300,a),pt(210,a+27),pt(175,a+23)],G)
   accent+=polygon([pt(282,a),pt(211,a-14),pt(166,a),pt(211,a+14)],W)
   accent+=polygon([pt(242,a+15),pt(217,a+20),pt(186,a+20),pt(204,a+14)],D)
 elif code=='rondo':
  # Paired counter-rotating magatama seals; two broad comma bodies.
  for a,color in [(190,W),(10,G)]:
   core+=arcband(278,163,a,a+135,color,n=24)
   core+=polygon([pt(278,a+124),pt(163,a+124),pt(296,a+158)],color)
   x,y=pt(215,a+5);core+=disc(x,y,49,color)
   accent+=disc(x,y,26,D if color==W else W)
   accent+=arcband(245,211,a+36,a+87,G if color==W else W,n=12)
 else:
  for a,b in [(-163,-67),(-53,39),(53,133)]:core+=arcband(278,160,a,b,G,n=8)
  for a,b in [(-152,-119),(-41,-9),(65,96)]:accent+=arcband(257,201,a,b,W,n=4)
  core+=disc(320,527,68,G)+polygon([(253,533),(272,586),(320,611),(368,586),(387,533)],G)
  accent+=disc(304,510,41,W)+polygon([(363,531),(349,574),(321,589),(338,549)],D)
  accent+=polygon([(61,280),(115,303),(115,337),(61,359)],W)
 node=polygon([(320,304),(334,320),(320,336),(306,320)],G)+polygon([(320,310),(328,320),(320,327),(312,320)],W)
 for kind,body in [('Shell',core),('Sigil',accent),('Node',node)]:
  path=SRC/f'{code}-{kind}.svg';path.write_text(svg(body));cairosvg.svg2png(url=str(path),write_to=str(OUT/f'{code}-{kind}.png'))
 # Namespace masks so the icon and composed production layers render identically.
 path=SRC/f'{code}-Icon.svg';path.write_text(svg(core+accent));cairosvg.svg2png(url=str(path),write_to=str(ICONS/(code.title()+'.png')))
print('Independent flat filled components and icons exported from vector masters.')
