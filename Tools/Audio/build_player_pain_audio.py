#!/usr/bin/env python3
"""Cici Fyre Type 1 damage takes, at original pitch. Requires ffmpeg and numpy."""
from pathlib import Path
import hashlib,json,subprocess,wave
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Assets/Audio/Moonkov/Clips/PlayerPain'; OUT.mkdir(parents=True,exist_ok=True)
RATE=48000
records=[];sources=[];audition=[]
for i in range(1,4):
 source=ROOT/f'SourceData/Audio/PlayerPain/CiciFyre/damaged{i}.wav'
 sources.append(dict(asset=str(source.relative_to(ROOT)),archive_entry=f'RPG Voice Starter Pack/Type 1/damaged{i}.wav',sha256=hashlib.sha256(source.read_bytes()).hexdigest()))
 raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(source),'-ac','1','-ar',str(RATE),'-f','f32le','-'])
 y=np.frombuffer(raw,dtype='<f4').astype(float).copy(); y-=y.mean()
 fade=round(.004*RATE); y[:fade]*=np.linspace(0,1,fade); y[-fade:]*=np.linspace(1,0,fade)
 y*=.72/max(1e-9,np.max(np.abs(y)))
 p=OUT/f'PlayerPain{i}.wav'
 with wave.open(str(p),'wb') as w:
  w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(y*32767).astype('<i2').tobytes())
 records.append(dict(asset=str(p.relative_to(ROOT)),source=sources[-1]['asset'],seconds=len(y)/RATE,sha256=hashlib.sha256(p.read_bytes()).hexdigest()))
 audition.extend([y*10**(-9/20),np.zeros(RATE//2)])
manifest=dict(author='Cici Fyre / cicifyre',title='Female RPG Voice Starter Pack, Type 1 damaged takes',license='CC0-1.0',source_page='https://opengameart.org/content/female-rpg-voice-starter-pack',download_url='https://opengameart.org/sites/default/files/RPG%20Voice%20Starter%20Pack.zip',download_date='2026-10-25',sources=sources,processing='48 kHz mono PCM16; original performance and pitch; DC removal; 4 ms edge fades; peak 0.72. Previous fourth take is retained on disk but no longer referenced.',clips=records)
(ROOT/'Assets/Audio/Moonkov/PlayerPainSources.json').write_text(json.dumps(manifest,indent=2)+'\n')
preview=ROOT/'LocalData/AudioReview/PlayerPain-Anime-Audition.wav';preview.parent.mkdir(parents=True,exist_ok=True)
with wave.open(str(preview),'wb') as w:
 w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(np.concatenate(audition)*32767).astype('<i2').tobytes())
print(json.dumps(records,indent=2))
