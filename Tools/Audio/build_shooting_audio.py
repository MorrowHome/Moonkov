#!/usr/bin/env python3
"""Build the mac/shooting reports from the CC0 Free Firearm Sound Library.
Requires Python + numpy + ffmpeg. Extract the source archive under
LocalData/ShootingAV/Sources first; see Assets/Audio/Moonkov/Licenses/FreeFirearmSoundLibrary.md.
Only writes the named presentation clips and their provenance manifest.
"""
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SOURCES = ROOT / 'LocalData/ShootingAV/Sources/Prepared SFX Library'
OUTPUT = ROOT / 'Assets/Audio/Moonkov/Clips/Weapons'
RATE = 48000
PAGE = 'https://opengameart.org/content/the-free-firearm-sound-library'
ARCHIVE = 'https://opengameart.org/sites/default/files/Prepared%20SFX%20Library.7z'
records = []
cache = {}

def read(relative):
    if relative not in cache:
        path = SOURCES / relative
        raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(path), '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
        cache[relative] = np.frombuffer(raw, np.float32).astype(np.float64)
    return cache[relative]

def excerpt(relative, event, duration, offset=0):
    source = read(relative)
    a, b = int((event-.06)*RATE), int((event+.025)*RATE)
    search = np.abs(source[max(0,a):b])
    threshold = search.max() * .045
    onset = max(0,a) + int(np.flatnonzero(search >= threshold)[0]) - int(.0007*RATE)
    start = max(0, onset + int(offset*RATE))
    result = source[start:start+int(duration*RATE)].copy()
    return np.pad(result, (0,max(0,int(duration*RATE)-len(result)))), start/RATE

def equalize(samples, role):
    n = len(samples); size = 1 << (n*2-1).bit_length()
    freq = np.fft.rfftfreq(size, 1/RATE)
    highpass = freq**2/(freq**2 + 35**2)
    weight = {'rifle': .35, 'revolver': .45, 'sniper': .65, 'crack': 0, 'tail': .2}[role]
    response = highpass * (1 + weight/(1+(freq/230)**4))
    if role == 'crack': response *= freq**4/(freq**4+2000**4)
    if role == 'tail': response *= 1/(1+(freq/1100)**4)
    result = np.fft.irfft(np.fft.rfft(samples, size)*response, size)[:n]
    if role in ('rifle', 'revolver', 'sniper'):
        normalized = result / max(float(np.max(np.abs(result))), 1e-9)
        result = .5*normalized + .5*np.tanh(normalized*4)
    # A very short attack prevents a digital discontinuity without blunting the report.
    attack = max(2,int(.0003*RATE)); fade = min(n//2,int((.07 if role=='rifle' else .12)*RATE))
    result[:attack] *= np.linspace(0,1,attack)
    result[-fade:] *= np.linspace(1,0,fade)**1.5
    result *= 10**(-1/20)/max(float(np.max(np.abs(result))),1e-9)
    return result

def write(name, relative, event, duration, role, offset=0):
    samples, start = excerpt(relative,event,duration,offset)
    samples = equalize(samples,role)
    path = OUTPUT / (name+'.wav')
    with wave.open(str(path),'wb') as output:
        output.setnchannels(1);output.setsampwidth(2);output.setframerate(RATE)
        output.writeframes(np.round(np.clip(samples,-1,1)*32767).astype('<i2').tobytes())
    rms = float(np.sqrt(np.mean(samples[:min(len(samples),4800)]**2)))
    record = dict(asset=path.relative_to(ROOT).as_posix(), source=relative, source_sha256=hashlib.sha256((SOURCES/relative).read_bytes()).hexdigest(),
                  source_start_seconds=round(start,6), duration=duration, sample_rate=RATE, channels=1,
                  role=role, processing='onset-aligned; DC/highpass 35 Hz; role-specific low shelf or band shaping; 50% parallel soft saturation at 4x drive for body reports; 0.3 ms attack and 70/120 ms tail fade; peak -1 dBFS; PCM16',
                  sha256=hashlib.sha256(path.read_bytes()).hexdigest(), first_100ms_rms_dbfs=round(20*np.log10(max(rms,1e-9)),2))
    records.append(record)
    return samples

OUTPUT.mkdir(parents=True,exist_ok=True)
clips = {}
for i,(source,t) in enumerate([('AR-15/D_24P.wav',.58),('AR-15/D_24P.wav',3.935),('AR-15/D_32P.wav',.705)],1):
    clips['RiflePunch'+str(i)] = write('RiflePunch'+str(i),source,t,.24,'rifle')
for i,(source,t) in enumerate([('Smith & Wesson 642/V_27P.wav',.805),('Smith & Wesson 642/V_27P.wav',6.215),('Smith & Wesson 642/V_22P.wav',.67)],1):
    clips['RevolverPunch'+str(i)] = write('RevolverPunch'+str(i),source,t,.48,'revolver')
for i,t in enumerate([1.035,5.02,9.155],1):
    clips['SniperPunch'+str(i)] = write('SniperPunch'+str(i),'Mosin Nagant/M_21P.wav',t,.72,'sniper')
clips['DischargeCrack'] = write('DischargeCrack','AR-15/D_24P.wav',.58,.055,'crack')
clips['SniperAftershock'] = write('SniperAftershock','Mosin Nagant/M_21P.wav',1.035,.8,'tail',.065)
manifest = dict(collection='The Free Firearm Sound Library',authors=['Ben Jaszczak','Brian Nelson','Kevin Heras','Matthew Nanney'],license='CC0-1.0',
                source_page=PAGE,download=ARCHIVE,archive_sha256=hashlib.sha256((SOURCES.parent/'Prepared SFX Library.7z').read_bytes()).hexdigest(),clips=records)
(ROOT/'Assets/Audio/Moonkov/ShootingSources.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
# Dry mix audition follows the final body/crack/tail gains, before user master and SFX bus.
audition = np.zeros(RATE*9)
for name,when,gain in [('RiflePunch1',.3,.48),('RiflePunch1',1.1,.48),('RiflePunch2',1.2,.47),('RiflePunch3',1.3,.46),('RiflePunch1',1.4,.45),
                        ('RevolverPunch1',3,.56),('RevolverPunch2',3.8,.56),('SniperPunch1',5.4,.63),('SniperAftershock',5.418,.075)]:
    a=clips[name]*gain;start=int(when*RATE);audition[start:start+len(a)] += a
for when in [.3,1.1,1.2,1.3,1.4,3,3.8,5.4]:
    a=clips['DischargeCrack']*.85*10**(-27/20);start=int(when*RATE);audition[start:start+len(a)] += a
path=ROOT/'LocalData/ShootingAV/NewGunfire-Audition.wav'
with wave.open(str(path),'wb') as out:
    out.setnchannels(1);out.setsampwidth(2);out.setframerate(RATE);out.writeframes(np.round(np.clip(audition,-1,1)*32767).astype('<i2').tobytes())
print(json.dumps(dict(clips=len(records),audition_peak=float(np.max(np.abs(audition))),audition=str(path),metrics=[(r['asset'],r['first_100ms_rms_dbfs']) for r in records]),indent=2))
