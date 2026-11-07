"""Read the GL125 native map and spool evaluated triangles in bounded batches.

Run Blender in background mode on the safe landing scene with --disable-autoexec.
Pass the scratch output directory after --.
"""

import collections
import hashlib
import json
import pathlib
import sys
import time

import bpy
import numpy as np


SCENE_NAME = "GL125_FULLMAP_LAYOUT_NATIVE_LINKED_STATIC_NOT_RUNTIME"
SOURCE_HASH = "730d2651933762900f231ede202018e83bc1f347e1212d2c38bab7136afd4d73"
V73_HASH = "b5c613a051352f3b1f74ddcfeea3d0ed0835d9f9f3c9ced59792451c35818d6e"
GEOMETRY_TYPES = {"MESH", "CURVE", "FONT", "SURFACE"}


def identity(obj):
    library = pathlib.Path(bpy.path.abspath(obj.library.filepath)).name if obj.library else "LOCAL"
    return f"{library}::{obj.name}"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def visible_objects(collection, prefix=()):
    if collection.hide_render:
        return
    path = (*prefix, collection.name)
    for obj in collection.objects:
        if not obj.hide_render:
            yield obj, path
    for child in collection.children:
        yield from visible_objects(child, path)


def copy_file_image(image, output):
    if image.source != "FILE":
        return None
    path = pathlib.Path(bpy.path.abspath(image.filepath, library=image.library))
    if image.packed_file:
        data = bytes(image.packed_file.data)
    elif path.is_file():
        data = path.read_bytes()
    else:
        raise ValueError(f"Missing texture {image.name}: {path}")
    name = hashlib.sha256(data).hexdigest() + (path.suffix.lower() or ".png")
    target = output / "Textures" / name
    if not target.exists():
        target.write_bytes(data)
    return {"file": name, "space": image.colorspace_settings.name}


def image_input(socket, output, normal=False):
    if not socket.is_linked:
        return None
    node = socket.links[0].from_node
    if normal and node.type == "NORMAL_MAP" and node.space == "TANGENT" and node.inputs["Color"].is_linked:
        node = node.inputs["Color"].links[0].from_node
    if node.type != "TEX_IMAGE" or not node.image or node.projection != "FLAT":
        return None
    coordinates = node.inputs["Vector"]
    if coordinates.is_linked and coordinates.links[0].from_socket.name != "UV":
        return None
    return copy_file_image(node.image, output)


