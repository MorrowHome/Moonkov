"""Choose one visible face for each same-sided, coincident GL125 triangle."""

import argparse
import collections
import functools
import hashlib
import json
import pathlib

import numpy as np
import OpenEXR

from pack_gl125 import json_read, json_write, read_mesh


# Each pair is an exact face match in the active source. The left material is
# the visible finish or top layer, the right one is its underlying surface.
FINISH_ORDER = (
    ("OC47_PALE_SEGMENTED_PROTECTION", "OC47_STRUCTURAL_COATED_STEEL"),
    ("LD2_Hardstand_SinteredRegolith", "LD2_Hull_CeramicPaint"),
    ("CY80_LARGE_CROWN_PALE_CAST", "CY80_DEEP_CROWN_LOAD_WEB"),
    ("HM02_J30_MACHINED", "HM02_J30_CHASSIS"),
    ("SM54_PALE_DEEP_RETURN_COATING", "SM54_MAIN_EDGE_LOAD_STEEL"),
    ("B_Dark_Deck_And_Sealed_Cover", "B_DeepGray_Structural_Steel"),
    ("SC53_PALE_PRESS_FORMED_ARMOR", "SC53_EXPOSED_LOAD_STEEL"),
    ("R19_CoatedPanel_PRIVATE_PBR", "B_GrayWhite_Rough_Cladding"),
    ("SH82_PALE_MAIN_SHAFT_MANTLE", "SH82_DEEP_RECESSED_PROCESS_CHANNEL"),
    ("HM02_N64L_COATED_OUTER", "HM02_N64L_MATTE_LIGHT_TRAP"),
    ("HM02_N68_COATED_OUTER", "HM02_N68_MATTE_LIGHT_TRAP"),
    ("B_GrayWhite_Rough_Cladding", "R19_MineralBase_PRIVATE_PBR"),
    ("LD2_Deck_NonslipCoat", "LD2_Hardstand_SinteredRegolith"),
    ("LD2_Brushed_Aluminium", "LD2_Structure_GraphiteCoat"),
)


