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
    r"^LD2_.*(?:painted|ceramic|graphite|floor|deck|ceiling|aluminium|steel)|"
    r"^HM02_.*(?:deck|coated|panel|structural_steel|primary_shell|cladding)",
    re.IGNORECASE,
)
EDGE_METRES = .085
MATCH_METRES = .060
FILL_FRACTION = .82


def finishes(prepared, manifest):
    materials = {i for i, row in enumerate(manifest["materials"]) if FINISH.search(row["name"])}
    groups = collections.defaultdict(list)
    for chunk in manifest["chunks"]:
        x, y, z = chunk["origin"]
        tower = 1900 <= x <= 2110 and 2080 <= y <= 2250 and 200 <= z <= 355
        mc00 = chunk["name"].startswith("MC00_")
        if chunk["atlas"] < 0 or not (mc00 or tower):
            continue
        if not materials.intersection(chunk["materials"]):
            continue
        vertices, sections = read_mesh(prepared / "Meshes" / chunk["file"])
        vertices[:, :3] += np.asarray(chunk["origin"], dtype=np.float32)
        for material, faces in sections:
            if material in materials:
                groups[mc00, material].append((vertices[faces][:, :, [0, 1, 2, 8, 9]], chunk["atlas"]))
    for parts in groups.values():
        yield (np.concatenate([triangle for triangle, _ in parts]),
               np.concatenate([np.full(len(triangle), atlas, np.int32) for triangle, atlas in parts]))


def bordering_faces(geometry):
    """Pair narrow returns with parallel boundaries of nearby wide surfaces."""
    xyz = geometry[:, :, :3]
    cross = np.cross(xyz[:, 1] - xyz[:, 0], xyz[:, 2] - xyz[:, 0])
    area2 = np.linalg.norm(cross, axis=1)
    normals = cross / np.maximum(area2[:, None], 1e-9)
    directions = np.roll(xyz, -1, axis=1) - xyz
    lengths = np.linalg.norm(directions, axis=2)
    height = area2 / np.maximum(lengths.max(axis=1), 1e-6)
    broad, broad_edge = np.where((height >= .26)[:, None] & (lengths >= .32))
    narrow = np.flatnonzero((height <= .31) & (height > .001) & (lengths.max(axis=1) > .27))
    if not len(broad) or not len(narrow):
        return
    origins = xyz[broad, broad_edge]
    edges = directions[broad, broad_edge]
    extents = lengths[broad, broad_edge]
    units = edges / extents[:, None]
    steps = np.clip(np.ceil(extents / .18).astype(int), 2, 280) + 1
    owners = np.repeat(np.arange(len(steps)), steps)
    t = (np.arange(len(owners)) - np.repeat(np.cumsum(steps) - steps, steps)) / (steps[owners] - 1)
    tree = cKDTree(origins[owners] + t[:, None] * edges[owners])
    for target in np.array_split(narrow, max(1, int(np.ceil(len(narrow) / 4000)))):
        edge_length = lengths[target]
        row, edge_index = np.where(edge_length > np.maximum(.27, .76 * edge_length.max(axis=1))[:, None])
        target = target[row]
        line = directions[target, edge_index]
        line_unit = line / lengths[target, edge_index, None]
        matches = set()
        # Check both halves of a return. An adjacent long face need not share
        # its endpoints or UV chart, and may span several render tiles.
        for fraction in (.28, .72):
            probe = xyz[target, edge_index] + fraction * line
            distances, samples = tree.query(probe, k=20, distance_upper_bound=.19)
            nearby = owners[np.minimum(samples, len(owners) - 1)]
            offset = probe[:, None, :] - origins[nearby]
            along = np.einsum("ijk,ijk->ij", offset, units[nearby])
            orthogonal = offset - np.clip(along, 0, extents[nearby])[:, :, None] * units[nearby]
            distance = np.linalg.norm(orthogonal, axis=2)
            alignment = np.abs(np.einsum("ik,ijk->ij", line_unit, units[nearby]))
            facing = np.einsum("ik,ijk->ij", normals[target], normals[broad[nearby]])
            valid = (np.isfinite(distances) & (distance < MATCH_METRES) & (alignment > .96)
                     & (facing > -.72) & (broad[nearby] != target[:, None]))
            best = np.where(valid, distance, np.inf).argmin(axis=1)
            for row in np.flatnonzero(valid[np.arange(len(target)), best]):
                neighbor = nearby[row, best[row]]
                matches.add((target[row], broad[neighbor], broad_edge[neighbor]))
        yield from sorted(matches)


