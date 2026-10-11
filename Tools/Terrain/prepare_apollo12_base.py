"""Reproduce the Apollo 12 base candidate from measured LROC elevations.

Requires numpy and Pillow. Original heights and the small authored base grading
are separate outputs; no vertical exaggeration or procedural craters are used.
Run from any directory with: python Tools/Terrain/prepare_apollo12_base.py
"""
from pathlib import Path
import hashlib
import json
import math
import urllib.request

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
RAW = ROOT / "SourceData/Moon/Apollo12"
OUT = ROOT / "Assets/MoonEnvironment/Variants/Apollo12Base/Source"
BASE_URL = "https://pds.lroc.im-ldi.com/data/LRO-L-LROC-5-RDR-V1.0/LROLRC_2001/DATA/SDP/NAC_DTM/APOLLO12/"
EXPECTED = {
    "TIF": "d19ddf661a8675d1c3faed5e15574a220bb5976ce463034375cfefcde33f3103",
    "LBL": "4e6d8cafe404bd3feda366c9b3b70af37566fb54152b514359bb785e4d3a0457",
}


def main():
    RAW.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)
    for ext, expected in EXPECTED.items():
        path = RAW / f"NAC_DTM_APOLLO12.{ext}"
        if not path.exists():
            with urllib.request.urlopen(BASE_URL + path.name, timeout=90) as response:
                path.write_bytes(response.read())
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError(f"Source hash changed: {path}; inspect before rebuilding")

    with Image.open(RAW / "NAC_DTM_APOLLO12.TIF") as image:
        source = np.array(image, dtype=np.float32)
        pixel_scale = list(image.tag_v2[33550])
        tiepoint = list(image.tag_v2[33922])
        assert source.shape == (1367, 2468)
        assert np.allclose(pixel_scale[:2], [2, 2])

    # Selected by comparing valid 2 km crops and ~350 m base footprints.
    # This fixed crop is entirely observed, including its natural impact craters.
    col, row, n = 736, 112, 1025
    heights = np.flipud(source[row:row+n, col:col+n]).copy()
    assert heights.shape == (n, n)
    assert np.isfinite(heights).all() and (heights > -1e30).all()
    base_x, base_z = 1184.0, 1248.0
    gx, gz = np.meshgrid(np.arange(n) * 2.0, np.arange(n) * 2.0)
    # Base (3) footprint is 315 x 323 m. Grading is limited to the base
    # footprint plus an 8 m apron and a 32 m smooth transition.
    dx = np.maximum(np.abs(gx - base_x) - 165.5, 0)
    dz = np.maximum(np.abs(gz - base_z) - 169.5, 0)
    distance = np.hypot(dx, dz)
    footprint = (dx == 0) & (dz == 0)
    base_elevation = float(np.median(heights[footprint]))
    t = np.clip(distance / 32.0, 0, 1)
    weight = 1 - t*t*(3-2*t)
    graded = (heights + (base_elevation - heights) * weight).astype('<f4')
    heights = heights.astype('<f4')
    offset = math.floor(float(heights.min())) - 8
    height_range = math.ceil(float(heights.max()) - offset) + 8
    metadata = {
        "product": "NAC_DTM_APOLLO12", "version": "v1.9",
        "sourcePage": "https://data.lroc.im-ldi.com/lroc/view_rdr/NAC_DTM_APOLLO12",
        "sourceUrl": BASE_URL + "NAC_DTM_APOLLO12.TIF",
        "sourceFileBytes": (RAW / "NAC_DTM_APOLLO12.TIF").stat().st_size,
        "sourceSha256": EXPECTED['TIF'], "labelSha256": EXPECTED['LBL'],
        "sourceRasterSize": [2468, 1367], "sourceWindow": [col, row, n, n],
        "geoTiffPixelScale": pixel_scale, "geoTiffTiepoint": tiepoint,
        "resolution": n, "spacingMeters": 2, "sizeMeters": 2048,
        "nativeSourceSpacingMeters": 2, "verticalExaggeration": 1,
        "validFraction": 1, "axes": "Unity X east, Z north; TIFF rows flipped",
        "format": "float32 little endian row-major absolute elevation in metres",
        "minimumHeight": float(heights.min()), "maximumHeight": float(heights.max()),
        "verticalOffset": offset, "terrainHeight": height_range,
        "baseX": base_x, "baseZ": base_z, "baseElevation": base_elevation,
        "baseUnityY": base_elevation - offset,
        "baseFootprintRelief": float(np.ptp(heights[footprint])),
        "gradingHalfX": 165.5, "gradingHalfZ": 169.5, "gradingBlendMeters": 32,
        "gradingMaxCutMeters": float(np.maximum(heights-graded, 0).max()),
        "gradingMaxFillMeters": float(np.maximum(graded-heights, 0).max()),
        "gradingAreaFraction": float((weight > 0).mean()),
        "note": "Observed elevations retained separately. Local base grading is authored cut/fill, not measured terrain. No synthetic craters or global flattening.",
    }
    for name, data in [("apollo12_observed", heights), ("apollo12_base", graded)]:
        blob = data.tobytes()
        (OUT / f"{name}.bytes").write_bytes(blob)
        metadata[name + "Sha256"] = hashlib.sha256(blob).hexdigest()
    (OUT / "dem.json").write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    (OUT / "NAC_DTM_APOLLO12.LBL.txt").write_bytes((RAW / "NAC_DTM_APOLLO12.LBL").read_bytes())

    # A data-derived hillshade preview; real craters remain visible around the pad.
    zgrad, xgrad = np.gradient(heights, 2)
    light = np.array([-0.6, 0.4, 0.69]); light /= np.linalg.norm(light)
    shade = np.clip((-xgrad*light[0] + light[1] - zgrad*light[2]) / np.sqrt(1+xgrad*xgrad+zgrad*zgrad), 0, 1)
    rgba = np.repeat((45 + shade*200).astype('uint8')[..., None], 3, axis=2)
    preview = Image.fromarray(np.flipud(rgba))
    draw = ImageDraw.Draw(preview)
    px, py = base_x/2, (2048-base_z)/2
    draw.rectangle((px-82.75, py-84.75, px+82.75, py+84.75), outline=(240,190,60), width=3)
    draw.text((16, 16), "APOLLO 12 | 2048 m | native 2 m | north up | base footprint in gold", fill=(255,235,190))
    preview.save(OUT / "Apollo12SiteHillshade.png")
    print(json.dumps({k: metadata[k] for k in ['sourceFileBytes','sourceSha256','minimumHeight','maximumHeight','baseUnityY','baseFootprintRelief','gradingMaxCutMeters','gradingMaxFillMeters','gradingAreaFraction']}, indent=2))


if __name__ == '__main__':
    main()
