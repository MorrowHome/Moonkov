#!/usr/bin/env python3
"""Hunt syringe excerpt and cotton-bandage foley; preserves source pitch and speed."""
from pathlib import Path
import hashlib, json, subprocess, wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'SourceData/Audio/MedicalFoley'
OUT = ROOT / 'Assets/Audio/Moonkov/Clips/Medical'
RATE = 48000

def source_path(name):
    return SOURCE / (name if Path(name).suffix else name+'.mp3')

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    records = []
    def read(name, start=0, seconds=None, filters='highpass=f=100,lowpass=f=8500'):
        path = source_path(name)
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
            sources=[dict(file=str(source_path(s).relative_to(ROOT)),sha256=hashlib.sha256(source_path(s).read_bytes()).hexdigest()) for s in sources],
            sha256=hashlib.sha256(path.read_bytes()).hexdigest(),peak=float(max(abs(a))),rms=float(np.sqrt(np.mean(a*a)))))
    cap=read('SyringePop',1.735,.16,'highpass=f=250,lowpass=f=7000')
    save('InjectorDryCap',cap,'1.735-1.895 s dry plunger pop used as cap foley; filtered, no liquid layer',['SyringePop'])
    # Use the user's requested original game audio. Only move the three excerpts
    # into our authored action window; no synthesis, filtering or time stretching.
    injection=np.zeros(round(1.38*RATE))
    def layer(a,at,gain):
        start=round(at*RATE);injection[start:start+len(a)]+=a*gain
    original='HuntSyringeOriginal.wav'
    layer(read(original,.04,.30,'anull'),0,1)
    layer(read(original,.62,1.12,'anull'),.24,1)
    layer(read(original,2.65,.37,'anull'),1,1)
    save('InjectorHuntShot',injection,'Original Hunt: Showdown v0.309 syringe audio from its sound designer demonstration. Source video 8.64-8.94 s at 0 s, 9.22-10.34 s at 0.24 s, 11.25-11.62 s at 1.00 s. Only crop/reposition, mono conversion, DC removal, edge fades and linear gain. No synthesis, EQ, compression or pitch/speed change.', [original])
    tear=read('BandageTear')
    save('BandageOpen',tear,'Complete recorded tape/bandage tear',['BandageTear'])
    first=read('ClothWrap1');second=read('ClothWrap2')
    wrap=np.zeros(round(1.2*RATE));wrap[:len(first)]+=first;at=round(.62*RATE);wrap[at:at+len(second)]+=second*.92
    save('BandageWrap',wrap,'Two complete cotton strokes at 0 and 0.62 s, with rest between turns; 1.2 s loop',['ClothWrap1','ClothWrap2'],True)
    save('BandageTighten',second,'One complete cotton stroke for tightening the dressing',['ClothWrap2'])
    save('MedicalGelRub',first*.7,'Soft cotton-contact foley for applying gel; separate from bandage loop',['ClothWrap1'])
    manifest=dict(processing='Mono 48 kHz PCM16; DC removal, 8 ms fades; natural pitch/speed; target RMS <=0.13, peak <=0.6. InjectorHuntShot uses original game audio excerpts with linear gain only; other clips retain their foley filters.',
        sources=json.loads((SOURCE/'Sources.json').read_text()),clips=records)
    (ROOT/'Assets/Audio/Moonkov/MedicalFoleySources.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print(json.dumps(dict(clips=len(records),seconds=sum(r['seconds'] for r in records))))

if __name__=='__main__':main()
