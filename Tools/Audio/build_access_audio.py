#!/usr/bin/env python3
"""Build the opening/login sound palette offline. Requires numpy and ffmpeg.

Run from any directory. Voice source and license: SourceData/Audio/Access/README.md.
No runtime synthesis; only the actual visual events determine playback timing.
"""
from pathlib import Path
import hashlib
import json
import subprocess
import wave

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Audio/Moonkov/Clips/Access'
SOURCE = ROOT / 'SourceData/Audio/Access/IndoorMurmur.mp3'
STATIC = ROOT / 'SourceData/Audio/Access/ScatterNoise1.mp3'
DEEP = ROOT / 'SourceData/Audio/Access/DeepImpact.mp3'
FIELD = ROOT / 'SourceData/Audio/Access/ForceFieldImpact.mp3'
RATE = 48000
RNG = np.random.default_rng(1701)
RECORDS = []
CLIPS = {}


def eq(x, low, high):
    # Circular filtering keeps both background loops continuous at their seam.
    f = np.fft.rfftfreq(len(x), 1 / RATE)
    response = f * f / (f * f + low * low) / (1 + (f / high) ** 6)
    return np.fft.irfft(np.fft.rfft(x, axis=0) * response[:, None], n=len(x), axis=0)


def noise(seconds, low, high):
    return eq(RNG.normal(size=(round(seconds * RATE), 2)), low, high)


def rms(x, db):
    return x * (10 ** (db / 20) / max(np.sqrt(np.mean(x * x)), 1e-8))


def fade(x, attack=.004, release=.025):
    a, b = min(round(attack * RATE), len(x)), min(round(release * RATE), len(x))
    x[:a] *= np.sin(np.linspace(0, np.pi / 2, a))[:, None] ** 2
    x[-b:] *= np.cos(np.linspace(0, np.pi / 2, b))[:, None] ** 2
    return x


def read(path, channels=1):
    x = np.frombuffer(subprocess.check_output([
        'ffmpeg', '-v', 'error', '-i', str(path), '-ac', str(channels), '-ar', str(RATE), '-f', 'f32le', '-'
    ]), '<f4').astype(float)
    return x if channels == 1 else x.reshape(-1, channels)


def impact_layer(path, low, high):
    x = read(path, 2)
    active = np.flatnonzero(np.max(np.abs(x), axis=1) > np.max(np.abs(x)) * .025)
    x = x[max(0, active[0] - 96):]
    # Zero padding keeps the authored reverb from wrapping into the attack during EQ.
    padded = np.zeros((RATE * 10, 2)); padded[:len(x)] = x
    x = eq(padded, low, high)[:round(3.2 * RATE)]
    return x / max(np.max(np.abs(x)), 1e-8)


