"""Author the complete bottle interaction on the saved production Blender rig.

Uses the accepted aiming hand-axis/finger helpers. Bottle/grip/cap are actual
inventory geometry. This edits Blender source only; it does not export Unity data.
"""
import ast, bpy, json, math, sys
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'Tools/Blender'))
from retarget_hatsune_miku import Rig,Retargeter,frame as anatomical_frame,qslerp
SOURCE=ROOT/'ArtSource/DollSinger/SurvivalActions.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene
rig=next(o for o in scene.objects if o.type=='ARMATURE')
body=next(o for o in scene.objects if o.type=='MESH' and len(o.vertex_groups)>100)
bind=Rig(json.loads((ROOT/'Tools/Blender/DollSingerBind.json').read_text()));solver=Retargeter(bind,bind)
library=json.loads((ROOT/'SourceData/Animations/Survival/AuthoringSeed.json').read_text())
motion=next(m for m in library['motions'] if m['action']=='Drink' and 'water' in m.get('items',[]))
prop=next(p for p in json.loads((ROOT/'SourceData/Animations/Survival/ReviewProps.json').read_text())['items'] if p['code']=='water')
calibration=next(r for r in json.loads((ROOT/'SourceData/Animations/Survival/Calibration.json').read_text())['rigs'] if r['rig']=='DollSinger')
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)));P={n:Vector(v) for n,v in bind.p.items()};R={n:Quaternion((q[3],q[0],q[1],q[2])) for n,q in bind.r.items()}
BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
def tupleq(q):return (q.x,q.y,q.z,q.w)
def readq(q):return Quaternion((q[3],q[0],q[1],q[2]))
def sm(a,b,x):t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def lerp(a,b,t):return Vector(a).lerp(Vector(b),t)

def make_chest_penalty(mesh_object):
    names=[max(((mesh_object.vertex_groups[g.group].name,g.weight) for g in v.groups),key=lambda pair:pair[1])[0] if v.groups else '' for v in mesh_object.data.vertices]
    def torso(name):
        while name in bind.parent:
            if name in ('腕.L','腕.R','首'):return False
            name=bind.parent[name]
        return True
    faces=[tuple(p.vertices) for p in mesh_object.data.polygons if all(torso(names[i]) for i in p.vertices)]
    surface=BVHTree.FromPolygons([C@(mesh_object.matrix_world@v.co) for v in mesh_object.data.vertices],faces,all_triangles=True)
    def penalty(elbow,wrist):
        elbow=Vector(elbow);wrist=Vector(wrist);cost=0
        for t in (.20,.45,.70,.88):
            p=elbow.lerp(wrist,t);radius=.046-.012*t
            for x,y in ((0,0),(.015,0),(-.015,0),(0,.015),(0,-.015)):
                hit,_,_,_=surface.ray_cast(Vector((p.x+x,p.y+y,2)),Vector((0,0,-1)),4)
                if hit is not None:cost+=(max(0,hit.z+radius-p.z)/.008)**4
        return cost
    return penalty

pose_penalty=make_chest_penalty(body)

# Read only the verified helper function definitions; never execute the aiming
# source generator or write its accepted .blend / Unity libraries.
tree=ast.parse((ROOT/'Tools/Blender/weapon_gestures.py').read_text())
helpers=ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in ('norm','frame','rotbetween','position','setdir','fingers')],type_ignores=[])
context={'Vector':Vector,'Quaternion':Quaternion,'Matrix':Matrix,'math':math,'P':P,'R':R,'parents':bind.parent}
exec(compile(helpers,'accepted_aim_finger_helpers','exec'),context)

def hand_rotation(side,forward,across):
    hand='手首.'+side
    rest_forward=P['中指１.'+side]-P[hand];rest_across=P['人指１.'+side]-P['小指１.'+side]
    return readq(anatomical_frame(tuple(forward),tuple(across)))@readq(anatomical_frame(tuple(rest_forward),tuple(rest_across))).inverted()@R[hand]

def ready_pose(side):
    wrist=Vector((.17 if side=='R' else -.17,P['上半身2'].y+.015,.19))
    hand=hand_rotation(side,Vector((0,-.75,.661438)),Vector((-1 if side=='R' else 1,0,0)))
    return wrist,hand

