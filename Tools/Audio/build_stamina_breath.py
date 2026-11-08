#!/usr/bin/env python3
"""Build genuine panting cycles at their recorded speed and pitch."""
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
RATE = 48000
source = ROOT / 'SourceData/Audio/StaminaBreath/FemaleRunBreath.mp3'
page = 'https://freesound.org/people/MelvinJaepel/sounds/435468/'
raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(source),
    '-af', 'highpass=f=85,lowpass=f=10000,acompressor=threshold=0.06:ratio=2.5:attack=2:release=55',
    '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
recording = np.frombuffer(raw, dtype='<f4').astype(float)
clips = []
for suffix, start, end in [('', .32, 1.55), ('2', 1.78, 2.99), ('3', 6.25, 7.81)]:
    output = ROOT / ('Assets/Audio/Moonkov/Clips/Raid/SuitBreathEffort' + suffix + '.wav')
    samples = recording[round(start * RATE):round(end * RATE)].copy()
    samples -= samples.mean()
    edge = round(.012 * RATE)
    samples[:edge] *= np.linspace(0, 1, edge); samples[-edge:] *= np.linspace(1, 0, edge)
    samples *= min(.18 / max(1e-9, np.sqrt(np.mean(samples**2))), .7 / max(1e-9, max(abs(samples))))
    with wave.open(str(output), 'wb') as wav:
        wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(RATE)
        wav.writeframes(np.round(samples * 32767).astype('<i2').tobytes())
    clips.append(dict(asset=str(output.relative_to(ROOT)), source_excerpt_seconds=[start, end],
        seconds=len(samples)/RATE, peak=float(max(abs(samples))), rms=float(np.sqrt(np.mean(samples**2))),
        sha256=hashlib.sha256(output.read_bytes()).hexdigest()))
manifest = dict(source=str(source.relative_to(ROOT)), source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
    author='MelvinJaepel', license='CC0-1.0', source_page=page,
    download='https://cdn.freesound.org/previews/435/435468_6125359-hq.mp3',
    source_format='Freesound high-quality MP3 preview of woman run and breath.wav; not lossless original.',
    processing='Recorded panting; unchanged pitch/speed; HP85/LP10000; gentle peak compression 2.5:1; DC removal; 12 ms fades; RMS target 0.18 / peak cap 0.7; 48 kHz mono PCM16.', clips=clips)
(ROOT / 'Assets/Audio/Moonkov/StaminaBreathSources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print(json.dumps(dict(clips=len(clips), seconds=[c['seconds'] for c in clips])))
