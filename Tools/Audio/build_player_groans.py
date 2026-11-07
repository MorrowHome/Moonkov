#!/usr/bin/env python3
"""Build long female pain phrases from the credited CC0 recorded performances."""
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
RATE = 48000
OUT = ROOT / 'Assets/Audio/Moonkov/Clips/PlayerGroan'
OUT.mkdir(parents=True, exist_ok=True)
entries = ['DEATH -- HIgh dying noise.mp3', 'DEATH -- misc female british noise.mp3',
           'DEATH -- nerf this death noises.mp3']
sources, clips = [], []
for i, entry in enumerate(entries, 1):
    source = ROOT / f'SourceData/Audio/PlayerGroan/SkyRae/Groan{i}.mp3'
    raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(source),
        '-af', 'highpass=f=85,lowpass=f=9500,atempo=0.9',
        '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
    samples = np.frombuffer(raw, dtype='<f4').astype(float).copy()
    samples -= samples.mean()
    edge = round(.012 * RATE)
    samples[:edge] *= np.linspace(0, 1, edge)
    samples[-edge:] *= np.linspace(1, 0, edge)
    samples *= .72 / max(1e-9, np.max(np.abs(samples)))
    output = OUT / f'PlayerGroan{i}.wav'
    with wave.open(str(output), 'wb') as wav:
        wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(RATE)
        wav.writeframes(np.round(samples * 32767).astype('<i2').tobytes())
    sources.append(dict(asset=str(source.relative_to(ROOT)), archive_entry=entry,
        sha256=hashlib.sha256(source.read_bytes()).hexdigest()))
    clips.append(dict(asset=str(output.relative_to(ROOT)), source=sources[-1]['asset'],
        seconds=len(samples)/RATE, peak=float(np.max(np.abs(samples))),
        sha256=hashlib.sha256(output.read_bytes()).hexdigest()))
manifest = dict(author='Sky Rae; commissioned and published by Galacti-Chron for Star Shift',
    title='Dying Voices - Female - RPG', license='CC0-1.0',
    source_page='https://opengameart.org/content/dying-voices-female-rpg',
    download_url='https://opengameart.org/sites/default/files/Dying-Voices-FEMALE-RPG.zip',
    download_date='2026-11-08',
    processing='Complete recorded vocal phrases; HP85/LP9500; atempo 0.9 preserves pitch; DC removal; 12 ms fades; peak 0.72; 48 kHz mono PCM16. No synthesized or repeated short cries.',
    sources=sources, clips=clips)
(ROOT / 'Assets/Audio/Moonkov/PlayerGroanSources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print(json.dumps(clips, indent=2))
