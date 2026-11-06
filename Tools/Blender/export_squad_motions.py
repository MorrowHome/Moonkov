"""Read the saved squad actions and bake exact local tracks for all three rigs."""
import bpy
import hashlib
import json
import math
import sys
from pathlib import Path
from mathutils import Matrix, Quaternion

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Blender'))
from retarget_hatsune_miku import Rig, Retargeter, qmul, qinverse, qangle

SOURCE = ROOT / 'ArtSource/DollSinger/SquadMotions.blend'
OUT = ROOT / 'Assets/Resources/Moonkov/SquadMotions'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
bind = Rig(json.loads((ROOT / 'Tools/Blender/DollSingerBind.json').read_text()))
tracks = json.loads(scene['SquadBones'])
specs = json.loads(scene['SquadMotions'])
C = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
BR = {b.name: (rig.matrix_world @ b.matrix_local).to_quaternion() for b in rig.data.bones}
motions = []
for spec in specs:
    rig.animation_data.action = bpy.data.actions[spec['action']]
    values = []
    for f in range(spec['frames']):
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
        for n in tracks:
            values.extend(round(v, 7) for v in qmul(qinverse(world[bind.parent[n]]), world[n]))
    motions.append(dict(name=spec['name'], frames=spec['frames'], seconds=spec['seconds'], rotations=values))

source_hash = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
reports = []
for name in ('DollSinger', 'HatsuneMiku', 'Shian'):
    target = Rig(json.loads((ROOT / ('Tools/Blender/' + name + 'Bind.json')).read_text()))
    solver = Retargeter(bind, target)
    owned = solver.torso_tracks + ['首', '頭'] + solver.arm_tracks
    baked = []
    for motion in motions:
        values = []
        for f in range(motion['frames']):
            if name == 'DollSinger':
                start = f * len(tracks) * 4
                local = {n: tuple(motion['rotations'][start + i * 4:start + i * 4 + 4]) for i, n in enumerate(tracks)}
            else:
                arms = solver.sample(tracks, motion['rotations'], f, melee=True)
                local = {n: tuple(arms[i * 4:i * 4 + 4]) for i, n in enumerate(solver.torso_tracks + solver.arm_tracks)}
                start = f * len(tracks) * 4
                source_local = {n: tuple(motion['rotations'][start + i * 4:start + i * 4 + 4]) for i, n in enumerate(tracks)}
                _, source_world = bind.evaluate(local=source_local)
                _, target_world = target.evaluate(local=local)
                for n in ('首', '頭'):
                    q = solver.bind_delta(n, source_world)
                    local[n] = qmul(qinverse(target_world[target.parent[n]]), q)
                    target_world[n] = q
            for n in owned:
                values.extend(round(v, 7) for v in local[n])
        baked.append(dict(motion, rotations=values))
    seam = 0
    for motion in baked:
        stride = len(owned) * 4
        assert len(motion['rotations']) == motion['frames'] * stride
        assert all(math.isfinite(v) for v in motion['rotations'])
        for i in range(len(owned)):
            a = tuple(motion['rotations'][i * 4:i * 4 + 4])
            j = (motion['frames'] - 1) * stride + i * 4
            b = tuple(motion['rotations'][j:j + 4])
            seam = max(seam, qangle(qmul(a, qinverse(b))))
    assert seam < .1, (name, 'loop seam', seam)
    data = dict(version=1, rig=name, source=str(SOURCE.relative_to(ROOT)), sourceSha256=source_hash, bones=owned, motions=baked)
    path = OUT / (name + '.json')
    path.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    reports.append(dict(rig=name, bones=len(owned), bytes=path.stat().st_size, maximumLoopSeamDegrees=seam, **solver.stats))
report = dict(source=str(SOURCE.relative_to(ROOT)), sourceSha256=source_hash, motions=[m['name'] for m in motions], rigs=reports)
report_path = ROOT / 'SourceData/Animations/SquadMotions/Export.json'
report_path.parent.mkdir(parents=True, exist_ok=True)
report_path.write_text(json.dumps(report, indent=2) + '\n')
print('@@JSON@@' + json.dumps(report))
