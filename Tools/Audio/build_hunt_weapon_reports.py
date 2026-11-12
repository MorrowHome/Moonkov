#!/usr/bin/env python3
"""Build the selected first-person Hunt reports from cached gameplay recordings."""

import argparse
import hashlib
import io
import json
from pathlib import Path
import subprocess
import wave

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SOURCES = ROOT / "LocalData/HuntGunshotReview/Sources"
CLIPS = ROOT / "Assets/Audio/Moonkov/Clips/HuntWeaponReports"
MANIFEST = ROOT / "Assets/Audio/Moonkov/HuntWeaponReportSources.json"
RATE = 48000

SOURCES = {
    "showcase": ("allweapons.m4a", "BV1XG411W7vZ/?p=1", "勤劳的小六"),
    "release": ("BV1CJ411w7Cw.m4a", "BV1CJ411w7Cw/", "勤劳的小六"),
    "v19": ("BV1p34y1J7dG.m4a", "BV1p34y1J7dG/", "linmumumumumumu"),
    "basic": ("BV1eW411E7UG.m4a", "BV1eW411E7UG/", "勤劳的小六"),
    "demo1": ("BV1Tt411t7sy_p1.m4a", "BV1Tt411t7sy/?p=1", "噬魂の魅影"),
    "demo2": ("BV1Tt411t7sy_p2.m4a", "BV1Tt411t7sy/?p=2", "噬魂の魅影"),
}

# Duration cap, low-mid EQ boost (dB), and source/start pairs. Assignment is by
# report character. The weapon names below are Moonkov IDs.
REPORTS = {
    "Halo": (.85, 2.5, ("showcase", 72.425), ("showcase", 128.780), ("release", 9.765)),
    "Revolver": (.90, 2.0, ("v19", 3440.725), ("v19", 3460.400), ("basic", 130.300)),
    "Shotgun": (1.15, 1.0, ("showcase", 255.810), ("showcase", 625.260), ("showcase", 682.385)),
    "Sniper": (1.35, 1.7, ("showcase", 189.350), ("showcase", 191.420), ("demo1", 512.005)),
    "Cicada": (.53, 2.6, ("showcase", 322.030), ("release", 163.100), ("release", 833.990)),
    "Trine": (.74, 2.4, ("showcase", 975.855), ("v19", 3524.670), ("basic", 126.140)),
    "Aegis": (.83, 1.5, ("demo1", 388.535), ("demo1", 393.440), ("release", 597.290)),
    "Vesper": (.92, 2.5, ("showcase", 302.230), ("release", 205.840), ("release", 234.670)),
    "Thorn": (.88, 1.5, ("release", 356.315), ("release", 409.330), ("demo2", 11.535)),
    "Sundown": (1.35, 1.0, ("showcase", 738.600), ("release", 717.805), ("release", 750.820)),
    "Rondo": (.57, 2.2, ("showcase", 386.200), ("showcase", 392.425), ("showcase", 861.840)),
    "Tempest": (1.05, 1.0, ("demo2", 265.730), ("demo2", 308.685), ("demo1", 406.445)),
    "Nocturne": (.96, 1.9, ("showcase", 588.820), ("release", 554.205), ("release", 562.140)),
}
TAKE_TRIM_DB = {("Tempest", 3): -2.1}


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def dbfs(value):
    return round(20 * np.log10(max(1e-9, value)), 2)


def render(source, start, duration, boost, trim_db):
    # Keep the actual report and reflections, with only clip gain, EQ and peak shaping.
    raw = subprocess.check_output([
        "ffmpeg", "-nostdin", "-v", "error", "-ss", f"{start:.3f}", "-i", str(source),
        "-t", f"{duration + .16:.3f}", "-ac", "1", "-ar", str(RATE), "-af",
        f"highpass=f=38,equalizer=f=175:t=q:w=.85:g={boost}", "-f", "f32le", "-",
    ])
    samples = np.frombuffer(raw, dtype="<f4").astype(np.float64)
    if len(samples) < int(RATE * duration):
        raise ValueError(f"Incomplete excerpt at {source.name} {start:.3f}")
    ms = RATE // 1000
    head = samples[:180 * ms].reshape(-1, ms)
    levels = np.sqrt(np.mean(head ** 2, axis=1))
    onset = int(np.flatnonzero(levels > max(.018, levels.max() * .12))[0])
    if onset > 55:
        raise ValueError(f"Late report at {source.name} {start:.3f}: {onset} ms")
    first = max(0, onset - 1) * ms
    samples = samples[first:first + int(duration * RATE)].copy()

    # Compress the tallest peaks gently to keep more of the body at a fixed peak budget.
    samples /= np.max(np.abs(samples))
    samples = np.tanh(1.25 * samples) * (.84 / np.tanh(1.25))
    samples *= 10 ** (trim_db / 20)
    samples[:ms] *= np.linspace(0, 1, ms)
    fade = 85 * ms
    samples[-fade:] *= np.linspace(1, 0, fade) ** 1.5

    wav = io.BytesIO()
    with wave.open(wav, "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(np.rint(samples * 32767).astype("<i2").tobytes())
    return wav.getvalue(), {
        "start_seconds": round(start + first / RATE, 3),
        "seconds": duration,
        "peak_dbfs": dbfs(np.max(np.abs(samples))),
        "attack_rms_dbfs": dbfs(np.sqrt(np.mean(samples[:50 * ms] ** 2))),
        "body_rms_dbfs": dbfs(np.sqrt(np.mean(samples[50 * ms:250 * ms] ** 2))),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sources", type=Path, default=DEFAULT_SOURCES)
    parser.add_argument("--verify", action="store_true", help="compare generated audio with the checked-in assets")
    args = parser.parse_args()
    sources = {
        key: {"cache_file": file, "page": "https://www.bilibili.com/video/" + page, "uploader": uploader,
              "sha256": sha256((args.sources / file).read_bytes())}
        for key, (file, page, uploader) in SOURCES.items()
    }
    manifest = {
        "game": "Hunt: Showdown",
        "copyright": "Crytek GmbH",
        "license": "All rights reserved; no Creative Commons license asserted",
        "processing": "Gameplay recordings. Mono 48 kHz PCM16, onset trim, 38 Hz high pass, low-mid EQ, mild peak shaping and edge fades. Original pitch and cadence within each recording.",
        "sources": sources,
        "clips": [],
    }
    files = {}
    for weapon, (duration, boost, *takes) in REPORTS.items():
        for variant, (key, start) in enumerate(takes, 1):
            path = CLIPS / f"{weapon}Report{variant}.wav"
            trim_db = TAKE_TRIM_DB.get((weapon, variant), 0)
            content, metrics = render(args.sources / sources[key]["cache_file"], start, duration, boost, trim_db)
            files[path] = content
            manifest["clips"].append({
                "asset": path.relative_to(ROOT).as_posix(), "source": key, "sha256": sha256(content), **metrics,
            })
    files[MANIFEST] = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode()
    if args.verify:
        mismatched = [str(path.relative_to(ROOT)) for path, data in files.items() if path.read_bytes() != data]
        if mismatched:
            raise SystemExit("Different output files: " + ", ".join(mismatched))
    else:
        CLIPS.mkdir(parents=True, exist_ok=True)
        for path, content in files.items():
            path.write_bytes(content)
    print(f"{'Verified' if args.verify else 'Rendered'} {len(manifest['clips'])} Hunt weapon reports.")


if __name__ == "__main__":
    main()
