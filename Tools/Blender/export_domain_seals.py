"""Read saved domain-seal Actions; splice six weapons into three exact rigs.

The seven other motions, including rifle/revolver/shotgun/sniper, must compare
equal before any install. Timing, burst count and aiming variants are preserved.
Default output is ignored staging data; --install explicitly installs it.
"""
import ast
import bpy
import copy
import hashlib
import json
import math
import random
import sys
from pathlib import Path
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Blender'))
from retarget_hatsune_miku import Rig, Retargeter, qmul, qinverse, add, sub, mul

SOURCE = ROOT / 'ArtSource/DollSinger/DomainSeals.blend'
STAGED = ROOT / 'LocalData/JujutsuGestureReview/DomainSeals/Staged'
OUTPUTS = {
    'DollSinger': ROOT / 'Assets/DollSinger/Animation/Blender/WeaponGestures.json',
    'HatsuneMiku': ROOT / 'Assets/HatsuneMiku/Animation/WeaponGestures.json',
    'Shian': ROOT / 'Assets/Shian/Animation/WeaponGestures.json',
}
original = {name: json.loads(path.read_text()) for name, path in OUTPUTS.items()}
bind = Rig(json.loads((ROOT / 'Tools/Blender/DollSingerBind.json').read_text()))
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
specs = json.loads(scene['DomainSealSpecs'])
clips = json.loads(scene['DomainSealClips'])
changed = {s['weapon'] for s in specs}
assert changed == {6, 7, 9, 10, 12, 13}
C = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
BR = {b.name: (rig.matrix_world @ b.matrix_local).to_quaternion() for b in rig.data.bones}
data = copy.deepcopy(original['DollSinger'])


def variant_at(library, weapon, pitch):
    motion = library['motions'][weapon - 1]
    return motion if pitch == 0 else next(p for p in motion['aimPoses'] if p['pitch'] == pitch)


for clip in clips:
    action = bpy.data.actions.get(clip['action'])
    if action is None:
        raise ValueError('Missing saved Action: ' + clip['action'])
    assert round(action.frame_range[1]) == clip['frames']
    rig.animation_data.action = action
    values = []
    for f in range(clip['frames']):
        scene.frame_set(f + 1)
        bpy.context.view_layer.update()
        world = {}
        for n in bind.order:
            if n not in rig.pose.bones:
                continue
            delta = (rig.matrix_world @ rig.pose.bones[n].matrix).to_quaternion() @ BR[n].inverted()
            q = (C @ delta.to_matrix() @ C.transposed()).to_quaternion()
            r = bind.r[n]
            q = q @ Quaternion((r[3], *r[:3]))
            world[n] = (q.x, q.y, q.z, q.w)
        for n in data['bones']:
            values.extend(round(v, 7) for v in qmul(qinverse(world[bind.parent[n]]), world[n]))
    variant = variant_at(data, clip['weapon'], clip['pitch'])
    if clip['burst'] >= 0:
        variant['burstFires'][clip['burst']]['values'] = values
    else:
        variant[clip['mode']] = values
        if clip['mode'] == 'fire' and variant.get('burstFires'):
            variant['burstFires'][0]['values'] = values.copy()
    print('READ_SAVED_DOMAIN', clip['action'], flush=True)


def pinch_context(target):
    env = dict(Vector=Vector, Quaternion=Quaternion, Matrix=Matrix, math=math, random=random,
        P={n: Vector(p) for n, p in target.p.items()},
        R={n: Quaternion((q[3], *q[:3])) for n, q in target.r.items()}, parents=target.parent)
    for file, names in (
        ('weapon_gestures.py', ('norm', 'frame', 'rotbetween', 'position', 'setdir')),
        ('domain_seals.py', ('axes', 'thumb_parameters', 'fit_pinch'))):
        tree = ast.parse((ROOT / 'Tools/Blender' / file).read_text())
        tree = ast.Module(body=[n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name in names], type_ignores=[])
        exec(compile(tree, file + '_readonly_helpers', 'exec'), env)
    return env


