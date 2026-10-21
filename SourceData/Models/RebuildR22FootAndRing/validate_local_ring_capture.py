#!/usr/bin/env python3
"""Read-only R22 contract validator. Prints a proposal, NEVER changes Unity assets.

Usage:
  python validate_local_ring_capture.py --contract RING_12_FACE_DELTA_CONTRACT.json \
    --capture DOT00_CAPTURE.json --receipt DOT00_TRUSTED_RECEIPT.json \
    --trusted-receipt-sha256 <independently reviewed receipt SHA-256>

Run --schema for the input contract. An unreviewed capture/receipt cannot establish
Unity ownership; pinning a receipt's own hash without independent DOT00 review is
NOT verification. Missing evidence, unsupported polygons, duplicate candidates,
changed UV/winding/material, or non-bijective whole-mesh matches block all output.
All output is JSON on stdout. Exit 0 = proposal only, 2 = blocked. No apply mode.
"""
import argparse
import hashlib
import itertools
import json
import math
import re
import sys
from pathlib import Path

OLD = "R8_Dark_Protective_Steel"
NEW = "R21_Ring_Satin_Graphite_Coated_Steel"
SOURCE = "R2_MODULE_RING_SECTOR_00"
R21_PATH = "E:/UnityProjects/FPS_Template/Docs/AI/Moonkov_Rebuild_R21_RingFinish_Delivery.json"
COMMITS = {"fc975c6b", "46169763"}
OWNER_KEYS = ("scene_asset_path", "scene_guid", "renderer_global_object_id",
              "mesh_asset_path", "mesh_guid", "mesh_local_file_id")
SHA_RE = re.compile(r"^[0-9a-f]{64}$")
GUID_RE = re.compile(r"^[0-9a-f]{32}$")
MAX_INPUT_BYTES = 32 * 1024 * 1024


class Blocked(Exception):
    pass


def require(condition, message):
    if not condition:
        raise Blocked(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"),
                      allow_nan=False, ensure_ascii=False).encode("utf-8")


def non_target_record(t):
    # Moving triangles out of a submesh necessarily shifts later ordinal positions.
    # Preserve all other evidence, including raw index triplet and material/submesh.
    return {key: value for key, value in t.items() if key != "triangle_index_in_submesh"}


