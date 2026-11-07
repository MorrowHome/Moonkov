"""Pack the GL125 evaluated mesh export for Unity and the Cycles bake.

Use Python with NumPy and xatlas. The output stays outside Assets until installation.
"""

import argparse
import collections
import hashlib
import json
import math
import pathlib
import re
import struct
import time

import numpy as np


FAR_TERRAIN = "FT09_Unchanged_Outside_Display"
STRUCTURAL = re.compile(
    r"floor|tread|landing|stairs?|stairway|wall|threshold|deck|platform|catwalk|ramp|street|road|"
    r"walkway|walkable|path|lintel|jamb|terrain|ground|ceiling|roof|foundation|plinth|shell|bridge|"
    r"cladding|support|girder|pillar|column|bulkhead|guardrail|balustrade|barrier|parapet|sill|pane|"
    r"slab|hull|chassis|housing|service[_ ]route|pressure[_ ]door|closed[_ ]leaf|courtyard|footing|skirt",
    re.IGNORECASE,
)
DETAIL = re.compile(
    r"decal|fastener|bolt|washer|label|marker|logo|cable|fillet|trim|indicator|switch|paint|stripe|"
    r"diffuser|decor|inscription|signage|stowed telescopic door|individual leaf carriage",
    re.IGNORECASE,
)


def json_read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def json_write(path, value):
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    temp.replace(path)


def keep_faces(positions):
    ab = positions[:, 1] - positions[:, 0]
    ac = positions[:, 2] - positions[:, 0]
    area = np.linalg.norm(np.cross(ab, ac), axis=1)
    return np.isfinite(positions).all(axis=(1, 2)) & (area > 2e-8)


def weld(rows):
    rows = np.ascontiguousarray(rows, dtype=np.float32)
    keys = rows.view(np.dtype((np.void, rows.shape[1] * 4))).ravel()
    _, first, inverse = np.unique(keys, return_index=True, return_inverse=True)
    return rows[first], inverse.reshape(-1, 3).astype(np.uint32)


def write_mesh(path, vertices, submeshes):
    """Mesh vertices are Z-up in metres. Each submesh is a material ID and triangle indices."""
    with path.open("wb") as stream:
        stream.write(struct.pack("<4sIII", b"GLM1", len(vertices), vertices.shape[1], len(submeshes)))
        np.asarray(vertices, dtype="<f4").tofile(stream)
        for material, triangles in submeshes:
            stream.write(struct.pack("<iI", material, triangles.size))
            np.asarray(triangles, dtype="<u4").tofile(stream)


def read_mesh(path):
    with path.open("rb") as stream:
        magic, count, stride, parts = struct.unpack("<4sIII", stream.read(16))
        if magic != b"GLM1" or stride not in (3, 10):
            raise ValueError(f"Unexpected packed mesh format in {path}")
        vertices = np.fromfile(stream, dtype="<f4", count=count * stride).reshape(count, stride)
        sections = []
        for _ in range(parts):
            material, count = struct.unpack("<iI", stream.read(8))
            indices = np.fromfile(stream, dtype="<u4", count=count).reshape(-1, 3)
            sections.append((material, indices))
    return vertices, sections


def cell_parts(triangles, step):
    points = triangles[:, :, :3].mean(axis=1)
    coords = np.floor(points / step).astype(np.int32)
    cells, membership = np.unique(coords, axis=0, return_inverse=True)
    order = np.argsort(membership, kind="stable")
    bounds = np.cumsum(np.bincount(membership))[:-1]
    for cell, faces in zip(cells, np.split(order, bounds)):
        yield tuple(map(int, cell)), faces


def tile_name(cell):
    return "_".join(map(str, cell))


