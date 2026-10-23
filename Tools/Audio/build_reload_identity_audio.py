#!/usr/bin/env python3
"""Build recorded reload montages; numpy + ffmpeg. Originals remain untouched."""
from pathlib import Path
import hashlib, json, re, subprocess, wave
import numpy as np
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT/'Assets/Audio/Moonkov/Clips/ReloadIdentity'
RATE = 48000
SOURCES = {
 'energy':'Assets/Audio/Moonkov/Clips/Weapons/HaloRecharge.wav',
 'cylinder':'Assets/Audio/Moonkov/Clips/Weapons/RevolverRecharge.wav',
 'rifle':'Assets/Audio/Clips/Weapons/Rifle/Reload/rifle_reload.wav',
 'shotgun':'Assets/Audio/Clips/Weapons/Shotgun/Reload/Shotgun_reload 1.wav',
 'pistol':'Assets/Audio/Clips/Weapons/Pistol/Reload/Pistol_Reload.wav',
 'lever':'Assets/Audio/Moonkov/Clips/Interaction/Equipment.wav',
}
# Distinct recorded mechanisms, cadence, weight, and charge-bed balance, not just random pitch.
SPECS = [
 ('Halo','rifle',1.0,6500,.28,[.03,.28,.57,.85]),
 ('Revolver','cylinder',1.08,7600,.03,[.02,.27,.49,.82]),
 ('Shotgun','shotgun',.88,4800,.08,[.02,.33,.56,.84]),
 ('Sniper','rifle',.73,4300,.15,[.02,.19,.61,.87]),
 ('Cicada','pistol',1.24,10000,.25,[.02,.22,.51,.82]),
 ('Trine','lever',1.08,7800,.34,[.02,.31,.43,.55,.86]),
 ('Aegis','rifle',.64,3200,.23,[.02,.18,.39,.66,.88]),
 ('Vesper','pistol',.93,6100,.17,[.02,.38,.69,.87]),
 ('Thorn','shotgun',1.13,8200,.20,[.02,.24,.36,.66,.85]),
 ('Sundown','shotgun',.60,2400,.46,[.02,.21,.60,.88]),
 ('Rondo','cylinder',1.10,6600,.18,[.02,.23,.34,.68,.79]),
 ('Tempest','shotgun',.77,3900,.30,[.02,.20,.40,.60,.85]),
 ('Nocturne','rifle',.86,5000,.37,[.02,.25,.37,.67,.80]),
]
def read(path):
 raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(ROOT/path),'-ac','1','-ar',str(RATE),'-f','f32le','-'])
 return np.frombuffer(raw,dtype='<f4').astype(float)
def shape(x,rate,cutoff):
 x=np.interp(np.arange(int(len(x)/rate))*rate,np.arange(len(x)),x)
 size=1<<(len(x)*2-1).bit_length(); f=np.fft.rfftfreq(size,1/RATE)
 x=np.fft.irfft(np.fft.rfft(x,size)*(f*f/(f*f+60**2))/(1+(f/cutoff)**4),size)[:len(x)]
 fade=min(len(x)//3,int(.008*RATE)); x[:fade]*=np.linspace(0,1,fade); x[-fade:]*=np.linspace(1,0,fade)
 return x/max(1e-9,np.max(np.abs(x)))
def gestures(x):
 # Extract separated recorded transients, preserving their own attack/tail.
 frame=int(.025*RATE); n=len(x)//frame
 levels=np.array([np.sqrt(np.mean(x[i*frame:(i+1)*frame]**2)) for i in range(n)])
 chosen=[]
 for index in np.argsort(levels)[::-1]:
  if all(abs(index-other)>7 for other in chosen): chosen.append(int(index))
  if len(chosen)==8: break
 return [x[max(0,i*frame-int(.018*RATE)):min(len(x),i*frame+int(.18*RATE))] for i in sorted(chosen)]
def save(path,x):
 with wave.open(str(path),'wb') as w:
  w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(np.round(x*32767).astype('<i2').tobytes())
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
raw={key:read(path) for key,path in SOURCES.items()}
parts={key:gestures(value) for key,value in raw.items()}
OUT.mkdir(parents=True,exist_ok=True)
records=[]; previews=[]; cues=[]; elapsed=0
for index,(name,kind,rate,cutoff,charge,positions) in enumerate(SPECS):
 weapon=ROOT/f'Assets/DollSinger/{name}Weapon.asset'
 duration=float(re.search(r'  ReloadTime: (\S+)',weapon.read_text())[1])
 for variant in range(3):
  result=np.zeros(round(duration*RATE)); layers=[]
  for step,fraction in enumerate(positions):
   source='lever' if step==len(positions)-1 else kind
   part_index=(step+variant+index)%len(parts[source])
   chunk=shape(parts[source][part_index],rate*(1+(variant-1)*.025),cutoff)
   # Different physical actions retain the same start/middle/latch milestones.
   at=int(max(0,fraction*duration+(variant-1)*.008*(step%2))*RATE)
   chunk=chunk[:min(len(chunk),len(result)-at)]
   gain=.65 if step==len(positions)-1 else .72 if step==0 else .48
   result[at:at+len(chunk)]+=chunk*gain
   layers.append(dict(source=source,gesture=part_index,at_seconds=at/RATE,rate=rate*(1+(variant-1)*.025),gain=gain))
  # Underlying recorded charging motion, stretched independently of mechanical accents.
  bed=raw['energy']; bed=np.interp(np.linspace(0,len(bed)-1,len(result)),np.arange(len(bed)),bed)
  envelope=np.sin(np.linspace(0,np.pi,len(result)))**1.3
  result+=bed/max(1e-9,np.max(np.abs(bed)))*envelope*charge
  result-=np.mean(result); result*=.78/max(1e-9,np.max(np.abs(result)))
  fade=int(.012*RATE); result[:fade]*=np.linspace(0,1,fade); result[-fade:]*=np.linspace(1,0,fade)
  assert np.all(np.isfinite(result)) and np.max(np.abs(result))<.8
  path=OUT/f'{name}Reload{variant+1}.wav'; save(path,result)
  records.append(dict(asset=str(path.relative_to(ROOT)),sha256=digest(path),seconds=duration,peak=float(np.max(np.abs(result))),layers=layers,charge_gain=charge,cutoff=cutoff))
  if variant==0:
   cues.append(dict(name=name,at_seconds=round(elapsed,3),seconds=duration)); previews.extend([result*10**(-12/20),np.zeros(int(.35*RATE))]); elapsed+=duration+.35
manifest=dict(version=1,sample_rate=RATE,processing='Recorded transient montage, rate/EQ, recorded charge bed, fades; peak <= 0.78',sources=[dict(key=k,asset=p,sha256=digest(ROOT/p)) for k,p in SOURCES.items()],license='Existing project recordings. Moonkov derived clips retain Sources.json/Edits.json Sonniss provenance; original FPS reload clips retain existing project licenses. No downloads.',clips=records)
(ROOT/'Assets/Audio/Moonkov/ReloadIdentitySources.json').write_text(json.dumps(manifest,indent=2)+'\n')
preview=ROOT/'LocalData/AudioReview';preview.mkdir(parents=True,exist_ok=True)
save(preview/'ReloadIdentity-Audition.wav',np.concatenate(previews))
(preview/'ReloadIdentity-Cues.json').write_text(json.dumps(cues,indent=2)+'\n')
print(json.dumps(dict(clips=len(records),preview_seconds=elapsed,bytes=sum((ROOT/r['asset']).stat().st_size for r in records)),indent=2))
