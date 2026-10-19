#!/usr/bin/env python3
"""Edit CC0 recorded mouth, shell, cloth and scrape layers into alien combat foley.
Requires numpy/ffmpeg. Writes only AlienCombat clips, manifest and its credits.
Existing source provenance: RaidAudioSources.json and AudioDetailSources.json.
"""
from pathlib import Path
import hashlib,json,subprocess,wave
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Audio/Moonkov/Clips/AlienCombat'; OUT.mkdir(parents=True,exist_ok=True)
RATE=48000; records=[]
def read(p):
 return np.frombuffer(subprocess.check_output(['ffmpeg','-v','error','-i',str(p),'-ac','1','-ar',str(RATE),'-f','f32le','-']),'<f4').astype(float)
def speed(x,r):return np.interp(np.arange(0,len(x)-1,r),np.arange(len(x)),x)
def eq(x,lo,hi):
 n=1<<(len(x)*2-1).bit_length();f=np.fft.rfftfreq(n,1/RATE)
 return np.fft.irfft(np.fft.rfft(x,n)*(f*f/(f*f+lo*lo))/(1+(f/hi)**4),n)[:len(x)]
def normal(x):return x/max(float(np.max(np.abs(x))),1e-9)
def mix(*xs):
 y=np.zeros(max(len(x) for x in xs))
 for x in xs:y[:len(x)]+=x
 return y
def tail(x,at):return np.pad(x,(int(at*RATE),0))
def write(name,x,paths,recipe):
 x=eq(x,45,10000);x-=x.mean()
 n=min(int(.008*RATE),len(x)//4);x[:n]*=np.linspace(0,1,n)**1.5
 n=min(int(.085*RATE),len(x)//3);x[-n:]*=np.linspace(1,0,n)**1.5
 x=normal(x)*.7;p=OUT/(name+'.wav')
 with wave.open(str(p),'wb') as w:
  w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(x*32767).astype('<i2').tobytes())
 records.append(dict(asset=p.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(p.read_bytes()).hexdigest(),seconds=round(len(x)/RATE,4),sources=[dict(path=q.relative_to(ROOT).as_posix(),sha256=hashlib.sha256(q.read_bytes()).hexdigest()) for q in paths],processing=recipe+'; HP45Hz; 8/85ms edge fades; peak -3.10dBFS; mono PCM16/48k'))
for i in range(1,4):
 mouth=ROOT/f'LocalData/RaidAudio/Sources/Monsters/monster_sfx_pack_2/monster-{[6,8,12][i-1]}.wav'
 shell=ROOT/f'Assets/Audio/Moonkov/Clips/Raid/AlienContact{i}.wav'
 cloth=ROOT/f'Assets/Audio/Moonkov/Clips/Details/Crawl{i}.wav'
 scrape=ROOT/f'Assets/Audio/Moonkov/Clips/Details/AlienWallScrape{i}.wav'
 voice=ROOT/f'Assets/Audio/Moonkov/Clips/Raid/AlienWarning{i}.wav'
 m=normal(eq(read(mouth),140,6500));b=normal(eq(read(shell),80,5000));c=normal(eq(read(cloth),160,5800));s=normal(eq(read(scrape),90,4200))
 write(f'AlienSwing{i}',mix(speed(c,1.5)*.65,speed(s,1.8)*.4,tail(speed(b,1.35)*.3,.045)),[cloth,scrape,shell],'Short air/cloth shear, abrasive leading edge and shell snap; varispeed 1.5/1.8/1.35')
 spit=speed(m,.95+i*.035)[:int(RATE*.42)]
 write(f'AlienSpit{i}',mix(spit,tail(eq(spit,120,1600)*.25,.026)),[mouth],'Close mouth expulsion, restrained 26ms damped reflection')
 write(f'AlienImpact{i}',mix(speed(b,.7)*.85,speed(s,.85)*.3,tail(eq(speed(b,.48),55,400)*.45,.018)),[shell,scrape],'Shell impact with abrasive breakup and lower recorded contact body')
 wet=speed(m,.68)[:int(RATE*.55)]
 write(f'AlienWetMove{i}',mix(eq(wet,170,2400)*.65,speed(c,.9)*.35),[mouth,cloth],'Quiet organic mouth texture and cloth shear for suction/slime travel; no boot strike')
 write(f'AlienHeavyStep{i}',mix(speed(b,.55),speed(s,.65)*.45,tail(eq(speed(b,.38),45,260)*.6,.035)),[shell,scrape],'Heavy shell/floor contact with low recorded body, 35ms settling layer')
 write(f'AlienGrip{i}',mix(speed(m,.8)[:int(RATE*.48)]*.65,speed(b,1.2)*.35),[mouth,shell],'Short suction/grasp mouth performance with small shell tension')
 v=read(voice)
 write(f'AlienBossVoice{i}',mix(speed(v,.76),tail(eq(speed(v,.54),55,750)*.35,.04)),[voice],'Large throat vocal from CC0 recorded warning, parallel low body, 40ms damped resonance')
manifest=dict(license='CC0-1.0',provenance=['RaidAudioSources.json (Ogrebane mouth and blukotek/Ali_6868 shell/gravel)','AudioDetailSources.json (Kenney cloth and AntumDeluge scrape)'],clips=records)
(ROOT/'Assets/Audio/Moonkov/AlienCombatAudioSources.json').write_text(json.dumps(manifest,indent=2)+'\n')
notice='''Moonkov alien combat foley — 2026-10-19
All new AlienCombat clips are edits of CC0 1.0 recordings already held in this project:
Ogrebane — Monster Sound Effects 2: https://opengameart.org/content/monster-sound-effects-2
blukotek / Ali_6868 — bone/gravel layers: https://opengameart.org/content/footsteps-on-different-surfaces
Kenney — 50 RPG sound effects: https://opengameart.org/node/21999
AntumDeluge — Scrapes: https://opengameart.org/content/scrapes
License: https://creativecommons.org/publicdomain/zero/1.0/
No metal/boots CC BY recordings are used in these new clips. Existing CC BY notices remain in MoonkovRaidAudioCredits.txt.
Exact source/output SHA256 and edit recipes: Assets/Audio/Moonkov/AlienCombatAudioSources.json.
'''
(ROOT/'Assets/Audio/Moonkov/Licenses/AlienCombatAudio.txt').write_text(notice)
(ROOT/'Assets/StreamingAssets/MoonkovAlienCombatAudioCredits.txt').write_text(notice)
print(f'{len(records)} clips; {sum(x["seconds"] for x in records):.2f}s; {sum((ROOT/x["asset"]).stat().st_size for x in records)} WAV bytes')
