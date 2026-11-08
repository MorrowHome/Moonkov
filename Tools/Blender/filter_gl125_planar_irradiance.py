"""Remove mottling from planar baked irradiance without crossing lightmap charts.

Requires NumPy, SciPy and OpenEXR. Writes only changed atlases to a separate
directory, using the same UV coordinates, image size and linear RGB encoding.
"""
import argparse
from collections import defaultdict
from pathlib import Path

import numpy as np
import OpenEXR
from scipy.ndimage import distance_transform_edt, gaussian_filter
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components

from pack_gl125 import json_read, read_mesh


LUMINANCE = np.array([.2126, .7152, .0722], dtype=np.float32)


def planar_groups(faces, normals):
    edges = np.sort(np.stack((faces, np.roll(faces, -1, axis=1)), axis=2), axis=2)
    edges = np.ascontiguousarray(edges.reshape(-1, 2))
    key = edges.view(np.uint64).ravel()
    order = key.argsort()
    adjacent = key[order[1:]] == key[order[:-1]]
    first = order[:-1][adjacent] // 3
    second = order[1:][adjacent] // 3
    same_plane = (np.einsum("ij,ij->i", normals[first], normals[second]) >= .985) & (first != second)
    graph = coo_matrix((np.ones(same_plane.sum(), np.uint8), (first[same_plane], second[same_plane])),
                       shape=(len(faces), len(faces)))
    count, labels = connected_components(graph, directed=False)
    order = labels.argsort(kind="stable")
    groups = np.split(order, np.cumsum(np.bincount(labels, minlength=count))[:-1])
    return groups


def groups_for_chunk(prepared, chunk, size):
    vertices, sections = read_mesh(prepared / "Meshes" / chunk["file"])
    for _, faces in sections:
        xyz = vertices[faces, :3]
        cross = np.cross(xyz[:, 1] - xyz[:, 0], xyz[:, 2] - xyz[:, 0])
        area2 = np.linalg.norm(cross, axis=1)
        normals = cross / np.maximum(area2[:, None], 1e-12)
        uvs = vertices[faces, 8:10] * size - .5
        edge_a, edge_b = uvs[:, 1] - uvs[:, 0], uvs[:, 2] - uvs[:, 0]
        uv_area2 = np.abs(edge_a[:, 0] * edge_b[:, 1] - edge_a[:, 1] * edge_b[:, 0])
        for group in planar_groups(faces, normals):
            surface = area2[group].sum()
            if surface < .8:
                continue
            pixels = uv_area2[group].sum()
            if pixels < 128:
                continue
            triangles = uvs[group]
            lo = np.maximum(np.ceil(triangles.min(axis=(0, 1))).astype(int), 0)
            hi = np.minimum(np.floor(triangles.max(axis=(0, 1))).astype(int) + 1, size)
            if (hi - lo).min() < 9:
                continue
            yield triangles, lo, hi, float(np.sqrt(pixels / surface))


def blur(signal, mask, sigma):
    kernel = (sigma, sigma, 0) if signal.ndim == 3 else sigma
    weights = gaussian_filter(mask, sigma, mode="constant")
    if signal.ndim == 3:
        weights = weights[:, :, None]
        mask = mask[:, :, None]
    return gaussian_filter(signal * mask, kernel, mode="constant") / np.maximum(weights, 1e-6)


def ramp(value, lower, upper):
    t = np.clip((value - lower) / (upper - lower), 0, 1)
    return t * t * (3 - 2 * t)


def rasterize(triangles, lo, hi):
    width, height = hi - lo
    mask = np.zeros((height, width), bool)
    for triangle in triangles - lo:
        a, b, c = triangle
        orientation = np.sign((b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]))
        if not orientation:
            continue
        x0, y0 = np.maximum(np.ceil(triangle.min(axis=0)).astype(int), 0)
        x1, y1 = np.minimum(np.floor(triangle.max(axis=0)).astype(int) + 1, [width, height])
        if x0 >= x1 or y0 >= y1:
            continue
        y, x = np.ogrid[y0:y1, x0:x1]
        inside = np.ones((y1 - y0, x1 - x0), bool)
        for start, end in ((a, b), (b, c), (c, a)):
            edge = end - start
            inside &= orientation * (edge[0] * (y - start[1]) - edge[1] * (x - start[0])) >= -1e-5
        mask[y0:y1, x0:x1] |= inside
    return mask