def arc_chain(local,world,names,target,outward):
    points,_=bind.evaluate(local=local,world=world)
    base=Vector(points[names[0]])
    lengths=[(P[b]-P[a]).length for a,b in zip(names,names[1:])]
    delta=target-base;distance=delta.length
    if distance>sum(lengths)-.0003:target=base+delta.normalized()*(sum(lengths)-.0003);delta=target-base;distance=delta.length
    direction=delta.normalized();outward=(outward-direction*outward.dot(direction)).normalized()
    def span(theta):
        x=sum(l*math.cos(a) for l,a in zip(lengths,(theta,0,-theta)))
        y=sum(l*math.sin(a) for l,a in zip(lengths,(theta,0,-theta)))
        return math.hypot(x,y),math.atan2(y,x)
    low,high=0,math.radians(90)
    for _ in range(30):
        middle=(low+high)*.5
        if span(middle)[0]>distance:low=middle
        else:high=middle
    theta=(low+high)*.5;_,offset=span(theta)
    for name,child,angle in zip(names,names[1:],(theta-offset,-offset,-theta-offset)):
        desired=direction*math.cos(angle)+outward*math.sin(angle)
        side=name[-1];hand='手首.'+side
        existing=readq(world[hand])@R[hand].inverted()@R[name]
        current=existing@R[name].inverted()@(P[child]-P[name])
        if current.normalized().dot(desired)<-.99999:swing=Quaternion(direction.cross(outward).normalized(),math.pi)
        else:swing=current.normalized().rotation_difference(desired)
        world[name]=tupleq(swing@existing)

def thumb_on_bottle(local,world,center,up,forward,normal,side='R',target=None):
    target=target if target is not None else center+up*.015-forward*.044+normal*.008
    arc_chain(local,world,[n+'.'+side for n in ['親指０','親指１','親指２','親指先']],target,-forward)

def install(rotations):
    for n in bind.order:
        if n not in rig.pose.bones:continue
        bone=rig.pose.bones[n];parent=bind.parent[n]
        target=(C.transposed()@(readq(rotations[n])@R[n].inverted()).to_matrix()@C).to_quaternion()@BR[n]
        pt=(C.transposed()@(readq(rotations[parent])@R[parent].inverted()).to_matrix()@C).to_quaternion()@BR[parent] if parent in rotations and parent in BR else BR.get(parent,Quaternion())
        rest=(rig.data.bones[parent].matrix_local.inverted()@bone.bone.matrix_local).to_quaternion() if parent in rig.data.bones else bone.bone.matrix_local.to_quaternion()
        bone.rotation_mode='QUATERNION';bone.rotation_quaternion=rest.inverted()@pt.inverted()@target
    bpy.context.view_layer.update()

