#!/usr/bin/env python3
"""Prepare three recorded, non-metallic body thuds without changing their timing."""
from pathlib import Path
import hashlib, json, subprocess, wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
RATE = 48000
out = ROOT / 'Assets/Audio/Moonkov/Clips/BodyImpact'
out.mkdir(parents=True, exist_ok=True)
sources, clips = [], []
for i in range(1, 4):
    source = ROOT / f'SourceData/Audio/BodyImpact/StarNinjas/impact{i}.ogg'
    raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(source),
        '-af', 'highpass=f=55,lowpass=f=6500', '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
    samples = np.frombuffer(raw, dtype='<f4').astype(float).copy()
    samples -= samples.mean()
    peak = np.max(np.abs(samples))
    active = np.flatnonzero(np.abs(samples) > peak * .035)
    start = max(0, int(active[0]) - round(.008 * RATE))
    tail = np.flatnonzero(np.abs(samples) > peak * .005)
    end = min(len(samples), int(tail[-1]) + round(.03 * RATE))
    samples = samples[start:end]
    edge = round(.002 * RATE)
    samples[:edge] *= np.linspace(0, 1, edge)
    samples[-edge:] *= np.linspace(1, 0, edge)
    samples *= .72 / max(1e-9, np.max(np.abs(samples)))
    output = out / f'BodyImpact{i}.wav'
    with wave.open(str(output), 'wb') as wav:
        wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(RATE)
        wav.writeframes(np.round(samples * 32767).astype('<i2').tobytes())
    sources.append(dict(asset=str(source.relative_to(ROOT)), archive_entry=f'impact.{i}.ogg',
        sha256=hashlib.sha256(source.read_bytes()).hexdigest()))
    clips.append(dict(asset=str(output.relative_to(ROOT)), source=sources[-1]['asset'], trim_start=start/RATE, seconds=len(samples)/RATE,
        sha256=hashlib.sha256(output.read_bytes()).hexdigest()))
manifest = dict(author='StarNinjas', title='10 Impact/Shield Blocks', license='CC0-1.0',
    source_page='https://opengameart.org/content/10-impactshield-blocks',
    download_url='https://opengameart.org/sites/default/files/impact_-_starninjas.zip', download_date='2026-11-08',
    processing='Recorded cardboard impacts used as body-contact Foley; trim recording silence, preserve performance speed/pitch; HP55/LP6500; DC removal; 2 ms fades; peak 0.72; 48 kHz mono PCM16.',
    sources=sources, clips=clips)
(ROOT / 'Assets/Audio/Moonkov/BodyImpactSources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print(json.dumps(clips, indent=2))