def reduce_mottle(rgb, coverage, pixels_per_metre):
    # The input represents radiance only. Texture, roughness, sun and contact
    # shadows are applied separately by Unity. Keep coherent baked light edges.
    sigma = float(np.clip(pixels_per_metre * .23, 2.0, 6.0))
    rgb = rgb.astype(np.float32)
    luminance = np.maximum(rgb @ LUMINANCE, 0)
    if np.quantile(luminance[coverage], .75) < .018:
        return None
    valid = coverage.astype(np.float32)
    guide = blur(np.log(luminance + .018), valid, max(.85, sigma * .40))
    dy, dx = np.gradient(guide)
    yy = blur(dy * dy, valid, sigma * .78)
    xx = blur(dx * dx, valid, sigma * .78)
    xy = blur(dx * dy, valid, sigma * .78)
    power = xx + yy
    coherence = np.sqrt((xx - yy) ** 2 + 4 * xy * xy) / np.maximum(power, 1e-8)
    edge = ramp(np.sqrt(power), .024, .085) * ramp(coherence, .38, .78)
    background = blur(rgb, valid, sigma)
    smooth_luma = np.maximum(background @ LUMINANCE, 0)
    residual = (luminance - smooth_luma) / (smooth_luma + .024)
    detail = np.sqrt(blur(residual * residual, valid, max(1.2, sigma * .75)))
    amount = .86 * ramp(detail, .009, .032) * (1 - .88 * edge)
    # Fade to the original near the chart padding sampled by bilinear filtering.
    distance = distance_transform_edt(np.pad(coverage, 1))[1:-1, 1:-1]
    amount *= ramp(distance, .4, 2.5)
    correction = (background - rgb) * amount[:, :, None]
    return correction


def filter_atlas(prepared, lightmaps, output, atlas, chunks):
    path = f"Irradiance_{atlas['id']:03}.exr"
    with OpenEXR.File(str(lightmaps / path)) as source:
        rgb = source.channels()["RGB"].pixels[::-1].copy()
    size = atlas["size"]
    if rgb.shape != (size, size, 3) or not np.isfinite(rgb).all():
        raise ValueError(f"Invalid linear RGB input {path}")
    result = rgb.copy()
    touched = np.zeros((size, size), bool)
    changes = 0
    for chunk in chunks:
        for triangles, lo, hi, density in groups_for_chunk(prepared, chunk, size):
            x0, y0 = lo
            x1, y1 = hi
            mask = rasterize(triangles, lo, hi)
            if mask.sum() < 48:
                continue
            src = rgb[y0:y1, x0:x1]
            delta = reduce_mottle(src, mask, density)
            if delta is None:
                continue
            occupied = touched[y0:y1, x0:x1]
            keep = mask & ~occupied & (np.max(np.abs(delta), axis=2) > .001)
            if not keep.any():
                continue
            target = result[y0:y1, x0:x1]
            target[keep] = np.maximum(src[keep] + delta[keep], 0)
            occupied[keep] = True
            changes += 1
    if changes:
        hdr = {"compression": OpenEXR.ZIP_COMPRESSION, "type": OpenEXR.scanlineimage}
        channels = {name: np.ascontiguousarray(result[::-1, :, i], dtype=np.float16)
                    for i, name in enumerate("RGB")}
        with OpenEXR.File(hdr, channels) as target:
            target.write(str(output / path))
    return changes, int(touched.sum())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("prepared", type=Path)
    parser.add_argument("lightmaps", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--atlases", type=int, nargs="+")
    args = parser.parse_args()
    if args.lightmaps.resolve() == args.output.resolve():
        raise ValueError("The input and output lightmap directories must differ")
    world = json_read(args.prepared / "World.json")
    chunks = defaultdict(list)
    for chunk in world["chunks"]:
        chunks[chunk["atlas"]].append(chunk)
    args.output.mkdir(parents=True, exist_ok=True)
    for atlas in world["atlases"]:
        index = atlas["id"]
        if not atlas["terrain"] and (not args.atlases or index in args.atlases):
            groups, texels = filter_atlas(args.prepared, args.lightmaps, args.output, atlas, chunks[index])
            print(f"{index:03}: {groups} planar patches, {texels} filtered texels", flush=True)


if __name__ == "__main__":
    main()
