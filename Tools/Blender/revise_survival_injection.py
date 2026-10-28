"""Author the sourced injection on the actual skin, injector and thumb button.

Edits saved Blender source only. Source performance is BURNER CC BY 4.0;
physical grips, cap removal and contact are adapted for the production rig.
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
library=json.loads((ROOT/'SourceData/Animations/Survival/AuthoringSeed.json').read_text());motion=next(m for m in library['motions'] if m['action']=='Inject' and 'medkit' in m.get('items',[]))
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)));P={n:Vector(v) for n,v in bind.p.items()};R={n:Quaternion((q[3],q[0],q[1],q[2])) for n,q in bind.r.items()};BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
# Load pure authoring helpers without running another action's generator.
tree=ast.parse((ROOT/'Tools/Blender/revise_survival_drink.py').read_text());names=('tupleq','readq','sm','lerp','hand_rotation','arc_chain','thumb_on_bottle','install','make_chest_penalty','ready_pose')
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names],type_ignores=[]),'survival_authoring_helpers','exec'),globals())
pose_penalty=make_chest_penalty(body)
tree=ast.parse((ROOT/'Tools/Blender/weapon_gestures.py').read_text());helpers=ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in ('norm','frame','rotbetween','position','setdir','fingers')],type_ignores=[])
context={'Vector':Vector,'Quaternion':Quaternion,'Matrix':Matrix,'math':math,'P':P,'R':R,'parents':bind.parent};exec(compile(helpers,'accepted_aim_finger_helpers','exec'),context)
root=bpy.data.objects['Using_medkit'];parts=[o for o in scene.objects if o.name.startswith('medkit_') and o.get('source_part')]
plunger=next(o for o in parts if o['source_part']=='Plunger')
cap=bpy.data.objects.get('medkit_NeedleSheath')
if cap is None:
    # A closed, removable needle sheath; dimensions are metres, Y is injector up.
    vertices=[];faces=[];segments=16
    for y in (-.018,.005):
        for i in range(segments):
            a=i*2*math.pi/segments;vertices.append(C.transposed()@Vector((.0045*math.cos(a),y,.0045*math.sin(a))))
    for i in range(segments):
        j=(i+1)%segments;faces.extend([(i,j,segments+j),(i,segments+j,segments+i)])
    for ring in (0,segments):
        center=len(vertices);vertices.append(C.transposed()@Vector((0,-.018 if ring==0 else .005,0)))
        for i in range(segments):faces.append((center,ring+i,ring+(i+1)%segments))
    mesh=bpy.data.meshes.new('NeedleSheath');mesh.from_pydata(vertices,[],faces);mesh.update()
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    cap=bpy.data.objects.new('medkit_NeedleSheath',mesh);root.users_collection[0].objects.link(cap);mesh.materials.append(bpy.data.materials['Survival_Item_dark']);cap['source_part']='NeedleSheath';parts.append(cap)
    cap.select_set(True);bpy.context.view_layer.objects.active=cap
    with bpy.context.temp_override(object=cap,active_object=cap,selected_objects=[cap],selected_editable_objects=[cap]):
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.01);bpy.ops.object.mode_set(mode='OBJECT')
    cap.select_set(False)
for obj in [root]+parts:obj.animation_data_clear()
cap.parent=None
for track in rig.animation_data.nla_tracks:track.mute=True
rig.animation_data.action=bpy.data.actions.new('Survival_Inject_PhysicalContact');action=rig.animation_data.action;action.use_fake_user=True
body_names=[max(((body.vertex_groups[g.group].name,g.weight) for g in v.groups),key=lambda x:x[1])[0] if v.groups else '' for v in body.data.vertices]
def childof(name,ancestor):
    while name in bind.parent:
        if name==ancestor:return True
        name=bind.parent[name]
    return False
forearm_faces=[tuple(p.vertices) for p in body.data.polygons if all(childof(body_names[i],'ひじ.L') and not childof(body_names[i],'手首.L') for i in p.vertices)]
forearm_materials=[body.data.materials[p.material_index].name for p in body.data.polygons if all(childof(body_names[i],'ひじ.L') and not childof(body_names[i],'手首.L') for i in p.vertices)]
if not forearm_faces:raise RuntimeError('No actual left forearm skin')
chest=P['上半身2'];grip_local=Vector((0,.1345,0));cap_center=Vector((0,-.0065,0));previous={};report=[]
for f in range(181):
    phase=f/180;scene.frame_set(f+1);intro=sm(0,.10,phase);outro=sm(.84,1,phase);active=intro*(1-outro)
    source_frame=phase*(motion['frames']-1);a=int(source_frame);b=min(a+1,motion['frames']-1);stride=len(library['bones'])*4
    local={n:qslerp(bind.local[n],qslerp(tuple(motion['rotations'][a*stride+i*4:a*stride+i*4+4]),tuple(motion['rotations'][b*stride+i*4:b*stride+i*4+4]),source_frame-a),active) for i,n in enumerate(library['bones'])}
    for n in local:
        if n.startswith('肩') or n.endswith(('先.L','先.R')):local[n]=bind.local[n]
    starts,_=bind.evaluate(local=local)
    prep_tip=Vector((-.02,chest.y+.035,.29));prep_up=Vector((0,1,0));prep_normal=Vector((0,0,-1));prep_forward=prep_up.cross(prep_normal).normalized()
    prep_rotation=Matrix(((1,0,0),(0,1,0),(0,0,1))).to_quaternion();prep_root=prep_tip-prep_rotation@Vector((0,-.014,0))
    removal=sm(.12,.23,phase);cap_pre=prep_root+prep_rotation@cap_center-prep_up*.052*removal
    cap_hand=hand_rotation('L',Vector((0,0,1)),Vector((0,1,0)));cap_delta=cap_hand@R['手首.L'].inverted()
    cap_wrist=cap_pre-cap_delta@(P['人指１.L']-P['手首.L'])-Vector((1,0,0))*.019
    forearm_forward=Vector((1,.15,.09)).normalized();forearm_normal=(Vector((0,1,0))-forearm_forward*forearm_forward.y).normalized();forearm_across=forearm_forward.cross(forearm_normal).normalized()
    exposed_hand=hand_rotation('L',forearm_forward,forearm_across);expose=sm(.24,.37,phase)
    left_hand=cap_hand.slerp(exposed_hand,expose).slerp(ready_pose('L')[1],outro).slerp(ready_pose('L')[1],1-intro)
    left_wrist=cap_wrist.lerp(Vector((.025,chest.y-.005,.26)),expose).lerp(ready_pose('L')[0],outro).lerp(ready_pose('L')[0],1-intro)
    world={'手首.L':tupleq(left_hand)}
    left_guide=Vector((-.22,chest.y-.09,.18)).lerp(P['ひじ.L'],outro)
    try:solver.arm('L',tuple(left_wrist),tuple(left_guide),world,starts,previous=previous.get('L'),anatomical_wrist_limit=50,pose_penalty=pose_penalty)
    except ValueError as error:raise RuntimeError((phase,tuple(left_wrist),starts['腕.L'])) from error
    points,rotations=bind.evaluate(local=local,world=world)
    if expose>0:
        natural_forward=(Vector(points['手首.L'])-Vector(points['ひじ.L'])).normalized();natural_normal=(Vector((0,1,0))-natural_forward*natural_forward.y).normalized();natural_across=natural_forward.cross(natural_normal).normalized()
        left_hand=left_hand.slerp(hand_rotation('L',natural_forward,natural_across),expose*(1-outro));world['手首.L']=tupleq(left_hand)
        points,rotations=bind.evaluate(local=local,world=world)
    install(rotations)
    # Ray-cast the deformed production surface, rather than using a guessed radius.
    ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
    vertices=[C@(ev.matrix_world@v.co) for v in mesh.vertices];surface=BVHTree.FromPolygons(vertices,forearm_faces,all_triangles=True)
    elbow=Vector(points['ひじ.L']);wrist=Vector(points['手首.L']);axis=(wrist-elbow).normalized();normal=(Vector((0,1,0))-axis*axis.y).normalized();site=elbow.lerp(wrist,.50)
    hit=None;skin_material=None;site_fraction=None
    for fraction in (.28,.33,.23,.38,.43,.48,.50):
        candidate=elbow.lerp(wrist,fraction);point,hit_normal,index,_=surface.ray_cast(candidate+normal*.15,-normal,.3)
        if point is not None and forearm_materials[index].lower()=='body':
            hit=point;skin_material=forearm_materials[index];site_fraction=fraction;break
    if hit is None:
        hit,hit_normal,_,_=surface.ray_cast(site+normal*.15,-normal,.3)
        if hit is None:hit=site+normal*.027
    if .48<=phase<=.72 and skin_material!='body':raise RuntimeError('Injection must contact exposed forearm skin, never clothing.')
    ev.to_mesh_clear()
    entry=sm(.37,.48,phase);withdraw=sm(.72,.80,phase)
    top=prep_up.lerp(normal,entry).normalized();reference_right=Vector((.707107,0,-.707107));right_axis=(reference_right-top*reference_right.dot(top)).normalized();prop_front=right_axis.cross(top).normalized()
    rotation=Matrix(((right_axis.x,top.x,prop_front.x),(right_axis.y,top.y,prop_front.y),(right_axis.z,top.z,prop_front.z))).to_quaternion()
    needle_target=prep_tip.lerp(hit-normal*.0025,entry)+normal*.045*withdraw
    root_position=needle_target-rotation@Vector((0,-.014,0));grip=root_position+rotation@grip_local
    right_normal=-prop_front;right_forward=top.cross(right_normal).normalized();right_hand=hand_rotation('R',right_forward,top).slerp(ready_pose('R')[1],outro).slerp(ready_pose('R')[1],1-intro)
    right_delta=right_hand@R['手首.R'].inverted();right_wrist=grip-right_delta@(P['中指１.R']-P['手首.R'])*.95-right_normal*.037
    right_wrist=right_wrist.lerp(ready_pose('R')[0],outro).lerp(ready_pose('R')[0],1-intro)
    if outro>0 or intro<1:
        current_up=(right_delta@(P['人指１.R']-P['小指１.R'])).normalized();current_f=(right_delta@(P['中指１.R']-P['手首.R'])).normalized();current_n=-current_up.cross(current_f).normalized()
        x=current_up.cross(-current_n).normalized();rotation=Matrix(((x.x,current_up.x,-current_n.x),(x.y,current_up.y,-current_n.y),(x.z,current_up.z,-current_n.z))).to_quaternion()
        root_position=right_wrist+right_delta@(P['中指１.R']-P['手首.R'])*.95+current_n*.037-rotation@grip_local;top=rotation@Vector((0,1,0));right_normal=current_n;right_forward=current_f
    world['手首.R']=tupleq(right_hand)
    solver.arm('R',tuple(right_wrist),tuple(Vector((.24,chest.y+.03,.04)).lerp(P['ひじ.R'],outro)),world,starts,previous=previous.get('R'),anatomical_wrist_limit=50,elbow_max_y=P['腕.R'].y-.025,pose_penalty=pose_penalty)
    cup={n:readq(q) for n,q in rotations.items()};cup['手首.L']=left_hand;cup['手首.R']=right_hand
    context['fingers']('R','grip',cup,overrides={'人指':[33,50,40],'中指':[34,50,40],'薬指':[34,50,40],'小指':[32,50,40]})
    context['fingers']('L','open',cup)
    for n,q in cup.items():
        if n.startswith(('人指','中指','薬指','小指','親指')) and not n.endswith(('先.L','先.R')):world[n]=tupleq(q)
    virtual_grip=right_wrist+right_delta@(P['中指１.R']-P['手首.R'])*.95+right_normal*.037
    preliminary,_=bind.evaluate(local=local,world=world)
    for digit,radius,n_offset in [('人指',.029,.004),('中指',.024,.004),('薬指',.023,.004),('小指',.021,-.012)]:
        names=[digit+s+'.R' for s in ['１','２','３','先']];base=Vector(preliminary[names[0]]);height=(base-virtual_grip).dot(top)
        arc_chain(local,world,names,virtual_grip+top*height+right_forward*radius+right_normal*n_offset,right_forward)
    press=sm(.50,.70,phase);button=root_position+rotation@Vector((0,.1935-.010*press,0))+top*.010-right_forward*.012-right_normal*.012
    arc_chain(local,world,['親指０.R','親指１.R','親指２.R','親指先.R'],button,top)
    if expose<1:
        cap_f=(left_hand@R['手首.L'].inverted()@(P['中指１.L']-P['手首.L'])).normalized();cap_n=Vector((1,0,0))
        cap_across=(left_hand@R['手首.L'].inverted()@(P['人指１.L']-P['小指１.L'])).normalized();cap_n=cap_across.cross(cap_f).normalized()
        virtual_cap=left_wrist+left_hand@R['手首.L'].inverted()@(P['人指１.L']-P['手首.L'])+cap_n*.019
        closed=cup.copy();context['fingers']('L','grip',closed,openness=.4)
        for n,q in closed.items():
            if n.endswith('.L') and n.startswith(('人指','中指','薬指','小指','親指')) and not n.endswith('先.L'):world[n]=tupleq(q)
        arc_chain(local,world,['人指１.L','人指２.L','人指３.L','人指先.L'],virtual_cap+cap_f*.010,cap_f)
        arc_chain(local,world,['親指０.L','親指１.L','親指２.L','親指先.L'],virtual_cap-cap_f*.010,-cap_f)
        for n in world:
            if n.endswith('.L') and n.startswith(('人指','中指','薬指','小指','親指')) and not n.endswith('先.L'):world[n]=tupleq(readq(world[n]).slerp(cup[n],expose))
    points,rotations=bind.evaluate(local=local,world=world);install(rotations)
    previous={s:{n:rotations[n] for n in ('腕.'+s,'ひじ.'+s)} for s in ('L','R')}
    for n in library['bones']:rig.pose.bones[n].keyframe_insert('rotation_quaternion',frame=f+1,group=n)
    frame=631+f;root.matrix_world=Matrix.Translation(C.transposed()@root_position)@(C.transposed()@rotation.to_matrix()@C).to_4x4();root.rotation_mode='QUATERNION';root.keyframe_insert('location',frame=frame);root.keyframe_insert('rotation_quaternion',frame=frame)
    plunger.location=C.transposed()@Vector((0,-.010*press,0));plunger.keyframe_insert('location',frame=frame)
    if phase<=.12:cap_pos=root_position+rotation@cap_center;cap_rotation=rotation
    elif phase<=.24:cap_pos=prep_root+prep_rotation@cap_center-prep_up*.052*removal;cap_rotation=prep_rotation
    else:
        seconds=(phase-.24)*3;cap_pos=prep_root+cap_center-prep_up*.052+Vector((-.35*seconds,-.20*seconds-4.905*seconds*seconds,.08*seconds));cap_rotation=Quaternion(Vector((0,0,1)),seconds*5)
    cap.matrix_world=Matrix.Translation(C.transposed()@cap_pos)@(C.transposed()@cap_rotation.to_matrix()@C).to_4x4()@Matrix.Translation(-(C.transposed()@cap_center));cap.rotation_mode='QUATERNION';cap.keyframe_insert('location',frame=frame);cap.keyframe_insert('rotation_quaternion',frame=frame)
    for obj in parts:
        obj.hide_render=False;obj.hide_viewport=False;obj.keyframe_insert('hide_render',frame=frame);obj.keyframe_insert('hide_viewport',frame=frame)
    bends={s:math.degrees((Vector(points['手首.'+s])-Vector(points['ひじ.'+s])).angle(Vector(points['中指１.'+s])-Vector(points['手首.'+s]))) for s in ('L','R')}
    report.append({'frame':frame,'wristBend':bends,'needleSkinDistance':(needle_target-hit).length if .48<=phase<=.72 else None,'injectionSurface':skin_material,'forearmFraction':site_fraction,'thumbButtonGap':(Vector(points['親指先.R'])-button).length})
rig.animation_data.action=None;track=next(t for t in rig.animation_data.nla_tracks if t.name=='Inject');strip=track.strips[0];strip.action=action
if action.slots:strip.action_slot=action.slots[0]
strip.action_frame_start=1;strip.action_frame_end=181;strip.frame_start=631;strip.frame_end=811;strip.extrapolation='NOTHING'
for track in rig.animation_data.nla_tracks:track.mute=False
for obj in parts:
    for frame in (1,630,812):
        obj.hide_render=True;obj.hide_viewport=True;obj.keyframe_insert('hide_render',frame=frame);obj.keyframe_insert('hide_viewport',frame=frame)
for obj in [rig,root]+parts:
    action_data=obj.animation_data.action if obj.animation_data else None
    if obj==rig:action_data=action
    if action_data:
        for layer in action_data.layers:
            for st in layer.strips:
                for bag in st.channelbags:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points:key.interpolation='CONSTANT' if curve.data_path.startswith('hide_') else 'LINEAR'
scene.frame_end=811;scene.frame_set(736);scene.camera=bpy.data.objects['Review_Front'];bpy.context.view_layer.update();bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE),compress=True)
(OUT/'Inject-Anatomy.json').write_text(json.dumps(report,indent=2))
for phase in (.18,.36,.6,.9):
    scene.frame_set(631+round(180*phase));bpy.context.view_layer.update()
    for view in ('Review_Front','Review_Side','Review_ThreeQuarter'):
        scene.camera=bpy.data.objects[view];scene.render.filepath=str(OUT/('Inject-Physical-'+view+'-'+str(phase)+'.png'));bpy.ops.render.render(write_still=True)
print('@@JSON@@'+json.dumps({'blend':str(SOURCE),'wristMax':{s:max(r['wristBend'][s] for r in report) for s in ('L','R')},'thumbButtonMaximumGap':max(r['thumbButtonGap'] for r in report)}))
