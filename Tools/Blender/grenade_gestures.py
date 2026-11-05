"""Author compact grenade grips and aim/release poses in a separate Blender source.

Uses the production skeletons and established anatomical hand helpers. No mesh,
source survival action, camera setting or gameplay rule is changed.
Run with the local Blender in background mode; exports exact Unity bone rotations.
"""
import ast
import bpy
import hashlib
import json
import math
import sys
from pathlib import Path
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Blender'))
from retarget_hatsune_miku import Rig, Retargeter, qmul, qinverse, qslerp

OUT = ROOT / 'SourceData/Animations/Grenades'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = ROOT / 'ArtSource/DollSinger/GrenadeGestures.blend'
C = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))

def readq(q): return Quaternion((q[3], q[0], q[1], q[2]))
def tupleq(q): return (q.x, q.y, q.z, q.w)
def smooth(t):
    t = max(0, min(1, t))
    return t*t*(3-2*t)

helpers = ast.parse((ROOT / 'Tools/Blender/weapon_gestures.py').read_text())
helpers = ast.Module(body=[n for n in helpers.body if isinstance(n, ast.FunctionDef)
    and n.name in ('norm', 'frame', 'rotbetween', 'position', 'setdir', 'hand', 'fingers')], type_ignores=[])

def author(name):
    bind = Rig(json.loads((ROOT / ('Tools/Blender/'+name+'Bind.json')).read_text()))
    solver = Retargeter(bind, bind)
    P = {n: Vector(p) for n, p in bind.p.items()}
    R = {n: readq(q) for n, q in bind.r.items()}
    context = dict(Vector=Vector, Quaternion=Quaternion, Matrix=Matrix, math=math, P=P, R=R, parents=bind.parent)
    exec(compile(helpers, 'accepted_hand_axes', 'exec'), context)
    scale = (P['頭'].y-P['足首.R'].y)/1.17
    chest, head = P['上半身2'], P['頭']
    def target(x, head_offset, z): return Vector((x*scale, head.y+head_offset*scale, z*scale))
    holding = target(.175, -.055, .24)
    aiming = target(.255, .005, .035)
    release = target(.13, -.07, .285)
    low_back = target(.18, -.32, .13)
    low_release = target(.14, -.22, .245)
    def pose(wrist, opening=0, aiming_weight=0, right_weight=0):
        shoulder=P['腕.R'];reach=(P['ひじ.R']-shoulder).length+(P['手首.R']-P['ひじ.R']).length
        if (wrist-shoulder).length>reach*.97:wrist=shoulder+(wrist-shoulder).normalized()*reach*.97
        world = {}
        # Thumb up, palm inward. The wrist follows the forearm rather than bending back.
        right_forward=Vector((-.08,.28,1)).lerp(Vector((-.05,.9,.43)),right_weight).normalized()
        context['hand']('R', right_forward, Vector((0,1,-.28)).lerp(Vector((1,.05,0)),right_weight), world)
        context['hand']('L', Vector((0,-.95,.31)).lerp(Vector((0,.02,1)),aiming_weight),
                        Vector((-1,0,0)).lerp(Vector((0,1,0)),aiming_weight), world)
        world = {n: tupleq(q) for n, q in world.items()}
        solver.arm('R', tuple(wrist), tuple(target(.30, -.19, .04)), world, bind.p, anatomical_wrist_limit=55)
        left = Vector((-.17*scale, chest.y-.17*scale, .08*scale)).lerp(target(-.125,-.08,.285),aiming_weight)
        solver.arm('L', tuple(left), tuple(Vector((-.24*scale, chest.y-.04*scale, .12*scale))), world, bind.p, anatomical_wrist_limit=55)
        closed = {n: readq(q) if not isinstance(q, Quaternion) else q for n, q in world.items()}
        context['fingers']('R', 'grip', closed, overrides={d: [38, 68, 50] for d in ('人指', '中指', '薬指', '小指')})
        context['fingers']('L', 'cup', closed)
        left_aim=closed.copy();context['fingers']('L','gun',left_aim,
            overrides={d:[30,55,40] for d in ('中指','薬指','小指')})
        left_delta=left_aim['手首.L']@R['手首.L'].inverted()
        left_forward=(left_delta@(P['中指１.L']-P['手首.L'])).normalized()
        left_across=(left_delta@(P['人指１.L']-P['小指１.L'])).normalized()
        left_palm=left_across.cross(left_forward).normalized()
        for joint,end,direction in zip(('親指０.L','親指１.L','親指２.L'),('親指１.L','親指２.L','親指先.L'),
            (left_forward*.5+left_palm*.6-left_across*.2,left_palm*.6-left_across*.6,left_forward*.2-left_across*.8)):
            context['setdir'](joint,end,direction.normalized(),left_aim)
        for n,q in closed.items():
            if n.endswith('.L') and n.startswith(('人指','中指','薬指','小指','親指')):closed[n]=q.slerp(left_aim[n],aiming_weight)
        delta=closed['手首.R']@R['手首.R'].inverted()
        forward=(delta@(P['中指１.R']-P['手首.R'])).normalized()
        across=(delta@(P['人指１.R']-P['小指１.R'])).normalized()
        palm=-across.cross(forward).normalized()
        for joint,end,direction in zip(('親指０.R','親指１.R','親指２.R'),('親指１.R','親指２.R','親指先.R'),
                                      (forward*.7+across*.3+palm*.5,forward*.3+palm*.8,palm-across*.2)):
            context['setdir'](joint,end,direction.normalized(),closed)
        opened = closed.copy()
        context['fingers']('R', 'open', opened)
        for n, q in closed.items():
            if n.endswith('.R') and n.startswith(('人指', '中指', '薬指', '小指', '親指')):
                q = q.slerp(opened.get(n, q), opening)
            world[n] = tupleq(q)
        points, rotations = bind.evaluate(world=world)
        return points, rotations
    motions = {}
    poses = {}
    for kind, frames in (('hold', 2), ('prime', 34), ('overhand', 61), ('underhand', 61)):
        samples = []
        poses[kind] = []
        for f in range(frames):
            t = f/(frames-1)
            if kind == 'hold': wrist = holding
            elif kind == 'prime': wrist = holding.lerp(aiming, smooth(t))
            elif kind == 'overhand':
                wrist = aiming.lerp(release, smooth(t/.55)) if t<=.55 else release.lerp(holding, smooth((t-.55)/.45))
            else:
                wrist = aiming.lerp(low_back, smooth(t/.24)) if t<.24 else low_back.lerp(low_release, smooth((t-.24)/.31)) if t<=.55 else low_release.lerp(holding, smooth((t-.55)/.45))
            opening = smooth((t-.53)/.12) if kind in ('overhand', 'underhand') else 0
            aiming_weight=0 if kind=='hold' else smooth(t) if kind=='prime' else 1-smooth((t-.55)/.45)
            # The empty left hand aims; the loaded right hand stays beside the shoulder.
            # Its wrist rolls forward with the throw, without a large overhead swing.
            right_weight=aiming_weight if kind in ('hold','prime') else 1-smooth(t/.55)
            points, rotations = pose(wrist, opening, aiming_weight, right_weight)
            poses[kind].append((points, rotations))
            for n in solver.arm_tracks:
                q = qmul(qinverse(rotations[bind.parent[n]]), rotations[n])
                samples.extend(round(v, 7) for v in q)
        motions[kind] = dict(frames=frames, rotations=samples)
    library = dict(rig=name, source='ArtSource/DollSinger/GrenadeGestures.blend / compact grip and aim',
                   release=.55, bones=solver.arm_tracks, **motions)
    (OUT / (name+'.json')).write_text(json.dumps(library, separators=(',', ':')))
    return bind, poses

