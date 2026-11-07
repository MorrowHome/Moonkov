"""Soften lightmap contrast on narrow exposed returns next to lit GL125 panels.

Requires NumPy, SciPy and OpenEXR. Reads the installed EXRs and writes
replacement EXRs outside Assets. Does not change chart layout or mesh data.
"""

import argparse
import collections
import functools
import pathlib
import re

import numpy as np
import OpenEXR
from scipy.spatial import cKDTree

from pack_gl125 import json_read, read_mesh


FINISH = re.compile(
    r"^LD2_.*(?:painted|ceramic|graphite|floor|deck|ceiling)|"
    r"^HM02_.*(?:FOLDED_DECK|DARK_SATIN_COATED_REINFORCING_EDGE|PAINT_PANEL_LIGHT|primary_shell)",
    re.IGNORECASE,
)
EDGE_METRES = .045
MATCH_METRES = .015
FILL_FRACTION = .62


def finishes(prepared, manifest):
    materials = {i for i, row in enumerate(manifest["materials"]) if FINISH.search(row["name"])}
    groups = collections.defaultdict(list)
    for chunk in manifest["chunks"]:
        x, y, z = chunk["origin"]
        tower = 1900 <= x <= 2110 and 2080 <= y <= 2250 and 200 <= z <= 355
        if chunk["atlas"] < 0 or not (chunk["name"].startswith("MC00_") or tower):
            continue
        if not materials.intersection(chunk["materials"]):
            continue
        vertices, sections = read_mesh(prepared / "Meshes" / chunk["file"])
        vertices[:, :3] += np.asarray(chunk["origin"], dtype=np.float32)
        for material, faces in sections:
            if material in materials:
                groups[material].append((vertices[faces][:, :, [0, 1, 2, 8, 9]], chunk["atlas"]))
    for parts in groups.values():
        yield (np.concatenate([triangle for triangle, _ in parts]),
               np.concatenate([np.full(len(triangle), atlas, np.int32) for triangle, atlas in parts]))


