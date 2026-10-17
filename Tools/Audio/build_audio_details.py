#!/usr/bin/env python3
"""Build contact/gear detail banks from CC0 recordings. numpy + ffmpeg required.
Extract Scrapes.zip and KenneyRPG.zip in LocalData/AudioDetails/Sources/{Scrapes,Kenney}.
Uses existing CC0 AlienContact clips as a quiet exoskeleton articulation layer.
Only writes Clips/Details, AudioDetailSources.json and distributable detail credits.
"""
from pathlib import Path
import subprocess, hashlib, json, wave
import numpy as np
ROOT=Path(__file__).resolve().parents[2]; SOURCE=ROOT/'LocalData/AudioDetails/Sources'
OUT=ROOT/'Assets/Audio/Moonkov/Clips/Details'; RATE=48000; records=[]; made={}
OUT.mkdir(parents=True,exist_ok=True)
def read(p):return np.frombuffer(subprocess.check_output(['ffmpeg','-v','error','-i',str(p),'-ac','1','-ar',str(RATE),'-f','f32le','-']),'<f4').astype(float)
def speed(x,r):return np.interp(np.arange(0,len(x)-1,r),np.arange(len(x)),x)
def eq(x,lo,hi):
 n=1<<(len(x)*2-1).bit_length();f=np.fft.rfftfreq(n,1/RATE)
 return np.fft.irfft(np.fft.rfft(x,n)*(f*f/(f*f+lo*lo))/(1+(f/hi)**4),n)[:len(x)]
def normalize(x):return x/max(np.max(np.abs(x)),1e-9)
def part(p,start,length,rate=1):return speed(read(p)[int(start*RATE):int((start+length)*RATE)],rate)
def blend(*xs):
 y=np.zeros(max(len(x) for x in xs))
 for x in xs:y[:len(x)]+=x
 return y
def write(name,x,paths,recipe):
 x=eq(x,75,9000);x-=x.mean(); n=min(int(.018*RATE),len(x)//4);x[:n]*=np.sin(np.linspace(0,np.pi/2,n))**2
 n=min(int(.07*RATE),len(x)//3);x[-n:]*=np.linspace(1,0,n)**1.5;x=normalize(x)*.6
 p=OUT/(name+'.wav')
 with wave.open(str(p),'wb') as w:
  w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(x*32767).astype('<i2').tobytes())
 made[name]=x
 records.append(dict(asset=p.relative_to(ROOT).as_posix(),sources=[dict(path=q.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(q.read_bytes()).hexdigest()) for q in paths],processing=recipe+'; HP75Hz; 18ms/70ms fades; -4.44dBFS peak; mono PCM16/48k',seconds=round(len(x)/RATE,4),sha256=hashlib.sha256(p.read_bytes()).hexdigest()))
k=SOURCE/'Kenney/OGG'; s=SOURCE/'Scrapes'
for i in range(1,4):
 drag=s/f'scrape-{i+2}.wav'; cloth=k/f'cloth{i}.ogg'; belt=k/('clothBelt.ogg' if i!=2 else 'clothBelt2.ogg');metal=k/'metalLatch.ogg'
 friction=normalize(eq(part(drag,.17,.45,.9+i*.03),160,5200))
 fabric=normalize(eq(part(cloth,0,.45,.92),120,3800))
 ring=normalize(eq(part(metal,0,.18,.78+i*.04),300,6500))
 shell=ROOT/f'Assets/Audio/Moonkov/Clips/Raid/AlienContact{i}.wav';bone=normalize(eq(read(shell),140,3600))
 write(f'SuitWallBrush{i}',blend(fabric*.8,eq(friction,180,2300)*.18),[cloth,drag],'Fabric contact with restrained abrasive surface shear')
 write(f'SuitWallMetal{i}',blend(fabric*.8,friction*.18,ring*.09),[cloth,drag,metal],'Fabric contact, brighter shear and soft real metal fastening resonance')
 write(f'AlienWallScrape{i}',blend(friction*.8,bone*.17),[drag,shell],'Recorded cinder-block scraping with quiet shell articulation')
 write(f'AlienWallMetal{i}',blend(friction*.6,bone*.13,ring*.19),[drag,shell,metal],'Scrape with shell articulation and recorded metallic resonance')
 heavy=normalize(eq(part(belt,0,.7,.86+i*.03),100,3000))
 write(f'Crawl{i}',blend(heavy*.85,eq(friction,140,2000)*.22),[belt,drag],'Long cloth/belt pull and low floor friction, no heel strike')
 write(f'StanceChange{i}',blend(heavy[:int(.42*RATE)]*.8,fabric*.3),[belt,cloth],'Short clothing compression and harness adjustment')
 leather=k/('beltHandle1.ogg' if i==1 else 'beltHandle2.ogg');gear=normalize(eq(part(leather,0,.32,1+i*.025),180,3600))
 write(f'GearMotion{i}',blend(gear*.75,fabric[:int(.22*RATE)]*.3,ring*.05),[leather,cloth,metal],'Quiet belt/fabric articulation with restrained fastening detail')
 creak=k/('creak1.ogg' if i!=2 else 'creak2.ogg');body=normalize(eq(part(creak,0,.5,.72+i*.04),120,1900))
 write(f'AlienIdleMotion{i}',blend(body*.45,bone*.55),[creak,shell],'Low short body creak and shell tension; no generated vocal tone')
manifest=dict(collections=[dict(name='Scrapes',author='AntumDeluge',license='CC0-1.0',url='https://opengameart.org/content/scrapes',download='https://opengameart.org/sites/default/files/scrapes.zip',sha256=hashlib.sha256((SOURCE/'Scrapes.zip').read_bytes()).hexdigest()),dict(name='50 RPG sound effects',author='Kenney',license='CC0-1.0',url='https://opengameart.org/node/21999',download='https://opengameart.org/sites/default/files/RPGsounds_Kenney.zip',sha256=hashlib.sha256((SOURCE/'KenneyRPG.zip').read_bytes()).hexdigest())],existing_shell_layer='AlienContact: see RaidAudioSources.json / RaidAudio.md (CC0)',clips=records)
(ROOT/'Assets/Audio/Moonkov/AudioDetailSources.json').write_text(json.dumps(manifest,indent=2)+'\n')
# Texture audition only; runtime applies quieter SoundDef/category gains and distance attenuation.
y=np.zeros(RATE*14)
for name,at in [('SuitWallBrush1',.3),('SuitWallMetal2',1.5),('Crawl1',2.8),('StanceChange2',4.4),('GearMotion3',5.5),('AlienIdleMotion1',6.7),('AlienWallScrape1',8.3),('AlienWallMetal3',10)]:
 x=made[name]*.6;a=int(at*RATE);y[a:a+len(x)]+=x
with wave.open(str(ROOT/'LocalData/AudioDetails/ContactFoley-Audition.wav'),'wb') as w:
 w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(y*32767).astype('<i2').tobytes())
credits=ROOT/'Assets/StreamingAssets/MoonkovAudioDetailCredits.txt';credits.write_text((ROOT/'Assets/Audio/Moonkov/Licenses/AudioDetails.md').read_text())
print(json.dumps(dict(clips=len(records),seconds=sum(r['seconds'] for r in records),bytes=sum((ROOT/r['asset']).stat().st_size for r in records))))