def reject_duplicates(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key: " + key)
        result[key] = value
    return result


def parse_json(raw):
    require(len(raw) <= MAX_INPUT_BYTES, "JSON input exceeds 32 MiB")
    return json.loads(raw.decode("utf-8-sig"), object_pairs_hook=reject_duplicates,
                      parse_constant=lambda x: (_ for _ in ()).throw(Blocked("Non-finite JSON number")))


def finite_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def vector(value, size, label):
    require(isinstance(value, list) and len(value) == size
            and all(finite_number(x) for x in value), "Invalid " + label)
    return value


def matrix(value, size, label):
    require(isinstance(value, list) and len(value) == size, "Invalid " + label)
    for row in value:
        vector(row, size, label)
    return value


def near(a, b, tolerance):
    return len(a) == len(b) and all(abs(x - y) <= tolerance for x, y in zip(a, b))


def xyz_near(a, b, tolerance):
    return sum((x-y)**2 for x, y in zip(a, b)) <= tolerance*tolerance


def determinant3(m):
    return (m[0][0] * (m[1][1]*m[2][2]-m[1][2]*m[2][1])
            - m[0][1] * (m[1][0]*m[2][2]-m[1][2]*m[2][0])
            + m[0][2] * (m[1][0]*m[2][1]-m[1][1]*m[2][0]))


def transform(m, point):
    p = list(point) + [1.0]
    return [sum(a*b for a, b in zip(row, p)) for row in m[:-1]]


def check_sha(value, label):
    require(isinstance(value, str) and bool(SHA_RE.fullmatch(value)), "Invalid SHA-256: " + label)


def check_owner(owner):
    require(isinstance(owner, dict), "Missing owner")
    for key in OWNER_KEYS:
        require(isinstance(owner.get(key), str) and owner[key].strip()
                and not any(x in owner[key].lower() for x in ("unknown", "todo", "placeholder", "<fill")),
                "Missing/unresolved owner field: " + key)
    for key in ("scene_guid", "mesh_guid"):
        require(bool(GUID_RE.fullmatch(owner[key])), "Invalid owner GUID: " + key)
    require(owner["renderer_global_object_id"].startswith("GlobalObjectId_V1-"),
            "Renderer must use a reviewed Unity GlobalObjectId, not a name")
    require(bool(re.fullmatch(r"-?[0-9]+", owner["mesh_local_file_id"]))
            and int(owner["mesh_local_file_id"]) != 0, "Invalid mesh local file ID")
    require(owner["scene_asset_path"].endswith(".unity"), "Scene path must identify a .unity asset")
    require(owner["mesh_asset_path"].endswith(".asset"), "Target must be the existing private mesh .asset clone")


def owner_key(owner):
    return (owner["renderer_global_object_id"], owner["mesh_guid"], owner["mesh_local_file_id"])


def check_normalization(normalization, contract):
    require(isinstance(normalization, dict), "Missing reviewed normalization")
    require(normalization.get("units") == "meters"
            and normalization.get("space") == "blender_source_local"
            and normalization.get("winding") == "source_polygon", "Unverified coordinate/winding convention")
    require(normalization.get("verified_from_local_receipt_and_scene_refs") is True,
            "Normalization has not been verified by DOT00")
    require(normalization.get("source_matrix_world_blender") == contract.get("matrix_world_blender"),
            "Receipt source world matrix differs from contract")
    matrix(contract.get("matrix_world_blender"), 4, "contract source world matrix")
    m = matrix(normalization.get("mesh_local_to_blender_source_local"), 4, "normalization affine")
    require(near(m[3], [0, 0, 0, 1], 1e-12) and abs(determinant3(m)) > 1e-15,
            "Normalization is singular or not affine")
    u = matrix(normalization.get("mesh_uv_to_source_uv"), 3, "UV affine")
    require(near(u[2], [0, 0, 1], 1e-12)
            and abs(u[0][0]*u[1][1]-u[0][1]*u[1][0]) > 1e-15, "UV normalization is singular or not affine")
    order = normalization.get("normalized_corner_order")
    require(order in ([0, 1, 2], [0, 2, 1]), "Corner order must explicitly preserve or reverse Unity winding")
    return m, u, order


def check_triangle(t, normalization, label):
    require(isinstance(t, dict), "Invalid triangle: " + label)
    require(isinstance(t.get("triangle_id"), str) and t["triangle_id"], "Missing stable triangle ID: " + label)
    for key in ("submesh", "triangle_index_in_submesh"):
        require(type(t.get(key)) is int and t[key] >= 0, "Invalid " + key + ": " + label)
    indices = t.get("vertex_indices")
    require(isinstance(indices, list) and len(indices) == 3
            and all(type(i) is int and i >= 0 for i in indices)
            and len(set(indices)) == 3, "Invalid original vertex indices: " + label)
    require(isinstance(t.get("material"), str) and t["material"], "Missing material: " + label)
    for key in ("xyz", "raw_xyz"):
        require(isinstance(t.get(key), list) and len(t[key]) == 3, "Missing per-corner " + key + ": " + label)
        for p in t[key]:
            vector(p, 3, key)
    for key in ("uv", "raw_uv"):
        require(isinstance(t.get(key), dict) and set(t[key]) == {"UVMap"}
                and len(t[key]["UVMap"]) == 3, "Missing/unexpected UV layer: " + label)
        for p in t[key]["UVMap"]:
            vector(p, 2, key)
    m, u, order = normalization
    for corner, raw_corner in enumerate(order):
        require(near(transform(m, t["raw_xyz"][raw_corner]), t["xyz"][corner], 1e-8),
                "Normalized xyz does not follow reviewed affine: " + label)
        require(near(transform(u, t["raw_uv"]["UVMap"][raw_corner]), t["uv"]["UVMap"][corner], 1e-10),
                "Normalized UV does not follow reviewed affine: " + label)
    a, b, c = t["xyz"]
    ab, ac = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
    cross = [ab[1]*ac[2]-ab[2]*ac[1], ab[2]*ac[0]-ab[0]*ac[2], ab[0]*ac[1]-ab[1]*ac[0]]
    require(sum(x*x for x in cross) > 1e-24, "Degenerate triangle: " + label)


def check_face(face, label):
    require(isinstance(face, dict) and type(face.get("source_face_index")) is int
            and face["source_face_index"] >= 0, "Invalid source face index: " + label)
    xyz = face.get("xyz")
    require(isinstance(xyz, list) and len(xyz) in (3, 4),
            "Unsupported baseline polygon; explicit canonical triangulation is required: " + label)
    for p in xyz:
        vector(p, 3, label + " xyz")
    uv = face.get("uv")
    require(isinstance(uv, dict) and set(uv) == {"UVMap"}
            and isinstance(uv["UVMap"], list) and len(uv["UVMap"]) == len(xyz), "Invalid source UV: " + label)
    for p in uv["UVMap"]:
        vector(p, 2, label + " UVMap")
    vector(face.get("normal"), 3, label + " normal")
    require(sum(x*x for x in face["normal"]) > 1e-16, "Invalid zero source normal: " + label)


def face_alternatives(face):
    if len(face["xyz"]) == 3:
        return [((0, 1, 2),)]
    return [((0, 1, 2), (0, 2, 3)), ((0, 1, 3), (1, 2, 3))]


def matches(t, face, corners, xyz_tol, uv_tol):
    # Cyclic rotations preserve winding. Reversal is intentionally not tried.
    for offset in range(3):
        if all(xyz_near(t["xyz"][(j+offset) % 3], face["xyz"][c], xyz_tol)
               and near(t["uv"]["UVMap"][(j+offset) % 3], face["uv"]["UVMap"][c], uv_tol)
               for j, c in enumerate(corners)):
            return True
    return False


def spatial_key(p, cell):
    return tuple(math.floor(v / cell) for v in p)


def match_inventory(faces, triangles, xyz_tol, uv_tol):
    # Index all three corners; candidate lookup remains exhaustive under xyz_tol.
    index = {}
    for i, triangle in enumerate(triangles):
        for p in triangle["xyz"]:
            index.setdefault(spatial_key(p, xyz_tol), set()).add(i)
    mapped, consumed = {}, set()
    for face in faces:
        alternatives, relevant = [], set()
        for group in face_alternatives(face):
            candidates = []
            for corners in group:
                key = spatial_key(face["xyz"][corners[0]], xyz_tol)
                possible = set()
                for delta in itertools.product((-1, 0, 1), repeat=3):
                    possible.update(index.get(tuple(key[k]+delta[k] for k in range(3)), ()))
                hits = [i for i in sorted(possible) if matches(triangles[i], face, corners, xyz_tol, uv_tol)]
                relevant.update(hits)
                candidates.append(hits)
            if all(candidates):
                # Duplicate matching triangles are ambiguity, never choose by ID/material.
                require(all(len(hits) == 1 for hits in candidates),
                        "Ambiguous/duplicate triangle candidates for source face " + str(face["source_face_index"]))
                ids = tuple(hits[0] for hits in candidates)
                require(len(set(ids)) == len(ids), "One triangle matched twice within a source face")
                alternatives.append(ids)
        require(len(alternatives) == 1, "Missing or ambiguous triangulation for source face " + str(face["source_face_index"]))
        chosen = set(alternatives[0])
        require(relevant == chosen, "Extra matching diagonal/duplicate triangle for source face " + str(face["source_face_index"]))
        require(not consumed.intersection(chosen), "Triangle is owned by more than one baseline source face")
        for i in chosen:
            require(triangles[i]["material"] == face["material"],
                    "Current material differs from baseline at source face " + str(face["source_face_index"]))
        consumed.update(chosen)
        mapped[face["source_face_index"]] = sorted(chosen)
    require(consumed == set(range(len(triangles))), "Full inventory contains triangles not owned by the source baseline")
    return mapped


def check_protection(contract, receipt, capture, target_owner, triangles):
    protected_source = contract.get("protected_prior_R21_faces", {})
    objects = protected_source.get("objects", {})
    require(protected_source.get("required_existing_total_faces") == 150
            and protected_source.get("required_existing_total_triangles") == 300
            and protected_source.get("current_material") == NEW
            and isinstance(objects, dict) and len(objects) == 5 and SOURCE in objects,
            "Missing full protected R21 source contract")
    baseline = {f["source_face_index"]: f for f in contract["baseline_source_faces"]}
    protected_source_ids = set()
    for face in objects[SOURCE].get("faces", []):
        fid = face.get("source_face_index")
        require(fid in baseline and fid not in protected_source_ids
                and all(face.get(k) == baseline[fid].get(k) for k in ("xyz", "uv", "material"))
                and face.get("material") == NEW, "Protected source face differs from full target baseline")
        protected_source_ids.add(fid)
    require(protected_source_ids == {fid for fid, face in baseline.items() if face["material"] == NEW},
            "Protected source face inventory does not cover all existing R21 target faces")
    rows = receipt.get("protected_prior_r21")
    require(isinstance(rows, list) and len(rows) == 5, "Need all five existing R21 renderer protection records")
    require(capture.get("protected_prior_r21") == rows, "Capture R21 protection differs from trusted receipt")
    seen_owners, seen_sources, seen_ids, target_protected = set(), set(), set(), set()
    face_total = tri_total = 0
    for row in rows:
        source_name = row.get("source_object")
        require(source_name in objects and source_name not in seen_sources, "Missing/duplicate protected source object")
        seen_sources.add(source_name)
        source_faces = objects[source_name].get("faces", [])
        require(isinstance(source_faces, list) and row.get("source_face_count") == len(source_faces)
                and all(len(f.get("xyz", [])) == 4 and f.get("material") == NEW for f in source_faces),
                "Protected per-owner face count differs from source contract")
        check_owner(row.get("owner"))
        key = owner_key(row["owner"])
        require(key not in seen_owners, "Duplicate protected renderer owner")
        seen_owners.add(key)
        check_sha(row.get("mesh_asset_sha256"), "protected mesh")
        check_sha(row.get("protected_triangle_records_sha256"), "protected triangle records")
        ids = row.get("triangle_ids")
        require(isinstance(ids, list) and ids and all(isinstance(x, str) and x for x in ids)
                and len(set(ids)) == len(ids), "Invalid protected triangle IDs")
        require(type(row.get("source_face_count")) is int and row["source_face_count"] > 0
                and len(ids) == 2*row["source_face_count"], "Prior R21 protected face/triangle count mismatch")
        for tid in ids:
            require((key, tid) not in seen_ids, "Duplicate qualified protected triangle ID")
            seen_ids.add((key, tid))
        face_total += row["source_face_count"]
        tri_total += len(ids)
        if key == target_owner:
            require(source_name == SOURCE, "Target owner mapped to a different protected source object")
            target_protected = set(ids)
            require(row["mesh_asset_sha256"] == capture["mesh_asset_sha256"], "Target protected mesh hash differs")
            protected = sorted([t for t in triangles if t["triangle_id"] in target_protected], key=lambda t: t["triangle_id"])
            require(len(protected) == len(ids) and all(t["material"] == NEW for t in protected),
                    "Missing or changed existing R21 triangles in target clone")
            require(sha(canonical(protected)) == row["protected_triangle_records_sha256"],
                    "Protected target triangle record hash differs from receipt")
    require(face_total == 150 and tri_total == 300, "Prior R21 protection must cover exactly 150 faces / 300 triangles")
    require(target_owner in seen_owners, "Target clone is not one of the five reviewed R21 renderer owners")
    actual_new = {t["triangle_id"] for t in triangles if t["material"] == NEW}
    require(actual_new == target_protected, "Existing R21 material inventory differs from protected target IDs")
    return target_protected


def validate(contract, capture, receipt, contract_hash, capture_hash, receipt_hash, trusted_receipt_hash):
    check_sha(trusted_receipt_hash, "independent receipt pin")
    require(receipt_hash == trusted_receipt_hash, "Trusted ownership receipt hash mismatch")
    require(receipt.get("schema") == "r22-dot00-readonly-receipt-v1"
            and receipt.get("verified_by") == "DOT00"
            and receipt.get("verification_mode") == "read_only", "Missing trusted DOT00 read-only receipt")
    require(capture.get("schema") == "r22-normalized-triangle-capture-v1", "Unsupported capture schema")
    require(receipt.get("contract_sha256") == contract_hash and receipt.get("capture_sha256") == capture_hash,
            "Receipt does not bind these exact contract and capture bytes")
    prior = receipt.get("r21_delivery_receipt", {})
    require(prior.get("path") == R21_PATH, "Wrong/missing R21 local delivery receipt path")
    check_sha(prior.get("sha256"), "R21 delivery receipt")
    commits = receipt.get("verified_r21_commits")
    require(isinstance(commits, list) and set(commits) == COMMITS, "Both known R21 commits must be independently verified")
    require(receipt.get("existing_private_material_count") == 1 and receipt.get("existing_private_mesh_clone_count") == 5,
            "Existing R21 private material/mesh clone inventory not verified")
    require(contract.get("source_object") == SOURCE and capture.get("source_object") == SOURCE
            and receipt.get("source_object") == SOURCE, "Wrong source object")
    require(isinstance(contract.get("source_mesh"), str) and contract["source_mesh"]
            and capture.get("source_mesh") == contract["source_mesh"]
            and receipt.get("source_mesh") == contract["source_mesh"], "Wrong source mesh")
    check_owner(receipt.get("owner"))
    require(capture.get("owner") == receipt["owner"], "Capture owner differs from independently reviewed owner")
    for key in ("mesh_asset_sha256", "scene_asset_sha256", "vertex_streams_sha256", "collider_references_sha256"):
        check_sha(capture.get(key), key)
        require(receipt.get(key) == capture[key], "Local byte/buffer hash differs: " + key)
    require(receipt.get("existing_finish_material", {}).get("name") == NEW, "Wrong existing R21 finish material")
    material_ref = receipt["existing_finish_material"]
    require(isinstance(material_ref.get("guid"), str) and GUID_RE.fullmatch(material_ref["guid"])
            and isinstance(material_ref.get("asset_path"), str) and material_ref["asset_path"].endswith(".mat"),
            "Existing R21 finish material path/GUID must be resolved from local refs")
    require(capture.get("existing_finish_material") == material_ref, "Capture finish material ownership mismatch")
    normalization = check_normalization(receipt.get("normalization"), contract)
    require(capture.get("normalization") == receipt["normalization"], "Capture normalization differs from trusted receipt")
    tol = contract.get("tolerances", {})
    xyz_tol, uv_tol = tol.get("xyz_m"), tol.get("uv")
    require(finite_number(xyz_tol) and 0 < xyz_tol <= 0.001
            and finite_number(uv_tol) and 0 < uv_tol <= 0.00001, "Contract tolerances exceed allowed bounds")
    faces = contract.get("baseline_source_faces")
    require(isinstance(faces, list) and len(faces) == 1604, "Need complete 1,604-polygon source baseline")
    by_face = {}
    for face in faces:
        check_face(face, "baseline")
        fid = face["source_face_index"]
        require(fid not in by_face and isinstance(face.get("material"), str) and face["material"], "Duplicate/invalid baseline face")
        by_face[fid] = face
    require(set(by_face) == set(range(1604)), "Baseline source indices must cover every polygon, without gaps")
    targets = contract.get("target_faces")
    require(isinstance(targets, list) and len(targets) == 12
            and {f.get("source_face_index") for f in targets} == set(range(1334, 1346)), "Target must be exactly diagnostic source faces 1334-1345")
    for target in targets:
        check_face(target, "target")
        require(len(target["xyz"]) == 4 and target.get("old_material") == OLD
                and target.get("new_material") == NEW, "Wrong target shape/material transition")
        base = by_face[target["source_face_index"]]
        require(all(target[k] == base[k] for k in ("xyz", "uv", "normal")) and base["material"] == OLD,
                "Target geometry/material does not equal full baseline")
        require(target.get("triangulations") == [[[0, 1, 2], [0, 2, 3]], [[0, 1, 3], [1, 2, 3]]],
                "Target does not declare both permitted winding-preserving quad diagonals")
    triangles = capture.get("triangles")
    require(isinstance(triangles, list) and len(triangles) == sum(len(f["xyz"])-2 for f in faces),
            "Capture must contain the full target clone triangle inventory")
    require(type(capture.get("vertex_count")) is int and capture["vertex_count"] > 0, "Invalid original vertex count")
    require(type(capture.get("submesh_count")) is int and capture["submesh_count"] > 0, "Invalid original submesh count")
    slots = capture.get("material_slots")
    require(isinstance(slots, list) and len(slots) == capture["submesh_count"]
            and all(isinstance(x, str) and x for x in slots), "Material slots must exactly describe every original submesh")
    require(receipt.get("material_slots") == slots, "Original material slots differ from trusted receipt")
    ids, positions, raw_vertices = set(), {}, {}
    for t in triangles:
        check_triangle(t, normalization, str(t.get("triangle_id", "?")))
        require(t["triangle_id"] not in ids, "Duplicate triangle ID")
        ids.add(t["triangle_id"])
        require(t["submesh"] < len(slots) and slots[t["submesh"]] == t["material"], "Triangle material/submesh assignment differs")
        pos = (t["submesh"], t["triangle_index_in_submesh"])
        require(pos not in positions, "Duplicate original submesh triangle position")
        positions[pos] = t["triangle_id"]
        for j, vertex in enumerate(t["vertex_indices"]):
            require(vertex < capture["vertex_count"], "Original vertex index out of range")
            record = (t["raw_xyz"][j], t["raw_uv"]["UVMap"][j])
            require(vertex not in raw_vertices or raw_vertices[vertex] == record,
                    "One original vertex index has inconsistent xyz/UV data")
            raw_vertices[vertex] = record
    for submesh in range(capture["submesh_count"]):
        indices = {position for sub, position in positions if sub == submesh}
        require(indices == set(range(len(indices))), "Incomplete original submesh triangle positions")
    protected = check_protection(contract, receipt, capture, owner_key(receipt["owner"]), triangles)
    mapped = match_inventory(faces, triangles, xyz_tol, uv_tol)
    selected_indices = [i for f in sorted(targets, key=lambda x: x["source_face_index"]) for i in mapped[f["source_face_index"]]]
    selected = {triangles[i]["triangle_id"] for i in selected_indices}
    require(len(selected_indices) == len(selected) == 24, "Delta does not identify exactly 24 distinct triangles")
    require(not selected.intersection(protected), "Delta overlaps protected prior R21 triangles")
    plan = []
    for face in sorted(targets, key=lambda x: x["source_face_index"]):
        for i in mapped[face["source_face_index"]]:
            t = triangles[i]
            require(t["material"] == OLD, "A proposed triangle is not currently in the required old material")
            plan.append({"source_face_index": face["source_face_index"], "triangle_id": t["triangle_id"],
                         "original_submesh": t["submesh"], "original_triangle_index_in_submesh": t["triangle_index_in_submesh"],
                         "original_vertex_indices": t["vertex_indices"], "old_material": OLD, "new_material": NEW})
    untouched = sorted([non_target_record(t) for t in triangles if t["triangle_id"] not in selected], key=lambda t: t["triangle_id"])
    return {"status": "VALID_PROPOSAL_ONLY", "applied": False,
            "warning": "Synthetic/JSON validation is not Unity acceptance. DOT00 must inspect the local receipt and scene refs; this validator never reads or changes Unity assets.",
            "owner": receipt["owner"], "existing_finish_material": material_ref,
            "contract_sha256": contract_hash, "capture_sha256": capture_hash, "trusted_receipt_sha256": receipt_hash,
            "mesh_asset_sha256_before": capture["mesh_asset_sha256"], "scene_asset_sha256_before": capture["scene_asset_sha256"],
            "vertex_streams_sha256_must_remain": capture["vertex_streams_sha256"],
            "collider_references_sha256_must_remain": capture["collider_references_sha256"],
            "complete_source_polygon_count": len(faces), "complete_clone_triangle_count": len(triangles),
            "proposed_source_face_count": 12, "proposed_triangle_count": 24,
            "protected_prior_r21_source_face_count": 150, "protected_prior_r21_triangle_count": 300,
            "protected_prior_r21_renderer_count": 5,
            "non_target_triangle_records_sha256_must_remain": sha(canonical(untouched)),
            "non_target_digest_rule": "Sort by stable triangle_id, omit only triangle_index_in_submesh (ordinal shifts after removals), then canonical JSON SHA-256. Preserve all other record fields. Preserve relative index order within each original submesh; append new finish triangles after protected existing triangles.",
            "original_material_slots": slots, "original_submesh_count": capture["submesh_count"],
            "rollback": "Retain the exact hash-bound capture and original mesh asset. Restore original submesh positions and vertex-index triplets below; use the full original capture for exact ordering. No rollback is executed here.",
            "plan": plan}


SCHEMA = """Input and trust requirements (all evidence is prepared locally by DOT00):
1. Contract: source_object, source_mesh, matrix_world_blender, tolerances,
   baseline_source_faces (all 1604), target_faces (12). Each source face has
   source_face_index, xyz, uv:{UVMap:[...]}, normal, material. Targets instead
   include old_material/new_material and both legal triangulations. Baseline
   materials MUST describe pre-R22 Unity state, including existing R21 finish.
   xyz tolerance is Euclidean distance; UV tolerance is per component.
   Only triangles/quads supported; unsupported baseline topology blocks.
2. Capture schema r22-normalized-triangle-capture-v1: source_object/source_mesh,
   owner, mesh_asset_sha256, scene_asset_sha256, vertex_streams_sha256,
   collider_references_sha256 (canonical current collider-to-mesh refs),
   vertex_count, submesh_count, material_slots, existing_finish_material,
   normalization, protected_prior_r21, triangles (COMPLETE target clone).
   Each triangle: triangle_id (unique within owner), submesh,
   triangle_index_in_submesh (zero-based, contiguous), vertex_indices (original
   Unity order), raw_xyz[3][3], raw_uv:{UVMap:[3][2]}, xyz[3][3],
   uv:{UVMap:[3][2]}, material. Raw arrays follow original vertex_indices.
   Normalized arrays use the reviewed affine and normalized_corner_order.
3. Receipt schema r22-dot00-readonly-receipt-v1: verified_by:DOT00,
   verification_mode:read_only, contract_sha256, capture_sha256, source_object,
   source_mesh, owner, four hashes above, material_slots, normalization,
   existing_finish_material:{name,asset_path,guid},
   r21_delivery_receipt:{path,sha256}, verified_r21_commits:[fc975c6b,46169763],
   existing_private_material_count:1, existing_private_mesh_clone_count:5,
   protected_prior_r21 (same exact five records as capture).
   Owner: scene_asset_path, scene_guid (32 lowercase hex),
   renderer_global_object_id (Unity GlobalObjectId_V1-...), mesh_asset_path
   (existing .asset), mesh_guid (32 lowercase hex), mesh_local_file_id (string).
   No guessed or name-only owners. DOT00 must bind GUID/file IDs by reading the
   R21 local receipt and the current scene's actual references before pinning.
4. Normalization: units:meters, space:blender_source_local,
   winding:source_polygon, verified_from_local_receipt_and_scene_refs:true,
   source_matrix_world_blender (exact contract matrix),
   mesh_local_to_blender_source_local (invertible affine 4x4),
   mesh_uv_to_source_uv (invertible affine 3x3),
   normalized_corner_order:[0,1,2] or [0,2,1]. It is a reviewed transform,
   not nearest-neighbor fitting or a transform inferred solely from 12 faces.
5. Each protected_prior_r21 record: source_object, owner, mesh_asset_sha256,
   source_face_count, triangle_ids, protected_triangle_records_sha256.
   Exactly five distinct owners, totaling 150 source quads / 300 triangles.
   Hash canonical JSON of complete protected triangle records sorted by
   triangle_id (json.dumps sort_keys=True, separators=(',',':'),
   ensure_ascii=False, allow_nan=False, UTF-8). Target record is recomputed;
   other four renderer records are independently pinned local evidence, NOT
   revalidated from target-only geometry. IDs are qualified by owner.
6. Pin receipt hash independently AFTER DOT00 reviews that every hash/owner/
   protected-ID assertion came from current read-only local capture, source
   receipt, and actual scene refs. A capture cannot authenticate itself.

Output is proposal-only. Exactly 24 original triangle assignments are supplied
for review/rollback. No new material/mesh is created. No Unity/Blender import,
submesh change, save, automatic patch, runtime hook, or external contact occurs.
The full hash-bound capture and original asset remain required for rollback.
Re-run on a fresh capture immediately before any separately authorized change;
verify untouched buffer/triangle hashes and 300 protected triangles afterward.
"""


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--contract")
    parser.add_argument("--capture")
    parser.add_argument("--receipt")
    parser.add_argument("--trusted-receipt-sha256")
    parser.add_argument("--schema", action="store_true", help="Print detailed local input schema")
    args = parser.parse_args(argv)
    if args.schema:
        print(SCHEMA)
        return 0
    try:
        require(all((args.contract, args.capture, args.receipt, args.trusted_receipt_sha256)),
                "Required: --contract, --capture, --receipt and independently pinned --trusted-receipt-sha256")
        raw = [Path(path).read_bytes() for path in (args.contract, args.capture, args.receipt)]
        objects = [parse_json(x) for x in raw]
        require(all(isinstance(x, dict) for x in objects), "Inputs must be JSON objects")
        result = validate(*objects, *(sha(x) for x in raw), args.trusted_receipt_sha256)
        print(json.dumps(result, indent=2, ensure_ascii=False, allow_nan=False))
        return 0
    except (Blocked, OSError, ValueError, TypeError, KeyError, AttributeError, IndexError, OverflowError, RecursionError) as error:
        print(json.dumps({"status": "BLOCKED", "applied": False, "reason": str(error), "plan": []}, ensure_ascii=False))
        return 2


if __name__ == "__main__":
    sys.exit(main())
