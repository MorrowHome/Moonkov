"""Adapt Quaternius PickUp_Table's complete looping reach to a container opening."""
import ast,bpy,json,math,sys
from pathlib import Path
from mathutils import Vector,Quaternion,Matrix
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'Tools/Blender'))
from retarget_hatsune_miku import Rig,Retargeter,frame as anatomical_frame,qslerp
SOURCE=ROOT/'ArtSource/DollSinger/SurvivalActions.blend';OUT=ROOT/'LocalData/Actions/Blender'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene
rig=next(o for o in scene.objects if o.type=='ARMATURE');body=next(o for o in scene.objects if o.type=='MESH' and len(o.vertex_groups)>100);bind=Rig(json.loads((ROOT/'Tools/Blender/DollSingerBind.json').read_text()));solver=Retargeter(bind,bind)
library=json.loads((ROOT/'SourceData/Animations/Survival/AuthoringSeed.json').read_text());motion=next(m for m in library['motions'] if m['action']=='Treat')
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)));P={n:Vector(v) for n,v in bind.p.items()};R={n:Quaternion((q[3],q[0],q[1],q[2])) for n,q in bind.r.items()};BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
tree=ast.parse((ROOT/'Tools/Blender/revise_survival_drink.py').read_text());names=('tupleq','readq','sm','lerp','hand_rotation','arc_chain','thumb_on_bottle','install','make_chest_penalty')
exec(compile(ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names],type_ignores=[]),'survival_authoring_helpers','exec'),globals())
pose_penalty=make_chest_penalty(body)
tree=ast.parse((ROOT/'Tools/Blender/weapon_gestures.py').read_text());helpers=ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in ('norm','frame','rotbetween','position','setdir','fingers')],type_ignores=[])
context={'Vector':Vector,'Quaternion':Quaternion,'Matrix':Matrix,'math':math,'P':P,'R':R,'parents':bind.parent};exec(compile(helpers,'accepted_aim_finger_helpers','exec'),context)
reference=bpy.data.objects.get('Reference_SearchOpening')
if reference is None:
    points=[Vector((x,y,z)) for y in (.79,1.01) for z in (.22,.46) for x in (-.20,.20)]
    edges=[(0,1),(1,3),(3,2),(2,0),(4,5),(5,7),(7,6),(6,4),(0,4),(1,5),(2,6),(3,7)]
    curve=bpy.data.curves.new('Reference opening only, never exported','CURVE');curve.dimensions='3D';curve.bevel_depth=.0025;curve.bevel_resolution=1
    for a,b in edges:
        line=curve.splines.new('POLY');line.points.add(1);line.points[0].co=(*(C.transposed()@points[a]),1);line.points[1].co=(*(C.transposed()@points[b]),1)
    reference=bpy.data.objects.new('Reference_SearchOpening',curve);scene.collection.objects.link(reference);reference['reference_only']=True
    mat=bpy.data.materials.new('Search opening reference');mat.diffuse_color=(.22,.26,.29,1);curve.materials.append(mat)
reference.animation_data_clear()
for track in rig.animation_data.nla_tracks:track.mute=True
rig.animation_data.action=bpy.data.actions.new('Survival_Search_ContainerReach');action=rig.animation_data.action;action.use_fake_user=True;action['source_motion']=motion['source'];action['loop']=True
samples=[]
for f in range(motion['frames']):
    stride=len(library['bones'])*4;local={n:tuple(motion['rotations'][f*stride+i*4:f*stride+i*4+4]) for i,n in enumerate(library['bones'])};samples.append(bind.evaluate(local=local))
