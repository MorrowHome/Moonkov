"""Author an isolated oxygen take; preserve the four accepted survival takes.
Run setup(), author_range(a,b), finish_source() through the dedicated Blender MCP.
Runtime poses use the same full-chain offline solver, never wrist IK.
"""
import ast,bpy,json,math,sys,hashlib
from pathlib import Path
from mathutils import Vector,Quaternion,Matrix
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/Blender'))
from retarget_hatsune_miku import Rig,Retargeter,frame as anatomical_frame,qslerp,qmul,qinverse,qrotate
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
OUT=ROOT/'ArtSource/DollSinger/OxygenRefill.blend'
DATA=ROOT/'SourceData/Animations/OxygenRefill'
helpers=ast.parse((ROOT/'Tools/Blender/revise_survival_drink.py').read_text(encoding='utf-8-sig'))
helpers=ast.Module(body=[n for n in helpers.body if isinstance(n,ast.FunctionDef) and n.name in ('tupleq','readq','sm','hand_rotation','ready_pose','arc_chain','thumb_on_bottle','install')],type_ignores=[])
exec(compile(helpers,'oxygen_shared_hand_axes','exec'),globals())

def configure(rig_name):
 global bind,solver,P,R,context,names,active_rig
 active_rig=rig_name
 bind=Rig(json.loads((ROOT/f'Tools/Blender/{rig_name}Bind.json').read_text()))
 solver=Retargeter(bind,bind);names=solver.arm_tracks
 P={n:Vector(v) for n,v in bind.p.items()};R={n:Quaternion((q[3],q[0],q[1],q[2])) for n,q in bind.r.items()}
 tree=ast.parse((ROOT/'Tools/Blender/weapon_gestures.py').read_text())
 fn=ast.Module(body=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in ('norm','frame','rotbetween','position','setdir','fingers')],type_ignores=[])
 context={'Vector':Vector,'Quaternion':Quaternion,'Matrix':Matrix,'math':math,'P':P,'R':R,'parents':bind.parent}
 exec(compile(fn,'accepted_grip_axes','exec'),context)

def pose(f,code,previous=None):
 phase=f/120;chest=P['上半身2'];large=code=='oxygen_reserve'
 radius=.051 if large else .037;height=.2968 if large else .1908
 lift=sm(0,.18,phase)*(1-sm(.83,1,phase));turn=sm(.22,.36,phase)*(1-sm(.70,.84,phase))
 up=Vector((0,1,0));normal=Quaternion(up,math.radians(-18))@Vector((-1,0,0));forward=up.cross(normal).normalized()
 right=hand_rotation('R',forward,up);delta=right@R['手首.R'].inverted()
 root=chest+Vector((.09,-.08+.02*lift,.17+.07*lift-(.035 if large else 0)))
 if large and active_rig!='DollSinger':root.y-=.05
 grip=root+Vector((0,-.012,0));wrist=grip-delta@(P['中指１.R']-P['手首.R'])*.85-normal*(radius+.007)
 valve=root+up*(height*.5-.010)
 left=Quaternion(up,math.radians(38)*turn)@hand_rotation('L',Vector((.65,.23 if active_rig!='DollSinger' else 0,.759934)),Vector((.759934,0,-.65)))
 ld=left@R['手首.L'].inverted();left_normal=(ld@(P['人指１.L']-P['小指１.L'])).cross(ld@(P['中指１.L']-P['手首.L'])).normalized()
 left_wrist=valve-ld@(P['人指１.L']-P['手首.L'])-left_normal*.022
 world={'手首.R':tupleq(right),'手首.L':tupleq(left)};starts,_=bind.evaluate()
 for side,point,guide in [('R',wrist,chest+Vector((.25,-.17,.10))),('L',left_wrist,chest+Vector((-.24,-.12,.10)))]:
  solver.arm(side,tuple(point),tuple(guide),world,starts,previous=(previous or {}).get(side),anatomical_wrist_limit=55,elbow_max_y=P['腕.'+side].y-.015)
 cup={n:readq(q) for n,q in bind.r.items()};cup['手首.R']=right;cup['手首.L']=left
 context['fingers']('R','grip',cup,overrides={d:[25,42,28] for d in ('人指','中指','薬指','小指')})
 context['fingers']('L','cup',cup,openness=.4,overrides={d:[35,55,30] for d in ('中指','薬指','小指')})
 for n,q in cup.items():
  if n.startswith(('人指','中指','薬指','小指','親指')) and not n.endswith(('先.L','先.R')):world[n]=tupleq(q)
 for digit in ('人指','中指','薬指','小指'):
  wrap_cylinder(world,[digit+n+'.R' for n in ('１','２','３','先')],root,radius+.007,right)
 thumb_on_bottle(bind.local,world,grip,up,forward,normal,target=grip+up*.033-forward*(radius*.85)+normal*.012)
 pinch=Quaternion(up,math.radians(38)*turn)
 for digit,offset in [('人指',Vector((.018,.003,.013))),('親指',Vector((-.018,.003,.013)))]:
  chain=['０','１','２','先'] if digit=='親指' else ['１','２','３','先']
  arc_chain(bind.local,world,[digit+n+'.L' for n in chain],valve+pinch@offset,Vector((0,0,1)))
 points,rotations=bind.evaluate(world=world)
 # The inventory visual is centred in metre space; Blender FBX arrives with its base at Z=0.
 return points,rotations,root,Quaternion(),{s:{n:rotations[n] for n in ('腕.'+s,'ひじ.'+s)} for s in ('L','R')}

