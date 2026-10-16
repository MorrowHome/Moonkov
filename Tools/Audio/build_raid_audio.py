#!/usr/bin/env python3
"""Offline foley/creature editing. Source archives, credits and SHA256s in RaidAudioSources.json.
Requires numpy + ffmpeg; archives extracted under LocalData/RaidAudio/Sources.
Only writes new Raid/ clips. Never normalizes an entire bus or edits protected gun/UI/music clips.
"""
from pathlib import Path
import hashlib, json, subprocess, wave
import numpy as np
ROOT=Path(__file__).resolve().parents[2]; SRC=ROOT/'LocalData/RaidAudio/Sources'; OUT=ROOT/'Assets/Audio/Moonkov/Clips/Raid'
OUT.mkdir(parents=True,exist_ok=True); RATE=48000; records=[]; clips={}
def read(p):
 return np.frombuffer(subprocess.check_output(['ffmpeg','-v','error','-i',str(p),'-ar',str(RATE),'-ac','1','-f','f32le','-']),'<f4').astype(float)
def speed(x,rate): return np.interp(np.arange(0,len(x)-1,rate),np.arange(len(x)),x)
def eq(x,low=45,high=6500):
 n=1<<(len(x)*2-1).bit_length(); f=np.fft.rfftfreq(n,1/RATE)
 return np.fft.irfft(np.fft.rfft(x,n)*(f*f/(f*f+low*low))/(1+(f/high)**4),n)[:len(x)]
def mix(*layers):
 y=np.zeros(max(len(x) for x in layers))
 for x in layers:y[:len(x)]+=x
 return y