def write(name, x, recipe, loop=False, source='Original synthesis', attack=.004, release=.025):
    x -= x.mean(axis=0)
    if not loop:
        x = fade(x, attack, release)
        envelope = fade(np.ones((len(x), 1)), attack, release)
        x -= envelope * x.mean(axis=0) / envelope.mean()
    x *= .63 / max(np.max(np.abs(x)), 1e-8)
    assert np.max(np.abs(x)) < 1
    path = OUT / (name + '.wav')
    with wave.open(str(path), 'wb') as wav:
        wav.setparams((2, 2, RATE, 0, 'NONE', 'not compressed'))
        wav.writeframes(np.round(x * 32767).astype('<i2').tobytes())
    CLIPS[name] = x
    RECORDS.append(dict(asset=path.relative_to(ROOT).as_posix(), seconds=len(x) / RATE,
                        peak_dbfs=round(float(20 * np.log10(np.max(np.abs(x)))), 2),
                        rms_dbfs=round(float(20 * np.log10(np.sqrt(np.mean(x * x)))), 2),
                        loop=loop, seam_jump=float(np.max(np.abs(x[0] - x[-1]))),
                        recipe=recipe, source=source,
                        sha256=hashlib.sha256(path.read_bytes()).hexdigest()))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    # Broad soft receiver noise; avoid a piercing upper treble hiss.
    x = noise(8, 110, 4200)
    t = np.arange(len(x))[:, None] / RATE
    x = rms(x, -24) + .025 * np.sin(2 * np.pi * 67 * t) + .012 * np.sin(2 * np.pi * 134 * t)
    write('AccessCarrier', x, 'Periodic filtered white noise with quiet 67/134 Hz equipment hum; 8 s loop', True)

    # Real recorded walla, softened and layered to obscure individual words.
    raw = read(SOURCE)
    speech = np.interp(np.linspace(0, len(raw) - 1, RATE * 26), np.arange(len(raw)), raw)
    overlap = RATE * 2
    phase = np.linspace(0, np.pi / 2, overlap, endpoint=False)
    join = speech[-overlap:] * np.cos(phase) ** 2 + speech[:overlap] * np.sin(phase) ** 2
    speech = np.concatenate((join, speech[overlap:-overlap]))
    voices = np.stack((speech + .45 * np.roll(speech, RATE * 7),
                       np.roll(speech, round(RATE * .037)) + .45 * np.roll(speech, RATE * 11)), axis=1)
    voices = eq(voices, 190, 1300)
    for delay, gain in [(.11, .22), (.23, .14), (.37, .08)]:
        voices += np.roll(voices, round(RATE * delay), axis=0) * gain
    t = np.arange(len(voices))[:, None] / RATE
    voices = rms(voices, -22)
    voices = .1 * np.tanh(voices / .1)  # Restrain isolated louder syllables in the recorded crowd.
    voices *= .8 + .2 * np.cos(2 * np.pi * t / 24)
    bed = rms(noise(24, 140, 3500), -29) + .008 * np.sin(2 * np.pi * 67 * t)
    write('AccessRoom', voices + bed, 'CC0 human walla stretched, layered, HP190/LP1300, short diffuse reflections, soft peak compression; filtered white noise; 2 s loop crossfade', True, 'SpliceSound 260122 + original synthesis')

    raw = read(SOURCE.parent / 'Pop1.wav')
    active = np.flatnonzero(np.abs(raw) > np.max(np.abs(raw)) * .015)
    raw = raw[max(0, active[0] - 96):min(len(raw), active[-1] + 192)]
    raw /= np.max(np.abs(raw))
    t = np.arange(len(raw))[:, None] / RATE
    pop = .72 * np.tanh(np.stack((raw, raw), axis=1) * 1.9)
    punch = .4 * np.sin(2 * np.pi * 118 * t) * np.exp(-t * 55)
    snap = eq(np.random.default_rng(441).normal(size=(len(raw), 1)), 1800, 6000)
    snap = rms(snap, -12) * np.exp(-t * 300)
    pop = (pop + punch + snap) * (.7 + .3 * np.exp(-t * 80))
    write('AccessTerminal1', pop, 'CC0 pop1 at original pitch/speed; driven transient, short 118 Hz punch and 1.8-6 kHz snap; one fixed sample for all windows', source='EZduzziteh Pop sounds + original transient layers', attack=.0005, release=.007)

    x = noise(.2, 120, 5700)
    t = np.arange(len(x))[:, None] / RATE
    gate = np.where(t < .05, 1, np.where(t < .1, .16, 1 - (t - .1) / .1))
    x = rms(x, -16) * gate + .13 * np.sin(2 * np.pi * (80 * t - 90 * t * t)) * np.exp(-t * 24)
    write('AccessCut', x, '200 ms three-part signal break matching the 4.7 s visual flash and 4.9 s clear')

    # Layer authored game/cinematic impacts: grounded body, force-field edge and space.
    deep = impact_layer(DEEP, 30, 1700)
    field = impact_layer(FIELD, 320, 7400)
    t = np.arange(len(deep))[:, None] / RATE
    body = deep * (.82 * np.exp(-t * .32))
    texture = field * .42
    impact = body + texture
    for delay, gain in [(.071, .12), (.127, .08)]:
        n = round(delay * RATE)
        impact[n:] += texture[:-n, ::-1] * gain
    write('AccessReveal', impact.copy(), 'Deep Cinematic Impact body + Sci-Fi Impact force-field edge; original stereo space, short reflections and 3.2 s release', source='zazz.sound.design 754421 + Kierham 646831', attack=.001, release=.45)
    x = impact[:round(.75 * RATE)][::-1].copy()
    t = np.arange(len(x))[:, None] / RATE
    progress = t / .75
    rise = rms(noise(.75, 140, 900), -21) * (1 - progress) + rms(noise(.75, 450, 7500), -17) * progress ** 2
    # The final 120 ms reaches a dense, bright, rough crest before the black-frame cut.
    edge = rms(noise(.75, 1100, 10500), -8) * progress ** 5
    chatter = .72 + .28 * np.sin(2 * np.pi * (8 * t + 55 * t * t)) ** 2
    x = (.7 * rms(x, -12) + rise + edge * chatter) * (.015 + .985 * progress ** 2.2)
    x = np.tanh(x * (1.1 + 2.5 * progress ** 3))
    write('AccessClose', x, 'Reversed layered impact, widening rise, accelerating rough upper edge and saturated final crest; 750 ms buildup then a 2 ms cut at full closure', source='zazz.sound.design 754421 + Kierham 646831 + original riser', attack=.015, release=.002)

    static = read(STATIC)
    for i, duration in enumerate([.075, .11, .14], 1):
        start = round((3.1 + i * 5.7) * RATE)
        grain = static[start:start + round(duration * RATE)]
        x = eq(np.stack((grain, np.roll(grain, round(.0017 * RATE))), axis=1), 250, 4300)
        t = np.arange(len(x))[:, None] / RATE
        gate = .12 + .88 * (np.sin(2 * np.pi * (37 + i * 11) * t) > .2)
        x = rms(x, -17) * gate
        write('AccessGlitch' + str(i), x, 'CC0 ScatterNoise1 slice at %.2f s; bandpass, narrow stereo delay and soft gating; 75-140 ms' % (3.1 + i * 5.7), source='xhunterko ScatterNoise1')

    manifest = dict(sample_rate=RATE, channels=2, format='PCM16', sources=[dict(
        title='Indoor adult murmur, medium group.wav', author='SpliceSound', license='CC0-1.0',
        page='https://freesound.org/people/SpliceSound/sounds/260122/',
        download='https://cdn.freesound.org/previews/260/260122_1480854-hq.mp3',
        file=SOURCE.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest()), dict(
        title='Static / ScatterNoise1.mp3', author='xhunterko', license='CC0-1.0',
        page='https://opengameart.org/content/static', download='https://opengameart.org/sites/default/files/ScatterNoise1.mp3',
        file=STATIC.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(STATIC.read_bytes()).hexdigest()), dict(
        title='DSGNImpt_Deep Cinematic Impact 2_ZAZZ', author='zazz.sound.design', license='CC0-1.0',
        page='https://freesound.org/people/zazz.sound.design/sounds/754421/',
        download='https://cdn.freesound.org/previews/754/754421_726853-hq.mp3',
        file=DEEP.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(DEEP.read_bytes()).hexdigest()), dict(
        title='Sci-Fi Impact', author='Kierham', license='CC0-1.0',
        page='https://freesound.org/people/Kierham/sounds/646831/',
        download='https://cdn.freesound.org/previews/646/646831_11617040-hq.mp3',
        file=FIELD.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(FIELD.read_bytes()).hexdigest())] + [dict(
        title='Pop sounds / pop%d.wav' % i, author='EZduzziteh', license='CC0-1.0',
        page='https://opengameart.org/content/pop-sounds-0', download='https://opengameart.org/sites/default/files/pop%d.wav' % i,
        file=(SOURCE.parent / ('Pop%d.wav' % i)).relative_to(ROOT).as_posix(),
        sha256=hashlib.sha256((SOURCE.parent / ('Pop%d.wav' % i)).read_bytes()).hexdigest()) for i in [1]], clips=RECORDS)
    (ROOT / 'Assets/Audio/Moonkov/AccessAudioSources.json').write_text(json.dumps(manifest, indent=2) + '\n')
    audition()
    print(json.dumps(dict(clips=len(RECORDS), seconds=sum(r['seconds'] for r in RECORDS),
                          bytes=sum((ROOT / r['asset']).stat().st_size for r in RECORDS))))