def wrap_cylinder(world,chain,center,radius,hand):
 points,_=bind.evaluate(world=world);previous=Vector(points[chain[0]])
 for name,child in zip(chain,chain[1:]):
  v=previous-center;a=math.hypot(v.x,v.z);length=(P[child]-P[name]).length
  r=max(radius,a-length*.7)
  angle=math.atan2(v.z,v.x)+math.acos(max(-1,min(1,(a*a+r*r-length*length)/(2*a*r))))
  target=center+Vector((r*math.cos(angle),v.y,r*math.sin(angle)))
  existing=hand@R['手首.R'].inverted()@R[name]
  old=existing@R[name].inverted()@(P[child]-P[name])
  world[name]=tupleq(old.rotation_difference(target-previous)@existing)
  previous=target

def setup():
 global rig,BR,prop,action,previous,report
 if OUT.exists():raise RuntimeError('Oxygen source already exists; edit it explicitly instead of rebuilding.')
 configure('DollSinger');rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
 for track in rig.animation_data.nla_tracks:track.mute=True
 action=bpy.data.actions.new('OxygenRefill_Valve_2s');action.use_fake_user=True;rig.animation_data.action=action
 prop=bpy.data.objects['oxygen_canister'];prop.name='GEO-OxygenRefill-Canister'
 prop.data=prop.data.copy()
 for vertex in prop.data.vertices:vertex.co.z-=.0954
 prop.rotation_mode='QUATERNION';prop.animation_data_clear()
 for obj in bpy.context.scene.objects:
  if obj.get('source_part') or obj.name.startswith('Using_'):obj.hide_set(True)
 bpy.context.scene.render.fps=60;bpy.context.scene.frame_start=1;bpy.context.scene.frame_end=121
 previous={};report=[]
 print('Oxygen source setup: separate take, actual 74 x 191 mm inventory model')

def author_range(start,end):
 global previous,report
 if start==0:previous={};report=[]
 for f in range(start,end):
  bpy.context.scene.frame_set(f+1);points,rotations,rp,rq,previous=pose(f,'oxygen_canister',previous)
  install(rotations)
  for n in names:rig.pose.bones[n].keyframe_insert('rotation_quaternion',frame=f+1,group=n)
  prop.location=C.transposed()@rp;prop.rotation_quaternion=(C.transposed()@rq.to_matrix()@C).to_quaternion()
  prop.keyframe_insert('location',frame=f+1);prop.keyframe_insert('rotation_quaternion',frame=f+1)
  wrists={s:math.degrees((Vector(points['手首.'+s])-Vector(points['ひじ.'+s])).angle(Vector(points['中指１.'+s])-Vector(points['手首.'+s]))) for s in ('L','R')}
  report.append({'frame':f+1,'wrist':wrists})
 print(json.dumps({'frames':len(report),'wristMax':{s:max(r['wrist'][s] for r in report) for s in ('L','R')}}))

def finish_source():
 for obj in (rig,prop):
  for layer in obj.animation_data.action.layers:
   for strip in layer.strips:
    for bag in strip.channelbags:
     for curve in bag.fcurves:
      for key in curve.keyframe_points:key.interpolation='LINEAR'
 # Remove quaternion sign jumps in the actual saved source curves.
 for layer in action.layers:
  for strip in layer.strips:
   for bag in strip.channelbags:
    groups={}
    for curve in bag.fcurves:
     if curve.data_path.endswith('rotation_quaternion'):groups.setdefault(curve.data_path,{})[curve.array_index]=curve
    for curves in groups.values():
     if len(curves)!=4:continue
     previous_q=None
     for i in range(121):
      q=[curves[j].keyframe_points[i].co.y for j in range(4)]
      if previous_q is not None and sum(a*b for a,b in zip(q,previous_q))<0:
       q=[-v for v in q]
       for j in range(4):curves[j].keyframe_points[i].co.y=q[j]
      previous_q=q
 bpy.context.scene.frame_set(61);bpy.context.scene.camera=bpy.data.objects['Review_Front']
 bpy.context.preferences.filepaths.save_version=0
 bpy.ops.wm.save_as_mainfile(filepath=str(OUT),compress=True)
 DATA.mkdir(parents=True,exist_ok=True)
 (DATA/'Authoring.json').write_text(json.dumps({'frames':121,'seconds':2,'sourceSha256':hashlib.sha256(OUT.read_bytes()).hexdigest(),'maximumWristDegrees':{s:max(r['wrist'][s] for r in report) for s in ('L','R')}},indent=2))
 print(str(OUT))


