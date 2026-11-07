"""Author six domain hand seals on a separate copy of the accepted arm rig.

Only this explicit authoring command rebuilds the source. The saved-source
exporter is separate. No production JSON or accepted .blend is written here.
"""
import bpy
import hashlib
import json
import math
import random
import sys
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/DollSinger/DomainSeals.blend'
REVIEW = ROOT / 'LocalData/JujutsuGestureReview/DomainSeals'
REVIEW.mkdir(parents=True, exist_ok=True)
protected = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in (
    ROOT / 'ArtSource/DollSinger/WeaponGestures.blend',
    ROOT / 'ArtSource/DollSinger/HaloExpansion.blend')}
prefix = (ROOT / 'Tools/Blender/weapon_gestures.py').read_text().split('motions=[]')[0]
prefix = prefix.replace('/ArtSource/DollSinger/WeaponGestures.blend', '/ArtSource/DollSinger/HaloExpansion.blend')
prefix = prefix.replace('limit=-30 if weapon==1 else -60', 'limit=-90 if weapon>=5 else (-30 if weapon==1 else -60)')
prefix = prefix.replace("if kind=='open':spread={'人指':-8,'中指':0,'薬指':7,'小指':13}[digit]", "if kind=='open':spread={'人指':-8,'中指':0,'薬指':7,'小指':13}[digit]*(1 if side=='L' else -1)")
prefix = prefix.replace('score+=max(0,.115-abs(elbow.x))*200+max(0,elbow.y-1.31)*200', 'score+=max(0,.115-abs(elbow.x))*200+max(0,elbow.y-wrist.y-.015)*500')
if '--export-only' not in sys.argv:
    sys.argv.append('--export-only')
exec(compile(prefix, 'accepted_exact_rig_helpers', 'exec'), globals())
ROOT = Path(ROOT)
SOURCE = ROOT / 'ArtSource/DollSinger/DomainSeals.blend'
EXPORT_ONLY = False
DIGITS = ('人指', '中指', '薬指', '小指')
SPECS = {
    6: ('Trine_ThreefoldSeal', 'Gojo', '无量空处'),
    7: ('Aegis_AnchorPalms', 'Sukuna', '伏魔御厨子'),
    9: ('Thorn_FiveNeedles', 'Hakari', '坐杀博徒'),
    10: ('Sundown_SolarRelease', 'Kenjaku', '胎藏遍野'),
    12: ('Tempest_VortexPress', 'Megumi', '嵌合暗翳庭'),
    13: ('Nocturne_NightSeal', 'Yuji', '虎杖领域手印'),
}


def axes(side, G):
    h = '手首.' + side
    delta = G[h] @ R[h].inverted()
    f = norm(delta @ (P['中指１.' + side] - P[h]))
    x = delta @ (P['人指１.' + side] - P['小指１.' + side])
    x = norm(x - f * x.dot(f))
    return f, x, norm(x.cross(f)) * (1 if side == 'L' else -1)


def digit_directions(side, digit, directions, G):
    _, x, _ = axes(side, G)
    names = [digit + j + '.' + side for j in ('１', '２', '３', '先')]
    for n, end, direction in zip(names, names[1:], directions):
        G[n] = frame(direction, x) @ frame(P[end] - P[n], P['人指１.' + side] - P['小指１.' + side]).inverted() @ R[n]


def fold_thumb(side, G, lift=.7):
    f, x, palm = axes(side, G)
    directions = (norm(x * .6 + f * lift + palm * .40),
        norm(-x * .95 + f * .3 + palm * .05), norm(-x * .8 - f * .35 + palm * .05))
    for n, end, direction in zip(('親指０', '親指１', '親指２'), ('親指１', '親指２', '親指先'), directions):
        n += '.' + side
        end += '.' + side
        parent = parents[n]
        G[n] = G[parent] @ R[parent].inverted() @ R[n]
        setdir(n, end, direction, G)


def thumb_parameters(side, G, values):
    f, x, palm = axes(side, G)
    a, b, c, d, e, g = values
    directions = (norm(x * a + f * b + palm), norm(-x + f * c + palm * d), norm(-x + f * e + palm * g))
    for n, end, direction in zip(('親指０', '親指１', '親指２'), ('親指１', '親指２', '親指先'), directions):
        n += '.' + side
        end += '.' + side
        parent = parents[n]
        G[n] = G[parent] @ R[parent].inverted() @ R[n]
        setdir(n, end, direction, G)