def audition():
    # Reference mix only. Runtime events follow the actual animation clock, including stalls/skips.
    mix = np.zeros((RATE * 25, 2))

    def add(name, at, db, scale=1, duration=None):
        x = CLIPS[name].copy()
        if duration is not None:
            x = np.tile(x, (int(np.ceil(duration * RATE / len(x))), 1))[:round(duration * RATE)]
        x *= 10 ** (db / 20) * scale
        start = round(at * RATE)
        count = min(len(x), len(mix) - start)
        mix[start:start + count] += x[:count]

    n = round(4.7 * RATE)
    gain = (.25 + .75 * np.clip(np.arange(n) / (4.4 * RATE), 0, 1) ** 2)[:, None]
    add('AccessCarrier', 0, -18, gain, 4.7)
    frame = 0
    for i in range(33):
        if i:
            frame += 24 if i <= 4 else 12 if i <= 12 else 6 if i <= 20 else 2
        add('AccessTerminal1', frame / 60, -8, .65 + .35 * frame / 264)
    add('AccessCut', 4.7, -12)
    add('AccessReveal', 5.9, -9)
    n = round((25 - 367 / 60) * RATE)
    add('AccessRoom', 367 / 60, -15, np.minimum(1, np.arange(n) / (.6 * RATE))[:, None], n / RATE)
    for i, at in enumerate([10.0, 15.7, 21.1]):
        add('AccessGlitch' + str(i + 1), at, -24)
    add('AccessClose', 21.5, -8)
    mix[round(22.25 * RATE):] = 0
    add('AccessReveal', 22.35, -9)
    mix *= .8 * .65  # Existing default master/interface volume.
    mix = fade(mix, .012, .7)
    path = ROOT / 'LocalData/AccessAudio/OpeningAndLogin.wav'
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), 'wb') as wav:
        wav.setparams((2, 2, RATE, 0, 'NONE', 'not compressed'))
        wav.writeframes(np.round(np.clip(mix, -1, 1) * 32767).astype('<i2').tobytes())


if __name__ == '__main__':
    main()
