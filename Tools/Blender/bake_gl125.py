"""Bake the packed GL125 static lighting into linear HDR textures on the local GPU.

Run Blender in background mode. Pass the prepared directory, evaluated directory,
and V73 author library after --. Use --atlas ID for a bounded bake.
"""

import argparse
import collections
import hashlib
import json
import pathlib
import struct
import sys
import time

import bpy
import numpy as np
from mathutils import Matrix


SOURCE_TOOLS = pathlib.Path(__file__).resolve().parents[2] / "SourceData/Models/SourceFidelity20261028"
sys.path.insert(0, str(SOURCE_TOOLS))
from cycles_device import configure_cycles_device


MAIN_SUN = "HM02_N21_REFERENCE_NEUTRAL_SIDEFRONT_SUN"
WORLD = "HM02_BLACK_LUNAR_WORLD_V1"
VERSION = 2


def build_material(row, textures, images):
    material = bpy.data.materials.new(row["name"])
    material.use_nodes = True
    nodes = material.node_tree.nodes
    principled = nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = row["base"]
    principled.inputs["Metallic"].default_value = row["metallic"]
    principled.inputs["Roughness"].default_value = row["roughness"]
    principled.inputs["Emission Color"].default_value = (*row["emission"], 1)
    principled.inputs["Emission Strength"].default_value = 1
    if row.get("transparent"):
        principled.inputs["Alpha"].default_value = row.get("alpha", 0.25)
    for field, channel in (("baseMap", "Base Color"), ("normalMap", "Normal")):
        image = row.get(field)
        if not image:
            continue
        key = (image["file"], image["space"])
        if key not in images:
            data = bpy.data.images.load(str(textures / image["file"]), check_existing=False)
            data.colorspace_settings.name = image["space"]
            images[key] = data
        texture = nodes.new("ShaderNodeTexImage")
        texture.image = images[key]
        color = texture.outputs["Color"]
        if field == "normalMap":
            normal = nodes.new("ShaderNodeNormalMap")
            normal.uv_map = "Surface"
            material.node_tree.links.new(color, normal.inputs["Color"])
            color = normal.outputs["Normal"]
        material.node_tree.links.new(color, principled.inputs[channel])
    target = nodes.new("ShaderNodeTexImage")
    return material, target


def read_geometry(path, chunk):
    with path.open("rb") as stream:
        magic, vertex_count, stride, submesh_count = struct.unpack("<4sIII", stream.read(16))
        if magic != b"GLM1" or stride != 10 or vertex_count != chunk["vertices"]:
            raise ValueError(f"Invalid packed lightmap mesh {path}")
        vertices = np.fromfile(stream, dtype="<f4", count=vertex_count * stride).reshape(-1, stride)
        faces = []
        for slot in range(submesh_count):
            material_index, index_count = struct.unpack("<iI", stream.read(8))
            if material_index != chunk["materials"][slot] or index_count % 3:
                raise ValueError(f"Invalid material indices {path}")
            faces.append((material_index, np.fromfile(stream, dtype="<i4", count=index_count)))
    if sum(len(indices) for _, indices in faces) != 3 * chunk["triangles"]:
        raise ValueError(f"Incorrect triangle count {path}")
    return vertices, faces


