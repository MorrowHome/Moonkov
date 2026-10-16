"""Deterministic offline bake of one metric, non-tiling industrial deck material.
No source photograph, generated geometry or Blender-only shading dependency.
Python + numpy + Pillow; U is outward along bridge, V is across bridge.
"""
from pathlib import Path
import json
import numpy as np
from PIL import Image,ImageFilter
P=Path(__file__).resolve().parent;T=P/'textures';T.mkdir(exist_ok=True)
W,H=2048,1408;L,B=20.375,14.0;rng=np.random.default_rng(390176)
x=(np.arange(W,dtype=np.float32)+.5)/W*L;y=(np.arange(H,dtype=np.float32)+.5)/H*B
X,Y=np.meshgrid(x,y)
def smooth_noise(w,h):
 a=rng.uniform(0,1,(h,w));im=Image.fromarray((a*255).astype('uint8')).resize((W,H),Image.Resampling.BICUBIC)
 return np.asarray(im,dtype=np.float32)/255-.5
# 8 rows x 8 columns: 2.547 x 1.750 m replaceable steel plate pattern.
px=L/8;py=B/8;ix=np.minimum((X/px).astype(int),7);iy=np.minimum((Y/py).astype(int),7)
plate=rng.uniform(-.014,.014,(8,8))[iy,ix]
# Calm fabrication tone; no repeated mottled albedo noise.
fine=rng.normal(0,.0018,(H,W)).astype(np.float32)
longgrain=smooth_noise(175,24)
col=np.zeros((H,W,3),np.float32)+[.330,.353,.360]
col+=plate[...,None]+fine[...,None]+longgrain[...,None]*.008
# Thin gasketed butt joints: 14 mm dark core, restrained shoulder; no giant lines.
dx=np.minimum(X%px,px-X%px);dy=np.minimum(Y%py,py-Y%py);d=np.minimum(dx,dy)
seam=np.clip((.020-d)/.012,0,1);shoulder=np.exp(-d/.048)
col-=seam[...,None]*.105+shoulder[...,None]*.010
rough=np.full((H,W),.735,dtype=np.float32)+plate*.9+fine*2+rng.uniform(-.025,.025,(8,8))[iy,ix]
metal=np.full((H,W),.28,dtype=np.float32)
# Travel abrasion runs in U, confined to plausible diffuse paths and interrupted per panel.
flow=(.6+smooth_noise(21,95)*.7)
travel=np.exp(-((Y-6.3)/1.1)**2)+.6*np.exp(-((Y-3.25)/.65)**2)
wear=np.clip(travel*flow*(.5+.5*np.sin(X*.41+.7)**2),0,1)*.24
wear*=np.clip((d-.04)/.18,0,1)
wear_color=np.array([.385,.397,.390],dtype=np.float32)
col=col*(1-wear[...,None])+wear_color*wear[...,None]
rough-=wear*.38;metal+=wear*1.25
# Subtle lengthwise scuffs. Their field is anisotropic, not isotropic dirt blotches.
striations=smooth_noise(35,480);scuff=np.clip(striations-.15,0,.25)*travel*.05
col+=scuff[...,None];rough-=scuff*1.4
# Low-volume dust accumulates at side margins and joints, not uniformly over travel.
edge=np.minimum(Y,B-Y)
end=np.minimum(X,L-X)
dust=(np.exp(-edge/.24)*.33+np.exp(-end/.34)*.30+np.exp(-d/.055)*.08)*(0.65+smooth_noise(19,31)*1.1)
dust*=1-wear*.8
dust_color=np.array([.365,.339,.284],dtype=np.float32)
col=col*(1-dust[...,None])+dust_color*dust[...,None]
rough+=dust*.15;metal*=1-dust*.8
# 190 mm pitch paired diagonal non-slip tabs, about 95 x 18 mm, height under 0.8 mm.
pitch=.19;row=np.floor(Y/pitch);cx=(X/pitch+(.5*(row%2)))%1-.5;cy=(Y/pitch)%1-.5
sgn=np.where(row%2==0,1,-1);a=(cx+sgn*cy)*.7071*pitch;b=(-sgn*cx+cy)*.7071*pitch
rib=np.exp(-((a/.044)**8+(b/.009)**4))
rib*=np.clip((d-.045)/.045,0,1)
height=rib*.00075-seam*.0017-shoulder*.00015
# Height-field bake, tangent +Y points with increasing V (image bottom to top in Blender).
gy,gx=np.gradient(height,B/H,L/W);nx=-gx;ny=-gy;nz=np.ones_like(nx);length=np.sqrt(nx*nx+ny*ny+1)
norm=np.stack([nx/length,ny/length,nz/length],axis=-1)*.5+.5
col+=rib[...,None]*.0035
rough=np.clip(rough,.36,.87);metal=np.clip(metal,0,1)
def write(name,a,mode=None):
 # Mathematical rows run from V=0 upward; PNG rows run from top downward.
 a=np.flipud(np.clip(a,0,1));im=Image.fromarray(np.round(a*255).astype('uint8'),mode=mode);im.save(T/name,optimize=True)
write('R39_Deck0176_BaseColor_sRGB.png',col,'RGB')
write('R39_Deck0176_Normal_OpenGL.png',norm,'RGB')
write('R39_Deck0176_Roughness_Linear.png',rough,'L')
ms=np.zeros((H,W,4),np.float32);ms[...,0]=metal;ms[...,3]=1-rough
write('R39_Deck0176_MetallicSmoothness_Linear.png',ms,'RGBA')
recipe={'seed':390176,'dimensions_px':[W,H],'metric_surface_m':[L,B],'plates':[8,8],'plate_size_m':[px,py],'seam_dark_core_m':.014,'seam_shoulder_m':.048,'antislip_pitch_m':pitch,'antislip_tab_size_m':[.095,.018],'antislip_max_height_m':.00075,'dust_side_falloff_m':.24,'dust_end_falloff_m':.34,'UV':'U outward along bridge, V toward R38 service bay; one non-tiling atlas for selected top face','images':'PNG; BaseColor sRGB, Normal tangent OpenGL +Y, Roughness linear, MetallicSmoothness linear R=metallic A=1-roughness','method':'Deterministic metric procedural surface rasterized offline into PBR maps. No appearance reliance on Blender-only procedural nodes.','scope':'Only B_NORTH_TRANSFER_Deck_0176 polygon 4; original geometry unchanged'}
(P/'R39_Surface_Recipe.json').write_text(json.dumps(recipe,indent=2));print(json.dumps(recipe,indent=2))
