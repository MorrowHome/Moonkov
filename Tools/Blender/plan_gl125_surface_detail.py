"""Plan craters and mark authored geometry that lunar scree must avoid.

Requires NumPy, SciPy and Pillow. Coordinates come from the actual GL125 mesh
pack. The bit mask uses 2 m cells, indexed with x first and north as +z in Unity.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import binary_dilation, distance_transform_edt
from scipy.spatial import cKDTree

from pack_gl125 import read_mesh


CELL_SIZE = 2
SIDE = 2048
ZONES = (
    (1355, 2040, 1615, 2310),
    (1400, 1810, 1760, 2070),
    (1660, 2075, 1960, 2340),
    (1860, 2210, 2230, 2470),
    (2200, 1900, 2520, 2250),
    (1700, 1700, 2110, 2020),
    (1340, 2310, 1670, 2585),
    (1640, 2400, 2040, 2670),
)
POINT = re.compile(r"\{x: ([-+\d.eE]+), y: [-+\d.eE]+, z: ([-+\d.eE]+)\}")


def inspect_meshes(prepared, world):
    occupied = Image.new("1", (SIDE, SIDE))
    draw = ImageDraw.Draw(occupied)
    cells = np.zeros((SIDE, SIDE), bool)
    terrain = []
    for chunk in world["chunks"]:
        if chunk["atlas"] < 0:
            continue
        vertices, sections = read_mesh(prepared / "Meshes" / chunk["file"])
        points = vertices[:, :3] + np.asarray(chunk["origin"], dtype=np.float32)
        if world["atlases"][chunk["atlas"]]["terrain"]:
            keep = ((points[:, :2] >= [1290, 1650]) & (points[:, :2] <= [2570, 2720])).all(axis=1)
            if keep.any():
                terrain.append(np.column_stack((points[keep], vertices[keep, 3:6])))
            continue
        raster = np.floor(points[:, :2] / CELL_SIZE).astype(np.int32)
        within = ((raster >= 0) & (raster < SIDE)).all(axis=1)
        x, z = raster[within].T
        cells[z, x] = True
        for _, faces in sections:
            triangles = raster[faces]
            large = np.ptp(triangles, axis=1).max(axis=1) >= 2
            for triangle in triangles[large]:
                draw.polygon([tuple(p) for p in triangle], fill=1)
    # The extra cell covers sub-cell edges and the largest cosmetic fragment.
    blocked = binary_dilation(cells | np.asarray(occupied))
    if not terrain:
        raise ValueError("No GL125 terrain found in the crater placement area")
    return blocked, np.concatenate(terrain)


def gameplay_points(scene):
    fields = ("m_LootPositions", "m_EnemySpawnPositions", "m_AdditionalExtractionPositions", "m_ExtractionPosition")
    text = scene.read_text(encoding="utf-8")
    matches = []
    for name in fields:
        block = re.search(rf"^  {name}:([^\n]*\n(?:  - [^\n]*\n)*)", text, flags=re.M)
        if not block:
            raise ValueError(f"Missing formal scene position field {name}")
        matches.extend(POINT.findall(block[1]))
    spawn = scene.with_name("TheophilusSpawnPointsSubScene.unity").read_text(encoding="utf-8")
    matches.extend(re.findall(r"^  m_LocalPosition: " + POINT.pattern, spawn, flags=re.M))
    matches.extend([("2177.87", "2462.91"), ("2109.36", "2535.97")])
    return np.asarray(matches, dtype=np.float64)


def choose_craters(blocked, terrain, protected):
    rng = np.random.default_rng(7319)
    clearance = distance_transform_edt(~blocked) * CELL_SIZE
    search = cKDTree(terrain[:, :2])
    angles = np.arange(8) * np.pi / 4
    ring = np.column_stack((np.cos(angles), np.sin(angles)))
    checks = np.vstack((np.zeros((1, 2)), ring, ring * 1.63))
    selected = []
    for radius_limits, target in (((9.5, 22), 5), ((3.9, 8.4), 36)):
        for x0, z0, x1, z1 in ZONES:
            count = 0
            for _ in range(3600):
                center = rng.uniform([x0, z0], [x1, z1])
                radius = rng.uniform(*radius_limits)
                x, z = np.floor(center / CELL_SIZE).astype(int)
                margin = 1.8 * radius + 4
                if clearance[z, x] < margin:
                    continue
                if np.any(np.linalg.norm(protected - center, axis=1) < margin + 3):
                    continue
                if any(np.linalg.norm(center - other) < 1.8 * (radius + other_radius) + 2
                       for other, other_radius, _ in selected):
                    continue
                distance, index = search.query(center + checks * radius)
                if distance.max() > (2.9 if radius >= 9.5 else 1.55):
                    continue
                ground = terrain[index]
                if ground[:, 5].min() < .912:
                    continue
                plane = ground[0, 2] - (ground[:, :2] - ground[0, :2]) @ ground[0, 3:5] / ground[0, 5]
                if np.max(np.abs(ground[:, 2] - plane)) > .19 * radius + .26:
                    continue
                selected.append((center, radius, rng.uniform(.22, .73)))
                count += 1
                if count == target:
                    break
    medium = sum(radius >= 9.5 for _, radius, _ in selected)
    if medium < 14 or len(selected) < 70:
        raise ValueError(f"Only {medium} medium and {len(selected) - medium} small terrain-safe craters")
    return [dict(x=round(float(x), 3), z=round(float(z), 3), radius=round(float(radius), 3),
                 freshness=round(float(freshness), 4))
            for (x, z), radius, freshness in selected]


def write_facies(craters, output):
    resolution = 1024
    coords = np.array([(c["x"], c["z"]) for c in craters])
    start = np.floor((coords.min(axis=0) - 84) / 32) * 32
    end = np.ceil((coords.max(axis=0) + 84) / 32) * 32
    size = end - start
    texels = np.zeros((resolution, resolution, 3), np.float32)
    for crater in craters:
        center = np.array([crater["x"], crater["z"]])
        radius, fresh = crater["radius"], crater["freshness"]
        lower = np.maximum(0, np.floor((center - 3.5 * radius - start) / size * resolution).astype(int))
        upper = np.minimum(resolution, np.ceil((center + 3.5 * radius - start) / size * resolution).astype(int))
        x, z = (np.arange(a, b) + .5 for a, b in zip(lower, upper))
        dx = start[0] + x[None, :] * size[0] / resolution - center[0]
        dz = start[1] + z[:, None] * size[1] / resolution - center[1]
        angle = np.arctan2(dz, dx)
        r = np.sqrt(dx**2 + dz**2) / radius
        rays = (.5 + .5 * np.sin(angle * 11 + np.sin(angle * 5) * 1.8))**5
        rim = .7 * np.exp(-((r - 1.06) / .28)**2)
        ejecta = np.where(r > 1, np.exp(-(r - 1) * 1.1) * rays * fresh, 0)
        rubble = np.exp(-((r - 1.4) / .55)**2) * (.45 + .55 * fresh)
        t = np.clip((r - .25) / .65, 0, 1)
        basin = (1 - t * t * (3 - 2 * t)) * .65
        tile = texels[lower[1]:upper[1], lower[0]:upper[0]]
        # Blend the farthest ejecta to zero at the write boundary.
        weight = np.clip((3.5 - r) * 2, 0, 1)
        values = np.stack((rim + ejecta, rubble, basin), axis=2) * weight[:, :, None]
        np.maximum(tile, values, out=tile)
    image = Image.fromarray(np.rint(np.clip(texels[::-1], 0, 1) * 255).astype(np.uint8))
    image.save(output / "ImpactFacies.png", optimize=True)
    return np.r_[start, size].tolist()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("prepared", type=Path)
    parser.add_argument("formal_scene", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    raw = (args.prepared / "World.json").read_bytes()
    blocked, terrain = inspect_meshes(args.prepared, json.loads(raw))
    craters = choose_craters(blocked, terrain, gameplay_points(args.formal_scene))
    args.output.mkdir(parents=True, exist_ok=True)
    packed = np.packbits(blocked.reshape(-1), bitorder="little")
    (args.output / "GroundExclusion.bytes").write_bytes(packed.tobytes())
    rect = write_facies(craters, args.output)
    layout = dict(worldHash=hashlib.sha256(raw).hexdigest(), faciesRect=rect, craters=craters)
    (args.output / "CraterLayout.json").write_text(json.dumps(layout, indent=2) + "\n", encoding="utf-8")
    print(f"{sum(c['radius']>=9.5 for c in craters)} medium and "
          f"{sum(c['radius']<9.5 for c in craters)} small craters. {len(packed)} exclusion bytes.")


if __name__ == "__main__":
    main()