def build_geometry(name, chunks, prepared, materials, scene):
    # Unity keeps small culling tiles. Cycles pays its per-object setup cost
    # once for each atlas and light-link signature.
    origin = np.array(chunks[0]["origin"], dtype=np.float32)
    material_ids = sorted({id_ for chunk in chunks for id_ in chunk["materials"]})
    material_slots = {id_: i for i, id_ in enumerate(material_ids)}
    parts, indices, face_slots = [], [], []
    offset = 0
    for chunk in chunks:
        vertices, submeshes = read_geometry(prepared / "Meshes" / chunk["file"], chunk)
        vertices[:, :3] += np.array(chunk["origin"], dtype=np.float32) - origin
        parts.append(vertices)
        for material, faces in submeshes:
            indices.append(faces + offset)
            face_slots.append(np.full(len(faces) // 3, material_slots[material], np.int32))
        offset += len(vertices)
    vertices = np.concatenate(parts)
    loops = np.concatenate(indices)
    count = len(loops) // 3

    mesh = bpy.data.meshes.new(name)
    mesh.vertices.add(len(vertices))
    mesh.vertices.foreach_set("co", np.ascontiguousarray(vertices[:, :3]).ravel())
    mesh.loops.add(len(loops))
    mesh.loops.foreach_set("vertex_index", loops)
    mesh.polygons.add(count)
    mesh.polygons.foreach_set("loop_start", np.arange(0, len(loops), 3, dtype=np.int32))
    mesh.polygons.foreach_set("use_smooth", np.ones(count, dtype=bool))
    for material_index in material_ids:
        mesh.materials.append(materials[material_index][0])
    mesh.polygons.foreach_set("material_index", np.concatenate(face_slots))
    surface = mesh.uv_layers.new(name="Surface")
    surface.uv.foreach_set("vector", vertices[loops, 6:8].ravel())
    surface.active_render = True
    uv = mesh.uv_layers.new(name="Irradiance")
    uv.uv.foreach_set("vector", vertices[loops, 8:10].ravel())
    mesh.uv_layers.active = uv
    mesh.update(calc_edges=True)
    mesh.normals_split_custom_set_from_vertices(vertices[:, 3:6])
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    obj.location = origin
    return obj


def build_lights(rows, scene):
    receivers = {}
    for index, row in enumerate(rows):
        if row["name"] == MAIN_SUN:
            continue  # The main sun stays real-time in Unity, including shadows.
        data = bpy.data.lights.new(row["name"], row["type"])
        data.energy = row["energy"]
        data.color = row["color"]
        data.use_shadow = row["shadow"]
        if row["type"] == "AREA":
            data.shape = row["shape"]
            data.size = row["size"]
            data.size_y = row["sizeY"]
            data.spread = row["spread"]
        if row["type"] == "SUN":
            data.angle = row["angle"]
        obj = bpy.data.objects.new(row["name"], data)
        scene.collection.objects.link(obj)
        obj.matrix_world = Matrix(row["matrix"])
        if row.get("receiver_collection"):
            receivers[index] = obj
    return receivers


def filter_irradiance(pixels):
    """Damp isolated Monte Carlo samples inside the padded bake islands."""
    rgb = pixels[:, :, :3]
    valid = pixels[:, :, 3] > 0
    if not np.any(valid):
        raise ValueError("Cycles did not populate any lightmap texels")
    # A small separable kernel preserves texture and stops at clear texels.
    # Process strips to keep 4K atlas memory bounded.
    for axis in (0, 1):
        source = np.swapaxes(rgb, axis, 0).copy()
        mask = np.swapaxes(valid, axis, 0)
        destination = np.swapaxes(rgb, axis, 0)
        for start in range(1, len(source) - 1, 128):
            end = min(start + 128, len(source) - 1)
            center = source[start:end]
            before, after = mask[start - 1:end - 1, :, None], mask[start + 1:end + 1, :, None]
            filtered = (2 * center + source[start - 1:end - 1] * before + source[start + 1:end + 1] * after) / (2 + before + after)
            destination[start:end] = np.where(mask[start:end, :, None], filtered, center)
    return int(np.count_nonzero(valid))


def bake_supersampled(scene, atlas, objects, targets, output, world_hash, samples, scale):
    index, size = atlas["id"], atlas["size"]
    if size % scale:
        raise ValueError("The atlas size must divide evenly into supersample tiles")
    output.mkdir(exist_ok=True)
    scratch = output / f"Atlas_{index:03}_{scale}x"
    scratch.mkdir(exist_ok=True)
    receipt = output / f"Irradiance_{index:03}.json"
    configuration = dict(version=VERSION, worldHash=world_hash, id=index, size=size, samples=samples, supersample=scale)
    if receipt.is_file() and receipt.with_suffix(".exr").is_file():
        prior = json.loads(receipt.read_text(encoding="utf-8"))
        if all(prior.get(key) == value for key, value in configuration.items()):
            return
    checkpoint = scratch / "Configuration.json"
    if checkpoint.is_file() and json.loads(checkpoint.read_text()) != configuration:
        raise ValueError(f"Supersample tiles are from another atlas revision: {scratch}")
    checkpoint.write_text(json.dumps(configuration), encoding="utf-8")

    scene.cycles.samples = samples
    scene.render.bake.margin = 2 * scale
    padding = 8 * scale
    tile_size = size + padding * 2
    step = size // scale
    assembled = np.zeros((size, size, 4), dtype=np.float32)
    layers = []
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        mesh = obj.data
        uv = np.empty((len(mesh.loops), 2), np.float32)
        mesh.uv_layers["Irradiance"].uv.foreach_get("vector", uv.ravel())
        layer = mesh.uv_layers.new(name="Supersample")
        high_uv = uv * (size * scale)
        mesh.calc_loop_triangles()
        faces = np.empty((len(mesh.loop_triangles), 3), np.int32)
        mesh.loop_triangles.foreach_get("loops", faces.ravel())
        triangles = high_uv[faces]
        layers.append((mesh, layer, high_uv, triangles.min(axis=1), triangles.max(axis=1)))
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    started = time.monotonic()
    try:
        for ty in range(scale):
            for tx in range(scale):
                tile = scratch / f"Tile_{ty:02}_{tx:02}.npy"
                x, y = tx * size, ty * size
                if tile.is_file():
                    downsample = np.load(tile, allow_pickle=False)
                else:
                    lower = np.array([x - padding, y - padding], np.float32)
                    upper = lower + tile_size
                    if not any(np.any(np.all((maxima >= lower) & (minima <= upper), axis=1))
                               for _, _, _, minima, maxima in layers):
                        continue
                    image = bpy.data.images.new("GL125 supersample tile", tile_size, tile_size, alpha=True, float_buffer=True, is_data=True)
                    for material, node in targets:
                        node.image = image
                        material.node_tree.nodes.active = node
                    for mesh, layer, uv, _, _ in layers:
                        mapped = (uv - lower) / tile_size
                        layer.uv.foreach_set("vector", mapped.ravel())
                        mesh.update()
                    print("GL125_SUPERSAMPLE_TILE", index, tx, ty, flush=True)
                    status = bpy.ops.object.bake(type="DIFFUSE", pass_filter={"DIRECT", "INDIRECT"}, uv_layer="Supersample")
                    if "FINISHED" not in status:
                        raise RuntimeError(f"Cycles did not finish supersample tile {tx}, {ty}: {status}")
                    raw = np.empty((tile_size, tile_size, 4), np.float32)
                    image.pixels.foreach_get(raw.ravel())
                    bpy.data.images.remove(image)
                    crop = raw[padding:padding + size, padding:padding + size].reshape(step, scale, step, scale, 4)
                    mask = crop[:, :, :, :, 3:4] > 0
                    samples_per_pixel = mask.sum(axis=(1, 3))
                    downsample = np.empty((step, step, 4), dtype=np.float32)
                    downsample[:, :, :3] = (crop[:, :, :, :, :3] * mask).sum(axis=(1, 3)) / np.maximum(1, samples_per_pixel)
                    downsample[:, :, 3] = samples_per_pixel[:, :, 0] > 0
                    pending = tile.with_suffix(".writing.npy")
                    np.save(pending, downsample, allow_pickle=False)
                    pending.replace(tile)
                assembled[ty * step:(ty + 1) * step, tx * step:(tx + 1) * step] = downsample
    finally:
        for mesh, layer, _, _, _ in layers:
            mesh.uv_layers.remove(layer)
        scene.render.bake.margin = 2
    if not np.isfinite(assembled).all():
        raise ValueError(f"Invalid supersampled lightmap {index}")
    covered = filter_irradiance(assembled)
    image = bpy.data.images.new(receipt.stem, size, size, alpha=True, float_buffer=True, is_data=True)
    image.pixels.foreach_set(assembled.ravel())
    temporary = receipt.with_suffix(".writing.exr")
    image.save_render(str(temporary), scene=scene)
    temporary.replace(receipt.with_suffix(".exr"))
    bpy.data.images.remove(image)
    result = dict(configuration, coveredPixels=covered, seconds=round(time.monotonic() - started, 1))
    receipt.write_text(json.dumps(result), encoding="utf-8")
    print("GL125_SUPERSAMPLE_DONE", result, flush=True)


def bake_one(scene, atlas, objects, targets, output, world_hash, samples):
    index = atlas["id"]
    target = output / f"Irradiance_{index:03}.exr"
    receipt = target.with_suffix(".json")
    if target.is_file() and receipt.is_file():
        prior = json.loads(receipt.read_text(encoding="utf-8"))
        if (prior["version"], prior["worldHash"], prior["samples"]) == (VERSION, world_hash, samples):
            return

    scene.cycles.samples = samples
    started = time.monotonic()
    size = atlas["size"]
    image = bpy.data.images.new(target.stem, size, size, float_buffer=True, alpha=True, is_data=True)
    for material, bake_node in targets:
        bake_node.image = image
        material.node_tree.nodes.active = bake_node
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    print("GL125_BAKE_BEGIN", index, size, atlas["triangles"], samples, flush=True)
    status = bpy.ops.object.bake(type="DIFFUSE", pass_filter={"DIRECT", "INDIRECT"}, uv_layer="Irradiance")
    if "FINISHED" not in status:
        raise RuntimeError(f"Cycles did not finish atlas {index}: {status}")
    pixels = np.empty(size * size * 4, np.float32)
    image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(size, size, 4)
    if not np.isfinite(pixels).all():
        raise ValueError(f"Invalid linear irradiance in atlas {index}")
    covered = filter_irradiance(pixels)
    rgb = pixels[:, :, :3]
    image.pixels.foreach_set(pixels.ravel())
    temp = output / (target.stem + ".writing.exr")
    image.save_render(str(temp), scene=scene)
    temp.replace(target)
    bpy.data.images.remove(image)
    result = dict(version=VERSION, worldHash=world_hash, id=index, size=size, samples=samples, coveredPixels=covered,
                  maximum=float(np.max(rgb)), mean=float(rgb.mean()), seconds=round(time.monotonic() - started, 1))
    receipt.write_text(json.dumps(result), encoding="utf-8")
    print("GL125_BAKE_DONE", json.dumps(result), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("prepared", type=pathlib.Path)
    parser.add_argument("evaluated", type=pathlib.Path)
    parser.add_argument("author_library", type=pathlib.Path)
    parser.add_argument("--atlas", type=int, nargs="+")
    parser.add_argument("--samples", type=int, default=32)
    parser.add_argument("--scale", type=int, choices=(1, 2, 4, 8), default=1)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    world_json = (args.prepared / "World.json").read_bytes()
    world_hash = hashlib.sha256(world_json).hexdigest()
    world = json.loads(world_json)
    output = args.prepared / "Lightmaps"
    output.mkdir(exist_ok=True)
    scene = bpy.data.scenes.new("GL125 static light transport")
    bpy.context.window.scene = scene
    with bpy.data.libraries.load(str(args.author_library), link=True) as (available, selected):
        if WORLD not in available.worlds:
            raise ValueError("GL125 author world unavailable")
        selected.worlds = [WORLD]
    scene.world = selected.worlds[0]
    scene.render.engine = "CYCLES"
    configure_cycles_device(scene)
    scene.cycles.max_bounces = 5
    scene.cycles.diffuse_bounces = 3
    scene.cycles.glossy_bounces = 2
    scene.cycles.sample_clamp_indirect = 4
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = 0.025
    scene.render.bake.margin = 2
    scene.render.bake.margin_type = "EXTEND"
    scene.render.bake.use_clear = True
    scene.render.image_settings.file_format = "OPEN_EXR"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.color_depth = "16"
    scene.render.image_settings.exr_codec = "ZIP"

    lights = build_lights(world["lights"], scene)
    groups = {index: bpy.data.collections.new(f"Receiver {index}") for index in lights}
    for index, light in lights.items():
        light.light_linking.receiver_collection = groups[index]
    meshes = {atlas["id"]: [] for atlas in world["atlases"]}
    images = {}
    materials = [build_material(row, args.evaluated / "Textures", images) for row in world["materials"]]
    batches = collections.defaultdict(list)
    for chunk in world["chunks"]:
        if chunk["atlas"] >= 0:
            batches[(chunk["atlas"], tuple(chunk["receiverSignature"]))].append(chunk)
    for (atlas, signatures), chunks in batches.items():
        obj = build_geometry(f"Bake_{atlas:03}_{len(meshes[atlas])}", chunks, args.prepared, materials, scene)
        meshes[atlas].append(obj)
        for signature in signatures:
            index = signature if signature >= 0 else -signature - 1
            collection = groups[index]
            collection.objects.link(obj)
            collection.collection_objects[-1].light_linking.link_state = "INCLUDE" if signature >= 0 else "EXCLUDE"
    for index, light in lights.items():
        if not groups[index].objects:
            light.light_linking.receiver_collection = None
    print("GL125_BAKE_INPUTS", sum(map(len, meshes.values())), len(world["lights"]) - 1, flush=True)
    for atlas in world["atlases"]:
        index = atlas["id"]
        if args.atlas is None or index in args.atlas:
            if args.scale > 1:
                bake_supersampled(scene, atlas, meshes[index], materials, args.prepared / "LightmapsHQ", world_hash, args.samples, args.scale)
            else:
                bake_one(scene, atlas, meshes[index], materials, output, world_hash, args.samples)
    print("GL125_BAKE_FINISHED", args.atlas, flush=True)


if __name__ == "__main__":
    main()