def bordering_faces(geometry):
    """Find narrow returns with a parallel edge belonging to a broad finish face."""
    xyz = geometry[:, :, :3]
    cross = np.cross(xyz[:, 1] - xyz[:, 0], xyz[:, 2] - xyz[:, 0])
    area2 = np.linalg.norm(cross, axis=1)
    normals = cross / np.maximum(area2[:, None], 1e-9)
    lengths = np.linalg.norm(np.roll(xyz, -1, axis=1) - xyz, axis=2)
    height = area2 / np.maximum(lengths.max(axis=1), 1e-6)
    broad = np.flatnonzero(height >= .22)
    narrow = np.flatnonzero((height <= .34) & (height > .001) & (lengths.max(axis=1) > .35))
    if not len(broad) or not len(narrow):
        return

    tree = cKDTree(xyz[broad].reshape(-1, 3))
    distances, vertices = tree.query(xyz[narrow], k=8, distance_upper_bound=MATCH_METRES)
    safe_vertices = np.minimum(vertices, len(broad) * 3 - 1)
    nearest = broad[safe_vertices // 3]
    best_score = np.full(len(narrow), np.inf)
    best_face = np.full(len(narrow), -1)
    best_edge = np.zeros(len(narrow), dtype=np.int32)
    best_vertices = np.zeros((len(narrow), 2), dtype=np.int32)
    rows = np.arange(len(narrow))
    for a in range(3):
        b = (a + 1) % 3
        source_a = nearest[:, a]
        source_b = nearest[:, b]
        agree = source_a[:, :, None] == source_b[:, None, :]
        score = distances[:, a, :, None] + distances[:, b, None, :]
        different = safe_vertices[:, a, :, None] != safe_vertices[:, b, None, :]
        alignment = np.einsum("ij,ikj->ik", normals[narrow], normals[source_a])
        good = agree & different & (np.abs(alignment)[:, :, None] < .94) & (lengths[narrow, a, None, None] > .35)
        score = np.where(good, score, np.inf).reshape(len(narrow), -1)
        first = np.argmin(score, axis=1)
        update = score[rows, first] < best_score
        best_score[update] = score[rows[update], first[update]]
        best_face[update] = source_a[rows[update], first[update] // 8]
        best_edge[update] = a
        best_vertices[update, 0] = safe_vertices[rows[update], a, first[update] // 8] % 3
        best_vertices[update, 1] = safe_vertices[rows[update], b, first[update] % 8] % 3
    for row in np.flatnonzero(best_face >= 0):
        i, a = narrow[row], best_edge[row]
        yield i, best_face[row], a, best_vertices[row], area2[i] / lengths[i, a]


def bilinear(image, uv):
    size = image.shape[0]
    positions = np.clip(uv * size - .5, 0, size - 1.00001)
    lo = positions.astype(np.int32)
    fraction = positions - lo
    x, y = lo.T
    u, v = fraction.T[:, :, None]
    return ((1 - u) * (1 - v) * image[y, x] + u * (1 - v) * image[y, x + 1]
            + (1 - u) * v * image[y + 1, x] + u * v * image[y + 1, x + 1])


def soften(image, original, neighbour, triangle, adjacent, edge, corners, world_height):
    size = image.shape[0]
    a, b, c = triangle[[edge, (edge + 1) % 3, (edge + 2) % 3]] * size - .5
    matrix = np.column_stack((b - a, c - a))
    if abs(np.linalg.det(matrix)) < .001:
        return
    lower = np.maximum(0, np.floor(np.min([a, b, c], axis=0) - 1).astype(int))
    upper = np.minimum(size - 1, np.ceil(np.max([a, b, c], axis=0) + 1).astype(int))
    x, y = np.meshgrid(np.arange(lower[0], upper[0] + 1), np.arange(lower[1], upper[1] + 1))
    x, y = x.ravel(), y.ravel()
    inv = np.linalg.inv(matrix)
    w, v = inv @ (np.stack((x, y)) - a[:, None])
    # Cover the bilinear footprint at the rim. Leave the deep return shaded.
    near = (w >= 0) & (w + np.maximum(0, v) <= 1) & (v >= -.75 * np.linalg.norm(inv[1])) & (v * world_height < EDGE_METRES)
    x, y, w, v = x[near], y[near], w[near], v[near]
    if not len(x):
        return
    p, q = adjacent[corners]
    inside = adjacent[3 - int(corners.sum())]
    along = np.clip(w / np.maximum(.0001, 1 - v), 0, 1)
    source = p + (q - p) * along[:, None]
    # Sample a texel inside the larger face to avoid its own seam and padding.
    inward = inside - source
    distance = np.maximum(np.linalg.norm(inward, axis=1), 1e-6)
    source += inward * np.minimum(.25, 1.5 / neighbour.shape[0] / distance)[:, None]
    light = bilinear(neighbour, source)
    old = original[y, x].astype(np.float32)
    lift = FILL_FRACTION * (1 - np.clip(v * world_height / EDGE_METRES, 0, 1))
    target = light * lift[:, None]
    selected = (light.mean(axis=1) >= .14) & (old.mean(axis=1) < target.mean(axis=1))
    x, y, target = x[selected], y[selected], target[selected]
    image[y, x] = np.maximum(image[y, x], target)


def repair(prepared, lightmaps, output, selected):
    manifest = json_read(prepared / "World.json")
    patches = collections.defaultdict(list)
    for geometry, atlases in finishes(prepared, manifest):
        for i, j, edge, corners, height in bordering_faces(geometry):
            if selected and int(atlases[i]) not in selected:
                continue
            patches[int(atlases[i])].append((int(atlases[j]), geometry[i, :, 3:5].copy(),
                                           geometry[j, :, 3:5].copy(), edge, corners, height))

    def name(atlas):
        return f"Irradiance_{atlas:03}.exr"

    @functools.lru_cache(maxsize=5)
    def load(atlas):
        with OpenEXR.File(str(lightmaps / name(atlas))) as file:
            return file.header(), file.channels()["RGB"].pixels[::-1]

    output.mkdir(parents=True, exist_ok=True)
    for atlas, edits in sorted(patches.items()):
        header, source = load(atlas)
        result = source.copy()
        for other, triangle, adjacent, edge, corners, height in sorted(edits, key=lambda edit: edit[0]):
            soften(result, source, load(other)[1], triangle, adjacent, edge, corners, height)
        changed = np.count_nonzero(np.any(source != result, axis=2))
        if not changed:
            continue
        header = {key: value for key, value in header.items() if key != "channels"}
        temp = output / (name(atlas) + ".tmp")
        pixels = np.ascontiguousarray(result[::-1])
        OpenEXR.File(header, {"RGB": pixels}).write(str(temp))
        temp.replace(output / name(atlas))
        print(f"{atlas:03}: {changed} seam texels raised", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("prepared", type=pathlib.Path)
    parser.add_argument("lightmaps", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--atlases", nargs="+", type=int)
    args = parser.parse_args()
    repair(args.prepared, args.lightmaps, args.output, set(args.atlases or ()))


if __name__ == "__main__":
    main()
