#!/usr/bin/env python3
"""Prepare recorded room/suit ventilation loops and the actor's jump efforts."""
from pathlib import Path
import hashlib, json, subprocess, wave
import numpy as np
ROOT = Path(__file__).resolve().parents[2]
RATE = 48000
OUT = ROOT / 'Assets/Audio/Moonkov/Clips/Presence'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = ROOT / 'SourceData/Audio/RaidPresence'
records = []
def render(name, source, filters, loop=False):
    args = ['ffmpeg','-v','error','-i',str(source)]
    if loop: args += ['-ss','10','-t','9']
    raw = subprocess.check_output(args + ['-af',filters,'-ac','1','-ar',str(RATE),'-f','f32le','-'])
    a = np.frombuffer(raw,dtype='<f4').astype(float).copy(); a -= a.mean()
    if loop:
        n = RATE; blend = np.linspace(0,1,n)
        a = np.concatenate([a[n:-n], a[-n:]*(1-blend)+a[:n]*blend])
        # The end of the crossfade meets the original sample at one second.
        a[-1] = a[0]
    else:
        n = round(.004*RATE); a[:n] *= np.linspace(0,1,n); a[-n:] *= np.linspace(1,0,n)
    a *= .7 / max(1e-9,max(abs(a)))
    path = OUT / (name+'.wav')
    with wave.open(str(path),'wb') as w:
        w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.round(a*32767).astype('<i2').tobytes())
    records.append(dict(asset=str(path.relative_to(ROOT)),source=str(source.relative_to(ROOT)),filters=filters,
        source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
        seconds=len(a)/RATE,loop=loop,peak=float(max(abs(a))),rms=float(np.sqrt(np.mean(a*a)))))
render('InteriorVent',SOURCE/'AirVent.mp3','highpass=f=110,lowpass=f=6000',True)
render('SuitVent',SOURCE/'AirVent.mp3','highpass=f=220,lowpass=f=2200',True)
for i in range(1,4):render('JumpEffort'+str(i),SOURCE/('jump'+str(i)+'.wav'),'highpass=f=90,lowpass=f=11000')
manifest=dict(license='CC0-1.0',vent_author='lfyaudio',vent_page='https://freesound.org/people/lfyaudio/sounds/848969/',
    vent_download=(SOURCE/'download-url.txt').read_text().strip(),voice_author='Cici Fyre',
    voice_page='https://opengameart.org/content/female-rpg-voice-starter-pack',
    voice_archive='RPG Voice Starter Pack/Type 1/jump1.wav through jump3.wav',
    processing='Natural pitch/speed; ventilation crop 10-19 s with 1 s loop crossfade; mono 48k PCM16; DC removal and peak 0.7.',clips=records)
(ROOT/'Assets/Audio/Moonkov/RaidPresenceSources.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps(records,indent=2))