def fit_pinch(side, G):
    tip = position('人指先.' + side, G)
    root = position('親指０.' + side, G)
    # The delivered thumb end marker extends about 2.5 mm beyond its skin.
    target = tip - norm(root - tip) * .0025
    bounds = ((-.5, 1), (.05, 1.7), (-.6, 1.7), (-.1, 1.8), (-.8, 1.6), (-.1, 1.6))
    def score(values):
        thumb_parameters(side, G, values)
        points = [position(n + '.' + side, G) for n in ('親指０', '親指１', '親指２', '親指先')]
        angles = [(points[i + 1] - points[i]).angle(points[i + 2] - points[i + 1]) for i in (0, 1)]
        return (points[-1] - target).length_squared + sum(max(0, a - math.radians(80)) ** 2 * .002 for a in angles)
    rng = random.Random(1707)
    best = [.1, .6, .15, .55, 0, .4]
    value = score(best)
    for _ in range(350):
        v = [rng.uniform(lo, hi) for lo, hi in bounds]
        s = score(v)
        if s < value:
            best, value = v, s
    for step in (.3, .15, .07, .03, .01, .003):
        for _ in range(4):
            for i in range(6):
                for sign in (-1, 1):
                    v = best.copy()
                    v[i] = max(bounds[i][0], min(bounds[i][1], v[i] + step * sign))
                    s = score(v)
                    if s < value:
                        best, value = v, s
    thumb_parameters(side, G, best)
    return best


def pair_at(center, G, pairs):
    separation = Vector()
    for left, right in pairs:
        l = position(left + '.L', G) - position('手首.L', G)
        r = position(right + '.R', G) - position('手首.R', G)
        separation += r - l
    separation /= len(pairs)
    separation.x -= .006
    return dict(L=Vector(center) + separation * .5, R=Vector(center) - separation * .5)


pinch = None


def make_pose(weapon, phase=0, mode='pose', fan_scale=1):
    global pinch
    G = {}
    kick = bell(phase, .28) if mode == 'fire' else 0
    gather = math.sin(math.pi * phase) if mode == 'reload' else 0
    if weapon == 6:
        hand('R', (0, 1, .08), (-1, 0, 0), G)
        fingers('R', 'grip', G, overrides={'人指': [0, 0, 0], '中指': [8, 25, 50], '薬指': [90, 85, 45], '小指': [90, 85, 45]})
        f, x, p = axes('R', G)
        digit_directions('R', '中指', (norm(f * .8 + x * .17 + p * .63), norm(f * .92 + x * .42 + p * .10), norm(f * .5 + x * .7 - p * .35)), G)
        fold_thumb('R', G, .4)
        hand('L', (.9, .3, .15), (0, 1, -.3), G)
        fingers('L', 'cup', G)
        wrists = dict(R=Vector((.100, 1.115, .245)), L=Vector((-.100, 1.06, .235)))
    elif weapon == 9:
        hand('R', (0, 1, .08), (-1, 0, 0), G)
        fingers('R', 'open', G, overrides={'人指': [15, 90, 65], '中指': [2, 0, 0], '薬指': [2, 0, 0], '小指': [2, 0, 0]})
        if pinch is None:
            pinch = fit_pinch('R', G)
        thumb_parameters('R', G, pinch)
        hand('L', (.85, .15, .08), (0, 1, -.1), G)
        fingers('L', 'open', G)
        wrists = dict(R=Vector((.09, 1.115, .245)), L=Vector((-.12, 1.075, .235)))
    elif weapon == 7:
        for side, sign in (('R', 1), ('L', -1)):
            hand(side, (-sign * .22, 1, 0), (0, 0, -1), G)
            fingers(side, 'grip', G, overrides={'人指': [90, 85, 30], '中指': [15, 0, 0], '薬指': [20, 0, 0], '小指': [90, 85, 35]})
            fold_thumb(side, G)
        wrists = pair_at((0, 1.08, .252), G, [('中指先', '中指先'), ('薬指先', '薬指先')])
    elif weapon == 10:
        for side, sign in (('R', 1), ('L', -1)):
            hand(side, (-sign * .03, 1, 0), (0, 0, 1), G)
            fingers(side, 'open', G, overrides={d: [0, 0, 0] for d in DIGITS})
        wrists = dict(R=Vector((.014, 1.08, .252)), L=Vector((-.014, 1.08, .252)))
    elif weapon == 12:
        for side, sign in (('R', 1), ('L', -1)):
            hand(side, (-sign * .25, 1, 0), (0, 0, -1), G)
            fingers(side, 'grip', G, overrides={'人指': [15, 65, 10], '中指': [75, 10, 5], '薬指': [90, 85, 45], '小指': [90, 85, 45]})
            fold_thumb(side, G)
            f, _, _ = axes(side, G)
            opposition = Quaternion(f, math.radians(-30 if side == 'R' else 30))
            for n in list(G):
                if n.startswith('親指') and n.endswith('.' + side):
                    G[n] = opposition @ G[n]
        wrists = dict(R=Vector((.058, 1.075, .26)), L=Vector((-.058, 1.058, .26)))
    else:
        for side, sign in (('R', 1), ('L', -1)):
            hand(side, (-sign * .3, 1, 0), (0, 0, -1), G)
            fingers(side, 'grip', G, overrides={'人指': [90, 85, 45], '中指': [0, 0, 0], '薬指': [90, 85, 45], '小指': [90, 85, 45]})
            fold_thumb(side, G)
        wrists = dict(R=Vector((.048, 1.08, .266)), L=Vector((-.048, 1.08, .254)))
    # Fire keeps the identifying finger connections. Move the pair as one unit.
    for side in ('L', 'R'):
        # Use the actual gameplay eye/FOV, rather than the front-review camera.
        # The interlocked Megumi fingers sit lower than the upright signs.
        height = .11 if weapon == 12 else .085 if weapon == 7 else .08
        wrists[side] += Vector((0, height - .010 * gather, .035 - .009 * kick - .012 * gather))
        arm(side, wrists[side], (-.18 if side == 'L' else .18, .88, .11), G, maxroll=100)
    for n in order:
        if n not in G:
            p = parents[n]
            G[n] = G[p] @ R[p].inverted() @ R[n] if p in G else R[n]
    return G


