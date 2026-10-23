#!/usr/bin/env python3
"""Thirteen distinct weapon reports from existing licensed project recordings.
Requires numpy and ffmpeg. Does not alter the source recordings or legacy banks.
"""
from pathlib import Path
import hashlib, json, subprocess, wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Audio/Moonkov/Clips/WeaponIdentity'
RATE = 48000
# id, name, recorded source, playback rate, body seconds, low-mid weight,
# resonance Hz, tail seconds, interval, shots. Rate/EQ are only part of identity:
# duration, transient envelope, body resonance and tail rhythm differ as well.
SPECS = [
 (2,'Halo','Rifle',1.00,.22,.50,135,.22,.10,6),
 (3,'Revolver','Revolver',.90,.46,.85,96,.42,.75,2),
 (4,'Shotgun','Shotgun',.94,.50,1.05,82,.48,.80,2),
 (5,'Sniper','Sniper',.88,.68,1.00,68,.78,1.40,2),
 (6,'Cicada','Rifle',1.28,.12,.25,210,.13,.065,8),
 (7,'Trine','Rifle',1.08,.16,.50,172,.20,.085,3),
 (8,'Aegis','Rifle',.76,.28,1.15,91,.25,.11,6),
 (9,'Vesper','Sniper',1.20,.30,.55,145,.32,.32,3),
 (10,'Thorn','Shotgun',1.25,.29,.55,190,.29,.55,2),
 (11,'Sundown','Sniper',.63,.78,1.60,55,.90,1.05,2),
 (12,'Rondo','Revolver',1.17,.22,.60,155,.24,.07,2),
 (13,'Tempest','Shotgun',.77,.35,1.25,75,.34,.26,4),
 (14,'Nocturne','Sniper',.97,.48,.75,112,.58,.62,2),
]
records=[]
def read(kind,index):
    if kind=='Shotgun': path=ROOT/f'Assets/Audio/Clips/Weapons/Shotgun/Shoot/Shotgun_Shoot-00{index}.wav'
    else: path=ROOT/f'Assets/Audio/Moonkov/Clips/Weapons/{kind}Punch{index}.wav'
    raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(path),'-ac','1','-ar',str(RATE),'-f','f32le','-'])
    y=np.frombuffer(raw,np.float32).astype(float)
    # Remove source lead-in, preserving the first audible transient rather than its peak.
    onset=np.flatnonzero(np.abs(y)>=np.max(np.abs(y))*.025)[0]
    return y[max(0,onset-24):],path

def band(y,low,high,boost=0,resonance=100):
    size=1<<(len(y)*2-1).bit_length(); f=np.fft.rfftfreq(size,1/RATE)
    response=(f*f/(f*f+low*low))/(1+(f/high)**4)
    response*=1+boost*np.exp(-.5*((f-resonance)/max(40,resonance*.65))**2)
    return np.fft.irfft(np.fft.rfft(y,size)*response,size)[:len(y)]

