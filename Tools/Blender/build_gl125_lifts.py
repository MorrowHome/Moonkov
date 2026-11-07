"""Split the four authored GL125 elevators out of the static render and collision meshes."""

import argparse
import collections
import hashlib
import json
import pathlib
import re

import numpy as np

from build_gl125_collision import surface_triangles
from pack_gl125 import json_read, json_write, keep_faces, read_mesh, weld, write_mesh


UL39_GATE = re.compile(r"UL39_(CAR_DOOR|LOWER_GATE|UPPER_GATE)_([LR][12])_")
N62_GATE = re.compile(r"HM02_N62_(CAB|LOWER|UPPER)_GATE_LEAF_([0-4])$")
N62_UPPER_TRIM = re.compile(r"HM02_N76_FORMED_EDGE_([0-4])$")
FT55 = re.compile(r"(FT55_(?:LOWER|UPPER))_(.*)$")


def group(name):
    if match := UL39_GATE.match(name):
        section, leaf = match.groups()
        return "UL39_" + section + "_" + leaf
    if match := N62_GATE.fullmatch(name):
        section, leaf = match.groups()
        return "N62_" + section + "_GATE_" + leaf
    if match := N62_UPPER_TRIM.fullmatch(name):
        return "N62_UPPER_GATE_" + match[1]
    if name.startswith(("UL39_CAR_", "UL39_CABIN_")):
        return "UL39_Cabin"
    if name.startswith("HM02_N62_CAB_"):
        return "N62_Cabin"
    if name in ("HM02_N62_HOIST_CABLE_0", "HM02_N62_HOIST_CABLE_1"):
        return name.removeprefix("HM02_")
    if match := FT55.fullmatch(name):
        lift, part = match.groups()
        for motion in ("CAR_GATE_IN", "CAR_GATE_OUT", "LOW_GATE", "HIGH_GATE", "SCREW_A", "SCREW_B", "DRIVE_SHAFT"):
            if part == motion or part.startswith(motion + "_"):
                return lift + "_" + motion
        if part.startswith(("CAR_", "TRAVEL_NUTS", "SAFETY_NUTS", "NUT_CARRIERS", "GUIDE_SLIDERS", "GUIDE_ROLLERS")):
            return lift + "_Cabin"
    return None


def keys(triangles):
    rows = np.ascontiguousarray(triangles, dtype="<f4")
    return rows.reshape(len(rows), rows.shape[1] * rows.shape[2]).view(
        np.dtype((np.void, rows.shape[1] * rows.shape[2] * 4))
    ).ravel()


def ordered(triangles):
    order = np.lexsort((triangles[:, :, 2], triangles[:, :, 1], triangles[:, :, 0]), axis=1)
    return np.take_along_axis(triangles, order[:, :, None], axis=1)


def split_chunks(chunks, directory, triangles, materials=None):
    render = materials is not None
    widths = 8 if render else 3
    minimum = triangles[:, :, :3].min(axis=1)
    maximum = triangles[:, :, :3].max(axis=1)
    found = np.zeros(len(triangles), dtype=bool)
    edits = []
    for chunk in chunks:
        origin = np.array(chunk["origin"], np.float32)
        # Each affected GL125 render tile or collision tile has its origin within
        # 80 metres of one of the rig's triangles.
        candidates = np.flatnonzero(np.all((maximum >= origin - 80) & (minimum <= origin + 80), axis=1))
        if not len(candidates):
            continue
        vertices, sections = read_mesh(directory / chunk["file"])
        local = vertices[:, :3]
        near = (maximum[candidates] >= local.min(axis=0) + origin - .001).all(axis=1) & (
            minimum[candidates] <= local.max(axis=0) + origin + .001
        ).all(axis=1)
        candidates = candidates[near]
        if not len(candidates):
            continue
        selected = triangles[candidates, :, :widths].copy()
        selected[:, :, :3] -= origin
        slots = []
        for slot, (material, faces) in enumerate(sections):
            row = np.flatnonzero(materials[candidates] == material) if render else np.arange(len(candidates))
            if not len(row):
                continue
            expected = keys(selected[row] if render else ordered(selected[row]))
            current = vertices[faces, :widths]
            actual = keys(current if render else ordered(current))
            removed = np.flatnonzero(np.isin(actual, expected))
            if not len(removed):
                continue
            found[candidates[row[np.isin(expected, actual[removed])]]] = True
            slots.append(dict(slot=slot, count=len(faces), remove=removed.tolist()))
        if slots:
            edits.append(dict(name=chunk["name"], vertices=len(vertices), slots=slots, collision=not render))
    if not np.all(found):
        raise ValueError(f"{np.count_nonzero(~found)} moving {'render' if render else 'collision'} triangles are absent from the static input.")
    return edits


