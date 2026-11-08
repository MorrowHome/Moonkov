#!/usr/bin/env python3
"""A/B excerpts at bank gain and default game mix, without audition loudness boosts.
Run MovementAudioAssets.Install/Export in Unity first. Before files are local review snapshots.
"""
from pathlib import Path
import json, re, wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
AUDIO = ROOT / 'Assets/Audio/Moonkov'
BEFORE = ROOT / 'LocalData/GameAudioReview/MovementBefore'
AFTER = ROOT / 'LocalData/GameAudioReview/MovementAfter'
OUT = ROOT / 'LocalData/GameAudioReview/Previews'
RATE = 48000
OUT.mkdir(parents=True, exist_ok=True)
# Defaults: master .8, effects .85. Footsteps has its existing +5.6785083 dB mixer trim.
SFX = .8 * .85
FEET = SFX * 10 ** (5.6785083 / 20)

def raw(path):
    with wave.open(str(path)) as w:
        assert w.getnchannels() == 1 and w.getframerate() == RATE and w.getsampwidth() == 2
        return np.frombuffer(w.readframes(w.getnframes()), '<i2').astype(float) / 32768

def old(name, folder='Raid'):
    p = BEFORE / (name + '.wav')
    return raw(p if p.exists() else AUDIO / 'Clips' / folder / (name + '.wav'))

def new(name):
    return np.fromfile(AFTER / (name + '.f32'), '<f4').astype(float)

def bank_db(name, before=False):
    p = (BEFORE if before else AUDIO / 'SoundDefs') / (name + '.asset')
    values = [float(re.search(r'^    '+key+r': ([-\d.]+)', p.read_text(), re.M)[1]) for key in ['VolumeMin','VolumeMax']]
    return sum(values)/2

def gain(name, before=False, amount=1): return 2 ** (bank_db(name, before)/6) * amount

def place(out, x, at, level):
    start = round(at*RATE); out[start:start+len(x)] += x*level

def save(name, out):
    peak=float(max(abs(out))); assert peak < .98, (name,peak)
    with wave.open(str(OUT/(name+'.wav')), 'wb') as w:
        w.setparams((1,2,RATE,0,'NONE','not compressed')); w.writeframes(np.round(out*32767).astype('<i2').tobytes())
    return dict(file=name+'.wav',seconds=len(out)/RATE,peak=peak)

reports=[]
x=np.zeros(RATE*13)
# Old running metal steps with the two previous friction layers.
for i in range(6):
    at=.25+i*.34
    place(x,old('Metal'+str(i+1)),at,gain('MetalFootsteps',True)*FEET)
    place(x,old('Cloth'+str(i%2+1),'Character'),at,gain('Cloth',True,.55)*FEET)
    if i%2:place(x,old('GearMotion'+str(i%3+1),'Details'),at,gain('GearMotion',True,.75)*FEET)
# Current materials, same stride speed. No per-step cloth or equipment layers.
for start,surface,bank in [(3.25,'Metal','MetalFootsteps'),(6.25,'Gravel','RegolithFootsteps'),(9.25,'Concrete','HardFootsteps')]:
    for i in range(6): place(x,new('CSSFoot'+surface+str(i%4+1)),start+i*.34,gain(bank)*FEET)
place(x,new('TarkovPouchHandle1'),12,.6*gain('Cloth')*FEET)
reports.append(save('FootstepsBeforeAfter',x))

x=np.zeros(RATE*12)
for at in [.2,2.6]:place(x,old('SuitBreathEffort'),at,gain('SuitBreathEffort',True,1.25)*SFX)
for at,name in [(5.5,'SuitBreathEffort'),(7.2,'SuitBreathEffort2'),(8.9,'SuitBreathEffort3')]:
    place(x,new(name),at,gain('SuitBreathEffort',False,1.2)*SFX)
reports.append(save('ExhaustedBreathBeforeAfter',x))

x=np.zeros(RATE*7)
for i in range(3):
    place(x,old('JumpEffort'+str(i+1),'Presence'),.2+i*.9,gain('JumpEffort',True,.85)*SFX)
    place(x,old('Jump','Character'),.2+i*.9,gain('Jump',True)*FEET)
    place(x,new('JumpEffort'+str(i+1)),3.7+i*.9,gain('JumpEffort')*SFX)
    place(x,new('Jump'),3.7+i*.9,gain('Jump')*FEET)
reports.append(save('JumpBeforeAfter',x))
(OUT/'MovementPreview.json').write_text(json.dumps(dict(mix='Bank mid-range dB; native imported new PCM; default master .8 / SFX .85; existing footsteps trim; no loudness normalization or audition boost.',
    timelines={'FootstepsBeforeAfter':'0–2.6 old running metal+friction; 3.25 new metal; 6.25 new gravel; 9.25 new concrete; 12 turn cloth',
               'ExhaustedBreathBeforeAfter':'0–4.9 old exhausted breath; 5.5–10.5 new exhausted breath',
               'JumpBeforeAfter':'0–2.6 old jumps; 3.7–6.1 new jumps'},files=reports),ensure_ascii=False,indent=2)+'\n')
print(json.dumps(reports))