def delay(x,seconds):return np.pad(x,(int(seconds*RATE),0))
def write(name,x,sources,description,peak=.75):
 x=eq(x,35,11000); x-=x.mean(); n=min(len(x)//3,int(.004*RATE)); x[:n]*=np.linspace(0,1,n)
 n=min(len(x)//3,int(.045*RATE)); x[-n:]*=np.linspace(1,0,n)**1.5
 x*=peak/max(np.max(np.abs(x)),1e-9)
 p=OUT/(name+'.wav')
 with wave.open(str(p),'wb') as w:
  w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(x*32767).astype('<i2').tobytes())
 clips[name]=x
 records.append(dict(asset=p.relative_to(ROOT).as_posix(),sources=[dict(path=q.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(q.read_bytes()).hexdigest()) for q in sources],processing=description,seconds=round(len(x)/RATE,4),peak_dbfs=round(20*np.log10(peak),2),sha256=hashlib.sha256(p.read_bytes()).hexdigest()))
feet=SRC/'Footsteps/footsteps'; monsters=SRC/'Monsters/monster_sfx_pack_2'
for surface,folder,rate in [('Regolith','gravel',.9),('Metal','metal',.91),('Hard','boots',.94)]:
 for i in range(8):
  p=feet/folder/(str(i)+'.ogg'); x=eq(speed(read(p),rate),65,4300 if surface=='Regolith' else 5800)
  # Preserve sole/transient detail; body is a filtered duplicate of the actual contact.
  x=mix(x,eq(x,60,300)*.28)
  write(surface+str(i+1),x,[p],f'Recorded {folder} contact; speed {rate}; HP65Hz, restrained HF, low contact body; 4ms/45ms edge fades; peak -2.5dBFS')
for cue,ids,rate,seconds in [('Warning',[1,4,9],.67,1.15),('Strike',[2,6,8],.77,.55),('Hurt',[10,12,15],.88,.45),('Death',[5,14,17],.6,1.1),('Idle',[3,7,13],.62,1.3)]:
 for i,num in enumerate(ids,1):
  p=monsters/f'monster-{num}.wav'; raw=read(p); x=eq(speed(raw,rate),60,4500)
  # Parallel formant/body layer uses the same performance: an organic throat, no sine-wave beeps.
  sub=eq(speed(raw,rate*.74),45,650)*.32
  x=mix(x,sub,delay(eq(x,120,1900)*.16,.037),delay(eq(x,150,1300)*.08,.083))
  if len(x)>RATE*seconds:x=x[:int(RATE*seconds)]
  write('Alien'+cue+str(i),x,[p],f'Ogrebane mouth performance; varispeed {rate}, parallel low throat at {rate*.74:.3f}; HP60Hz, LP4500Hz; 37/83ms damped resonances; edge fades; peak -2.5dBFS')
for i in range(1,5):
 p=feet/'bones'/f'{i-1}.ogg'; q=feet/'gravel'/f'{i+2}.ogg'
 x=mix(eq(speed(read(p),.82),110,4200),delay(eq(speed(read(q),1.22),160,2400)*.3,.016))
 write('AlienContact'+str(i),x,[p,q],'Shell/bone articulation + dry gravel shear; separate contacts at real gait cadence')
# Detailed equipment cues retain their field-recorded textures.
base=ROOT/'Assets/Audio/Moonkov/Clips'
def existing(rel):return base/rel
for name,rel,rate,length in [('Aim','Character/Cloth1.wav',1.2,.15),('Light','Interaction/Equipment.wav',1.7,.09),('DryFire','Interaction/Equipment.wav',1.9,.075),('ReloadReady','Interaction/Equipment.wav',1.12,.22),('Medical','Character/Cloth2.wav',.85,.65),('Pickup','Interaction/Container.wav',1.3,.28),('OxygenRefill','Character/Cloth1.wav',.65,.7)]:
 p=existing(rel); x=speed(read(p),rate)[:int(length*RATE)]
 write(name,x,[p],'Short edited existing licensed foley, trimmed, varispeed and edge fades',.6)
# Electronic suit telemetry is intentionally designed, unlike organic creature vocals.
rng=np.random.default_rng(20261016)
t=np.arange(int(RATE*2.8))/RATE
noise=eq(rng.normal(0,1,len(t)),180,1800)
env=np.sin(np.pi*np.clip(t/1.1,0,1))**1.7+.65*np.sin(np.pi*np.clip((t-1.45)/1.25,0,1))**1.6
write('SuitBreath',noise*env,[],'Seeded filtered airflow; separated inhale/exhale; no outdoor wind; peak -8dBFS',.4)
t=np.arange(int(RATE*.95))/RATE
heart=np.zeros_like(t)
for onset,freq,gain in [(0,58,1),(.19,72,.62)]:
 v=np.maximum(t-onset,0); heart+=gain*np.sin(2*np.pi*(freq*v-12*v*v))*np.exp(-v*25)*(t>=onset)*np.minimum(v/.012,1)
write('Heartbeat',heart,[],'Double soft thoracic pulse; HP35Hz; critical-health use only',.55)
for name,notes in [('SuitWarning',[(0,780,.12),(.22,620,.15)]),('ExtractStart',[(0,540,.13),(.15,810,.18)]),('ExtractCancel',[(0,640,.12),(.15,420,.22)]),('ExtractTick',[(0,920,.065)])]:
 t=np.arange(int(RATE*.65))/RATE;x=np.zeros_like(t)
 for at,f,duration in notes:
  v=t-at; env=np.sin(np.pi*np.clip(v/duration,0,1))**2
  x+=(np.sin(2*np.pi*f*v)+.12*np.sin(2*np.pi*f*2*v))*env
 write(name,x,[],'Designed restrained suit telemetry; sine-squared envelopes; no harsh square-wave alarm',.55)
manifest=dict(collections=[dict(name='Monster Sound Effects 2',author='Ogrebane',license='CC0-1.0',url='https://opengameart.org/content/monster-sound-effects-2',archive='https://opengameart.org/sites/default/files/monster_sfx_pack_2.zip'),dict(name='Footsteps on different surfaces',author='congusbongus (mastering); Ali_6868 (gravel); Eelke (metal); swuing (boots); blukotek (bones)',license='CC0-1.0 gravel/bones; CC-BY-3.0 metal/boots',url='https://opengameart.org/content/footsteps-on-different-surfaces',archive='https://opengameart.org/sites/default/files/footsteps_0.zip')],clips=records)
(ROOT/'Assets/Audio/Moonkov/RaidAudioSources.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
y=np.zeros(RATE*22)
for name,at,gain in [('Regolith1',.2,.7),('Regolith3',.9,.7),('Regolith5',1.6,.7),('Metal1',3,.6),('Metal4',3.7,.6),('Metal6',4.4,.6),('AlienIdle1',6,.5),('AlienWarning2',8.2,.65),('AlienStrike1',10,.7),('AlienHurt2',11.5,.6),('AlienDeath3',13,.7),('AlienContact1',15,.5),('AlienContact3',15.3,.5),('SuitBreath',17,.35),('SuitWarning',20.4,.4)]:
 x=clips[name]*gain;start=int(at*RATE); y[start:start+len(x)]+=x
with wave.open(str(ROOT/'LocalData/RaidAudio/Raid-Audition.wav'),'wb') as w:
 w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(np.clip(y,-1,1)*32767).astype('<i2').tobytes())
print(f'{len(records)} clips; {sum(r["seconds"] for r in records):.1f}s mono PCM; peak {max(r["peak_dbfs"] for r in records)}dBFS')

# StreamingAssets is included in standalone players, preserving required CC BY credits on redistribution.
credits = ROOT/"Assets/StreamingAssets/MoonkovRaidAudioCredits.txt"
credits.parent.mkdir(parents=True, exist_ok=True)
credits.write_text((ROOT/"Assets/Audio/Moonkov/Licenses/RaidAudio.md").read_text())