root=bpy.data.objects['Using_water'];parts=[o for o in scene.objects if o.get('source_part') and o.name.startswith('water_')];cap=next(o for o in parts if o['source_part']=='Cap')
cap_geometry=next(p for p in prop['parts'] if p['name']=='Cap')
verts=[Vector(v) for v in cap_geometry['vertices']];cap_center=(Vector(tuple(min(v[i] for v in verts) for i in range(3)))+Vector(tuple(max(v[i] for v in verts) for i in range(3))))*.5
for obj in [root]+parts:obj.animation_data_clear()
cap_world=cap.matrix_world.copy();cap.parent=None;cap.matrix_world=cap_world
for track in rig.animation_data.nla_tracks:track.mute=True
action=bpy.data.actions.new('Survival_Drink_PhysicalGrip');action.use_fake_user=True;action['notes']='Correct right palm side, full cap operation and gravity-consistent bottle tilt.'
rig.animation_data.action=action
previous={};results=[];chest=P['上半身2'];mouth=Vector(calibration['mouth'])
for f in range(181):
    phase=f/180;scene.frame_set(f+1)
    return_weight=sm(.84,1,phase)
    source_frame=phase*(motion['frames']-1);a=int(source_frame);b=min(a+1,motion['frames']-1);stride=len(library['bones'])*4
    local={n:qslerp(tuple(motion['rotations'][a*stride+i*4:a*stride+i*4+4]),tuple(motion['rotations'][b*stride+i*4:b*stride+i*4+4]),source_frame-a) for i,n in enumerate(library['bones'])}
    local={n:qslerp(q,bind.local[n],return_weight) for n,q in local.items()}
    for n in local:
        if n.startswith('肩') or n.endswith(('先.L','先.R')):local[n]=bind.local[n]
    source_points,source_rotations=bind.evaluate(local=local)
    raise_weight=sm(.26,.42,phase)*(1-sm(.64,.76,phase))
    up=lerp((0,1,0),(0,-.3,-.953939),raise_weight).normalized()
    normal=Quaternion(up,math.radians(-18))@Vector((-1,0,0));forward=up.cross(normal).normalized()
    right_hand=hand_rotation('R',forward,up).slerp(ready_pose('R')[1],return_weight)
    hand_delta=right_hand@R['手首.R'].inverted()
    forward=(hand_delta@(P['中指１.R']-P['手首.R'])).normalized()
    up=(hand_delta@(P['人指１.R']-P['小指１.R'])).normalized()
    normal=-up.cross(forward).normalized()
    # A right palm is -cross(knuckle-across, palm-forward). The previous generic
    # attachment used the back of the hand for every item.
    prop_right=up.cross(normal).normalized();prop_rotation=Matrix(((prop_right.x,up.x,normal.x),(prop_right.y,up.y,normal.y),(prop_right.z,up.z,normal.z))).to_quaternion()
    low_top=Vector((.015,chest.y+.075,.26))
    top=low_top.lerp(mouth,raise_weight)+Vector((0,0,.025*math.sin(math.pi*raise_weight)))
    root_position=top-prop_rotation@Vector(prop['contact'])
    grip=root_position+prop_rotation@Vector(prop['grip'])
    right_delta=right_hand@R['手首.R'].inverted()
    right_mid=right_delta@(P['中指１.R']-P['手首.R'])*.95
    right_wrist=grip-right_mid-normal*.048
    right_wrist=right_wrist.lerp(ready_pose('R')[0],return_weight)+Vector((0,0,.06*math.sin(math.pi*return_weight)))
    grip=right_wrist+right_mid+normal*.048
    root_position=grip-prop_rotation@Vector(prop['grip'])
    cap_attached=root_position+prop_rotation@cap_center
    detach=sm(.18,.27,phase)*(1-sm(.70,.78,phase))
    cap_pos=cap_attached.lerp(Vector((-.17,chest.y+.04,.25)),detach)+Vector((0,.025*math.sin(math.pi*detach),0))
    turn=sm(.08,.18,phase)*(1-sm(.20,.28,phase))+sm(.70,.76,phase)*(1-sm(.78,.84,phase))
    initial_prop=Matrix(((0,0,-1),(0,1,0),(1,0,0))).to_quaternion()
    held_cap=Quaternion(Vector((0,1,0)),math.radians(60)*turn)@initial_prop
    cap_rotation=(Quaternion(up,math.radians(60)*turn)@prop_rotation).slerp(held_cap,detach)
    left_up=cap_rotation@Vector((0,1,0));left_normal=-(cap_rotation@Vector((0,0,1)));left_forward=left_normal.cross(left_up).normalized()
    left_across=(left_up*.65+left_normal*.759934).normalized();left_normal=left_across.cross(left_forward).normalized()
    left_hand=hand_rotation('L',left_forward,left_across).slerp(ready_pose('L')[1],return_weight)
    left_delta=left_hand@R['手首.L'].inverted()
    left_wrist=cap_pos-left_delta@(P['人指１.L']-P['手首.L'])-left_normal*.032
    left_wrist=left_wrist.lerp(ready_pose('L')[0],return_weight)
    world={'手首.R':tupleq(right_hand),'手首.L':tupleq(left_hand)}
    cup={n:readq(q) for n,q in source_rotations.items()};cup['手首.R']=right_hand;cup['手首.L']=left_hand
    context['fingers']('R','grip',cup,overrides={'人指':[21,44,25],'中指':[24,45,25],'薬指':[25,35,30],'小指':[20,24,20]})
    context['fingers']('L','grip',cup,openness=.25,overrides={'中指':[15,20,10],'薬指':[15,20,10],'小指':[15,20,10]})
    for n,q in cup.items():
        if n.startswith(('人指','中指','薬指','小指','親指')) and not n.endswith(('先.L','先.R')):world[n]=tupleq(q.slerp(left_hand@R['手首.L'].inverted()@R[n],return_weight) if n.endswith('.L') else q)
    guides={'R':tuple(Vector((.24,chest.y-.10+.22*raise_weight,.18)).lerp(P['ひじ.R'],return_weight)),'L':tuple(Vector((-.23,chest.y-.055+.12*(1-detach),.17)).lerp(P['ひじ.L'],return_weight))}
    for side,wrist in [('R',right_wrist),('L',left_wrist)]:
        try:solver.arm(side,tuple(wrist),guides[side],world,source_points,previous=previous.get(side),anatomical_wrist_limit=50,pose_penalty=pose_penalty)
        except ValueError as error:raise RuntimeError((phase,side,tuple(wrist),source_points['腕.'+side])) from error
    thumb_on_bottle(local,world,grip,up,forward,normal)
    left_forward_actual=(left_delta@(P['中指１.L']-P['手首.L'])).normalized()
    left_across_actual=(left_delta@(P['人指１.L']-P['小指１.L'])).normalized()
    left_normal_actual=left_across_actual.cross(left_forward_actual).normalized()
    virtual_cap=left_wrist+left_delta@(P['人指１.L']-P['手首.L'])+left_normal_actual*.032
    thumb_on_bottle(local,world,virtual_cap,left_up,left_forward_actual,left_normal_actual,side='L',target=virtual_cap-left_forward_actual*.022+left_normal_actual*.004)
    arc_chain(local,world,[n+'.L' for n in ['人指１','人指２','人指３','人指先']],virtual_cap+left_forward_actual*.022+left_normal_actual*.004,left_forward_actual)
    for n in ['親指０.L','親指１.L','親指２.L','人指１.L','人指２.L','人指３.L']:
        world[n]=tupleq(readq(world[n]).slerp(left_hand@R['手首.L'].inverted()@R[n],return_weight))
    points,rotations=bind.evaluate(local=local,world=world)
    previous={s:{n:rotations[n] for n in ('腕.'+s,'ひじ.'+s)} for s in ('L','R')}
    install(rotations)
    for n in library['bones']:rig.pose.bones[n].keyframe_insert('rotation_quaternion',frame=f+1,group=n)
    frame=421+f
    root.matrix_world=Matrix.Translation(C.transposed()@root_position)@(C.transposed()@prop_rotation.to_matrix()@C).to_4x4();root.rotation_mode='QUATERNION';root.keyframe_insert('location',frame=frame);root.keyframe_insert('rotation_quaternion',frame=frame)
    cap.matrix_world=Matrix.Translation(C.transposed()@cap_pos)@(C.transposed()@cap_rotation.to_matrix()@C).to_4x4()@Matrix.Translation(-(C.transposed()@cap_center));cap.rotation_mode='QUATERNION';cap.keyframe_insert('location',frame=frame);cap.keyframe_insert('rotation_quaternion',frame=frame)
    for obj in parts:
        obj.hide_render=False;obj.hide_viewport=False;obj.keyframe_insert('hide_render',frame=frame);obj.keyframe_insert('hide_viewport',frame=frame)
    angular={}
    for side in ('L','R'):
        fore=Vector(points['手首.'+side])-Vector(points['ひじ.'+side]);palm=Vector(points['中指１.'+side])-Vector(points['手首.'+side]);angular[side]=math.degrees(fore.angle(palm))
    results.append({'frame':frame,'wristBend':angular})
