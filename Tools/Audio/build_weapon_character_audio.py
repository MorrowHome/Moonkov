#!/usr/bin/env python3
"""Distinct revolver, shotgun and precision reports. Never edits the red rifle.
Uses existing CC0/project recordings, numpy and ffmpeg. No pitch shifting.
"""
from pathlib import Path
import hashlib,json,subprocess,wave
import numpy as np
ROOT=Path(__file__).resolve().parents[2]; RATE=48000
OUT=ROOT/'Assets/Audio/Moonkov/Clips/WeaponCharacter'; OUT.mkdir(parents=True,exist_ok=True)
PREVIEW=ROOT/'LocalData/WeaponCharacter';PREVIEW.mkdir(parents=True,exist_ok=True)
SOURCES=ROOT/'LocalData/ShootingAV/Sources/Prepared SFX Library'
SPECS=[
 ('Revolver',.34,.62,100, .85,-5.5,-18, [('Smith & Wesson 642/V_27P.wav',.805),('Smith & Wesson 642/V_27P.wav',6.215),('Smith & Wesson 642/V_22P.wav',.67)]),
 ('Shotgun',.28,.52,150,.8,-5,-18,[(f'Assets/Audio/Clips/Weapons/Shotgun/Shoot/Shotgun_Shoot-00{i}.wav',None) for i in (1,2,3)]),
 ('Sniper',.58,1.4,85,1.6,-4.5,-16,[('Mosin Nagant/M_26P.wav',1.15),('Mosin Nagant/M_26P.wav',6.13),('Mosin Nagant/M_26P.wav',10.75)])]
records=[];cache={}; rendered={};cues=[]
def read(name):
    path=ROOT/name if name.startswith('Assets/') else SOURCES/name
    if str(path) not in cache:
        raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(path),'-ac','1','-ar',str(RATE),'-f','f32le','-'])
        cache[str(path)]=np.frombuffer(raw,np.float32).astype(np.float64)
    return cache[str(path)],path

def extract(name,event,seconds):
    y,path=read(name)
    a=max(0,int((event-.07)*RATE)) if event is not None else 0
    b=min(len(y),int((event+.05)*RATE)) if event is not None else len(y)
    region=np.abs(y[a:b]); hit=np.flatnonzero(region>=region.max()*.025)
    start=max(0,a+int(hit[0])-12)
    n=round(seconds*RATE)
    return np.pad(y[start:start+n],(0,max(0,n-len(y[start:start+n])))),path,start/RATE

def eq(x,low,high,shelf=0):
    n=1<<(len(x)*2-1).bit_length();f=np.fft.rfftfreq(n,1/RATE)
    response=(f*f/(f*f+low*low))/(1+(f/high)**4)*(1+shelf/(1+(f/190)**4))
    return np.fft.irfft(np.fft.rfft(x,n)*response,n)[:len(x)]

def finish(x,peak,fade):
    x=x.copy();x[:12]*=np.linspace(0,1,12)
    n=min(round(fade*RATE),len(x)//2);x[-n:]*=np.linspace(1,0,n)**1.5
    x*=peak/max(float(np.max(np.abs(x))),1e-9)
    return x

def write(name,x,path,offset,role):
    p=OUT/(name+'.wav')
    with wave.open(str(p),'wb') as f:
        f.setnchannels(1);f.setsampwidth(2);f.setframerate(RATE);f.writeframes(np.round(x*32767).astype('<i2').tobytes())
    source=str(path.relative_to(ROOT))
    records.append(dict(asset=str(p.relative_to(ROOT)),source=source,source_start_seconds=round(offset,6),
        source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),sha256=hashlib.sha256(p.read_bytes()).hexdigest(),role=role,
        seconds=len(x)/RATE,peak=float(np.max(np.abs(x))),rms_dbfs=float(20*np.log10(np.sqrt(np.mean(x*x)))),
        processing='Original playback rate; DC/highpass and low shelf; natural report decay; 0.25ms onset and ending fade; peak normalized; mono PCM16 48kHz'))

for name,body_seconds,tail_seconds,body_low,interval,body_db,tail_db,takes in SPECS:
    bodies=[]
    for i,(source,event) in enumerate(takes,1):
        raw,path,offset=extract(source,event,max(body_seconds,tail_seconds+.045))
        body=eq(raw[:round(body_seconds*RATE)],38,12500,.12 if name=='Revolver' else .35 if name=='Shotgun' else .22)
        body=finish(body,.86,.05 if name!='Sniper' else .10)
        write(name+'Body'+str(i),body,path,offset,'near report');bodies.append(body)
        if i==1:
            tail=raw[round(.045*RATE):round((.045+tail_seconds)*RATE)]
            tail=eq(tail,55,2400,.15);tail[:round(.025*RATE)]*=np.linspace(0,1,round(.025*RATE))
            tail=finish(tail,.62,.12)
            write(name+'Tail',tail,path,offset+.045,'natural recorded tail')
    rendered[name]=(bodies,tail,interval,body_db,tail_db)

# Three before/after groups at their configured gains: old two shots, then new two.
parts=[];elapsed=0
for name,_,_,_,interval,body_db,tail_db,_ in SPECS:
    for version in ('before','after'):
        length=round((.25+interval+2)*RATE); y=np.zeros(length)
        if version=='before':
            def old(stem):
                p=ROOT/f'Assets/Audio/Moonkov/Clips/WeaponIdentity/{name}{stem}.wav'
                with wave.open(str(p),'rb') as f:return np.frombuffer(f.readframes(f.getnframes()),'<i2').astype(float)/32768
            bodies=[old('Body1'),old('Body2')];tail=old('Tail');bg=10**(-6/20);tg=.85*10**(-23/20)
        else:
            bodies,tail,_,_,_=rendered[name];bg=10**(body_db/20);tg=.85*10**(tail_db/20)
        for i in range(2):
            start=round((.25+i*interval)*RATE)
            body=bodies[i];y[start:start+len(body)]+=body*bg;y[start:start+len(tail)]+=tail*tg
        assert np.max(np.abs(y))<.95,(name,version,np.max(np.abs(y)))
        cues.append(dict(weapon=name,version=version,start=round(elapsed,3),seconds=len(y)/RATE,peak=float(np.max(np.abs(y)))))
        parts.append(y);elapsed+=len(y)/RATE
mix=np.concatenate(parts)
with wave.open(str(PREVIEW/'WeaponCharacter-AB.wav'),'wb') as f:
    f.setnchannels(1);f.setsampwidth(2);f.setframerate(RATE);f.writeframes(np.round(mix*32767).astype('<i2').tobytes())
(PREVIEW/'WeaponCharacter-Cues.json').write_text(json.dumps(cues,indent=2)+'\n')
(ROOT/'Assets/Audio/Moonkov/WeaponCharacterSources.json').write_text(json.dumps(dict(version=1,
    licenses=['Assets/Audio/Moonkov/Licenses/FreeFirearmSoundLibrary.md','Existing project shotgun recordings retain their original license.'],
    rifle_unchanged=True,clips=records),indent=2)+'\n')
print(json.dumps(dict(clips=len(records),seconds=elapsed,audition=str(PREVIEW/'WeaponCharacter-AB.wav'),cues=cues),indent=2))
