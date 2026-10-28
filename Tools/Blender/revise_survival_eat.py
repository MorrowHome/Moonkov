"""Fit a real flat ration pouch, an opposed grip and a visible torn seal.

Authors the saved production Blender source. Never exports over Unity libraries.
"""
import ast,bpy,bmesh,json,math,sys
from pathlib import Path
from mathutils import Vector,Quaternion,Matrix
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'Tools/Blender'))
from retarget_hatsune_miku import Rig,Retargeter,frame as anatomical_frame,qslerp
SOURCE=ROOT/'ArtSource/DollSinger/SurvivalActions.blend';OUT=ROOT/'LocalData/Actions/Blender'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene
rig=next(o for o in scene.objects if o.type=='ARMATURE');body=next(o for o in scene.objects if o.type=='MESH' and len(o.vertex_groups)>100)
bind=Rig(json.loads((ROOT/'Tools/Blender/DollSingerBind.json').read_text()));solver=Retargeter(bind,bind)
library=json.loads((ROOT/'SourceData/Animations/Survival/AuthoringSeed.json').read_text());motion=next(m for m in library['motions'] if m['action']=='Eat' and 'ration' in m.get('items',[]))
calibration=next(r for r in json.loads((ROOT/'SourceData/Animations/Survival/Calibration.json').read_text())['rigs'] if r['rig']=='DollSinger')
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)));P={n:Vector(v) for n,v in bind.p.items()};R={n:Quaternion((q[3],q[0],q[1],q[2])) for n,q in bind.r.items()};BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
tree=ast.parse((ROOT/'Tools/Blender/revise_survival_drink.py').read_text());names=('tupleq','readq','sm','lerp','hand_rotation','arc_chain','thumb_on_bottle','install','make_chest_penalty','ready_pose')
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names],type_ignores=[]),'survival_authoring_helpers','exec'),globals())
pose_penalty=make_chest_penalty(body)
tree=ast.parse((ROOT/'Tools/Blender/weapon_gestures.py').read_text());helpers=ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in ('norm','frame','rotbetween','position','setdir','fingers')],type_ignores=[])
context={'Vector':Vector,'Quaternion':Quaternion,'Matrix':Matrix,'math':math,'P':P,'R':R,'parents':bind.parent};exec(compile(helpers,'accepted_aim_finger_helpers','exec'),context)
root=bpy.data.objects['Using_ration'];parts=[o for o in scene.objects if o.name.startswith('ration_') and o.get('source_part')];sealed=next(o for o in parts if o['source_part']=='Sealed');opened=next(o for o in parts if o['source_part']=='Opened')
tear=bpy.data.objects.get('ration_TearStrip')
if tear is None:
    mesh=sealed.data.copy();mesh.name='RationTornSeal';bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.000001,plane_co=C.transposed()@Vector((0,.172,0)),plane_no=C.transposed()@Vector((0,1,0)),clear_inner=True,clear_outer=False)
    edges=[e for e in bm.edges if e.is_boundary];bmesh.ops.holes_fill(bm,edges=edges,sides=0);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free();mesh.update()
    tear=bpy.data.objects.new('ration_TearStrip',mesh);root.users_collection[0].objects.link(tear);tear['source_part']='TearStrip';parts.append(tear)
