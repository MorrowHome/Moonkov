#!/usr/bin/env python3
"""Shorten the existing recorded breath at unchanged pitch; requires ffmpeg/numpy."""
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
RATE = 48000
source = ROOT / 'Assets/Audio/Moonkov/Clips/Raid/SuitBreath.wav'
output = source.with_name('SuitBreathEffort.wav')
raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(source),
    '-af', 'atempo=1.5', '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
samples = np.frombuffer(raw, dtype='<f4').astype(float).copy()
samples -= samples.mean()
edge = round(.008 * RATE)
samples[:edge] *= np.linspace(0, 1, edge)
samples[-edge:] *= np.linspace(1, 0, edge)
samples *= .4 / max(1e-9, np.max(np.abs(samples)))
with wave.open(str(output), 'wb') as wav:
    wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(RATE)
    wav.writeframes(np.round(samples * 32767).astype('<i2').tobytes())
manifest = dict(source=str(source.relative_to(ROOT)),
    source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
    author='mikeask', license='CC0-1.0',
    source_page='https://opengameart.org/content/breathing-tired',
    upstream_manifest='Assets/Audio/Moonkov/RaidAudioSources.json',
    processing='Existing helmet breath; atempo 1.5 preserves pitch; DC removal; 8 ms fades; peak 0.4; 48 kHz mono PCM16.',
    asset=str(output.relative_to(ROOT)), seconds=len(samples)/RATE,
    sha256=hashlib.sha256(output.read_bytes()).hexdigest())
(ROOT / 'Assets/Audio/Moonkov/StaminaBreathSources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print(json.dumps(manifest, indent=2))
