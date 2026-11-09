#!/usr/bin/env python3
"""Preserve a complete recorded dying cry, without looping or time stretching."""
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import urllib.request
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
RATE = 48000
source = ROOT / 'LocalData/DeathVoiceReview/9_kyaaa.wav'
download = 'http://www.vita-chi.net/sec/voi/hora/9_kyaaa.wav'
if not source.exists():
    source.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(download, timeout=30) as response:
        source.write_bytes(response.read())
if hashlib.sha256(source.read_bytes()).hexdigest() != '0aeac9c11599328c85af77883b2b8b25d739379a4a25453fe1b203eab7bb954f':
    raise ValueError('Death voice source hash mismatch')
output = ROOT / 'Assets/Audio/Moonkov/Clips/PlayerDeath/PlayerDeath.wav'
raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(source),
    '-af', 'highpass=f=80,lowpass=f=11000,acompressor=threshold=0.18:ratio=2:attack=2:release=60',
    '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
start, end = .94, 4.03
samples = np.frombuffer(raw, '<f4').astype(float)[round(start*RATE):round(end*RATE)].copy()
samples -= samples.mean()
samples[:480] *= np.linspace(0, 1, 480)
samples[-3840:] *= np.linspace(1, 0, 3840)
samples *= min(.18 / max(1e-9, np.sqrt(np.mean(samples**2))), .8 / max(1e-9, max(abs(samples))))
output.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(output), 'wb') as wav:
    wav.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
    wav.writeframes(np.round(samples * 32767).astype('<i2').tobytes())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
manifest = dict(author='びたちー（びたみんちぃ）', title='悲鳴７ きゃーーーー / 少女的悲鳴２',
    license='作者免费素材条款（须署名，非 CC0，不可单独再分发）',
    license_url='http://www.vita-chi.net/sozai1.htm', credit='声：びたちー素材館',
    source_page='http://www.vita-chi.net/sec/voi/hora/voivoi1.htm',
    download=download,
    download_date='2026-11-09', source=str(source.relative_to(ROOT)), source_sha256=sha(source),
    source_format='Original downloadable WAV from the voice performer; raw source stays in local rebuild cache.',
    processing='Original pitch/speed; HP80/LP11000; gentle 2:1 peak compression; DC removal; 10 ms entrance / 80 ms tail fades; RMS target 0.18 / peak cap 0.8; 48 kHz mono PCM16.',
    clips=[dict(asset=str(output.relative_to(ROOT)), source_excerpt_seconds=[start,end],
        seconds=len(samples)/RATE, peak=float(max(abs(samples))), rms=float(np.sqrt(np.mean(samples**2))), sha256=sha(output))])
(ROOT / 'Assets/Audio/Moonkov/PlayerDeathSources.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n')
print(json.dumps(manifest['clips']))