for obj in [root]+parts:obj.animation_data_clear()
tear.parent=None
for obj in parts:
    if obj.data.get('soft_pouch_v1') or obj.data.get('flexible_film_v2'):continue
    bm=bmesh.new();bm.from_mesh(obj.data)
    if obj==opened:
        cap_faces=[face for face in bm.faces if all(abs((C@v.co).y-.172)<.00002 for v in face.verts)]
        bmesh.ops.delete(bm,geom=cap_faces,context='FACES')
    for vertex in bm.verts:
        p=C@vertex.co
        width=max(0,math.sin(math.pi*max(0,min(1,(p.x+.06)/.12))))**.5
        height=max(0,math.sin(math.pi*max(0,min(1,p.y/.19))))**.3
        p.z*=.15+.85*width*height;vertex.co=C.transposed()@p
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free();obj.data.update();obj.data['soft_pouch_v1']=True
for track in rig.animation_data.nla_tracks:track.mute=True
rig.animation_data.action=bpy.data.actions.new('Survival_Eat_PhysicalPouch');action=rig.animation_data.action;action.use_fake_user=True
chest=P['上半身2'];mouth=Vector(calibration['mouth']);previous={};report=[];grip_local=Vector((.045,.115,0));seal_grip=Vector((-.046,.181,0))
for f in range(181):
    phase=f/180;scene.frame_set(f+1);intro=sm(0,.10,phase);outro=sm(.84,1,phase);active=intro*(1-outro);raise_weight=sm(.30,.45,phase)*(1-sm(.64,.78,phase))
    source_frame=phase*(motion['frames']-1);a=int(source_frame);b=min(a+1,motion['frames']-1);stride=len(library['bones'])*4
    local={n:qslerp(bind.local[n],qslerp(tuple(motion['rotations'][a*stride+i*4:a*stride+i*4+4]),tuple(motion['rotations'][b*stride+i*4:b*stride+i*4+4]),source_frame-a),active) for i,n in enumerate(library['bones'])}
    for n in local:
        if n.startswith('肩') or n.endswith(('先.L','先.R')):local[n]=bind.local[n]
    starts,source_rotations=bind.evaluate(local=local)
    up=lerp((0,1,0),(0,.94,-.341175),raise_weight).normalized();yaw=math.radians(45-30*raise_weight);x=Vector((math.cos(yaw),0,-math.sin(yaw)));x=(x-up*x.dot(up)).normalized();front=x.cross(up).normalized();normal=-front;forward=-x
    rotation=Matrix(((x.x,up.x,front.x),(x.y,up.y,front.y),(x.z,up.z,front.z))).to_quaternion()
    low_top=Vector((-.005,chest.y+.12,.28));top=low_top.lerp(mouth+Vector((0,0,.004)),raise_weight)
    root_position=top-rotation@Vector((0,.172,0));grip=root_position+rotation@grip_local
    right_hand=hand_rotation('R',forward,up).slerp(ready_pose('R')[1],1-intro).slerp(ready_pose('R')[1],outro);right_delta=right_hand@R['手首.R'].inverted()
    right_wrist=grip-right_delta@(P['中指１.R']-P['手首.R'])*.95-normal*.028
    right_wrist=right_wrist.lerp(ready_pose('R')[0],1-intro).lerp(ready_pose('R')[0],outro)
    if intro<1 or outro>0:
        up=(right_delta@(P['人指１.R']-P['小指１.R'])).normalized();forward=(right_delta@(P['中指１.R']-P['手首.R'])).normalized();normal=-up.cross(forward).normalized();x=-forward
        rotation=Matrix(((x.x,up.x,-normal.x),(x.y,up.y,-normal.y),(x.z,up.z,-normal.z))).to_quaternion();root_position=right_wrist+right_delta@(P['中指１.R']-P['手首.R'])*.95+normal*.028-rotation@grip_local
    torn=sm(.18,.30,phase);lower_left=sm(.32,.46,phase)
    base_rotation=Quaternion(Vector((0,1,0)),math.radians(45));strip_rotation=base_rotation.slerp(Quaternion(Vector((0,0,1)),math.radians(15)),torn)
    initial_root=low_top-Vector((0,.172,0));strip_position=initial_root+Vector((-.11*torn,.032*torn-.095*lower_left,.012*torn))
    if phase<.18:strip_position=root_position;strip_rotation=rotation
    strip_point=strip_position+strip_rotation@seal_grip
    left_forward=strip_rotation@Vector((0,0,1));left_across=strip_rotation@Vector((0,1,0));left_normal=left_across.cross(left_forward).normalized()
    left_hand=hand_rotation('L',left_forward,left_across).slerp(ready_pose('L')[1],1-intro).slerp(ready_pose('L')[1],outro);left_delta=left_hand@R['手首.L'].inverted()
    left_wrist=strip_point-left_delta@(P['人指１.L']-P['手首.L'])-left_normal*.027
    left_wrist=left_wrist.lerp(ready_pose('L')[0],1-intro).lerp(ready_pose('L')[0],outro)
    if outro>0:
        left_across=(left_delta@(P['人指１.L']-P['小指１.L'])).normalized();left_forward=(left_delta@(P['中指１.L']-P['手首.L'])).normalized();left_normal=left_across.cross(left_forward).normalized()
        strip_rotation=Matrix(((left_normal.x,left_across.x,left_forward.x),(left_normal.y,left_across.y,left_forward.y),(left_normal.z,left_across.z,left_forward.z))).to_quaternion();strip_position=left_wrist+left_delta@(P['人指１.L']-P['手首.L'])+left_normal*.027-strip_rotation@seal_grip
    world={'手首.L':tupleq(left_hand),'手首.R':tupleq(right_hand)}
    for side,wrist,guide in [('R',right_wrist,Vector((.24,chest.y-.06,.12))),('L',left_wrist,Vector((-.24,chest.y-.06,.12)))]:
        solver.arm(side,tuple(wrist),tuple(guide.lerp(P['ひじ.'+side],outro)),world,starts,previous=previous.get(side),anatomical_wrist_limit=50,elbow_max_y=P['腕.'+side].y-.015,pose_penalty=pose_penalty)
    cup={n:readq(q) for n,q in source_rotations.items()};cup['手首.R']=right_hand;cup['手首.L']=left_hand
    context['fingers']('R','grip',cup,overrides={d:[0,2,3] for d in ['人指','中指','薬指','小指']})
    context['fingers']('L','grip',cup,openness=.4,overrides={'中指':[15,20,10],'薬指':[15,20,10],'小指':[15,20,10]})
    for n,q in cup.items():
        if n.startswith(('人指','中指','薬指','小指','親指')) and not n.endswith(('先.L','先.R')):world[n]=tupleq(q)
    virtual_right=right_wrist+right_delta@(P['中指１.R']-P['手首.R'])*.95+normal*.028
    arc_chain(local,world,['親指０.R','親指１.R','親指２.R','親指先.R'],virtual_right-forward*.005+normal*.044,-forward)
    left_forward=(left_delta@(P['中指１.L']-P['手首.L'])).normalized();left_across=(left_delta@(P['人指１.L']-P['小指１.L'])).normalized();left_normal=left_across.cross(left_forward).normalized()
    virtual_left=left_wrist+left_delta@(P['人指１.L']-P['手首.L'])+left_normal*.027
    arc_chain(local,world,['人指１.L','人指２.L','人指３.L','人指先.L'],virtual_left+left_forward*.012+left_normal*.004,left_forward)
    arc_chain(local,world,['親指０.L','親指１.L','親指２.L','親指先.L'],virtual_left-left_forward*.012+left_normal*.004,-left_forward)
    points,rotations=bind.evaluate(local=local,world=world);install(rotations);previous={s:{n:rotations[n] for n in ('腕.'+s,'ひじ.'+s)} for s in ('L','R')}
    for n in library['bones']:rig.pose.bones[n].keyframe_insert('rotation_quaternion',frame=f+1,group=n)
    frame=211+f;root.matrix_world=Matrix.Translation(C.transposed()@root_position)@(C.transposed()@rotation.to_matrix()@C).to_4x4();root.rotation_mode='QUATERNION';root.keyframe_insert('location',frame=frame);root.keyframe_insert('rotation_quaternion',frame=frame)
    tear.matrix_world=Matrix.Translation(C.transposed()@strip_position)@(C.transposed()@strip_rotation.to_matrix()@C).to_4x4();tear.rotation_mode='QUATERNION';tear.keyframe_insert('location',frame=frame);tear.keyframe_insert('rotation_quaternion',frame=frame)
    for obj in parts:
        visible=(phase<.18 if obj==sealed else phase>=.18);obj.hide_render=not visible;obj.hide_viewport=not visible;obj.keyframe_insert('hide_render',frame=frame);obj.keyframe_insert('hide_viewport',frame=frame)
    report.append({'frame':frame,'wristBend':{s:math.degrees((Vector(points['手首.'+s])-Vector(points['ひじ.'+s])).angle(Vector(points['中指１.'+s])-Vector(points['手首.'+s]))) for s in ('L','R')}})