results = {name: author(name) for name in ('DollSinger', 'HatsuneMiku', 'Shian')}
# A new authoring document. Never overwrite the reviewed survival source.
bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'ArtSource/DollSinger/SurvivalActions.blend'))
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
rig.name = 'DollSinger_Grenade_Authoring'
rig.animation_data_clear()
bind, poses = results['DollSinger']
BR = {b.name: (rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
R = {n: readq(q) for n, q in bind.r.items()}
for obj in scene.objects:
    if obj.get('source_part') or obj.name.startswith('Using_'):
        obj.animation_data_clear();obj.hide_render=True;obj.hide_viewport=True
with bpy.data.libraries.load(str(ROOT / 'SourceData/Models/PhysicalItems/energy_grenade.blend')) as (src, dst):
    dst.objects = ['energy_grenade']
prop = dst.objects[0];scene.collection.objects.link(prop);prop.name='Grenade_Grip_Reference'
prop.animation_data_clear()
center = sum((Vector(p) for p in prop.bound_box), Vector())/8
prop.rotation_mode='QUATERNION'
rig.animation_data_create();rig.animation_data.action=bpy.data.actions.new('Grenade_Grip_Aim_Release')
rig.animation_data.action['notes']='Right hand grips the grenade beside the shoulder. Empty left hand points along the aim. Compact release.'
frames = {}
for kind, start in (('hold', 1), ('prime', 21), ('overhand', 81), ('underhand', 161)):
    frames[kind]=[start,start+len(poses[kind])-1]
    scene.timeline_markers.new(kind, frame=start)
    for f, (points, rotations) in enumerate(poses[kind]):
        frame = start+f;scene.frame_set(frame)
        for n in bind.order:
            if n not in rig.pose.bones: continue
            bone=rig.pose.bones[n];parent=bind.parent[n]
            target=(C.transposed()@(readq(rotations[n])@R[n].inverted()).to_matrix()@C).to_quaternion()@BR[n]
            pt=(C.transposed()@(readq(rotations[parent])@R[parent].inverted()).to_matrix()@C).to_quaternion()@BR[parent] if parent in rotations and parent in BR else BR.get(parent,Quaternion())
            rest=(rig.data.bones[parent].matrix_local.inverted()@bone.bone.matrix_local).to_quaternion() if parent in rig.data.bones else bone.bone.matrix_local.to_quaternion()
            bone.rotation_mode='QUATERNION';bone.rotation_quaternion=rest.inverted()@pt.inverted()@target
            bone.keyframe_insert('rotation_quaternion',frame=frame,group=n)
        wrist=Vector(points['手首.R']);middle=Vector(points['中指１.R']);across=(Vector(points['人指１.R'])-Vector(points['小指１.R'])).normalized()
        forward=(middle-wrist).normalized();normal=-across.cross(forward).normalized()
        position=wrist.lerp(middle,.75)+normal*.043
        # Unity LookRotation(-normal, across), then coordinate conversion to Blender.
        z=-normal;y=across;x=y.cross(z).normalized();y=z.cross(x)
        rotation=Matrix(((x.x,y.x,z.x),(x.y,y.y,z.y),(x.z,y.z,z.z)))
        prop.rotation_quaternion=(C.transposed()@rotation@C).to_quaternion()
        prop.location=C.transposed()@position-prop.rotation_quaternion@center
        prop.hide_render=kind in ('overhand','underhand') and f/(len(poses[kind])-1)>=.55
        for channel in ('location','rotation_quaternion','hide_render'):prop.keyframe_insert(channel,frame=frame)
scene.frame_start=1;scene.frame_end=221;scene.render.fps=60;scene.frame_set(1)
scene['GrenadeMotionFrames']=json.dumps(frames)
scene['UnityRuntimeSource']='SourceData/Animations/Grenades'
for obj in scene.objects:
    if obj.type=='MESH' and any(m.type=='ARMATURE' and m.object==rig for m in obj.modifiers):obj.hide_render=False;obj.hide_viewport=False
bpy.context.view_layer.update()
# This file is newly authored; suppress .blend1 backups of our own iterations.
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
report=dict(blender=bpy.app.version_string, source=str(SOURCE.relative_to(ROOT)),
            sourceSha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),frames=frames,
            geometryChanged=False, rigs=list(results), outputs=[n+'.json' for n in results])
(OUT/'Authoring.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print('@@JSON@@'+json.dumps(report))