def shape(y,seconds,rate,weight,resonance,variant):
    n=int(RATE*seconds); t=np.arange(n)/RATE
    x=np.interp(np.arange(n)*rate*(1+(variant-2)*.012),np.arange(len(y)),y,left=0,right=0)
    x=band(x,42,8800,weight,resonance)
    x/=max(np.max(np.abs(x)),1e-9)
    # Recorded low body, delayed by a few ms, adds weight without a sine-wave 'laser' pitch.
    low=band(x,45,340,weight,resonance); delay=int(.009*RATE)
    low=np.pad(low,(delay,0))[:n]
    x=.78*x+.32*low
    x=.72*x+.28*np.tanh(2*x)
    x*=np.exp(-t/(seconds*.8))
    x[:24]*=np.linspace(0,1,24); fade=min(n//2,int(.045*RATE))
    x[-fade:]*=np.linspace(1,0,fade)**1.5
    return x*.84/max(np.max(np.abs(x)),1e-9)

def tail(y,seconds,resonance,name):
    n=int(seconds*RATE); x=np.interp(np.arange(n)*.75+int(.018*RATE),np.arange(len(y)),y,left=0,right=0)
    x=band(x,55,1500,.4,resonance)
    # Short staggered resonances; a distinct double release for the twin-projectile Nocturne.
    base=x.copy()
    offsets=(.032,.071) if name=='Nocturne' else (.017,.039)
    for delay,gain in zip(offsets,(.38,.17)):
        k=int(delay*RATE);x[k:]+=base[:-k]*gain
    x*=np.exp(-np.arange(n)/RATE/(seconds*.36))
    x[:int(.012*RATE)]*=np.linspace(0,1,int(.012*RATE))
    x[-int(.06*RATE):]*=np.linspace(1,0,int(.06*RATE))
    return x*.62/max(np.max(np.abs(x)),1e-9)

def write(name,y,source,recipe):
    path=OUT/(name+'.wav')
    with wave.open(str(path),'wb') as w:
        w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(y*32767).astype('<i2').tobytes())
    records.append(dict(asset=str(path.relative_to(ROOT)),source=str(source.relative_to(ROOT)),
        source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
        seconds=len(y)/RATE,peak=float(np.max(np.abs(y))),rms_dbfs=float(20*np.log10(np.sqrt(np.mean(y*y)))),recipe=recipe))

OUT.mkdir(parents=True,exist_ok=True)
audition=[];cues=[];elapsed=0
for wid,name,kind,rate,duration,weight,resonance,tail_seconds,interval,count in SPECS:
    bodies=[]
    for variant in (1,2,3):
        raw,path=read(kind,variant);body=shape(raw,duration,rate,weight,resonance,variant);bodies.append(body)
        write(f'{name}Body{variant}',body,path,dict(rate=rate,weight=weight,resonance=resonance,variant=variant,processing='onset; band-shaped recorded body; delayed low body; parallel soft saturation; envelope; -1.51 dBFS peak'))
    raw,path=read(kind,1);release=tail(raw,tail_seconds,resonance,name)
    write(f'{name}Tail',release,path,dict(processing='recorded low-mid tail, two delayed reflections, exponential release',resonance=resonance))
    # Worst-case overlap check without runtime crossfades: conservative multi-shot peak.
    start=.25; section=np.zeros(int((start+interval*(count-1)+max(duration,tail_seconds)+.5)*RATE))
    for i in range(count):
        at=int((start+i*interval)*RATE)
        body=bodies[i%3]*10**(-6/20); release_mix=release*10**(-23/20)*.85
        section[at:at+len(body)]+=body;section[at:at+len(release_mix)]+=release_mix
    peak=float(np.max(np.abs(section)))
    if peak>=.94: raise RuntimeError(f'{name} accumulated peak {peak}')
    cues.append(dict(id=wid,name=name,starts_seconds=round(elapsed+start,3),seconds=len(section)/RATE,peak=peak))
    audition.append(section);elapsed+=len(section)/RATE
manifest=dict(version=1,source_licenses=['Assets/Audio/Moonkov/Licenses/FreeFirearmSoundLibrary.md','Existing project shotgun recordings retain their original license; no external download.'],weapons=cues,clips=records)
(ROOT/'Assets/Audio/Moonkov/WeaponIdentitySources.json').write_text(json.dumps(manifest,indent=2)+'\n')
preview=ROOT/'LocalData/AudioReview';preview.mkdir(parents=True,exist_ok=True)
y=np.concatenate(audition)
with wave.open(str(preview/'WeaponIdentity-Audition.wav'),'wb') as w:
    w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(y*32767).astype('<i2').tobytes())
(preview/'WeaponIdentity-Cues.json').write_text(json.dumps(cues,indent=2)+'\n')
print(json.dumps(dict(clips=len(records),seconds=elapsed,max_overlap_peak=max(x['peak'] for x in cues),audition=str(preview/'WeaponIdentity-Audition.wav')),indent=2))
