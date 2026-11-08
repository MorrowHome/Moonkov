#!/usr/bin/env python3
"""Dry injector and cotton-bandage foley; preserves recorded pitch and speed."""
from pathlib import Path
import hashlib, json, subprocess, wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'SourceData/Audio/MedicalFoley'
OUT = ROOT / 'Assets/Audio/Moonkov/Clips/Medical'
RATE = 48000

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    records = []
    def read(name, start=0, seconds=None, filters='highpass=f=100,lowpass=f=8500'):
        path = SOURCE / (name+'.mp3')
        args = ['ffmpeg','-v','error','-i',str(path),'-ss',str(start)]
        if seconds is not None: args += ['-t',str(seconds)]
        raw = subprocess.check_output(args+['-af',filters,'-ac','1','-ar',str(RATE),'-f','f32le','-'])
        a = np.frombuffer(raw,'<f4').astype(float).copy()
        a -= a.mean()
        n=min(int(.008*RATE),len(a)//4);a[:n]*=np.linspace(0,1,n);a[-n:]*=np.linspace(1,0,n)
        a *= .6/max(max(abs(a)),1e-9)
        return a
    def save(name,a,recipe,sources,loop=False):
        a = a.copy();a-=a.mean()
        n=min(int(.008*RATE),len(a)//4);a[:n]*=np.linspace(0,1,n);a[-n:]*=np.linspace(1,0,n)
        a *= min(.13/max(np.sqrt(np.mean(a*a)),1e-9),.6/max(max(abs(a)),1e-9))
        path=OUT/(name+'.wav')
        with wave.open(str(path),'wb') as w:
            w.setparams((1,2,RATE,0,'NONE','not compressed'));w.writeframes(np.round(a*32767).astype('<i2').tobytes())
        records.append(dict(asset=str(path.relative_to(ROOT)),recipe=recipe,loop=loop,seconds=len(a)/RATE,
            sources=[dict(file=str((SOURCE/(s+'.mp3')).relative_to(ROOT)),sha256=hashlib.sha256((SOURCE/(s+'.mp3')).read_bytes()).hexdigest()) for s in sources],
            sha256=hashlib.sha256(path.read_bytes()).hexdigest(),peak=float(max(abs(a))),rms=float(np.sqrt(np.mean(a*a)))))
    cap=read('SyringePop',1.735,.16,'highpass=f=250,lowpass=f=7000')
    save('InjectorDryCap',cap,'1.735-1.895 s dry plunger pop used as cap foley; filtered, no liquid layer',['SyringePop'])
    press=read('SyringePop',1.735,.24,'highpass=f=140,lowpass=f=2600')
    grip=read('ClothWrap2',0,.30,'highpass=f=130,lowpass=f=2800')
    injection=np.zeros(round(.34*RATE));injection[:len(press)]+=press*.85;injection[:len(grip)]+=grip*.12
    save('InjectorDryPress',injection,'Short muffled plunger release with quiet cotton grip texture; no water or squirt',['SyringePop','ClothWrap2'])
    tear=read('BandageTear')
    save('BandageOpen',tear,'Complete recorded tape/bandage tear',['BandageTear'])
    first=read('ClothWrap1');second=read('ClothWrap2')
    wrap=np.zeros(round(1.2*RATE));wrap[:len(first)]+=first;at=round(.62*RATE);wrap[at:at+len(second)]+=second*.92
    save('BandageWrap',wrap,'Two complete cotton strokes at 0 and 0.62 s, with rest between turns; 1.2 s loop',['ClothWrap1','ClothWrap2'],True)
    save('BandageTighten',second,'One complete cotton stroke for tightening the dressing',['ClothWrap2'])
    save('MedicalGelRub',first*.7,'Soft cotton-contact foley for applying gel; separate from bandage loop',['ClothWrap1'])
    manifest=dict(processing='Mono 48 kHz PCM16; DC removal, filters, 8 ms fades; natural pitch/speed; target RMS <=0.13, peak <=0.6. No peak compression.',
        sources=json.loads((SOURCE/'Sources.json').read_text()),clips=records)
    (ROOT/'Assets/Audio/Moonkov/MedicalFoleySources.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print(json.dumps(dict(clips=len(records),seconds=sum(r['seconds'] for r in records))))

if __name__=='__main__':main()
