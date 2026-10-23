"""Import the original PMX with upstream MMD Tools and export a clean Unity FBX.

Run in a single Blender process (the addon is registered only in that process):
  Blender --background --factory-startup --python Tools/Models/import_hatsune_miku.py -- \
    --mmd-tools /tmp/blender_mmd_tools-<revision>
No packages or user preferences are installed. Source MMD constraints/physics remain
in ArtSource; the FBX contains bind transforms, skin and vertex morphs only.
"""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import shutil
import struct
import sys
import tempfile
import zipfile


ADDON_REVISION = "e321bd02dd0288a3dc9f1974c06597803c060e11"
ADDON_URL = "https://github.com/MMD-Blender/blender_mmd_tools"
PROJECT = Path(__file__).resolve().parents[2]


class PmxReader:
    """Independent format audit; MMD Tools performs the actual Blender import."""

    def __init__(self, path):
        self.data = Path(path).read_bytes()
        self.offset = 0

    def read(self, fmt):
        fmt = "<" + fmt
        value = struct.unpack_from(fmt, self.data, self.offset)
        self.offset += struct.calcsize(fmt)
        return value[0] if len(value) == 1 else value

    def skip(self, count):
        self.offset += count

    def text(self):
        count = self.read("i")
        value = self.data[self.offset:self.offset + count].decode(self.encoding)
        self.offset += count
        return value

    def index(self, kind):
        return self.read({1: "b", 2: "h", 4: "i"}[self.header[kind]])

    def parse(self):
        assert self.read("4s") == b"PMX "
        version = self.read("f")
        assert version == 2.0, "This conversion is validated for the supplied PMX 2.0 source."
        self.header = list(self.read(str(self.read("B")) + "B"))
        self.encoding = "utf-16-le" if self.header[0] == 0 else "utf-8"
        model = dict(zip(("name", "englishName", "comment", "englishComment"),
                         [self.text() for _ in range(4)]))
        vertices = []
        weight_counts = collections.Counter()
        for _ in range(self.read("i")):
            position = self.read("3f")
            normal = self.read("3f")
            uv = self.read("2f")
            self.skip(self.header[1] * 16)
            kind = self.read("B")
            if kind == 0:
                indices, weights = [self.index(5)], [1.0]
            elif kind == 1:
                indices = [self.index(5), self.index(5)]
                weight = self.read("f")
                weights = [weight, 1.0 - weight]
            elif kind == 2:
                indices = [self.index(5) for _ in range(4)]
                weights = self.read("4f")
            else:
                raise ValueError("Unexpected skin type: " + str(kind))
            self.skip(4)
            for index, weight in zip(indices, weights):
                if weight > 0:
                    weight_counts[index] += 1
            vertices.append(dict(position=position, normal=normal, uv=uv,
                                 indices=indices, weights=weights))
        triangle_count = self.read("i") // 3
        self.skip(triangle_count * 3 * self.header[2])
        textures = [self.text() for _ in range(self.read("i"))]
        materials = []
        for _ in range(self.read("i")):
            material = dict(name=self.text(), englishName=self.text())
            material["diffuse"] = self.read("4f")
            self.skip(12 + 4 + 12)
            material["flags"] = self.read("B")
            self.skip(16 + 4)
            material["textureIndex"] = self.index(3)
            material["sphereIndex"] = self.index(3)
            material["sphereMode"] = self.read("B")
            shared_toon = self.read("B")
            self.skip(1 if shared_toon else self.header[3])
            material["memo"] = self.text()
            material["triangleCount"] = self.read("i") // 3
            materials.append(material)
        bones = []
        for index in range(self.read("i")):
            bone = dict(index=index, name=self.text(), englishName=self.text(),
                        position=self.read("3f"), parent=self.index(5), layer=self.read("i"))
            flags = self.read("H")
            bone["flags"] = flags
            bone["tail"] = self.index(5) if flags & 1 else self.read("3f")
            if flags & 0x300:
                bone["grant"] = dict(target=self.index(5), weight=self.read("f"))
            if flags & 0x400:
                bone["fixedAxis"] = self.read("3f")
            if flags & 0x800:
                bone["localAxes"] = self.read("6f")
            if flags & 0x2000:
                self.skip(4)
            if flags & 0x20:
                self.skip(self.header[5] + 8)
                for _ in range(self.read("i")):
                    self.skip(self.header[5])
                    self.skip(24 if self.read("B") else 0)
            bone["weightedVertexCount"] = weight_counts[index]
            bones.append(bone)
        morphs = []
        for _ in range(self.read("i")):
            morph = dict(name=self.text(), englishName=self.text(), panel=self.read("B"),
                         type=self.read("B"), count=self.read("i"))
            size = {0: self.header[6] + 4, 1: self.header[2] + 12,
                    2: self.header[5] + 28, 3: self.header[2] + 16,
                    4: self.header[2] + 16, 5: self.header[2] + 16,
                    6: self.header[2] + 16, 7: self.header[2] + 16,
                    8: self.header[4] + 113}[morph["type"]]
            self.skip(morph["count"] * size)
            morphs.append(morph)
        for _ in range(self.read("i")):
            self.text()
            self.text()
            self.skip(1)
            for _ in range(self.read("i")):
                self.skip(self.header[5] if self.read("B") == 0 else self.header[6])
        rigids = []
        for _ in range(self.read("i")):
            rigid = dict(name=self.text(), englishName=self.text(), bone=self.index(5),
                         group=self.read("B"), collisionMask=self.read("H"), shape=self.read("B"),
                         size=self.read("3f"), position=self.read("3f"), rotation=self.read("3f"))
            rigid.update(zip(("mass", "linearDamping", "angularDamping", "restitution", "friction"),
                             self.read("5f")))
            rigid["mode"] = self.read("B")
            rigids.append(rigid)
        joints = []
        for _ in range(self.read("i")):
            joint = dict(name=self.text(), englishName=self.text(), type=self.read("B"),
                         rigidA=self.index(7), rigidB=self.index(7), parameters=self.read("24f"))
            joints.append(joint)
        assert self.offset == len(self.data), "PMX audit did not consume the complete file"
        return dict(version=version, model=model, vertices=vertices, triangleCount=triangle_count,
                    textures=textures, materials=materials, bones=bones, morphs=morphs,
                    rigidBodies=rigids, joints=joints)