def export_runtime():
 """Read back the saved source, then adapt only oxygen takes to other rigs."""
 global rig,BR,prop
 if Path(bpy.data.filepath)!=OUT:raise RuntimeError('Export requires the dedicated saved oxygen source')
 rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
 prop=bpy.data.objects['GEO-OxygenRefill-Canister']
 configure('DollSinger');source=[];error=0;planned_previous={}
 for f in range(121):
  bpy.context.scene.frame_set(f+1);bpy.context.view_layer.update();world={}
  for n in bind.order:
   if n not in rig.pose.bones:continue
   current=(rig.matrix_world@rig.pose.bones[n].matrix).to_quaternion();delta=current@BR[n].inverted()
   q=(C@delta.to_matrix()@C.transposed()).to_quaternion()@R[n];world[n]=tupleq(q)
  points,rotations=bind.evaluate(world=world);planned,_,_,_,planned_previous=pose(f,'oxygen_canister',planned_previous)
  error=max(error,max((Vector(points[n])-Vector(planned[n])).length for n in names))
  source.append((points,rotations,C@prop.matrix_world.translation,(C@prop.matrix_world.to_quaternion().to_matrix()@C.transposed()).to_quaternion()))
 if error>.00001:raise RuntimeError('Saved Blender pose diverges: '+str(error))
 source_hash=hashlib.sha256(OUT.read_bytes()).hexdigest();summary=[]
 for rig_name in ('DollSinger','HatsuneMiku','Shian'):
  configure(rig_name)
  path=ROOT/f'Assets/Resources/Moonkov/Actions/{rig_name}.json';library=json.loads(path.read_text())
  library['motions']=[m for m in library['motions'] if m['action']!='RefillOxygen']
  for code in ('oxygen_canister','oxygen_reserve'):
   motion={'action':'RefillOxygen','items':[code],'seconds':2,'frames':121,'openMarker':.2,'contact':.45,'sourceSha256':source_hash,'source':'OxygenRefill.blend / OxygenRefill_Valve_2s','rotations':[],'parts':[],'root':{'positions':[],'rotations':[]}}
   previous={};minimum=float('inf');maximum_wrist=0
   for f in range(121):
    p,q,rp,rq,previous=pose(f,code,previous)
    if rig_name=='DollSinger' and code=='oxygen_canister':p,q,rp,rq=source[f]
    for n in library['bones']:
     value=qmul(qinverse(q[bind.parent[n]]),q[n]);motion['rotations'].extend(round(v,7) for v in value)
    hand=readq(q['手首.R']);relative=hand.inverted()@(Vector(rp)-Vector(p['手首.R']))
    motion['root']['positions'].extend(round(v,7) for v in relative)
    motion['root']['rotations'].extend(round(v,7) for v in tupleq(hand.inverted()@rq))
    radius=.051 if code=='oxygen_reserve' else .037
    for digit in ('人指','中指','薬指','小指'):
     for n in ('１','２','３','先'):
      v=Vector(p[digit+n+'.R'])-Vector(rp);minimum=min(minimum,math.hypot(v.x,v.z)-radius)
    for side in ('L','R'):
     a=Vector(p['手首.'+side])-Vector(p['ひじ.'+side]);b=Vector(p['中指１.'+side])-Vector(p['手首.'+side]);maximum_wrist=max(maximum_wrist,math.degrees(a.angle(b)))
   if minimum<-.001:raise RuntimeError((rig_name,code,'Finger axis entered canister',minimum))
   library['motions'].append(motion);summary.append({'rig':rig_name,'code':code,'minimumFingerAxisClearanceMm':minimum*1000,'maximumWristDegrees':maximum_wrist})
  path.write_text(json.dumps(library,separators=(',',':'))+'\n')
 configure('DollSinger');bpy.context.scene.frame_set(61)
 (DATA/'RuntimeValidation.json').write_text(json.dumps({'sourceSha256':source_hash,'savedPoseMaxErrorMm':error*1000,'motions':summary},indent=2))
 print(json.dumps({'savedPoseMaxErrorMm':error*1000,'motions':summary}))
