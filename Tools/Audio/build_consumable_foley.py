#!/usr/bin/env python3
"""Recorded medical handling, bottle closure and portable oxygen release. ffmpeg/numpy required."""
from pathlib import Path
import hashlib, json, subprocess, wave
import numpy as np
from build_survival_action_audio import prepare, present_level

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'SourceData/Audio/ConsumableFoley'
OUT = ROOT / 'Assets/Audio/Moonkov/Clips/Consumables'
RATE = 48000

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    _, bottle = prepare()
    records = []
    def render(name, source, start, seconds, filters='highpass=f=100,lowpass=f=11000'):
        raw = subprocess.check_output(['ffmpeg','-v','error','-i',str(source),'-ss',str(start),'-t',str(seconds),
            '-af',filters,'-ac','1','-ar',str(RATE),'-f','f32le','-'])
        a = np.frombuffer(raw, '<f4').astype(float).copy()
        a -= a.mean()
        n = min(int(.008*RATE),len(a)//4); a[:n] *= np.linspace(0,1,n)
        n = min(int(.05*RATE),len(a)//4); a[-n:] *= np.linspace(1,0,n)
        a = present_level(a)
        path = OUT/(name+'.wav')
        with wave.open(str(path),'wb') as w:
            w.setparams((1,2,RATE,0,'NONE','not compressed'))
            w.writeframes(np.round(a*32767).astype('<i2').tobytes())
        records.append(dict(asset=str(path.relative_to(ROOT)),source=str(source.relative_to(ROOT)),start=start,
            seconds=len(a)/RATE,filters=filters,source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
            sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    for i in range(1,4): render('BottleClose'+str(i),bottle/f'bottle-close-{i:02d}.flac',0,.65)
    render('InjectorPush1',SOURCE/'Syringe.mp3',7.15,.65)
    render('InjectorPush2',SOURCE/'Syringe.mp3',18,.65)
    render('MedicalHandling1',SOURCE/'Bandage.mp3',7.5,.8)
    render('MedicalHandling2',SOURCE/'Bandage.mp3',9,.65)
    render('MedicalFinish',SOURCE/'Bandage.mp3',10.05,.3)
    render('OxygenRelease',ROOT/'Assets/Audio/Moonkov/Airlock/Clips/ValveAir_Loop.wav',.3,.28,'highpass=f=180,lowpass=f=8500')
    manifest=dict(processing='Natural speed/pitch; mono PCM16 48 kHz; source crops, filtering, DC removal before/after compression, 8/50 ms fades; 4:1 peak compression, target RMS 0.16, peak at most 0.7.',
        sources=json.loads((SOURCE/'Sources.json').read_text()), existing_sources=['SurvivalActionSources.json','Airlock/Sources.json'],clips=records)
    (ROOT/'Assets/Audio/Moonkov/ConsumableFoleySources.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print(json.dumps(dict(clips=len(records),seconds=sum(x['seconds'] for x in records))))

if __name__ == '__main__': main()
