"""Reproduce the original synthesized raid textures; no downloaded recordings.

Run from the repository root with Python 3. All files are mono PCM16 / 22050 Hz.
"""
import math
from pathlib import Path
import random
import struct
import wave

RATE = 22050
OUT = Path("Assets/Audio/Moonkov/Clips/Ambience")


def write(name, samples):
    peak = max(abs(x) for x in samples)
    pcm = b"".join(struct.pack("<h", round(x / peak * 22000)) for x in samples)
    with wave.open(str(OUT / name), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        wav.writeframes(pcm)


def bed():
    # Every oscillator/modulator completes an integer number of cycles in 12 s.
    samples = []
    for i in range(RATE * 12):
        t = i / RATE
        s = math.sin(2 * math.pi * 37 * t) * (.36 + .06 * math.cos(2 * math.pi * t / 12))
        s += .22 * math.sin(2 * math.pi * 43 * t + .6 * math.sin(2 * math.pi * t / 6))
        s += .12 * math.sin(2 * math.pi * 59 * t) + .025 * math.sin(2 * math.pi * 181 * t)
        samples.append(s)
    write("RaidHullDrone.wav", samples)


def texture(name, seconds, seed, creature=False):
    rng = random.Random(seed)
    samples, low, phase = [], 0., 0.
    for i in range(int(seconds * RATE)):
        t = i / RATE
        x = t / seconds
        envelope = math.sin(math.pi * x) ** (1.4 if creature else .8)
        low += .075 * (rng.uniform(-1, 1) - low)
        frequency = (95 - 38 * x if creature else 155 - 70 * x)
        phase += 2 * math.pi * frequency / RATE
        pulse = .65 + .35 * math.sin(2 * math.pi * (6 if creature else 11) * t)
        body = math.sin(phase + 1.4 * math.sin(phase * 2.01))
        s = (body * .3 + low * (2 if creature else 1.4)) * envelope * pulse
        if not creature:
            s += .12 * math.sin(2 * math.pi * 329 * t) * math.exp(-5 * t) * min(1, t * 60)
        samples.append(s)
    write(name, samples)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    bed()
    texture("RaidStructureCreak.wav", 2.8, 37)
    texture("RaidAlienRustle.wav", 1.9, 53, creature=True)
    print("Generated 3 original raid atmosphere textures.")