scene = bpy.context.scene
specs = []
skeletons = {}
for weapon, (name, character, seal) in SPECS.items():
    action = bpy.data.actions.new('Domain_' + character + '_pose')
    action.use_fake_user = True
    rig.animation_data.action = action
    scene.frame_set(1)
    G = make_pose(weapon)
    skeletons[character] = {n: list(position(n, G)) for n in order if n.startswith(DIGITS + ('親指', '手首'))}
    install(G)
    for n in tracks:
        rig.pose.bones[n].keyframe_insert('rotation_quaternion', frame=1, group=n)
    specs.append(dict(weapon=weapon, name=name, character=character, seal=seal, action=action.name))
scene['DomainSealSpecs'] = json.dumps(specs, ensure_ascii=False)
(REVIEW / 'DraftSkeleton.json').write_text(json.dumps(skeletons, ensure_ascii=False, indent=2))
rig.animation_data.action = bpy.data.actions['Domain_Sukuna_pose']
scene.frame_set(1)
bpy.context.preferences.filepaths.save_version = 0
draft = REVIEW / 'DomainPoseDraft.blend'
clips = []
if '--author-actions' in sys.argv:
    library = json.loads((ROOT / 'Assets/DollSinger/Animation/Blender/WeaponGestures.json').read_text())
    for spec in specs:
        motion = library['motions'][spec['weapon'] - 1]
        variants = [motion] + motion.get('aimPoses', [])
        for variant in variants:
            pitch = variant.get('pitch', 0)
            prefix = 'Domain_' + spec['character'] + ('_Pitch' + str(pitch) if pitch else '')
            modes = [('pose', motion['poseFrames'], -1), ('fire', motion['fireFrames'], -1), ('reload', motion['reloadFrames'], -1)]
            modes += [('fire', motion['fireFrames'], i) for i in range(1, len(variant.get('burstFires', [])))]
            for mode, count, burst in modes:
                name = prefix + '_' + mode + (str(burst) if burst >= 0 else '')
                action = bpy.data.actions.get(name) or bpy.data.actions.new(name)
                action.use_fake_user = True
                rig.animation_data.action = action
                previous = {}
                for f in range(count):
                    scene.frame_set(f + 1)
                    phase = f / max(1, count - 1)
                    G = make_pitched_pose(spec['weapon'], phase, mode, pitch) if pitch else make_pose(spec['weapon'], phase, mode)
                    install(G)
                    for n in tracks:
                        bone = rig.pose.bones[n]
                        q = bone.rotation_quaternion.copy()
                        if n in previous and q.dot(previous[n]) < 0:
                            q.negate()
                        previous[n] = q.copy()
                        bone.rotation_quaternion = q
                        bone.keyframe_insert('rotation_quaternion', frame=f + 1, group=n)
                for layer in action.layers:
                    for strip in layer.strips:
                        for bag in strip.channelbags:
                            for curve in bag.fcurves:
                                for key in curve.keyframe_points:
                                    key.interpolation = 'LINEAR'
                clips.append(dict(weapon=spec['weapon'], pitch=pitch, mode=mode, burst=burst, frames=count, action=name))
        print('AUTHORED_DOMAIN', spec['character'], flush=True)
    # The previous versions remain in HaloExpansion.blend. Keep only the
    # revised versions in this separate editable source, plus protected Actions.
    for action in list(bpy.data.actions):
        if any(action.name.startswith(name + '_') for name, _, _ in SPECS.values()):
            bpy.data.actions.remove(action)
    scene['DomainSealClips'] = json.dumps(clips)
    scene['DomainProtectedMotionIds'] = json.dumps([1, 2, 3, 4, 5, 8, 11])
    rig.animation_data.action = bpy.data.actions['Domain_Sukuna_pose']
    scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
else:
    bpy.ops.wm.save_as_mainfile(filepath=str(draft))
assert all(hashlib.sha256(p.read_bytes()).hexdigest() == sha for p, sha in protected.items())
print('@@JSON@@' + json.dumps(dict(source=str(SOURCE) if clips else str(draft), specs=specs, clips=len(clips), productionUnchanged=True)))