def extract(evaluated, prepared, output):
    world_bytes = (prepared / "World.json").read_bytes()
    world_hash = hashlib.sha256(world_bytes).hexdigest()
    world = json.loads(world_bytes)
    exported = json_read(evaluated / "Export.json")
    collision_bytes = (prepared / "StaticCollision" / "Collision.json").read_bytes()
    collision = json.loads(collision_bytes)
    if world["sourceHash"] != exported["sourceHash"] or collision["worldHash"] != world_hash:
        raise ValueError("The GL125 render, export and collision manifests must belong to the same map.")

    movers = collections.defaultdict(list)
    solids = []
    for part in exported["parts"]:
        relevant = [(row, group(row["name"])) for row in part["objects"] if not row["proxy"]]
        relevant = [(row, name) for row, name in relevant if name]
        if not relevant:
            continue
        with np.load(evaluated / part["file"], allow_pickle=False) as packed:
            geometry, materials = packed["mesh"], packed["material"]
        for row, name in relevant:
            start = row["offset"]
            stop = start + row["count"]
            triangles = geometry[start:stop]
            valid = keep_faces(triangles[:, :, :3]) & np.isfinite(triangles).all(axis=(1, 2))
            movers[name].append((triangles[valid], materials[start:stop][valid]))
            surface = surface_triangles(row, triangles, row["name"])
            if surface is not None:
                solids.append(surface)
    if len(movers) != 47:
        raise ValueError(f"Expected 47 moving groups across the four lifts, found {len(movers)}.")

    output.mkdir(parents=True, exist_ok=True)
    parts = []
    world_triangles, world_materials = [], []
    for name, rows in sorted(movers.items()):
        triangles = np.concatenate([geometry for geometry, _ in rows])
        materials = np.concatenate([material for _, material in rows])
        world_triangles.append(triangles)
        world_materials.append(materials)
        moved = triangles.copy()
        # Keep the shortening N62 cables attached to the head of the shaft.
        origin = (moved[:, :, :3].min(axis=(0, 1)) + moved[:, :, :3].max(axis=(0, 1))) * .5
        if "_HOIST_CABLE_" in name:
            origin[2] = moved[:, :, 2].max()
        moved[:, :, :3] -= origin
        vertices, faces = weld(moved.reshape(-1, 8))
        submeshes = [(int(material), faces[materials == material]) for material in np.unique(materials)]
        file = name + ".bytes"
        write_mesh(output / file, vertices, submeshes)
        parts.append(dict(name=name, file=file, origin=origin.tolist(), materials=[m for m, _ in submeshes]))

    edits = split_chunks([chunk for chunk in world["chunks"] if chunk["atlas"] >= 0 and not chunk["collider"]],
                         prepared / "Meshes", np.concatenate(world_triangles), np.concatenate(world_materials))
    edits.extend(split_chunks(collision["chunks"], prepared / "StaticCollision" / "Meshes", np.concatenate(solids)))
    json_write(output / "Lifts.json", dict(version=1, worldHash=world_hash,
        collisionHash=hashlib.sha256(collision_bytes).hexdigest(), parts=parts, edits=edits))
    print(f"GL125 lifts: {len(parts)} moving parts, {sum(len(t) for t in world_triangles)} render triangles, "
          f"{len(edits)} static chunks edited.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evaluated", type=pathlib.Path)
    parser.add_argument("prepared", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    extract(args.evaluated, args.prepared, args.output)


if __name__ == "__main__":
    main()