def scan(prepared, lightmaps, lift_manifest, output):
    manifest = (prepared / "World.json").read_bytes()
    revision = hashlib.sha256(manifest).hexdigest()
    world = json.loads(manifest)
    lifts = json_read(lift_manifest)
    if lifts["worldHash"] != revision:
        raise ValueError("The lift and GL125 mesh manifests have different revisions.")
    # The formal scene already uses these submeshes with the lift triangles
    # removed. Keep each index relative to the active scene's mesh.
    removed_lifts = {(edit["name"], slot["slot"]): np.asarray(slot["remove"]) for edit in lifts["edits"]
                     if not edit["collision"] for slot in edit["slots"]}
    chunks = [row for row in world["chunks"] if row["atlas"] >= 0 and not row["collider"]]
    names = [row["name"] for row in world["materials"]]
    priority = {frozenset((top, bottom)): top for top, bottom in FINISH_ORDER}
    bins = collections.defaultdict(list)
    section_indices = {}
    face_light = {}

    @functools.lru_cache(maxsize=4)
    def atlas_image(atlas):
        with OpenEXR.File(str(lightmaps / f"Irradiance_{atlas:03}.exr")) as file:
            return file.channels()["RGB"].pixels[::-1].mean(axis=2, dtype=np.float32)

    for chunk_index, chunk in sorted(enumerate(chunks), key=lambda item: item[1]["atlas"]):
        vertices, sections = read_mesh(prepared / "Meshes" / chunk["file"])
        positions = vertices[:, :3] + np.array(chunk["origin"], dtype=np.float32)
        light = atlas_image(chunk["atlas"])
        size = light.shape[0]
        for slot, (material, faces) in enumerate(sections):
            lift_rows = removed_lifts.get((chunk["name"], slot))
            if lift_rows is not None:
                keep = np.ones(len(faces), dtype=bool)
                keep[lift_rows] = False
                faces = faces[keep]
            if not len(faces):
                continue
            # Unity reverses the winding when it swaps Blender's Y and Z.
            section_indices[chunk_index, slot] = faces[:, [0, 2, 1]].astype("<i4")
            tri = positions[faces]
            q = np.rint(tri * 4000).astype(np.int32)
            order = np.lexsort((q[:, :, 2], q[:, :, 1], q[:, :, 0]), axis=1)
            # Keep separate windings. Opposite faces can be legitimate and
            # can carry distinct lighting on either side of a sheet.
            parity = ((order[:, 0] > order[:, 1]).astype(np.int8)
                      + (order[:, 0] > order[:, 2]) + (order[:, 1] > order[:, 2])) % 2
            q = np.take_along_axis(q, order[:, :, None], axis=1).reshape(-1, 9)
            cell = np.floor(tri.mean(axis=1) / [128, 128, 80]).astype(np.int32)
            cells, membership = np.unique(cell, axis=0, return_inverse=True)
            # Centroid light and chart footprint distinguish a usable face
            # from the same shape's black, self-occluded second chart.
            uv = vertices[faces, 8:10]
            x, y = np.clip((uv.mean(axis=1) * size).astype(int), 0, size - 1).T
            a, b = (uv[:, 1] - uv[:, 0]).T
            c, d = (uv[:, 2] - uv[:, 0]).T
            chart_area = np.abs(a * d - b * c) * size * size
            face_light[chunk_index, slot] = np.column_stack((light[y, x], chart_area))
            for cell_index, key in enumerate(cells):
                ids = np.flatnonzero(membership == cell_index)
                fields = np.column_stack((q[ids], parity[ids], np.full(len(ids), chunk_index), np.full(len(ids), slot),
                                          ids, np.full(len(ids), material))).astype(np.int32)
                bins[tuple(key)].append(fields)

    edits = collections.defaultdict(list)
    mixed_count = 0
    for parts in bins.values():
        rows = np.concatenate(parts)
        keys = np.ascontiguousarray(rows[:, :10]).view(np.dtype((np.void, 40))).ravel()
        order = keys.argsort()
        sorted_keys = keys[order]
        same = sorted_keys[1:] == sorted_keys[:-1]
        boundaries = np.flatnonzero(np.diff(np.r_[False, same, False].astype(np.int8)))
        groups = boundaries.reshape(-1, 2)
        xyz = rows[order[groups[:, 0]], :9].reshape(-1, 3, 3).astype(np.float64) / 4000
        area2 = np.linalg.norm(np.cross(xyz[:, 1] - xyz[:, 0], xyz[:, 2] - xyz[:, 0]), axis=1)
        for start, end in groups[area2 >= 2e-6]:
            group = rows[order[start:end + 1], 10:]
            finishes = frozenset(names[m] for m in group[:, 3])
            if len(finishes) > 1:
                if finishes not in priority:
                    raise ValueError(f"Coincident faces have unreviewed finishes {sorted(finishes)}")
                top = priority[finishes]
                candidates = [tuple(row) for row in group if names[row[3]] == top]
                mixed_count += 1
            else:
                candidates = [tuple(row) for row in group]

            def score(row):
                chunk, slot, face, _ = row
                value, area = face_light[chunk, slot][face]
                return np.isfinite(value) and value > .01, value, area, -chunk, -face

            winner = max(candidates, key=score)
            for chunk, slot, face, material in group:
                if (chunk, slot, face, material) != winner:
                    edits[int(chunk), int(slot)].append(int(face))

    sections = collections.defaultdict(list)
    for (chunk, slot), faces in sorted(edits.items()):
        indices = section_indices[chunk, slot]
        keep = np.ones(len(indices), dtype=bool)
        keep[faces] = False
        sections[chunk].append(dict(slot=slot, count=len(indices), remove=sorted(faces),
                                    before=hashlib.sha256(indices.tobytes()).hexdigest(),
                                    after=hashlib.sha256(indices[keep].tobytes()).hexdigest()))
    result = [dict(name=chunks[index]["name"], vertices=chunks[index]["vertices"], slots=slots)
              for index, slots in sorted(sections.items())]
    total = sum(len(ids) for ids in edits.values())
    json_write(output, dict(version=1, worldHash=revision, removed=total, mixed=mixed_count, chunks=result))
    print(f"{total} redundant triangles, {mixed_count} reviewed mixed finishes, {len(result)} render chunks.", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("prepared", type=pathlib.Path)
    parser.add_argument("lightmaps", type=pathlib.Path)
    parser.add_argument("lift_manifest", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    scan(args.prepared, args.lightmaps, args.lift_manifest, args.output)


if __name__ == "__main__":
    main()