def describe_material(material, output):
    info = dict(name=material.name, key=identity(material), base=list(material.diffuse_color),
                metallic=material.metallic, roughness=material.roughness, emission=[0, 0, 0],
                transparent=False, doubleSided=not material.use_backface_culling, approximation=False)
    if not material.use_nodes or not material.node_tree:
        return info
    active = next((node for node in material.node_tree.nodes
                   if node.type == "OUTPUT_MATERIAL" and node.is_active_output), None)
    shader = active.inputs["Surface"].links[0].from_node if active and active.inputs["Surface"].is_linked else None
    if not shader or shader.type != "BSDF_PRINCIPLED":
        info["approximation"] = True
        shader = next((node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"), None)
    if not shader:
        return info
    inputs = shader.inputs
    base, normal = image_input(inputs["Base Color"], output), image_input(inputs["Normal"], output, True)
    info.update(base=list(inputs["Base Color"].default_value) if not inputs["Base Color"].is_linked else info["base"],
                metallic=inputs["Metallic"].default_value, roughness=inputs["Roughness"].default_value,
                emission=[v * inputs["Emission Strength"].default_value for v in inputs["Emission Color"].default_value[:3]],
                transparent=inputs["Alpha"].default_value < 0.999 or inputs["Transmission Weight"].default_value > 0.05,
                baseMap=base, normalMap=normal)
    info["alpha"] = min(inputs["Alpha"].default_value, 1.0 - 0.8 * inputs["Transmission Weight"].default_value)
    info["approximation"] |= any(socket.is_linked and not ((socket.name == "Base Color" and base) or
                                                              (socket.name == "Normal" and normal)) for socket in inputs)
    if material.name == "R8_Reference_DEM_TwoLayer_Regolith":
        images = {node.image.name: node.image for node in material.node_tree.nodes if node.type == "TEX_IMAGE" and node.image}
        info["baseMap"] = copy_file_image(images["R8_Regolith_LowTone_BaseColor.png"], output)
        info["normalMap"] = copy_file_image(images["R8_Regolith_3p7m_Normal_OpenGL.png"], output)
    return info


def read_array(collection, attr, shape, dtype):
    result = np.empty((len(collection), *shape), dtype=dtype)
    collection.foreach_get(attr, result.ravel())
    return result


def read_mesh(obj, matrix, graph, material_index, collision):
    mesh = obj.to_mesh(preserve_all_data_layers=True, depsgraph=graph)
    try:
        if not mesh or not mesh.polygons:
            return None, None
        mesh.calc_loop_triangles()
        triangles = read_array(mesh.loop_triangles, "loops", (3,), np.int32)
        loop_vertices = read_array(mesh.loops, "vertex_index", (), np.int32)
        vertices = read_array(mesh.vertices, "co", (3,), np.float32)
        transform = np.array(matrix, dtype=np.float64)
        positions = vertices @ transform[:3, :3].T + transform[:3, 3]
        if np.linalg.det(transform[:3, :3]) < 0:
            triangles = triangles[:, [0, 2, 1]]
        if collision:
            return positions[loop_vertices[triangles]].astype(np.float32), None

        corners = np.zeros((len(mesh.loops), 8), np.float32)
        corners[:, :3] = positions[loop_vertices]
        normals = read_array(mesh.corner_normals, "vector", (3,), np.float32) @ np.linalg.inv(transform[:3, :3])
        corners[:, 3:6] = normals / np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-12)
        uv = next((layer for layer in mesh.uv_layers if layer.active_render), mesh.uv_layers.active)
        if uv:
            corners[:, 6:8] = read_array(uv.uv, "vector", (2,), np.float32)
        else:
            # Give UV-less geometry a stable metric projection for ordinary tiled textures.
            axis = np.abs(corners[:, 3:6]).argmax(axis=1)
            for dim, plane in enumerate(((1, 2), (0, 2), (0, 1))):
                mask = axis == dim
                corners[mask, 6:8] = corners[mask][:, plane]
        slots = [material_index(mat) for mat in mesh.materials] or [0]
        face_slots = read_array(mesh.loop_triangles, "material_index", (), np.int32)
        return corners[triangles], np.asarray(slots, np.int32)[face_slots.clip(0, len(slots) - 1)]
    finally:
        obj.to_mesh_clear()


def read_light(obj, matrix):
    light = obj.data
    row = dict(name=obj.name, key=identity(obj.original), type=light.type, energy=light.energy,
               color=list(light.color), shadow=light.use_shadow, matrix=[list(r) for r in matrix],
               position=list(matrix.translation), forward=[-v for v in matrix.col[2][:3]], up=list(matrix.col[1][:3]))
    if light.type == "AREA":
        row.update(shape=light.shape, size=light.size, sizeY=light.size_y, spread=light.spread)
    if light.type == "SUN":
        row["angle"] = light.angle
    for field in ("receiver_collection", "blocker_collection"):
        collection = getattr(obj.original.light_linking, field)
        if collection:
            row[field] = [identity(o) for o in collection.all_objects]
            if collection.children:
                raise ValueError(f"Handle nested light linking explicitly: {collection.name}")
            row[field + "_states"] = {o.name: collection.collection_objects[i].light_linking.link_state
                                      for i, o in enumerate(collection.objects)}
    return row


def batches(objects):
    batch = []
    face_count = 0
    for obj in objects:
        faces = len(obj.data.polygons) if obj.type == "MESH" else 0
        if batch and (len(batch) >= 640 or face_count + faces > 105000):
            yield batch
            batch = []
            face_count = 0
        batch.append(obj)
        face_count += faces
    if batch:
        yield batch


def main():
    output, = (pathlib.Path(p).resolve() for p in sys.argv[sys.argv.index("--") + 1:])
    source = pathlib.Path(bpy.data.filepath)
    if digest(source) != SOURCE_HASH or digest(source.parent / "sources" / "V73_AUTHOR_ORIGINAL.blend") != V73_HASH:
        raise ValueError("GL125 or V73 input bytes do not match the reviewed delivery")
    scene = bpy.data.scenes[SCENE_NAME]
    if bpy.context.scene == scene or bpy.context.scene.objects:
        raise RuntimeError("Open the original safe landing scene in background mode")
    (output / "Textures").mkdir(parents=True, exist_ok=True)
    paths = collections.defaultdict(list)
    for obj, path in visible_objects(scene.collection):
        paths[obj].append(path)
    proxies = bpy.data.collections.get("FT09_VIEW__MC00_COLLISION_PROXIES")
    if not proxies or len(proxies.all_objects) != 277:
        raise ValueError("Missing V73 collision collection")
    for obj in proxies.all_objects:
        if obj in paths or obj.type != "MESH":
            raise ValueError(f"Unexpected visible or non-mesh proxy {identity(obj)}")
        paths[obj].append((proxies.name,))

    direct = set(paths)
    targets = sorted((obj for obj in direct if obj.type in GEOMETRY_TYPES or obj.type == "LIGHT" or
                      obj.instance_type == "COLLECTION"), key=identity)
    proxy_set = set(proxies.all_objects)
    scratch = bpy.data.scenes.new("GL125 export subset")
    bpy.context.window.scene = scratch
    scratch.frame_set(scene.frame_current)
    materials = [dict(name="Unassigned", key="", base=[0.5, 0.5, 0.5, 1], metallic=0, roughness=0.7, emission=[0, 0, 0])]
    lights, manifest = [], []
    material_ids = {None: 0}

    def material_index(mat):
        if mat not in material_ids:
            material_ids[mat] = len(materials)
            materials.append(describe_material(mat, output))
        return material_ids[mat]

    seen = set()
    start = time.monotonic()
    for part, batch in enumerate(batches(targets)):
        batch_set = set(batch)
        for obj in batch:
            scratch.collection.objects.link(obj)
        graph = bpy.context.evaluated_depsgraph_get()
        geometry, indices, collisions, rows = [], [], [], []
        offsets = [0, 0]
        for instance in graph.object_instances:
            evaluated = instance.object
            obj = evaluated.original
            parent = instance.parent.original if instance.is_instance and instance.parent else None
            if (parent if instance.is_instance else obj) not in batch_set:
                continue
            if obj.hide_render and obj not in proxy_set:
                continue
            matrix = instance.matrix_world.copy()
            name = identity(obj) if not parent else identity(parent) + "/" + identity(obj)
            pose = tuple(round(float(value), 6) for row in matrix for value in row)
            if (name, pose) in seen:
                continue
            seen.add((name, pose))
            if obj.type == "LIGHT":
                lights.append(read_light(evaluated, matrix))
                continue
            if obj.type not in GEOMETRY_TYPES:
                continue
            collision = obj in proxy_set
            triangle_data, material_data = read_mesh(evaluated, matrix, graph, material_index, collision)
            if triangle_data is None:
                continue
            rows.append(dict(key=name, name=obj.name, instance=parent.name if parent else None,
                             paths=paths[parent if parent else obj], count=len(triangle_data),
                             offset=offsets[collision], proxy=collision,
                             position=list(matrix.translation),
                             solid=obj.get("collision_solid") in (True, "True", "true")))
            offsets[collision] += len(triangle_data)
            if collision:
                collisions.append(triangle_data)
            else:
                geometry.append(triangle_data)
                indices.append(material_data)
        filename = f"Part_{part:03}.npz"
        np.savez(output / filename, mesh=np.concatenate(geometry) if geometry else np.empty((0, 3, 8), np.float32),
                 material=np.concatenate(indices) if indices else np.empty(0, np.int32),
                 collision=np.concatenate(collisions) if collisions else np.empty((0, 3, 3), np.float32))
        manifest.append(dict(file=filename, visualTriangles=offsets[0], proxyTriangles=offsets[1], objects=rows))
        for obj in batch:
            scratch.collection.objects.unlink(obj)
        print("GL125_EXPORT", part, len(rows), offsets, round(time.monotonic() - start, 1), flush=True)

    summary = dict(version=1, sourceHash=SOURCE_HASH, v73Hash=V73_HASH, lights=lights, materials=materials, parts=manifest)
    receipt = output / "Export.json.tmp"
    receipt.write_text(json.dumps(summary, ensure_ascii=False), encoding="utf-8")
    receipt.replace(output / "Export.json")
    links = {l["name"]: l["receiver_collection_states"] for l in lights if "receiver_collection_states" in l}
    (output / "ReceiverStates.json").write_text(json.dumps(links), encoding="utf-8")
    print("GL125_EXPORT_COMPLETE", len(manifest), len(materials), len(lights), flush=True)


if __name__ == "__main__":
    main()