def collision_geometry(row, name, points, covered):
    if row["name"] in covered and not row["instance"]:
        return False
    if any(any(part.endswith("NOT_ACCEPTED") for part in path) for path in row["paths"]):
        return False
    if DETAIL.search(name) or not (row["solid"] or STRUCTURAL.search(name)):
        return False
    # Tiny fasteners and details cannot obstruct the route. Keep thin structural
    # panels if both other axes span a physically meaningful area.
    span = np.sort(np.ptp(points.reshape(-1, 3), axis=0))
    return span[2] >= 0.24 and span[1] >= 0.08


def prepare_tiles(input_dir, exported, covered, originals, receiver_states):
    art = collections.defaultdict(list)
    physics = collections.defaultdict(list)
    receiver_signatures = collections.defaultdict(list)
    for index, light in enumerate(exported["lights"]):
        for name, state in receiver_states.get(light["name"], {}).items():
            receiver_signatures[name].append(index if state == "INCLUDE" else -index - 1)
    objects, omitted = 0, 0
    for part in exported["parts"]:
        with np.load(input_dir / part["file"], allow_pickle=False) as data:
            visual, mat, proxy = data["mesh"], data["material"], data["collision"]
        for row in part["objects"]:
            offset, count = row["offset"], row["count"]
            end = offset + count
            if row["proxy"]:
                source = proxy[offset:end]
                for cell, faces in cell_parts(source, np.array([80, 80, 24])):
                    physics[cell].append(source[faces])
                continue
            source = visual[offset:end]
            indices = mat[offset:end]
            good = keep_faces(source[:, :, :3]) & np.isfinite(source).all(axis=(1, 2))
            omitted += int(len(good) - np.count_nonzero(good))
            if not np.any(good):
                continue
            source, indices = source[good], indices[good]
            far = row["name"] == FAR_TERRAIN
            position = source[:, :, :3]
            name = originals.get(row["name"], row["name"])
            solid = far or collision_geometry(row, name, position, covered)
            center = position.mean(axis=(0, 1))
            dense = not far and 1510 <= center[0] <= 1720 and 2040 <= center[1] <= 2250 and 190 <= center[2] <= 286
            step = np.array([256, 256, 10000]) if far else np.array([16, 16, 8] if dense else [80, 80, 24])
            # Large terrain and deck surfaces may cross a number of culling cells.
            # Compact pieces stay together, so cutting a building at a tile edge
            # does not split every wall into independent lightmap islands.
            if far or np.any(np.ptp(position.reshape(-1, 3), axis=0) > step):
                parts = cell_parts(source, step)
            else:
                cell = tuple(map(int, np.floor(center / step)))
                parts = [(cell, slice(None))]
            for cell, faces in parts:
                signature = tuple(receiver_signatures.get(row["name"], ()))
                art[(far, dense, *cell, signature)].append((source[faces], indices[faces]))
            if solid and not far:
                for cell, faces in cell_parts(position, np.array([80, 80, 24])):
                    physics[cell].append(position[faces])
            objects += 1
    print("GL125_TILES", objects, len(art), len(physics), "degenerate faces omitted", omitted, flush=True)
    return art, physics, omitted


def atlas_groups(art):
    current, count = [], 0
    for cell, entries in sorted(art.items()):
        if cell[0]:
            continue
        faces = sum(len(geometry) for geometry, _ in entries)
        limit = 54000 if cell[1] else 105000
        if current and (current[-1][1] != cell[1] or count + faces > limit):
            yield current, count
            current, count = [], 0
        current.append(cell)
        count += faces
    if current:
        yield current, count