def canonical_name(name):
    if name.startswith(("左", "右")):
        side = ".L" if name[0] == "左" else ".R"
        name = name[1:] + side
    return name


def main():
    import bpy
    from mathutils import Matrix, Vector

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path,
                        default=Path.home() / "Downloads/Hatsune Miku_by_Boku_Wa_Luka/Hatsune Miku.pmx")
    parser.add_argument("--mmd-tools", type=Path, required=True)
    parser.add_argument("--height", type=float, default=1.65)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    source = args.source.resolve()
    parsed = PmxReader(source).parse()
    bounds_min = min(v["position"][1] for v in parsed["vertices"])
    bounds_max = max(v["position"][1] for v in parsed["vertices"])
    scale = args.height / (bounds_max - bounds_min)
    addon = args.mmd_tools.resolve()
    sys.path.insert(0, str(addon))
    for wheel in sorted((addon / "mmd_tools/wheels").glob("*.whl")):
        # OpenCC reads JSON dictionaries by filesystem path, so zipimport alone
        # cannot load its data. Unpack bundled wheels into temporary storage.
        wheel_runtime = Path(tempfile.gettempdir()) / "moonkov-miku-import" / wheel.stem
        wheel_runtime.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(wheel) as archive:
            archive.extractall(wheel_runtime)
        sys.path.insert(0, str(wheel_runtime))
    import mmd_tools
    mmd_tools.register()
    import_options = dict(filepath=str(source), scale=scale,
                          types={"MESH", "ARMATURE", "PHYSICS", "DISPLAY", "MORPHS"},
                          clean_model=False, remove_doubles=False, fix_bone_order=False,
                          fix_ik_links=False, apply_bone_fixed_axis=False, rename_bones=False,
                          import_adduv2_as_vertex_colors=True, save_log=False)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.mmd_tools.import_model(**import_options)
    original_mesh = next(o for o in bpy.data.objects if o.type == "MESH" and
                         len(o.data.vertices) == len(parsed["vertices"]))
    original_rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    assert all(b["name"] in original_rig.data.bones for b in parsed["bones"])
    source_blend = PROJECT / "ArtSource/HatsuneMiku/HatsuneMiku.blend"
    source_blend.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.file.pack_all()
    # Save the complete original import before creating the constraint-free export rig.
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(source_blend), check_existing=False)

    asset_root = PROJECT / "Assets/HatsuneMiku/Art"
    model_dir, texture_dir = asset_root / "Model", asset_root / "Textures"
    model_dir.mkdir(parents=True, exist_ok=True)
    texture_dir.mkdir(parents=True, exist_ok=True)
    texture_info = []
    for texture in parsed["textures"]:
        origin = source.parent / texture.replace("\\", "/")
        destination = texture_dir / origin.name
        shutil.copyfile(origin, destination)
        texture_info.append(dict(source=texture, asset=str(destination.relative_to(PROJECT)),
                                 sha256=hashlib.sha256(origin.read_bytes()).hexdigest()))

    names = {b["name"]: canonical_name(b["name"]) for b in parsed["bones"]}
    assert len(set(names.values())) == len(names), "Canonical bone name collision"
    rest = {b["name"]: original_rig.data.bones[b["name"]].matrix_local.copy()
            for b in parsed["bones"]}
    heads = {b["name"]: original_rig.data.bones[b["name"]].head_local.copy()
             for b in parsed["bones"]}
    lengths = {b["name"]: original_rig.data.bones[b["name"]].length for b in parsed["bones"]}
    rig = bpy.data.objects.new("HatsuneMiku_Rig", bpy.data.armatures.new("HatsuneMiku_Rig"))
    bpy.context.collection.objects.link(rig)
    rig.matrix_world = original_rig.matrix_world.copy()
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for bone in parsed["bones"]:
        edit = rig.data.edit_bones.new(names[bone["name"]])
        edit.matrix = rest[bone["name"]]
        edit.length = max(lengths[bone["name"]], .001)
        edit.use_deform = True
    for bone in parsed["bones"]:
        if bone["parent"] >= 0:
            edit = rig.data.edit_bones[names[bone["name"]]]
            edit.parent = rig.data.edit_bones[names[parsed["bones"][bone["parent"]]["name"]]]
            edit.use_connect = False
    tips = []
    for side, prefix in (("L", "左"), ("R", "右")):
        for digit in ("人指", "中指", "薬指", "小指", "親指"):
            terminal = prefix + digit + ("２" if digit == "親指" else "３")
            bone = next(b for b in parsed["bones"] if b["name"] == terminal)
            assert not isinstance(bone["tail"], int)
            offset = Vector(bone["tail"]).xzy * scale
            tip_name = digit + "先." + side
            edit = rig.data.edit_bones.new(tip_name)
            edit.head = heads[terminal] + offset
            edit.tail = edit.head + offset.normalized() * .008
            edit.parent = rig.data.edit_bones[names[terminal]]
            edit.use_deform = True
            tips.append(dict(name=tip_name, parent=names[terminal],
                             positionBlender=list(edit.head), sourceTailOffset=bone["tail"]))
    bpy.ops.object.mode_set(mode="OBJECT")
    mesh = bpy.data.objects.new("HatsuneMiku_Body", original_mesh.data.copy())
    bpy.context.collection.objects.link(mesh)
    mesh.matrix_world = original_mesh.matrix_world.copy()
    mesh.parent = rig
    mesh.matrix_parent_inverse = rig.matrix_world.inverted()
    mesh.data.name = "HatsuneMiku_Body"
    # A Mesh datablock copy carries deform indices, but vertex group definitions
    # belong to its Object. Rebuild the audited BDEF influences explicitly rather
    # than depending on the importer's helper-group ordering.
    mesh.vertex_groups.clear()
    groups = {bone["index"]: mesh.vertex_groups.new(name=names[bone["name"]])
              for bone in parsed["bones"]}
    for tip in tips:
        mesh.vertex_groups.new(name=tip["name"])
    for index, vertex in enumerate(parsed["vertices"]):
        influences = collections.defaultdict(float)
        for bone_index, weight in zip(vertex["indices"], vertex["weights"]):
            if weight > 0:
                influences[bone_index] += weight
        for bone_index, weight in influences.items():
            groups[bone_index].add([index], weight, "REPLACE")
    if mesh.data.shape_keys:
        mesh.data.shape_keys.animation_data_clear()
        wanted = {m["name"] for m in parsed["morphs"] if m["type"] == 1}
        for key in list(mesh.data.shape_keys.key_blocks)[1:]:
            if key.name not in wanted:
                mesh.shape_key_remove(key)
            else:
                key.value = 0
    modifier = mesh.modifiers.new("HatsuneMiku_Skin", "ARMATURE")
    modifier.object = rig
    for index, material in enumerate(mesh.data.materials):
        copied = material.copy()
        copied.animation_data_clear()
        copied.name = parsed["materials"][index]["name"]
        if copied.node_tree:
            copied.node_tree.animation_data_clear()
            for node in copied.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    candidate = texture_dir / Path(bpy.path.abspath(node.image.filepath)).name
                    if candidate.exists():
                        node.image = bpy.data.images.load(str(candidate), check_existing=True)
        mesh.data.materials[index] = copied
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    rig.data.pose_position = "REST"
    fbx = model_dir / "HatsuneMiku.fbx"
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    assert not rig.constraints and all(not b.constraints for b in rig.pose.bones)
    bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True,
                             object_types={"ARMATURE", "MESH"}, global_scale=1,
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE",
                             axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
                             mesh_smooth_type="OFF", add_leaf_bones=False,
                             use_armature_deform_only=False, bake_anim=False,
                             path_mode="RELATIVE", embed_textures=False)

    def unity_position(position):
        return [-position[0] * scale, position[1] * scale, -position[2] * scale]

    manifest = dict(version=1, source=str(source), sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                    sourceCredits=parsed["model"], sourceBlend=str(source_blend.relative_to(PROJECT)),
                    fbx=str(fbx.relative_to(PROJECT)), addon=dict(repository=ADDON_URL,
                    revision=ADDON_REVISION, version=mmd_tools.MMD_TOOLS_VERSION,
                    importerSha256=hashlib.sha256((addon / "mmd_tools/core/pmx/importer.py").read_bytes()).hexdigest()),
                    blenderVersion=bpy.app.version_string, scale=scale, targetHeight=args.height,
                    coordinateConversion="Blender=(PMX.x,PMX.z,PMX.y)*scale; Unity=(-PMX.x,PMX.y,-PMX.z)*scale",
                    grantPolicy="Complete source constraints retained only in source blend; no grants, IK, drivers, physics or constraints exported to Unity.",
                    vertexCount=len(mesh.data.vertices), triangleCount=len(mesh.data.polygons),
                    vertexMorphs=[m["name"] for m in parsed["morphs"] if m["type"] == 1],
                    materials=parsed["materials"], textures=texture_info,
                    bones=[dict(source=b["name"], canonical=names[b["name"]],
                                parent=names[parsed["bones"][b["parent"]]["name"]] if b["parent"] >= 0 else None,
                                positionUnity=unity_position(b["position"]),
                                weightedVertexCount=b["weightedVertexCount"],
                                originalGrant=b.get("grant")) for b in parsed["bones"]],
                    tipBones=tips,
                    hairRoots=["Center_hair_01_offset", "Left_BS_hair_01_offset", "Left_CS_hair_01_offset",
                               "Right_BS_hair_01_offset", "Right_CS_hair_01_offset"],
                    skirtRoots=["スカート_0_" + str(i) for i in range(8)],
                    tieRoots=["Center_acc_01_offset"],
                    originalRigidBodyCount=len(parsed["rigidBodies"]), originalJointCount=len(parsed["joints"]),
                    rigidBodies=[dict(**r, boneCanonical=names[parsed["bones"][r["bone"]]["name"]] if r["bone"] >= 0 else None,
                                      sizeMetres=[value * scale for value in r["size"]],
                                      positionUnity=unity_position(r["position"])) for r in parsed["rigidBodies"]],
                    joints=parsed["joints"],
                    notes=["PMX sizes: sphere radius; box half extents; capsule radius and cylinder height.",
                           "Collision rotations remain original PMX radians; convert orientation before building Unity colliders.",
                           "Source left/right Magic Bone both parent to right wrist twist; no Magic grant exported.",
                           "Humanoid legs use 足/ひざ/足首, not unweighted D copies; toes use 足先EX.",
                           "Model license absent from source package. Shadekai shader is not exported."])
    expected_morphs = set(manifest["vertexMorphs"])
    assert len(mesh.data.materials) == 3
    assert len(rig.data.bones) == len(parsed["bones"]) + 10
    assert set(k.name for k in mesh.data.shape_keys.key_blocks[1:]) == expected_morphs
    assert len(mesh.data.vertices) == len(parsed["vertices"])
    # Independently import the FBX into the same process to check the delivered file.
    prior = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(fbx), use_anim=False)
    delivered = [o for o in bpy.data.objects if o not in prior]
    delivered_mesh = next(o for o in delivered if o.type == "MESH")
    delivered_rig = next(o for o in delivered if o.type == "ARMATURE")
    delivered_morphs = {k.name for k in delivered_mesh.data.shape_keys.key_blocks[1:]}
    height = max((delivered_mesh.matrix_world @ v.co).z for v in delivered_mesh.data.vertices) - min(
        (delivered_mesh.matrix_world @ v.co).z for v in delivered_mesh.data.vertices)
    assert abs(height - args.height) < .0001, (height, args.height)
    assert delivered_morphs == expected_morphs, delivered_morphs
    assert len(delivered_rig.data.bones) == len(rig.data.bones)
    assert len(delivered_mesh.data.polygons) == parsed["triangleCount"]
    assert len(delivered_mesh.data.vertices) == len(parsed["vertices"])
    max_weight_error = 0.0
    max_vertex_error = 0.0
    max_shape_error = 0.0
    max_bone_error = 0.0
    weight_mismatches = []
    for vertex, pmx_vertex in zip(delivered_mesh.data.vertices, parsed["vertices"]):
        actual_weights = {delivered_mesh.vertex_groups[g.group].name: g.weight
                          for g in vertex.groups if g.weight > 0}
        expected_weights = collections.defaultdict(float)
        for index, weight in zip(pmx_vertex["indices"], pmx_vertex["weights"]):
            if weight > 0:
                expected_weights[names[parsed["bones"][index]["name"]]] += weight
        for name in actual_weights.keys() | expected_weights.keys():
            error = abs(actual_weights.get(name, 0) - expected_weights.get(name, 0))
            max_weight_error = max(max_weight_error, error)
            if error > 1e-6 and len(weight_mismatches) < 8:
                weight_mismatches.append(dict(vertex=vertex.index, bone=name,
                                               actual=actual_weights, expected=dict(expected_weights)))
        wanted_position = Vector(pmx_vertex["position"]).xzy * scale
        max_vertex_error = max(max_vertex_error,
                               (delivered_mesh.matrix_world @ vertex.co - wanted_position).length)
    for name in expected_morphs:
        wanted_key = mesh.data.shape_keys.key_blocks[name]
        actual_key = delivered_mesh.data.shape_keys.key_blocks[name]
        for wanted_vertex, actual_vertex in zip(wanted_key.data, actual_key.data):
            max_shape_error = max(max_shape_error, (mesh.matrix_world @ wanted_vertex.co -
                                  delivered_mesh.matrix_world @ actual_vertex.co).length)
    for bone in rig.data.bones:
        max_bone_error = max(max_bone_error, (rig.matrix_world @ bone.head_local -
                             delivered_rig.matrix_world @ delivered_rig.data.bones[bone.name].head_local).length)
    material_counts = collections.Counter(p.material_index for p in delivered_mesh.data.polygons)
    assert [material_counts[i] for i in range(3)] == [m["triangleCount"] for m in parsed["materials"]]
    assert max_weight_error < 1e-6, ("FBX skin weights differ", max_weight_error, weight_mismatches)
    assert max_vertex_error < 1e-5, ("FBX rest vertices differ", max_vertex_error)
    assert max_shape_error < 1e-5, ("FBX shape keys differ", max_shape_error)
    assert max_bone_error < 1e-5, ("FBX bind bones differ", max_bone_error)
    manifest["fbxRoundTrip"] = dict(passed=True, heightMetres=height,
        vertexCount=len(delivered_mesh.data.vertices), triangleCount=len(delivered_mesh.data.polygons),
        boneCount=len(delivered_rig.data.bones), vertexMorphCount=len(delivered_morphs),
        materialCount=len(delivered_mesh.data.materials), maxWeightError=max_weight_error,
        maxVertexErrorMetres=max_vertex_error, maxShapeErrorMetres=max_shape_error,
        maxBoneErrorMetres=max_bone_error, materialTriangleCounts=[material_counts[i] for i in range(3)])
    (model_dir / "HatsuneMikuImport.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    print("MIKU_IMPORT_SUCCESS", json.dumps(manifest["fbxRoundTrip"]))


if __name__ == "__main__":
    main()
