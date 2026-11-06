"""Read-only source skin/contact review. Existing reference topology is not edited."""
import bpy
import hashlib
import json
import math
import sys
from array import array
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/DollSinger/DomainSeals.blend'
ORIGINAL = ROOT / 'ArtSource/DollSinger/HaloExpansion.blend'
OUT = ROOT / 'SourceData/Animations/DomainSeals/BlenderReview.json'
source_sha = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
body = next(o for o in scene.objects if o.type == 'MESH' and len(o.vertex_groups) > 100)
specs = json.loads(scene['DomainSealSpecs'])
clips = json.loads(scene['DomainSealClips'])
DIGITS = ('人指', '中指', '薬指', '小指', '親指')
PROTECTED = ('Rifle_', 'Revolver_', 'Shotgun_', 'Sniper_', 'Cicada_', 'Vesper_', 'Rondo_')


def mesh_hash(mesh):
    points = array('f', [0]) * (len(mesh.vertices) * 3)
    mesh.vertices.foreach_get('co', points)
    indices = array('i', [0]) * len(mesh.loops)
    mesh.loops.foreach_get('vertex_index', indices)
    return hashlib.sha256(points.tobytes() + indices.tobytes()).hexdigest()


def protected_actions():
    result = {}
    for action in bpy.data.actions:
        if not action.name.startswith(PROTECTED):
            continue
        h = hashlib.sha256()
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        h.update((curve.data_path + str(curve.array_index)).encode())
                        for key in curve.keyframe_points:
                            h.update(array('f', key.co).tobytes())
        result[action.name] = h.hexdigest()
    return result


def label(n):
    if n.endswith(('.L', '.R')):
        side = n[-1]
        # 親指０ is the palm's thumb metacarpal, joined to the wrist/palm skin.
        # Its attachment seam is not a distal-finger collision surface.
        if n.startswith(('手首', '親指０')):
            return 'palm.' + side
        if n.startswith(DIGITS):
            return n[:2] + '.' + side
        if n.startswith(('ひじ', '手捩')):
            return 'forearm.' + side
        if n.startswith(('腕', '肩')):
            return 'upper.' + side
    return 'body'


geometry = mesh_hash(body.data)
protected = protected_actions()
labels = [label(body.vertex_groups[max(v.groups, key=lambda g: g.weight).group].name) if v.groups else 'body' for v in body.data.vertices]
faces = {}
for face in body.data.polygons:
    groups = {labels[i] for i in face.vertices}
    if len(groups) == 1:
        faces.setdefault(next(iter(groups)), []).append(tuple(face.vertices))
samples = []
failures = []
for spec in specs:
    rig.animation_data.action = bpy.data.actions[spec['action']]
    scene.frame_set(1)
    bpy.context.view_layer.update()
    ev = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = ev.to_mesh()
    vertices = [ev.matrix_world @ v.co for v in mesh.vertices]
    trees = {name: BVHTree.FromPolygons(vertices, fs, all_triangles=True) for name, fs in faces.items()}
    contacts = []
    for i, a in enumerate(sorted(trees)):
        for b in sorted(trees)[i + 1:]:
            finger = a.startswith(DIGITS) or b.startswith(DIGITS)
            body_clearance = b == 'body' and a.startswith(('forearm.', 'palm.')) or a == 'body' and b.startswith(('forearm.', 'palm.'))
            if not finger and not body_clearance:
                continue
            if a[-1:] == b[-1:] and (a.startswith('palm.') or b.startswith('palm.')):
                continue
            overlaps = trees[a].overlap(trees[b])
            if not overlaps:
                continue
            depth = 0
            for current, other in ((a, b), (b, a)):
                indices = {v for pair in overlaps for v in faces[current][pair[0] if current == a else pair[1]]}
                for index in indices:
                    near, normal, _, distance = trees[other].find_nearest(vertices[index])
                    if (vertices[index] - near).dot(normal) < 0:
                        depth = max(depth, distance)
            # Crossed fingers and thumb-on-index presses may compress by <=2 mm.
            allowed = ((spec['weapon'] == 6 and {a, b} == {'中指.R', '人指.R'}) or
                a[-1:] == b[-1:] and {a[:2], b[:2]} == {'人指', '親指'}) and depth <= .002
            record = dict(a=a, b=b, trianglePairs=len(overlaps), maximumDepthMm=round(depth * 1000, 4), expectedSoftContact=allowed)
            contacts.append(record)
            if not allowed:
                failures.append(dict(weapon=spec['weapon'], **record))
    ev.to_mesh_clear()
    samples.append(dict(weapon=spec['weapon'], character=spec['character'], contacts=contacts))

# Verify the complete sampled Actions preserve each finger's local shape while
# the whole paired sign moves, including steep aim and each burst variant.
shape_samples = 0
maximum_shape_error = 0
reference = {}
for clip in clips:
    rig.animation_data.action = bpy.data.actions[clip['action']]
    for frame in sorted({1, max(1, round(clip['frames'] / 3)), clip['frames']}):
        scene.frame_set(frame)
        for bone in rig.pose.bones:
            if not bone.name.startswith(DIGITS):
                continue
            key = (clip['weapon'], bone.name)
            q = bone.rotation_quaternion.copy()
            if key not in reference:
                reference[key] = q
            angle = math.degrees(q.rotation_difference(reference[key]).angle)
            maximum_shape_error = max(maximum_shape_error, min(angle, 360 - angle))
        shape_samples += 1
assert maximum_shape_error < .15, ('Finger shape changed between Actions', maximum_shape_error)
sys.path.insert(0, str(Path.home() / '.codex/skills/blender-asset-qa/scripts'))
import qa_check
reference_qa = qa_check.run(objects=[body.name], profile='render', no_modifiers=True, uv=False)
body_name = body.name
bpy.ops.wm.open_mainfile(filepath=str(ORIGINAL))
assert mesh_hash(bpy.data.objects[body_name].data) == geometry, 'Reference geometry changed'
assert protected_actions() == protected, 'Protected source Actions changed'
report = dict(source=str(SOURCE.relative_to(ROOT)), sourceSha256=source_sha,
    passed=not failures, unchangedReferenceMesh=True, protectedSourceActionsUnchanged=True,
    sourceUnchanged=source_sha == hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    staticSurfaceSamples=samples, failures=failures, shapeSamples=shape_samples,
    maximumFingerShapeErrorDegrees=maximum_shape_error,
    contactToleranceMm=2, attachmentSeamsExcluded=True,
    referenceMeshQA=dict(errors=reference_qa['errors'], warnings=reference_qa['warnings']),
    scope='Six neutral skin surfaces, all Action finger-shape samples; native Unity skin views and actual first-person checks are separate.')
OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
print('@@JSON@@' + json.dumps(report))
if failures:
    raise RuntimeError('Unexpected source skin intersections: inspect BlenderReview.json')