right_points=[Vector(p['手首.R']) for p,_ in samples];minimum=Vector(tuple(min(p[i] for p in right_points) for i in range(3)));maximum=Vector(tuple(max(p[i] for p in right_points) for i in range(3)))
previous={};report=[]
for f in range(181):
    phase=f/180;scene.frame_set(f+1);source_frame=(phase*2%1)*(motion['frames']-1);a=int(source_frame);b=min(a+1,motion['frames']-1);t=source_frame-a
    source_p={n:Vector(samples[a][0][n]).lerp(Vector(samples[b][0][n]),t) for n in bind.order}
    source_r={n:readq(samples[a][1][n]).slerp(readq(samples[b][1][n]),t) for n in bind.order}
    s=source_p['手首.R'];u=Vector(tuple((s[i]-minimum[i])/(maximum[i]-minimum[i]) for i in range(3)));reach=u.z
    right_wrist=Vector((.02+.14*u.x,1.05-.030*reach,.195+.090*reach));left_wrist=Vector((-.14,1.045,.168))
    rf=(source_p['中指１.R']-s).normalized();right_forward=Vector((rf.x*.45,-.65,1)).normalized();right_across=-Vector((1,0,0));right_across=(right_across-right_forward*right_across.dot(right_forward)).normalized()
    left_forward=Vector((0,-.52,1)).normalized();left_across=Vector((1,0,0))
    world={'手首.R':tupleq(hand_rotation('R',right_forward,right_across)),'手首.L':tupleq(hand_rotation('L',left_forward,left_across))};starts,_=bind.evaluate(local=bind.local)
    for side,wrist,guide in [('R',right_wrist,Vector((.24,1.04,.07))),('L',left_wrist,Vector((-.24,1.04,.07)))]:solver.arm(side,tuple(wrist),tuple(guide),world,starts,previous=previous.get(side),anatomical_wrist_limit=50,elbow_max_y=P['腕.'+side].y-.025,pose_penalty=pose_penalty)
    cup={n:readq(q) for n,q in bind.r.items()};cup['手首.R']=readq(world['手首.R']);cup['手首.L']=readq(world['手首.L'])
    context['fingers']('R','grip',cup,overrides={d:[12+reach*20,18+reach*26,10+reach*20] for d in ['人指','中指','薬指','小指']})
    context['fingers']('L','cup',cup,overrides={d:[8,12,8] for d in ['人指','中指','薬指','小指']})
    for n,q in cup.items():
        if n.startswith(('人指','中指','薬指','小指','親指')) and not n.endswith(('先.L','先.R')):world[n]=tupleq(q)
    points,rotations=bind.evaluate(local=bind.local,world=world);install(rotations);previous={s:{n:rotations[n] for n in ('腕.'+s,'ひじ.'+s)} for s in ('L','R')}
    for n in library['bones']:rig.pose.bones[n].keyframe_insert('rotation_quaternion',frame=f+1,group=n)
    report.append({'frame':f+1,'wristBend':{s:math.degrees((Vector(points['手首.'+s])-Vector(points['ひじ.'+s])).angle(Vector(points['中指１.'+s])-Vector(points['手首.'+s]))) for s in ('L','R')},'handPosition':list(points['手首.R'])})
rig.animation_data.action=None;strip=next(t for t in rig.animation_data.nla_tracks if t.name=='Search').strips[0];strip.action=action
if action.slots:strip.action_slot=action.slots[0]
strip.action_frame_start=1;strip.action_frame_end=181;strip.frame_start=1;strip.frame_end=181;strip.extrapolation='NOTHING'
for track in rig.animation_data.nla_tracks:track.mute=False
for frame,visible in ((1,True),(181,True),(182,False),(811,False)):
    reference.hide_render=not visible;reference.hide_viewport=not visible;reference.keyframe_insert('hide_render',frame=frame);reference.keyframe_insert('hide_viewport',frame=frame)
for data in (action,reference.animation_data.action):
    for layer in data.layers:
        for st in layer.strips:
            for bag in st.channelbags:
                for curve in bag.fcurves:
                    for key in curve.keyframe_points:key.interpolation='CONSTANT' if curve.data_path.startswith('hide_') else 'LINEAR'
scene.frame_set(55);bpy.context.view_layer.update();scene.camera=bpy.data.objects['Review_Front'];bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE),compress=True)
(OUT/'Search-Anatomy.json').write_text(json.dumps(report,indent=2))
for phase in (.10,.30,.60,.85):
    scene.frame_set(1+round(180*phase));bpy.context.view_layer.update()
    for view in ('Review_Front','Review_Side','Review_ThreeQuarter'):
        scene.camera=bpy.data.objects[view];scene.render.filepath=str(OUT/('Search-Physical-'+view+'-'+str(phase)+'.png'));bpy.ops.render.render(write_still=True)
print('@@JSON@@'+json.dumps({'blend':str(SOURCE),'wristMax':{s:max(r['wristBend'][s] for r in report) for s in ('L','R')}}))
