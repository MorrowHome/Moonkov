#!/usr/bin/env python3
"""Edit licensed recorded foley into short, close survival-action cues. Requires ffmpeg/numpy."""
from pathlib import Path
import hashlib
import json
import subprocess
import wave
import zipfile
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'SourceData/Audio/SurvivalActions'
WORK = ROOT / 'LocalData/SurvivalActionAudio/Sources'
OUT = ROOT / 'Assets/Audio/Moonkov/Clips/Actions'
RATE = 48000


def prepare():
    WORK.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)
    crunch = WORK / 'crunch_-_tito'
    if not crunch.exists():
        crunch.mkdir()
        with zipfile.ZipFile(SOURCE / 'crunch_-_tito.zip') as archive:
            for name in archive.namelist():
                if Path(name).name == name:
                    archive.extract(name, crunch)
    bottle = WORK / 'qubodup-bottle-and-drink-sounds'
    if not bottle.exists():
        bottle.mkdir()
        subprocess.run(['bsdtar', '-xf', str(SOURCE / 'qubodup-bottle-and-drink-sounds.7z'), '-C', str(bottle)], check=True)
    return crunch, bottle / 'qubodup-bottle-and-drink-sounds'


def clip(name, path, maximum):
    raw = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(path), '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
    audio = np.frombuffer(raw, '<f4').astype(np.float64)
    audio -= audio.mean()
    # Keep the actual performance, with a short lead-in and bounded tail.
    active = np.flatnonzero(np.abs(audio) > max(.003, np.max(np.abs(audio)) * .025))
    if not len(active):
        raise ValueError('Silent source: ' + str(path))
    start = max(0, int(active[0]) - int(.012 * RATE))
    audio = audio[start:min(len(audio), start + int(maximum * RATE))].copy()
    n = min(int(.008 * RATE), len(audio) // 4)
    audio[:n] *= np.linspace(0, 1, n)
    n = min(int(.055 * RATE), len(audio) // 4)
    audio[-n:] *= np.linspace(1, 0, n) ** 1.5
    audio *= .65 / max(np.max(np.abs(audio)), 1e-9)
    target = OUT / (name + '.wav')
    with wave.open(str(target), 'wb') as output:
        output.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        output.writeframes(np.round(audio * 32767).astype('<i2').tobytes())
    return dict(asset=target.relative_to(ROOT).as_posix(), source=path.name,
                source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                seconds=round(len(audio) / RATE, 4), sha256=hashlib.sha256(target.read_bytes()).hexdigest())


def main():
    crunch, bottle = prepare()
    made = []
    for i in range(1, 4):
        made.append(clip('EatBite' + str(i), crunch / f'crunch.{i}.ogg', .60))
        made.append(clip('DrinkSip' + str(i), bottle / f'swallow-{i:02d}.flac', .65))
        made.append(clip('BottleOpen' + str(i), bottle / f'bottle-open-{i:02d}.flac', .70))
    made.append(clip('PacketOpen1', SOURCE / 'Vinrax-Pickup.wav', .60))
    made.append(clip('InjectorCap1', SOURCE / 'Kenney-MetalClick.ogg', .22))
    made.append(clip('InjectorPress1', SOURCE / 'Kenney-HandleLeather.ogg', .35))
    manifest = dict(processing='Recorded foley; mono PCM16/48kHz; remove DC; trim leading silence; 8ms/55ms edge fades; -3.74dBFS peak. Runtime sound banks apply restrained gains.',
                    sources=[
                        dict(author='Tito / StarNinjas', license='CC0-1.0', url='https://opengameart.org/content/7-eating-crunches', file='crunch_-_tito.zip'),
                        dict(author='qubodup', license='CC0-1.0', url='https://opengameart.org/content/liquid-bottle-drink-set', file='qubodup-bottle-and-drink-sounds.7z'),
                        dict(author='Vinrax', license='CC-BY-3.0', url='https://opengameart.org/content/pickupplastic-sound', file='Vinrax-Pickup.wav'),
                        dict(author='Kenney', license='CC0-1.0', url='https://opengameart.org/node/21999', file='Kenney-MetalClick.ogg / Kenney-HandleLeather.ogg')], clips=made)
    (ROOT / 'Assets/Audio/Moonkov/SurvivalActionSources.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print(json.dumps(dict(clips=len(made), seconds=sum(c['seconds'] for c in made))))


if __name__ == '__main__':
    main()
