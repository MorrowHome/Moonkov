#!/usr/bin/env python3
"""Prepare a complete recorded cough at its original pitch and speed."""
from pathlib import Path
import hashlib, json, subprocess, wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
RATE = 48000
source = ROOT / 'SourceData/Audio/Cough/OwlStorm/CoughFemale.mp3'
out = ROOT / 'Assets/Audio/Moonkov/Clips/Cough/CoughFemale.wav'
out.parent.mkdir(parents=True, exist_ok=True)
raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(source),
    '-af', 'highpass=f=90,lowpass=f=10000', '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
samples = np.frombuffer(raw, dtype='<f4').astype(float).copy()
samples -= samples.mean()
edge = round(.008 * RATE)
samples[:edge] *= np.linspace(0, 1, edge)
samples[-edge:] *= np.linspace(1, 0, edge)
samples *= .72 / max(1e-9, np.max(np.abs(samples)))
with wave.open(str(out), 'wb') as wav:
    wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(RATE)
    wav.writeframes(np.round(samples * 32767).astype('<i2').tobytes())
manifest = dict(author='OwlStorm / Ashe Kirk / Owlish Media', title='Cough Female', license='CC0-1.0',
    source_page='https://freesound.org/people/OwlStorm/sounds/151213/',
    download_url='https://cdn.freesound.org/previews/151/151213_140737-hq.mp3', download_date='2026-11-08',
    processing='Public high-quality MP3 preview; full performance, original pitch/speed; HP90/LP10000; DC removal; 8 ms fades; peak 0.72; 48 kHz mono PCM16.',
    sources=[dict(asset=str(source.relative_to(ROOT)),sha256=hashlib.sha256(source.read_bytes()).hexdigest())],
    clips=[dict(asset=str(out.relative_to(ROOT)),source=str(source.relative_to(ROOT)),seconds=len(samples)/RATE,
        sha256=hashlib.sha256(out.read_bytes()).hexdigest())])
(ROOT / 'Assets/Audio/Moonkov/CoughSources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print(json.dumps(manifest['clips'], indent=2))
