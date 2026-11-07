#!/usr/bin/env python3
"""Prepare the existing character actor's three attack efforts at natural pitch."""
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
RATE = 48000
OUT = ROOT / 'Assets/Audio/Moonkov/Clips/MeleeVoice'
OUT.mkdir(parents=True, exist_ok=True)
sources, clips = [], []
for i in range(1, 4):
    source = ROOT / f'SourceData/Audio/MeleeVoice/CiciFyre/attack{i}.wav'
    raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(source),
        '-af', 'highpass=f=90,lowpass=f=11000', '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
    samples = np.frombuffer(raw, dtype='<f4').astype(float).copy()
    samples -= samples.mean()
    edge = round(.004 * RATE)
    samples[:edge] *= np.linspace(0, 1, edge)
    samples[-edge:] *= np.linspace(1, 0, edge)
    samples *= .72 / max(1e-9, np.max(np.abs(samples)))
    output = OUT / f'MeleeEffort{i}.wav'
    with wave.open(str(output), 'wb') as wav:
        wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(RATE)
        wav.writeframes(np.round(samples * 32767).astype('<i2').tobytes())
    sources.append(dict(asset=str(source.relative_to(ROOT)), archive_entry=f'RPG Voice Starter Pack/Type 1/attack{i}.wav',
        sha256=hashlib.sha256(source.read_bytes()).hexdigest()))
    clips.append(dict(asset=str(output.relative_to(ROOT)), source=sources[-1]['asset'], seconds=len(samples)/RATE,
        sha256=hashlib.sha256(output.read_bytes()).hexdigest()))
manifest = dict(author='Cici Fyre / cicifyre', title='Female RPG Voice Starter Pack, Type 1 attacks',
    license='CC0-1.0', source_page='https://opengameart.org/content/female-rpg-voice-starter-pack',
    download_url='https://opengameart.org/sites/default/files/RPG%20Voice%20Starter%20Pack.zip', download_date='2026-11-08',
    processing='Complete attack efforts; original pitch and timing; HP90/LP11000; DC removal; 4 ms fades; peak 0.72; 48 kHz mono PCM16.',
    sources=sources, clips=clips)
(ROOT / 'Assets/Audio/Moonkov/MeleeVoiceSources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print(json.dumps(clips, indent=2))