class DomainRetargeter(Retargeter):
    def __init__(self, source, target):
        super().__init__(source, target)
        self.weapon = 0
        self.pinch = None
        self.pinch_env = pinch_context(target)

    def contact_wrist_targets(self, source_p, world, wrists, sniper):
        if self.weapon == 9:
            G = {n: Quaternion((q[3], *q[:3])) for n, q in world.items()}
            if self.pinch is None:
                self.pinch = self.pinch_env['fit_pinch']('R', G)
            self.pinch_env['thumb_parameters']('R', G, self.pinch)
            for joint in ('親指０.R', '親指１.R', '親指２.R'):
                q = G[joint]
                world[joint] = (q.x, q.y, q.z, q.w)
        pairs = [('中指先.L', '中指先.R'), ('薬指先.L', '薬指先.R')] if self.weapon == 7 else []
        if self.weapon == 13:
            pairs = [('中指先.L', '中指先.R')]
        if not pairs:
            return wrists, []
        tips = self.relative_tips(world)
        desired = [(left, right, mul(sub(source_p[left], source_p[right]), self.scale), 1.) for left, right in pairs]
        candidates = [sub(gap, sub(tips[left], tips[right])) for left, right, gap, _ in desired]
        separation = mul(tuple(sum(v[j] for v in candidates) for j in range(3)), 1 / len(candidates))
        center = mul(add(wrists['L'], wrists['R']), .5)
        return {'L': add(center, mul(separation, .5)), 'R': sub(center, mul(separation, .5))}, desired


results = {'DollSinger': data}
reports = []
for name in ('HatsuneMiku', 'Shian'):
    target = Rig(json.loads((ROOT / ('Tools/Blender/' + name + 'Bind.json')).read_text()))
    solver = DomainRetargeter(bind, target)
    result = copy.deepcopy(original[name])
    assert result['bones'] == solver.arm_tracks
    for weapon in sorted(changed):
        solver.weapon = weapon
        result['motions'][weapon - 1] = solver.firearm_motion(data['bones'], data['motions'][weapon - 1])
        print('RETARGET_DOMAIN', name, weapon, flush=True)
    results[name] = result
    reports.append(dict(rig=name, **solver.stats))

STAGED.mkdir(parents=True, exist_ok=True)
source_sha = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
for name, result in results.items():
    before = original[name]
    protected = []
    for current, previous in zip(result['motions'], before['motions']):
        if current['weapon'] not in changed:
            assert current == previous, (name, 'Protected motion changed', current['weapon'])
            protected.append(current['weapon'])
            continue
        a = [current] + current.get('aimPoses', [])
        b = [previous] + previous.get('aimPoses', [])
        assert len(a) == len(b)
        for now, old in zip(a, b):
            for key in old:
                if key not in ('pose', 'fire', 'reload', 'aimPoses', 'burstFires'):
                    assert now[key] == old[key], (name, current['weapon'], key)
            for mode in ('pose', 'fire', 'reload'):
                assert len(now[mode]) == len(old[mode])
                assert all(math.isfinite(v) for v in now[mode])
            assert len(now.get('burstFires', [])) == len(old.get('burstFires', []))
    result['revision'] = hashlib.sha256(json.dumps(result['motions'], separators=(',', ':')).encode()).hexdigest()[:12]
    result['domainSealSource'] = dict(source=str(SOURCE.relative_to(ROOT)), sha256=source_sha, weapons=sorted(changed))
    path = STAGED / (name + '.json')
    path.write_text(json.dumps(result, ensure_ascii=False, separators=(',', ':')))
    reports.append(dict(rig=name, revision=result['revision'], protectedMotionIds=protected, bytes=path.stat().st_size))
    if '--install' in sys.argv:
        OUTPUTS[name].write_bytes(path.read_bytes())
report = dict(source=str(SOURCE.relative_to(ROOT)), sourceSha256=source_sha, changedMotionIds=sorted(changed),
    protectedMotionIds=[1, 2, 3, 4, 5, 8, 11], clips=len(clips), installed='--install' in sys.argv, rigs=reports)
out = ROOT / 'SourceData/Animations/DomainSeals/Export.json'
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
print('@@JSON@@' + json.dumps(report))