rig.animation_data.action=None
track=next(t for t in rig.animation_data.nla_tracks if t.name=='Drink');strip=track.strips[0];strip.action=action
if len(action.slots):strip.action_slot=action.slots[0]
strip.action_frame_start=1;strip.action_frame_end=181;strip.frame_start=421;strip.frame_end=601;strip.extrapolation='NOTHING'
for track in rig.animation_data.nla_tracks:track.mute=False
for obj in parts:
    for frame in (1,420,602,810):
        obj.hide_render=True;obj.hide_viewport=True;obj.keyframe_insert('hide_render',frame=frame);obj.keyframe_insert('hide_viewport',frame=frame)
for obj in [root]+parts:
    if obj.animation_data and obj.animation_data.action:
        for layer in obj.animation_data.action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points:key.interpolation='CONSTANT' if curve.data_path.startswith('hide_') else 'LINEAR'
for layer in action.layers:
    for strip in layer.strips:
        for bag in strip.channelbags:
            for curve in bag.fcurves:
                for key in curve.keyframe_points:key.interpolation='LINEAR'
scene.frame_set(511);scene.camera=bpy.data.objects['Review_Front'];bpy.context.view_layer.update()
rig['authoring_status']='Drink revised in Blender; remaining interactions still require source review.'
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE),compress=True)
out=ROOT/'LocalData/Actions/Blender';(out/'Drink-Anatomy.json').write_text(json.dumps(results,indent=2))
for phase in (.16,.24,.5,.9):
    scene.frame_set(421+round(180*phase));bpy.context.view_layer.update()
    for view in ('Review_Front','Review_Side','Review_Back'):
        scene.camera=bpy.data.objects[view];scene.render.filepath=str(out/('Drink-Physical-'+view+'-'+str(phase)+'.png'));bpy.ops.render.render(write_still=True)
print('@@JSON@@'+json.dumps({'blend':str(SOURCE),'wristMax':{s:max(r['wristBend'][s] for r in results) for s in ('L','R')},'views':str(out)}))
