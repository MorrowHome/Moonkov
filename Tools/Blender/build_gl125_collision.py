"""Build static collision from the visible, evaluated GL125 surface export."""

import argparse
import collections
import hashlib
import json
import pathlib
import re

import numpy as np

from pack_gl125 import FAR_TERRAIN, cell_parts, json_read, json_write, keep_faces, tile_name, weld, write_mesh


MOVING_DETAIL = re.compile(r"stowed telescopic door|individual leaf carriage", re.IGNORECASE)
MARKING = re.compile(r"\b(?:decal|glyph|label|locator|marker|logo|paint|stripe|inscription|signage)\b", re.IGNORECASE)
SMALL_DETAIL = re.compile(r"\b(?:fastener|bolt|washer|fillet|indicator|switch|diffuser)\b", re.IGNORECASE)
TILE_SIZE = np.array([80, 80, 24])


def surface_triangles(row, evaluated, original_name):
    name = re.sub(r"[_-]+", " ", original_name)
    if MOVING_DETAIL.search(name) or any(
        any("ROLLBACK" in part.upper() or part.upper().endswith("NOT_ACCEPTED") for part in path)
        for path in row["paths"]
    ):
        return None

    triangles = evaluated[:, :, :3]
    valid = keep_faces(triangles) & np.isfinite(evaluated).all(axis=(1, 2))
    triangles = triangles[valid]
    if not len(triangles):
        return None
    points = triangles.reshape(-1, 3)
    span = np.sort(np.ptp(points, axis=0))
    if not row["solid"] and (span[2] < .22 or (span[1] < .07 and span[2] < 1.25)):
        return None
    if SMALL_DETAIL.search(name) and span[2] < .8:
        return None

    cross = np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0])
    largest = int(np.argmax(np.einsum("ij,ij->i", cross, cross)))
    normal = cross[largest] / np.linalg.norm(cross[largest])
    plane = np.max(np.abs((points - triangles[largest, 0]) @ normal)) <= .002
    if plane and MARKING.search(name):
        return None
    # PhysX mesh faces are one-sided. A planar architectural sheet needs a
    # reverse face, separated by 2 mm so cooking cannot merge the two faces.
    if plane and np.all(cross @ normal > 0):
        return np.concatenate((triangles, triangles[:, [0, 2, 1]] - normal * .002))
    return triangles


def build_collision(evaluated, prepared, placement_contract):
    world_bytes = (prepared / "World.json").read_bytes()
    world = json.loads(world_bytes)
    exported = json_read(evaluated / "Export.json")
    if world["sourceHash"] != exported["sourceHash"]:
        raise ValueError("GL125 geometry and render manifest belong to different sources.")
    replacements = json_read(placement_contract)["replacements"]
    originals = {row["new_exact_name"]: row["old_exact_name"] for row in replacements}
    if len(originals) != 733:
        raise ValueError("Incomplete GL124/125 layout contract.")
    retired = set(originals.values())
    placed = set()
    tiles = collections.defaultdict(list)
    included, proxies = 0, 0
    for part in exported["parts"]:
        with np.load(evaluated / part["file"], allow_pickle=False) as data:
            geometry = data["mesh"]
        for row in part["objects"]:
            if row["proxy"]:
                proxies += 1
                continue
            name = row["name"]
            if name in retired:
                raise ValueError(f"Retired GL124 geometry is present in the active export: {name}")
            if name in originals:
                placed.add(name)
            if name == FAR_TERRAIN:
                continue  # The existing terrain colliders already share the visible DEM meshes.
            start = row["offset"]
            surface = surface_triangles(row, geometry[start:start + row["count"]], originals.get(name, name))
            if surface is None:
                continue
            included += 1
            if np.all(np.ptp(surface.reshape(-1, 3), axis=0) <= TILE_SIZE):
                cell = tuple(map(int, np.floor(surface.mean(axis=(0, 1)) / TILE_SIZE)))
                tiles[cell].append(surface)
            else:
                for cell, faces in cell_parts(surface, TILE_SIZE):
                    tiles[cell].append(surface[faces])
    if placed != originals.keys():
        raise ValueError("The active GL125 export is missing relocated building pieces.")

    output = prepared / "StaticCollision"
    meshes = output / "Meshes"
    meshes.mkdir(parents=True, exist_ok=True)
    chunks = []
    for cell, parts in sorted(tiles.items()):
        triangles = np.concatenate(parts)
        origin = ((triangles.min(axis=(0, 1)) + triangles.max(axis=(0, 1))) * .5).astype(np.float32)
        vertices, faces = weld(triangles.reshape(-1, 3) - origin)
        # Keep one copy of coincident faces. Reverse sheet faces have separate vertices.
        _, unique = np.unique(np.sort(faces, axis=1), axis=0, return_index=True)
        faces = faces[np.sort(unique)]
        name = "Structure_" + tile_name(cell)
        file = name + ".bytes"
        path = meshes / file
        write_mesh(path, vertices, [(-1, faces)])
        with path.open("rb") as stream:
            sha = hashlib.file_digest(stream, "sha256").hexdigest()
        chunks.append(dict(name=name, file=file, atlas=-1, origin=origin.tolist(), vertices=len(vertices),
                           triangles=len(faces), collider=True, materials=[], sha256=sha))
    json_write(output / "Collision.json", dict(version=1, worldHash=hashlib.sha256(world_bytes).hexdigest(),
                                                chunks=chunks))
    print(f"GL125 collision: {included} source objects, {len(chunks)} chunks, "
          f"{sum(chunk['triangles'] for chunk in chunks)} triangles, {proxies} old proxies skipped.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evaluated", type=pathlib.Path)
    parser.add_argument("prepared", type=pathlib.Path)
    parser.add_argument("placement_contract", type=pathlib.Path)
    args = parser.parse_args()
    build_collision(args.evaluated, args.prepared, args.placement_contract)


if __name__ == "__main__":
    main()
