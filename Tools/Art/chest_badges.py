"""Author the three subspace chest badges. Run in a dedicated background Blender.

python3 .../run_in_blender.py Tools/Art/chest_badges.py
Outputs stay in LocalData/ChestBadgeReview/Staging until the scoped Unity import.
The same build() is used by item_assets.py, so a catalog rebuild keeps this art.
"""
import json
import math
import pathlib
import sys

import bmesh
import bpy
from mathutils import Vector

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "LocalData/ChestBadgeReview/Staging"
STYLES = {"scout_rig": (4, .036, "Scout", (.22, .56, .70)),
          "rig": (6, .041, "Standard", (.50, .40, .72)),
          "field_rig": (8, .046, "Field", (.78, .52, .20))}


def material(name, color, metallic, roughness):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    node = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    node.inputs["Base Color"].default_value = (*color, 1)
    node.inputs["Metallic"].default_value = metallic
    node.inputs["Roughness"].default_value = roughness
    return mat


def build(code):
    lobes, radius, style, color = STYLES[code]
    materials = [material("Badge_Alloy", (.66, .72, .77), .8, .25),
                 material("Badge_Enamel", (.88, .87, .82), .05, .30),
                 material("Badge_" + style, color, .15, .30)]
    count = 96
    verts, faces, slots = [], [], []
    # One watertight surface. The scalloped rim, enamel and inset compass star
    # share vertices; there are no hovering decals or intersecting kitbash parts.
    rings = [(.88, .0038, "edge", 0), (1, .0020, "edge", 0),
             (1, -.0006, "edge", 0), (.965, -.0027, "edge", 0),
             (.90, -.0038, "edge", 0), (.85, -.0038, "edge", 0),
             (.815, -.0028, "edge", 0), (.75, -.0034, "edge", 1),
             (.43, -.0034, "star", 1), (.39, -.0048, "star", 0),
             (.335, -.0052, "star", 2), (.07, -.0052, "circle", 2)]
    for size, depth, shape, _ in rings:
        for i in range(count):
            angle = math.tau * i / count
            wave = .88 + .12 * math.cos(lobes * angle) if shape == "edge" else (
                1 / (1 + 2.6 * abs(math.sin(2 * angle))) if shape == "star" else 1)
            r = radius * size * wave
            verts.append((r * math.sin(angle), depth, radius + r * math.cos(angle)))
    for ring in range(len(rings) - 1):
        for i in range(count):
            j = (i + 1) % count
            faces.append((ring * count + i, ring * count + j,
                          (ring + 1) * count + j, (ring + 1) * count + i))
            slots.append(rings[ring + 1][3])
    for ring, depth, slot in [(0, .0038, 0), (len(rings) - 1, -.0052, 2)]:
        center = len(verts)
        verts.append((0, depth, radius))
        for i in range(count):
            faces.append((center, ring * count + i, ring * count + (i + 1) % count))
            slots.append(slot)
    mesh = bpy.data.meshes.new(code + "_badge")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(code, mesh)
    bpy.context.scene.collection.objects.link(obj)
    for mat in materials:
        mesh.materials.append(mat)
    for poly, index in zip(mesh.polygons, slots):
        poly.material_index = index
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=1.15192, island_margin=.008)
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def publish_all():
    OUT.mkdir(parents=True, exist_ok=True)
    sys.path.insert(0, str(pathlib.Path.home() / ".codex/skills/blender-asset-qa/scripts"))
    import qa_check
    import review_shots
    report = []
    for code in STYLES:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        scene.unit_settings.system = "METRIC"
        scene.unit_settings.scale_length = 1
        obj = build(code)
        qa = qa_check.run(objects=[obj.name], profile="game", eps=.00015, tol=.0003)
        if not qa["pass"] or qa["warnings"]:
            raise RuntimeError(json.dumps({"code": code, "qa": qa}))
        scene.render.engine = "CYCLES"
        scene.cycles.samples = 32
        scene.cycles.use_denoising = True
        scene.render.threads_mode = "FIXED"
        scene.render.threads = 4
        scene.render.resolution_x = scene.render.resolution_y = 512
        scene.render.resolution_percentage = 100
        scene.render.film_transparent = True
        scene.render.image_settings.file_format = "PNG"
        scene.render.image_settings.color_mode = "RGBA"
        scene.world = bpy.data.worlds.new("Badge studio")
        scene.world.color = (.20, .20, .20)
        center = Vector((0, 0, STYLES[code][1]))
        for name, pos, power, size in [("Key", (-.3, -.4, .55), 16, .4),
                                        ("Fill", (.35, -.2, .2), 7, .3),
                                        ("Rim", (.15, .2, .4), 10, .3)]:
            data = bpy.data.lights.new(name, "AREA")
            data.energy, data.shape, data.size = power, "DISK", size
            light = bpy.data.objects.new(name, data)
            scene.collection.objects.link(light)
            light.location = pos
            light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()
        cam = bpy.data.objects.new("BadgeCamera", bpy.data.cameras.new("BadgeCamera"))
        scene.collection.objects.link(cam)
        scene.camera = cam
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = STYLES[code][1] * 2.6
        cam.data.clip_start = .001
        cam.location = center + Vector((.08, -.42, .07))
        cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.view_settings.view_transform = "AgX"
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT / (code + ".blend")))
        bpy.ops.export_scene.fbx(filepath=str(OUT / (code + ".fbx")),
                                 use_selection=True, object_types={"MESH"},
                                 apply_unit_scale=True, axis_forward="-Z", axis_up="Y",
                                 bake_anim=False, add_leaf_bones=False)
        scene.render.filepath = str(OUT / (code + ".png"))
        bpy.ops.render.render(write_still=True)
        reviews = {}
        for mode in ["material", "wire"]:
            reviews[mode] = review_shots.run(objects=[code], out=str(OUT / (code + "_" + mode + ".png")),
                                              views=["front", "right", "back", "three_quarter"], mode=mode, res=320)
        mesh = obj.data
        mesh.calc_loop_triangles()
        report.append({"code": code, "triangles": len(mesh.loop_triangles),
                       "dimensions": list(obj.dimensions), "materials": 3, "qa": qa, "reviews": reviews})
    (OUT / "report.json").write_text(json.dumps(report, indent=2))
    print("@@JSON@@" + json.dumps({"report": str(OUT / "report.json"), "badges": report}))


if __name__ == "__main__":
    publish_all()