def bilinear(image, uv):
    size = image.shape[0]
    positions = np.clip(uv * size - .5, 0, size - 1.00001)
    lo = positions.astype(np.int32)
    fraction = positions - lo
    x, y = lo.T
    u, v = fraction.T[:, :, None]
    return ((1 - u) * (1 - v) * image[y, x] + u * (1 - v) * image[y, x + 1]
            + (1 - u) * v * image[y + 1, x] + u * v * image[y + 1, x + 1])


def soften(image, original, neighbour, triangle, adjacent, source_edge):
    size = image.shape[0]
    xyz = triangle[:, :3]
    edges = np.roll(xyz, -1, axis=0) - xyz
    width = np.linalg.norm(np.cross(edges[0], edges[1])) / np.linalg.norm(edges, axis=1).max()
    # A wide return is its own lit surface. Touch only its immediate seam,
    # with a smaller correction so an entire rail or frame cannot light up.
    wide = width >= .075
    reach = .033 if wide else EDGE_METRES
    a, b, c = triangle[:, 3:] * size - .5
    matrix = np.column_stack((b - a, c - a))
    if abs(np.linalg.det(matrix)) < .001:
        return
    lower = np.maximum(0, np.floor(np.min([a, b, c], axis=0) - 1).astype(int))
    upper = np.minimum(size - 1, np.ceil(np.max([a, b, c], axis=0) + 1).astype(int))
    x, y = np.meshgrid(np.arange(lower[0], upper[0] + 1), np.arange(lower[1], upper[1] + 1))
    x, y = x.ravel(), y.ravel()
    inv = np.linalg.inv(matrix)
    u, v = inv @ (np.stack((x, y)) - a[:, None])
    bary = np.stack((1 - u - v, u, v), axis=1)
    padding = .8 * np.linalg.norm(np.vstack((-inv.sum(axis=0), inv)), axis=1)
    near = (bary >= -padding).all(axis=1)
    x, y, bary = x[near], y[near], np.maximum(0, bary[near])
    if not len(x):
        return
    world = (bary / bary.sum(axis=1)[:, None]) @ triangle[:, :3]
    a, b, c = adjacent[[source_edge, (source_edge + 1) % 3, (source_edge + 2) % 3]]
    line = b[:3] - a[:3]
    along = np.clip((world - a[:3]) @ line / (line @ line), 0, 1)
    gap = np.linalg.norm(world - (a[:3] + along[:, None] * line), axis=1)
    rim = gap < reach
    x, y, along, gap = x[rim], y[rim], along[rim], gap[rim]
    if not len(x):
        return
    source = a[3:] + (b[3:] - a[3:]) * along[:, None]
    # Sample a texel inside the larger face to avoid its own seam and padding.
    inward = c[3:] - source
    distance = np.maximum(np.linalg.norm(inward, axis=1), 1e-6)
    source += inward * np.minimum(.25, 1.5 / neighbour.shape[0] / distance)[:, None]
    light = bilinear(neighbour, source)
    old = original[y, x].astype(np.float32)
    inner = .007 if wide else .021
    distance = np.clip((gap - inner) / (reach - inner), 0, 1)
    lift = FILL_FRACTION * (1 - distance * distance * (3 - 2 * distance))
    target = light * lift[:, None]
    if wide:
        target = np.minimum(target, old + light * .26)
    selected = (light.mean(axis=1) >= .14) & (old.mean(axis=1) < target.mean(axis=1))
    x, y, target = x[selected], y[selected], target[selected]
    image[y, x] = np.maximum(image[y, x], target)


def repair(prepared, lightmaps, output, selected):
    manifest = json_read(prepared / "World.json")
    patches = collections.defaultdict(list)
    for geometry, atlases in finishes(prepared, manifest):
        for i, j, edge in bordering_faces(geometry):
            if selected and int(atlases[i]) not in selected:
                continue
            patches[int(atlases[i])].append((int(atlases[j]), geometry[i].copy(), geometry[j].copy(), edge))

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
        for other, triangle, adjacent, edge in sorted(edits, key=lambda edit: edit[0]):
            soften(result, source, load(other)[1], triangle, adjacent, edge)
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
