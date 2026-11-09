#!/usr/bin/env python3
"""Build unvoiced inhale/exhale cycles from the same recording as calm breathing."""
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
RATE = 48000
source = ROOT / 'SourceData/Audio/StaminaBreath/BreathingTired.wav'
page = 'https://opengameart.org/content/breathing-tired'
if hashlib.sha256(source.read_bytes()).hexdigest() != '08d9c9e15426f0cd889b63867a3bc68bafd985e8d39fd57f805d4372b23aeeca':
    raise ValueError('Breathing source hash mismatch')
raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(source),
    '-af', 'highpass=f=80,lowpass=f=3200,equalizer=f=220:t=q:w=0.8:g=3',
    '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
recording = np.frombuffer(raw, dtype='<f4').astype(float)
# Match the calm recording's soft room-noise gate. No throat or resonant voice layer.
meter = np.sqrt(np.convolve(recording**2, np.ones(960)/960, mode='same'))
floor = np.sqrt(np.mean(recording[round(.85*RATE):round(1.15*RATE)]**2))
recording *= np.clip((meter-floor*.8)/(meter+1e-7), 0, 1)**.7
def excerpt(start, end):
    samples = recording[round(start*RATE):round(end*RATE)].copy()
    samples -= samples.mean()
    edge = round(.02*RATE)
    samples[:edge] *= np.linspace(0, 1, edge); samples[-edge:] *= np.linspace(1, 0, edge)
    return samples

clips = []
for suffix, inhale, exhale, pause in [('', (.08,.72), (1.2,1.95), .12),
        ('2', (2.42,3.08), (1.2,1.95), .14), ('3', (.06,.74), (1.18,1.98), .16)]:
    output = ROOT / ('Assets/Audio/Moonkov/Clips/Raid/SuitBreathEffort' + suffix + '.wav')
    samples = np.concatenate((excerpt(*inhale), np.zeros(round(pause*RATE)), excerpt(*exhale)))
    samples *= min(.1 / max(1e-9, np.sqrt(np.mean(samples**2))), .42 / max(1e-9, max(abs(samples))))
    with wave.open(str(output), 'wb') as wav:
        wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(RATE)
        wav.writeframes(np.round(samples * 32767).astype('<i2').tobytes())
    clips.append(dict(asset=str(output.relative_to(ROOT)), source_excerpts_seconds=[inhale,exhale], pause_seconds=pause,
        seconds=len(samples)/RATE, peak=float(max(abs(samples))), rms=float(np.sqrt(np.mean(samples**2))),
        sha256=hashlib.sha256(output.read_bytes()).hexdigest()))
manifest = dict(source=str(source.relative_to(ROOT)), source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
    author='mikeask', title='Breathing Tired', license='CC0-1.0', source_page=page,
    download='https://opengameart.org/sites/default/files/breathing%20tired.wav',
    source_format='Original WAV; same nonverbal air breaths used by SuitBreath. Three edits, not three independent takes.',
    processing='Original pitch/speed; HP80/LP3200; broad +3 dB at 220 Hz; soft room-noise gate; DC removal; 20 ms edge fades; separate recorded inhale/exhale with 120–160 ms pause; RMS target 0.10 / peak cap 0.42; 48 kHz mono PCM16.', clips=clips)
(ROOT / 'Assets/Audio/Moonkov/StaminaBreathSources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print(json.dumps(dict(clips=len(clips), seconds=[c['seconds'] for c in clips])))