rig.animation_data.action=None;track=next(t for t in rig.animation_data.nla_tracks if t.name=='Eat');strip=track.strips[0];strip.action=action
if action.slots:strip.action_slot=action.slots[0]
strip.action_frame_start=1;strip.action_frame_end=181;strip.frame_start=211;strip.frame_end=391;strip.extrapolation='NOTHING'
for track in rig.animation_data.nla_tracks:track.mute=False
for obj in parts:
    for frame in (1,210,392,811):obj.hide_render=True;obj.hide_viewport=True;obj.keyframe_insert('hide_render',frame=frame);obj.keyframe_insert('hide_viewport',frame=frame)
for obj in [rig,root]+parts:
    data=action if obj==rig else (obj.animation_data.action if obj.animation_data else None)
    if data:
        for layer in data.layers:
            for st in layer.strips:
                for bag in st.channelbags:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points:key.interpolation='CONSTANT' if curve.data_path.startswith('hide_') else 'LINEAR'
scene.frame_set(301);bpy.context.view_layer.update();scene.camera=bpy.data.objects['Review_Front'];bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE),compress=True)
(OUT/'Eat-Anatomy.json').write_text(json.dumps(report,indent=2))
for phase in (.12,.24,.5,.9):
    scene.frame_set(211+round(180*phase));bpy.context.view_layer.update()
    for view in ('Review_Front','Review_Side','Review_ThreeQuarter'):
        scene.camera=bpy.data.objects[view];scene.render.filepath=str(OUT/('Eat-Physical-'+view+'-'+str(phase)+'.png'));bpy.ops.render.render(write_still=True)
print('@@JSON@@'+json.dumps({'blend':str(SOURCE),'wristMax':{s:max(r['wristBend'][s] for r in report) for s in ('L','R')}}))