def pack_charts(meshes, density):
    import xatlas

    atlas = xatlas.Atlas()
    topology = []
    for triangles in meshes:
        positions, indices = weld(triangles[:, :, :3].reshape(-1, 3))
        atlas.add_mesh(positions - positions.mean(axis=0), indices)
        topology.append(indices)
    options = xatlas.PackOptions()
    if density:
        options.texels_per_unit = density
    else:
        options.resolution = 2048
    options.padding = 8
    options.bilinear = True
    options.blockAlign = True
    atlas.generate(pack_options=options)
    if atlas.atlas_count != 1:
        raise ValueError(f"Expected a single UV atlas, got {atlas.atlas_count}")
    # Preserve the physical texel density requested from xatlas, including its
    # chart padding. Normalizing a larger atlas into a fixed 2K map loses both.
    width, height = atlas.width, atlas.height
    natural_size = 2 ** math.ceil(math.log2(max(512, width, height)))
    if density and natural_size > 4096 and len(meshes) > 1:
        return None, natural_size, 0
    size = min(4096, natural_size) if density else 2048
    scale = np.asarray([width, height], np.float32) / natural_size if density else 1
    uv, collapsed = [], []
    for i, topology_indices in enumerate(topology):
        remap, indices, coords = atlas[i]
        if not np.array_equal(remap[indices], topology_indices):
            raise ValueError("UV unwrapping changed geometry or triangle ordering")
        coords = coords[indices] * scale
        doubled_area = np.abs((coords[:, 1, 0] - coords[:, 0, 0]) * (coords[:, 2, 1] - coords[:, 0, 1]) -
                              (coords[:, 1, 1] - coords[:, 0, 1]) * (coords[:, 2, 0] - coords[:, 0, 0]))
        collapsed.append((doubled_area <= 1e-12) | ~np.isfinite(coords).all(axis=(1, 2)))
        uv.append(coords)
    count = int(sum(np.count_nonzero(mask) for mask in collapsed))
    if not count:
        return uv, size, 0
    # On bevel seams xatlas can leave a valid but very thin face without a
    # chart. Give those faces individually padded texels instead of dropping
    # them or letting them sample another surface's irradiance.
    stride = 12
    while math.ceil(count / (size // stride)) * stride > size / 3:
        size *= 2
    columns = size // stride
    band = (math.ceil(count / columns) * stride + stride) / size
    offset = 0
    for coords, bad in zip(uv, collapsed):
        coords[~bad, :, 1] = coords[~bad, :, 1] * (1 - band) + band
        indices = np.arange(offset, offset + np.count_nonzero(bad))
        origin = np.column_stack([indices % columns, indices // columns])[:, None, :] * stride
        coords[bad] = (origin + np.array([[4, 4], [8, 4], [4, 8]])) / size
        offset += len(indices)
    return uv, size, count


def visual_chunk(output, cell, atlas, triangles, material_ids, uv):
    coordinates = triangles[:, :, :3]
    origin = ((coordinates.min(axis=(0, 1)) + coordinates.max(axis=(0, 1))) * 0.5).astype(np.float32)
    data = np.concatenate([triangles, uv], axis=2)
    data[:, :, :3] -= origin
    vertices, faces = weld(data.reshape(-1, 10))
    submeshes = [(int(mid), faces[material_ids == mid]) for mid in np.unique(material_ids)]
    signature = cell[-1]
    suffix = "_" + hashlib.sha256(str(signature).encode()).hexdigest()[:8] if signature else ""
    prefix = "DEM_" if cell[0] else "MC00_" if cell[1] else "Sector_"
    name = prefix + tile_name(cell[2:5]) + suffix
    file = name + ".bytes"
    write_mesh(output / "Meshes" / file, vertices, submeshes)
    return dict(name=name, file=file, atlas=atlas, origin=origin.tolist(), vertices=len(vertices),
                triangles=len(triangles), collider=bool(cell[0]), materials=[mid for mid, _ in submeshes],
                receiverSignature=signature)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("placement_contract", type=pathlib.Path)
    parser.add_argument("collision_contract", type=pathlib.Path)
    parser.add_argument("receiver_states", type=pathlib.Path)
    args = parser.parse_args()
    start = time.monotonic()
    exported = json_read(args.input / "Export.json")
    (args.output / "Meshes").mkdir(parents=True, exist_ok=True)
    contract = json_read(args.collision_contract)
    if contract["source_count"] != 3252 or contract["proxy_count"] != 277:
        raise ValueError("Unexpected V73 collision contract")
    covered = {row["derivative_render_output_object"] for row in contract["ranges"]}
    replacement = json_read(args.placement_contract)["replacements"]
    originals = {row["new_exact_name"]: row["old_exact_name"] for row in replacement}
    if len(originals) != 733:
        raise ValueError("Incomplete GL124/125 layout contract")
    receiver_states = json_read(args.receiver_states)
    art, physics, omitted = prepare_tiles(args.input, exported, covered, originals, receiver_states)

    chunks, atlases = [], []

    def pack_group(cells, meshes, materials):
        index = len(atlases)
        count = sum(map(len, meshes))
        print("GL125_UV_BEGIN", index, count, flush=True)
        atlas_uv, size, seam_triangles = pack_charts(meshes, 16 if cells[0][1] else None)
        if atlas_uv is None:
            mid = len(cells) // 2
            pack_group(cells[:mid], meshes[:mid], materials[:mid])
            pack_group(cells[mid:], meshes[mid:], materials[mid:])
            return
        for cell, triangles, mids, uv in zip(cells, meshes, materials, atlas_uv):
            chunks.append(visual_chunk(args.output, cell, index, triangles, mids, uv))
        atlases.append(dict(id=index, size=size, triangles=count, terrain=False, seamTriangles=seam_triangles))
        print("GL125_UV_DONE", index, size, seam_triangles, round(time.monotonic() - start, 1), flush=True)

    for cells, _ in atlas_groups(art):
        pairs = [art.pop(cell) for cell in cells]
        meshes = [np.concatenate([g for g, _ in pair]) for pair in pairs]
        materials = [np.concatenate([m for _, m in pair]) for pair in pairs]
        pack_group(cells, meshes, materials)

    # The exterior DEM is a continuous heightfield. Preserve this XY mapping
    # across the smaller render/collision tiles, with a border in each quadrant.
    dem_atlases = collections.defaultdict(list)
    for cell, pieces in sorted(art.items()):
        _, _, x, y, _, _ = cell
        dem_atlases[(x // 8, y // 8)].append((cell, pieces))
    for (qx, qy), tiles in sorted(dem_atlases.items()):
        index, count, size = len(atlases), 0, 2048
        for cell, pieces in tiles:
            triangles = np.concatenate([g for g, _ in pieces])
            mids = np.concatenate([m for _, m in pieces])
            corner = np.array([qx, qy], np.float32) * 2048
            uv = (triangles[:, :, :2] - corner) / 2048 * .98 + .01
            if uv.min() < 0 or uv.max() > 1:
                raise ValueError("Exterior triangle exceeds the terrain lightmap border")
            chunks.append(visual_chunk(args.output, cell, index, triangles, mids, uv))
            count += len(triangles)
        atlases.append(dict(id=index, size=size, triangles=count, terrain=True))

    for cell, pieces in sorted(physics.items()):
        triangles = np.concatenate(pieces)
        triangles = triangles[keep_faces(triangles)]
        if not len(triangles):
            continue
        origin = ((triangles.min(axis=(0, 1)) + triangles.max(axis=(0, 1))) * .5).astype(np.float32)
        vertices, faces = weld(triangles.reshape(-1, 3) - origin)
        name = "Collision_" + tile_name(cell)
        file = name + ".bytes"
        write_mesh(args.output / "Meshes" / file, vertices, [(-1, faces)])
        chunks.append(dict(name=name, file=file, atlas=-1, origin=origin.tolist(), vertices=len(vertices),
                           triangles=len(triangles), collider=True, materials=[]))

    world = dict(version=1, sourceHash=exported["sourceHash"], atlases=atlases, chunks=chunks,
                 lights=exported["lights"], materials=exported["materials"], omittedDegenerateFaces=omitted)
    json_write(args.output / "World.json", world)
    print("GL125_PACK_COMPLETE", len(chunks), len(atlases), round(time.monotonic() - start, 1), flush=True)


if __name__ == "__main__":
    main()
