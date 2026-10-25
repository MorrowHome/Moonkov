#!/usr/bin/env python3
"""Four short recorded pain reactions. Requires ffmpeg and numpy; no synthesis."""
from pathlib import Path
import hashlib,json,subprocess,wave
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'SourceData/Audio/PlayerPain/female_hurt_grunts_groans_1.ogg'
OUT=ROOT/'Assets/Audio/Moonkov/Clips/PlayerPain'; OUT.mkdir(parents=True,exist_ok=True)
RATE=48000
raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(SOURCE),'-ac','1','-ar',str(RATE),'-f','f32le','-'])
x=np.frombuffer(raw,dtype='<f4').astype(float)
records=[];audition=[]
for i,(start,end) in enumerate([(0.425,1.15),(2.09,2.73),(3.71,4.15),(7.33,8.12)],1):
 y=x[round(start*RATE):round(end*RATE)].copy(); y-=y.mean()
 fade=round(.008*RATE); y[:fade]*=np.linspace(0,1,fade); y[-fade:]*=np.linspace(1,0,fade)
 y*=.72/max(1e-9,np.max(np.abs(y)))
 p=OUT/f'PlayerPain{i}.wav'
 with wave.open(str(p),'wb') as w:
  w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(y*32767).astype('<i2').tobytes())
 records.append(dict(asset=str(p.relative_to(ROOT)),start=start,end=end,seconds=len(y)/RATE,sha256=hashlib.sha256(p.read_bytes()).hexdigest()))
 audition.extend([y*10**(-9/20),np.zeros(RATE//2)])
manifest=dict(author='AuraVoice / Nocturnal_Vanguard',title='Female Hurt Grunts & Groans',license='CC0-1.0',source_page='https://opengameart.org/content/female-hurt-grunts-groans',download_url='https://opengameart.org/sites/default/files/female_hurt_grunts_groans_1.ogg',download_date='2026-10-25',source=str(SOURCE.relative_to(ROOT)),source_sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),processing='48 kHz mono PCM16; separate recorded takes; DC removal; 8 ms edge fades; peak 0.72; no pitch transformation',clips=records)
(ROOT/'Assets/Audio/Moonkov/PlayerPainSources.json').write_text(json.dumps(manifest,indent=2)+'\n')
preview=ROOT/'LocalData/AudioReview/PlayerPain-Audition.wav';preview.parent.mkdir(parents=True,exist_ok=True)
with wave.open(str(preview),'wb') as w:
 w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(np.concatenate(audition)*32767).astype('<i2').tobytes())
print(json.dumps(records,indent=2))
