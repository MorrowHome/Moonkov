"""Author the four lobby performances on the production DollSinger skeleton.

Run explicitly to revise the source. The separate exporter only reads the saved
Blender actions, so hand edits in Blender remain the source of truth.
"""
import ast
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Blender'))
from retarget_hatsune_miku import Rig, Retargeter

SOURCE = ROOT / 'ArtSource/DollSinger/SquadMotions.blend'
C = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
bind = Rig(json.loads((ROOT / 'Tools/Blender/DollSingerBind.json').read_text()))
P = {n: Vector(p) for n, p in bind.p.items()}
R = {n: Quaternion((q[3], *q[:3])) for n, q in bind.r.items()}
solver = Retargeter(bind, bind)
helpers = ast.parse((ROOT / 'Tools/Blender/weapon_gestures.py').read_text())
helpers = ast.Module(body=[n for n in helpers.body if isinstance(n, ast.FunctionDef)
    and n.name in ('norm', 'frame', 'rotbetween', 'position', 'setdir', 'hand', 'fingers')], type_ignores=[])
ctx = dict(Vector=Vector, Quaternion=Quaternion, Matrix=Matrix, math=math, P=P, R=R, parents=bind.parent)
exec(compile(helpers, 'production_hand_axes', 'exec'), ctx)
scale = (P['頭'].y - P['足首.R'].y) / 1.17


def xyzw(q):
    return (q.x, q.y, q.z, q.w)


def target(x, below_head, z):
    return Vector((x * scale, P['頭'].y + below_head * scale, z * scale))


def pose(kind, phase):
    wave = math.sin(phase * math.tau)
    breath = math.cos(phase * math.tau)
    world = {}
    # The planted lower body remains owned by the menu's existing idle clip.
    roll = (0, -1, -3, 7)[kind] + wave * (.35 if kind < 2 else 1.3)
    yaw = (-7, 3, -2, -4)[kind]
    pitch = (-2, -3, 3, 5)[kind] + breath * .6
    for n, weight in (('上半身', .45), ('上半身2', .55)):
        q = Quaternion((0, 1, 0), math.radians(yaw * weight)) @ Quaternion((0, 0, 1), math.radians(roll * weight)) @ Quaternion((1, 0, 0), math.radians(pitch * weight))
        world[n] = xyzw(q @ R[n])
    # A readable head silhouette matters at the four-person camera distance.
    head_roll = (1, -3, -8, 10)[kind] + wave * (1 if kind < 2 else 2)
    for n, weight in (('首', .4), ('頭', .6)):
        parent = bind.parent[n]
        inherited = Quaternion((world[parent][3], *world[parent][:3])) @ R[parent].inverted() if parent in world else Quaternion()
        q = Quaternion((0, 0, 1), math.radians(head_roll * weight)) @ Quaternion((1, 0, 0), math.radians((-3, -4, 3, 5)[kind] * weight))
        world[n] = xyzw(inherited @ q @ R[n])
    starts, _ = bind.evaluate(world=world)
    for side in ('L', 'R'):
        sign = -1 if side == 'L' else 1
        if kind == 0:
            wrist = target(-sign * .035, -.16 if side == 'R' else -.205, .17 if side == 'R' else .145)
            guide = target(sign * .255, -.245, .07)
            forward = Vector((-sign, .12, .1))
            across = Vector((0, 1, .05))
            fingers = 'cup'
        elif kind == 1 and side == 'R':
            wrist = target(.125, .015 + breath * .002, .095)
            guide = target(.31, -.15, .045)
            forward = Vector((-.72, .69, -.025))
            across = Vector((0, 0, -1))
            fingers = 'open'
        elif kind == 1:
            wrist = target(-.17, -.335, .025)
            guide = target(-.29, -.215, -.015)
            forward = Vector((.1, -.98, .05))
            across = Vector((1, .1, 0))
            fingers = 'cup'
        elif kind == 2:
            wrist = target(sign * .10, -.14 + breath * .003, .205)
            guide = target(sign * .225, -.26, .12)
            # Palms face inward/down, so finger flexion makes the heart's
            # upper notch in the image plane instead of a pointed triangle.
            forward = Vector((-sign * .62, .78, .05))
            across = Vector((-sign * .25, -.2, -1))
            fingers = 'cup'
        else:
            wrist = target(sign * (.132 + wave * .006), -.032 + breath * .006, .135)
            guide = target(sign * .225, -.27, .14)
            forward = Vector((-sign * .12, .99, 0))
            across = Vector((-sign, -sign * .12, 0))
            fingers = 'grip'
        hand_world = {}
        ctx['hand'](side, forward, across, hand_world)
        world.update({n: xyzw(q) for n, q in hand_world.items()})
        shoulder = Vector(starts['腕.' + side])
        reach = (P['ひじ.' + side] - P['腕.' + side]).length + (P['手首.' + side] - P['ひじ.' + side]).length
        if (wrist - shoulder).length > reach * .96:
            wrist = shoulder + (wrist - shoulder).normalized() * reach * .96
        penalty = None
        if kind == 0:
            expected = target(sign * .14, -.255, .08)
            penalty = lambda elbow, wrist, e=expected: ((Vector(elbow) - e).length / .025) ** 2
        solver.arm(side, tuple(wrist), tuple(guide), world, starts, pose_penalty=penalty)
        hand_world = {n: Quaternion((q[3], *q[:3])) for n, q in world.items()}
        ctx['fingers'](side, fingers, hand_world)
        if kind == 2:
            ctx['fingers'](side, 'cup', hand_world,
                overrides={digit: [58, 42, 18] if digit == '人指' else [62, 35, 12] for digit in ('人指', '中指', '薬指', '小指')})
            for joint, end, direction in zip(('親指０', '親指１', '親指２'), ('親指１', '親指２', '親指先'),
                    ((-sign * .85, -.4, .1), (-sign * .8, -.6, .15), (-sign * .7, -.72, .15))):
                ctx['setdir'](joint + '.' + side, end + '.' + side, Vector(direction), hand_world)
        world.update({n: xyzw(q) for n, q in hand_world.items()})
    return bind.evaluate(world=world)[1]


bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'ArtSource/DollSinger/SurvivalActions.blend'))
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
rig.name = 'DollSinger_Squad_Authoring'
rig.animation_data_clear()
for bone in rig.pose.bones:
    bone.matrix_basis = Matrix.Identity(4)
for obj in scene.objects:
    if obj.type == 'MESH':
        obj.animation_data_clear()
        is_body = any(m.type == 'ARMATURE' and m.object == rig for m in obj.modifiers)
        obj.hide_render = obj.hide_viewport = not is_body
        if obj.data.shape_keys:
            obj.data.shape_keys.animation_data_clear()
            for key in obj.data.shape_keys.key_blocks:
                key.value = 0
BR = {b.name: (rig.matrix_world @ b.matrix_local).to_quaternion() for b in rig.data.bones}
tracks = solver.torso_tracks + ['首', '頭'] + solver.arm_tracks
specs = []
rig.animation_data_create()
for kind, name in enumerate(('ArmsCrossed', 'Salute', 'Heart', 'Coax')):
    action = bpy.data.actions.new('Squad_' + name)
    action.use_fake_user = True
    rig.animation_data.action = action
    previous = {}
    for f in range(61):
        scene.frame_set(f + 1)
        rotations = pose(kind, f / 60)
        for n in tracks:
            bone = rig.pose.bones[n]
            parent = bind.parent[n]
            r = Quaternion((rotations[n][3], *rotations[n][:3]))
            target_rotation = (C.transposed() @ (r @ R[n].inverted()).to_matrix() @ C).to_quaternion() @ BR[n]
            if parent in rotations and parent in BR:
                p = Quaternion((rotations[parent][3], *rotations[parent][:3]))
                parent_rotation = (C.transposed() @ (p @ R[parent].inverted()).to_matrix() @ C).to_quaternion() @ BR[parent]
            else:
                parent_rotation = BR.get(parent, Quaternion())
            rest = (bone.parent.bone.matrix_local.inverted() @ bone.bone.matrix_local).to_quaternion() if bone.parent else bone.bone.matrix_local.to_quaternion()
            q = rest.inverted() @ parent_rotation.inverted() @ target_rotation
            if n in previous and q.dot(previous[n]) < 0:
                q.negate()
            previous[n] = q.copy()
            bone.rotation_mode = 'QUATERNION'
            bone.rotation_quaternion = q
            bone.keyframe_insert('rotation_quaternion', frame=f + 1, group=n)
    # Linear sampled rotations have an exact seamless first/last sample.
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    for key in curve.keyframe_points:
                        key.interpolation = 'LINEAR'
    specs.append(dict(name=name, action=action.name, frames=61, seconds=4))
    rig.animation_data.action = None
    track = rig.animation_data.nla_tracks.new()
    track.name = name
    strip = track.strips.new(name, 1 + kind * 90, action)
    track.mute = True
scene['SquadMotions'] = json.dumps(specs)
scene['SquadBones'] = json.dumps(tracks, ensure_ascii=False)
scene['SquadSourceNotes'] = '0 Arms crossed; 1 crisp salute; 2 two-hand heart; 3 cheek fists and head tilt. Runtime owns the planted idle legs.'
scene.render.fps = 15
scene.frame_start = 1
scene.frame_end = 61
rig.animation_data.action = bpy.data.actions[specs[0]['action']]
scene.frame_set(1)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
print('@@JSON@@' + json.dumps(dict(source=str(SOURCE), motions=specs, bones=len(tracks), geometryChanged=False)))
