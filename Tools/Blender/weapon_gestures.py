"""Blender 5.x. Import the production FBX, author whole-chain actions and bake exact-rig rotations.
Run: Blender --background --python Tools/Blender/weapon_gestures.py
No Humanoid muscle conversion: preserve the MMD twist bones and original bind axes.
"""
import bpy, json, math, os, sys, hashlib
from mathutils import Vector, Quaternion, Matrix
from mathutils.bvhtree import BVHTree
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
SOURCE=ROOT+'/Assets/DollSinger/Art/Model/LuoTianYi_DollSinger.fbx'
OUT=ROOT+'/Assets/DollSinger/Animation/Blender/WeaponGestures.json'
EXPORT_ONLY='--export-only' in sys.argv
REVISE_OTHERS='--revise-other-weapons' in sys.argv
accepted=json.load(open(OUT)) if REVISE_OTHERS else None
if EXPORT_ONLY or REVISE_OTHERS:
 bpy.ops.wm.open_mainfile(filepath=ROOT+'/ArtSource/DollSinger/WeaponGestures.blend')
else:
 bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
 bpy.ops.import_scene.fbx(filepath=SOURCE)
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
rig.name='DollSinger_GestureAuthoring'
U={r['name']:r for r in json.load(open(ROOT+'/Tools/Blender/DollSingerBind.json'))['bones']}
def qread(q):return Quaternion((q[3],q[0],q[1],q[2]))
R={n:qread(r['rotation']) for n,r in U.items()}
P={n:Vector(r['position']) for n,r in U.items()}
# Unity world = (-Blender X, Blender Z, -Blender Y), a handedness change.
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
BP={b.name:rig.matrix_world@b.head_local for b in rig.data.bones}
parents={b.name:U[b.name]['parent'] for b in rig.data.bones if b.name in U}
order=[b.name for b in rig.data.bones if b.name in U]
roots=['肩P.L','肩P.R']
def childof(n,root):
 while n in parents:
  if n==root:return True
  n=parents[n]
 return False
tracks=[n for n in order if any(childof(n,r) for r in roots)]
# Exact transform tracks include weighted twist helpers, all finger joints and elbow auxiliaries.
def norm(v):return Vector(v).normalized()
def frame(y,x):
 y=norm(y); x=(Vector(x)-y*Vector(x).dot(y)).normalized(); z=x.cross(y)
 return Matrix(((x.x,y.x,z.x),(x.y,y.y,z.y),(x.z,y.z,z.z))).to_quaternion()
def rotbetween(a,b):return norm(a).rotation_difference(norm(b))
def position(n,G):
 if n not in parents or parents[n] not in P:return P[n]
 p=parents[n]
 return position(p,G)+(G.get(p,R[p])@R[p].inverted())@(P[n]-P[p])
def setdir(n,end,d,G):
 current=G.get(n,R[n]); existing=(current@R[n].inverted())@(P[end]-P[n])
 G[n]=rotbetween(existing,d)@current

def hand(side,fingers,thumbside,G):
 h='手首.'+side; m='中指１.'+side;i='人指１.'+side;l='小指１.'+side
 G[h]=frame(fingers,thumbside)@frame(P[m]-P[h],P[i]-P[l]).inverted()@R[h]

def fingers(side,kind,G,openness=0,overrides=None,thumb_drop=0):
 h='手首.'+side
 basis=G[h]@R[h].inverted()
 f=norm(basis@(P['中指１.'+side]-P[h]))
 x=basis@(P['人指１.'+side]-P['小指１.'+side]);x=norm(x-f*x.dot(f))
 # Palm flexion is toward the palm, independent of handedness.
 palm=norm(x.cross(f))*(1 if side=='L' else -1)
 for digit in ['人指','中指','薬指','小指']:
  straight=kind=='open' or (digit=='人指' and kind in ['gun','frame']) or (digit in ['人指','中指'] and kind in ['sword','vow']) or (digit in ['人指','小指'] and kind=='horn')
  curl=[0,0,0] if straight else ([22,30,20] if kind=='cup' else [62,85,65])
  if kind=='cup': spread={'人指':-15,'中指':-5,'薬指':8,'小指':19}[digit]
  else: spread=0
  # Slight spread in the fan/slap hand; fingertips remain parallel on the viewfinder edges.
  if kind=='open':spread={'人指':-8,'中指':0,'薬指':7,'小指':13}[digit]
  if kind=='vow':spread={'人指':-9,'中指':7,'薬指':0,'小指':0}[digit]*(1 if side=='L' else -1)
  if kind=='horn':
   curl=[0,0,0] if digit in ['人指','小指'] else [65*(1-openness),90*(1-openness),55*(1-openness)]
   spread=({'人指':-6,'中指':0,'薬指':0,'小指':8}[digit]*(1-openness)+{'人指':-24,'中指':-7,'薬指':10,'小指':27}[digit]*openness)*(1 if side=='L' else -1)
  if kind=='grip':curl=[52-15*openness,75-20*openness,48-15*openness]
  if overrides and digit in overrides:curl=overrides[digit]
  cumulative=0
  for j in range(1,4):
   n=digit+str(j).translate(str.maketrans('123','１２３'))+'.'+side
   end=digit+(str(j+1).translate(str.maketrans('234','２３先')))+'.'+side if j<3 else digit+'先.'+side
   cumulative+=curl[j-1]
   a=math.radians(cumulative)
   d=(f*math.cos(a)+palm*math.sin(a))
   d=Quaternion(palm,math.radians(spread))@d
   G[n]=frame(d,x)@frame(P[end]-P[n],P['人指１.'+side]-P['小指１.'+side]).inverted()@R[n]
 # Thumb's three joints have different bind axes. Aim each phalanx rather than applying Euler guesses.
 if kind=='frame': dirs=[norm(x*.65+f*.76),x,x]
 elif kind=='gun': dirs=[norm(x*.98+f*(.12+.28*thumb_drop)),norm(x+f*.45*thumb_drop),norm(x+f*.45*thumb_drop)]
 elif kind in ['sword','vow','horn','grip']: dirs=[norm(x*.6+f*.3+palm*.4),norm(f*.35+palm*.9),norm(palm)]
 else:dirs=[norm(x*.8+f*.6),norm(x*.8+f*.6),norm(x*.65+f*.7)]
 if kind=='grip':dirs=[norm(x*.4+f*.92),f,f]
 if kind=='horn':
  closed=[norm(x*.1+f*.3+palm),norm(-x*.9+f*.2+palm*.5),norm(-x*.9+palm*.4)]
  # Unfold around the closed knuckles, rather than cutting through them.
  lift=.9*math.sin(math.pi*openness)**.7
  dirs=[(d.lerp(norm(x*.8+f*.6),openness)+palm*lift).normalized() for d in closed]
 for k,d in enumerate(dirs):
  n='親指'+['０','１','２'][k]+'.'+side;end='親指'+['１','２','先'][k]+'.'+side
  if kind=='frame':
   G[n]=frame(d,palm)@frame(P[end]-P[n],(P['人指１.'+side]-P['小指１.'+side]).cross(P['中指１.'+side]-P[h])).inverted()@R[n]
  else:
   # Transport the actual thumb bind axes through the parent. A secondary
   # frame axis parallel to a flexed phalanx has no defined roll and can flip.
   parent=parents[n];G[n]=G[parent]@R[parent].inverted()@R[n]
   setdir(n,end,d,G)

# Sleeve clearance against the actual rest chest surface, used only by the
# authored pitch variants. Runtime keeps the resulting bones without re-solving.
body=next(o for o in bpy.data.objects if o.type=='MESH' and len(o.vertex_groups)>100)
bodyNames=[max([(body.vertex_groups[g.group].name,g.weight) for g in v.groups],key=lambda pair:pair[1])[0] if v.groups else '' for v in body.data.vertices]
bodyVertices=[body.matrix_world@v.co for v in body.data.vertices]
chestFaces=[tuple(poly.vertices) for poly in body.data.polygons if all(bodyNames[v].startswith(('胸','上半身')) for v in poly.vertices)]
chestSurface=BVHTree.FromPolygons(bodyVertices,chestFaces,all_triangles=True)
headFaces=[tuple(poly.vertices) for poly in body.data.polygons if all(childof(bodyNames[v],'首') for v in poly.vertices)]
headSurfaces={}
def head_surface(pitch):
 # Offline clearance proxy for the upright head look used by the production
 # player. Neck pitch is approximate; the actual Unity skin is checked separately.
 if pitch not in headSurfaces:
  neck=Quaternion(Vector((1,0,0)),math.radians(10.5+.6*pitch))
  head=Quaternion(Vector((1,0,0)),math.radians(pitch))
  origin=P['首']+neck@(P['頭']-P['首'])
  vertices=[origin+head@(C@v-P['頭']) for v in bodyVertices]
  headSurfaces[pitch]=BVHTree.FromPolygons(vertices,headFaces,all_triangles=True)
 return headSurfaces[pitch]

def head_penalty(surface,elbow,wrist):
 return sum(max(0,.055-surface.find_nearest(elbow.lerp(wrist,t))[3])*1800
            for t in [i/12 for i in range(13)])
def chest_penalty(elbow,wrist):
 # Query the front silhouette rather than normals: layered clothing includes
 # opposite-facing surfaces. Expand the query by the sleeve's visible radius.
 penalty=0
 for t in [.1,.25,.4,.55,.7,.85]:
  point=elbow.lerp(wrist,t)
  for ox,oy in [(0,0),(.05,0),(-.05,0),(0,.05),(0,-.05)]:
   origin=C.transposed()@Vector((point.x+ox,point.y+oy,2))
   hit,_,_,_=chestSurface.ray_cast(origin,C.transposed()@Vector((0,0,-1)),4)
   if hit is not None:
    front=(C@hit).z
    penalty+=max(0,front+.050-point.z)*1200
 return penalty

def arm(side,wrist,guide,G,maxroll=85,headSurface=None):
 """Offline elbow-circle solve with a fixed hinge. Forearm roll uses the actual weighted twist chain."""
 upper='腕.'+side; lower='ひじ.'+side; h='手首.'+side; twist='手捩.'+side; ut='腕捩.'+side
 start=position(upper,G);wrist=Vector(wrist);a=(P[lower]-P[upper]).length;b=(P[h]-P[lower]).length
 v=wrist-start;d=v.length
 if d>=a+b-.003:raise ValueError(('unreachable',side,d,a+b,wrist[:]))
 axis=v.normalized();along=(a*a-b*b+d*d)/(2*d);height=math.sqrt(max(0,a*a-along*along))
 pole=(Vector(guide)-start);pole=(pole-axis*pole.dot(axis)).normalized()
 restU=(P[lower]-P[upper]).normalized();restL=(P[h]-P[lower]).normalized()
 restSide=(P['人指１.'+side]-P['小指１.'+side]).normalized()
 restN=(restSide-restU*restSide.dot(restU)).normalized()
 if restN.dot(restU.cross(Vector((0,0,1))))<0:restN=-restN
 best=None
 for degrees in range(-75,76,3):
  bend=Quaternion(axis,math.radians(degrees))@pole
  elbow=start+axis*along+bend*height
  u=(elbow-start).normalized();l=(wrist-elbow).normalized();normal=u.cross(l).normalized()
  upperRot=frame(u,normal)@frame(restU,restN).inverted()@R[upper]
  baseLower=frame(l,normal)@frame(restL,restN).inverted()@R[lower]
  expected=baseLower@R[lower].inverted()@R[h]
  delta=G[h]@expected.inverted();v=Vector((delta.x,delta.y,delta.z));proj=l*v.dot(l)
  tq=Quaternion((delta.w,proj.x,proj.y,proj.z)).normalized()
  if tq.w<0:tq.negate()
  raw=math.degrees(tq.angle)
  applied=tq if raw<=maxroll else Quaternion(tq.axis,math.radians(maxroll))
  remainder=G[h]@(applied@expected).inverted()
  if remainder.w<0:remainder.negate()
  wristAngle=math.degrees(remainder.angle)
  score=(wristAngle/42)**4+(raw/maxroll)**4+.20*(degrees/55)**4
  # Reject an elbow folded through the ribs or raised into the head.
  score+=max(0,.115-abs(elbow.x))*200+max(0,elbow.y-1.31)*200
  if maxroll>85:score+=chest_penalty(elbow,wrist)
  if headSurface:score+=head_penalty(headSurface,elbow,wrist)
  if best is None or score<best[0]:best=(score,upperRot,baseLower,applied)
 _,upperRot,baseLower,tq=best
 G[upper]=upperRot
 G[ut]=(upperRot@R[upper].inverted())@R[ut]
 G[lower]=Quaternion().slerp(tq,.5)@baseLower
 G[twist]=tq@baseLower@R[lower].inverted()@R[twist]
 # Helpers not explicitly authored inherit their immediate parent's rest-relative orientation.


def bell(phase,peak):
 # Smooth attack and recovery; zero endpoint velocity avoids a reset pop on repeated shots.
 t=max(0,min(1,phase/peak if phase<=peak else (1-phase)/(1-peak)))
 return t*t*(3-2*t)

def make_pose(weapon,phase=0,mode='pose',fan_scale=1):
 G={}
 if weapon==4:
  # Opposite Ls. Both long edges share one plane and two fingertip contacts.
  hand('L',(1,0,0),(0,-1,0),G);hand('R',(-1,0,0),(0,1,0),G)
  fingers('L','frame',G);fingers('R','frame',G)
  # Determine the opposite wrist separation directly from actual shaped index/thumb tips.
  # For symmetric hands the two contact equations coincide; their mean rejects tiny bind asymmetry.
  def rel(n,s):return position(n,G)-position('手首.'+s,G)
  dl=rel('人指先.L','L')-rel('親指先.R','R');dr=rel('親指先.L','L')-rel('人指先.R','R')
  separation=(dl+dr)*.5
  center=Vector((0,1.19,.17))
  wl=center-separation*.5;wr=center+separation*.5
  # Reload opens the frame with a short iris gesture; fire keeps both corners closed.
  k=math.sin(math.pi*phase) if mode=='reload' else 0
  wl+=Vector((-.035,-.02,.0))*k;wr+=Vector((.035,.02,0))*k
  if mode=='fire':
   kick=math.sin(math.pi*min(1,phase*2))*math.exp(-phase*3)
   wl.z-=.012*kick;wr.z-=.012*kick
  arm('L',wl,(-.27,1.18,.08),G);arm('R',wr,(.27,1.08,.08),G)
 elif weapon==2:
  # Judgment fan: a canted finger pistol, with a split sword-hand resting over the hammer.
  sweep=bell(phase,.28) if mode=='fire' else 0
  flourish=math.sin(math.pi*phase) if mode=='reload' else 0
  recover=fan_scale*math.sin(math.pi*max(0,(phase-.42)/.58)) if mode=='fire' and phase>.42 else 0
  rf=Quaternion(Vector((0,0,1)),math.radians(18*flourish))@Vector((0,0,1))
  lf=Quaternion(Vector((0,0,1)),math.radians(-12*sweep-12*recover))@Vector((.98,.25,.16))
  hand('R',rf,(0,1,-.2),G);fingers('R','gun',G,thumb_drop=sweep)
  hand('L',lf,(0,.85,-.35),G);fingers('L','vow',G)
  wr=Vector((.060,1.125,.232))
  if mode=='fire':wr+=Vector((.006,.019,-.019))*bell(phase,.34)
  if mode=='reload':wr+=Vector((.008,-.012,-.012))*flourish*fan_scale
  # Place the actual sword fingertips over the authored thumb, rather than swatting past the pistol.
  hammer=position('親指先.R',G)-position('手首.R',G)
  blade=(position('人指先.L',G)+position('中指先.L',G))*.5-position('手首.L',G)
  wl=wr+hammer-blade+Vector((-.006,.020,-.028))
  if mode=='fire':wl+=Vector((.020,-.009,-.012))*sweep+Vector((-.030,.018,-.015))*recover
  if mode=='reload':wl+=Vector((.035*(math.cos(phase*math.tau)-1),.028*math.sin(phase*math.tau),-.018*flourish))*fan_scale
  arm('R',wr,(.23,1.01,.08),G);arm('L',wl,(-.25,1.20,.08),G)
 elif weapon==1:
  # Thunder seal: crossed sword fingers. The right middle finger snaps as the blade cuts forward.
  strike=bell(phase,.30) if mode=='fire' else 0
  gather=math.sin(math.pi*phase) if mode=='reload' else 0
  rf=Vector((-.65,.75,.10)).lerp(Vector((0,0,1)),strike)
  lf=Quaternion(Vector((0,0,1)),math.radians(22*gather))@Vector((.65,.75,.10))
  hand('R',rf,(.75,.65,0),G);hand('L',lf,(-.75,.65,0),G)
  snap=math.sin(math.pi*phase/.48)**2 if mode=='fire' and phase<.48 else 0
  fingers('R','sword',G,overrides={'中指':[30*snap,55*snap,25*snap]})
  fingers('L','sword',G)
  wr=Vector((.050,1.125,.255));wl=Vector((-.050,1.125,.200))
  wr+=Vector((.012,-.008,.025))*strike
  wl+=Vector((-.004,.008,-.005))*strike
  if mode=='reload':
   wl+=Vector((-.035,.025,-.015))*gather
   wr+=Vector((.015,-.025,-.012))*gather
  arm('R',wr,(.23,1.06,.06),G);arm('L',wl,(-.23,1.06,.06),G)
 else:
  # Demon cannon: an unmistakable horn sign, braced at the wrist; five fingers burst open on release.
  burst=bell(phase,.20) if mode=='fire' else 0
  pump=math.sin(math.pi*phase) if mode=='reload' else 0
  rf=Quaternion(Vector((0,0,1)),math.radians(-8*burst))@Vector((-.20,.70,.68))
  rf=rf.lerp(Vector((-.15,.88,.45)),pump*.35)
  hand('R',rf,(1,0,.12),G);fingers('R','horn',G,openness=burst)
  hand('L',(.9,.30,.30),(0,1,-.2),G);fingers('L','grip',G,openness=pump)
  wr=Vector((.055,1.135,.235))+Vector((-.010,.025,-.032))*burst+Vector((.005,-.015,-.015))*pump
  wl=wr+Vector((-.065,-.035,.060))+Vector((.008,-.025,-.012))*pump
  arm('R',wr,(.30,1.12,.30),G);arm('L',wl,(-.22,1.02,.07),G)
 # Fill all descendants in parent order, including zero-weight helper nodes.
 for n in order:
  if n not in G:
   p=parents[n]
   G[n]=G[p]@R[p].inverted()@R[n] if p in G else R[n]
 return G

def make_pitched_pose(weapon,phase,mode,pitch):
 # Author the complete arm chain for looking down; keep both hands as one
 # rigid authored arrangement, then route elbows around the torso/skirt.
 base=make_pose(weapon,phase,mode,fan_scale=1-.75*max(0,pitch)/80)
 rotation=Quaternion(Vector((1,0,0)),math.radians(pitch))
 handPitch=min(pitch,65) if weapon==3 and pitch>0 else pitch
 limit=-30 if weapon==1 else -60
 if pitch < limit and weapon!=2:
  forward=bell(phase,.30) if weapon==1 and mode=='fire' else 0
  handPitch=limit+(pitch-limit)*forward
 handRotation=Quaternion(Vector((1,0,0)),math.radians(handPitch))
 pivot=(P['肩P.L']+P['肩P.R'])*.5
 amount=(max(0,pitch)/65)**2
 offset=Vector((0,.075*min(1,max(0,pitch)/65),.170*amount))+Vector((0,0,.120))*(max(0,-pitch)/80)**4
 offset.z+=.024*math.sin(math.pi*max(0,pitch)/80)
 if weapon==1:offset+=Vector((0,.08,.04))*math.sin(math.pi*max(0,pitch)/80)
 if weapon==2:offset+=Vector((0,-.04,.04))*(max(0,pitch)/80)**4
 if weapon==3:
  offset+=Vector((-.08,.04,0))*max(0,pitch)/80
  offset.x+=.05*max(0,(pitch-65)/15)
 center=(position('手首.L',base)+position('手首.R',base))*.5
 centerTarget=pivot+rotation@(center-pivot)+offset
 targets={side:centerTarget+handRotation@(position('手首.'+side,base)-center) for side in ['L','R']}
 if weapon==2 and pitch>0:
  # At steep downward aim, fan along the hammer instead of bending the wrist
  # sideways across an extended forearm. Preserve the actual blade contact path.
  contact=(position('人指先.L',base)+position('中指先.L',base))*.5
  yaw=Quaternion(Vector((0,1,0)),math.radians(-15*pitch/80))
  for n in order:
   if childof(n,'手首.L'):base[n]=yaw@base[n]
  blade=(position('人指先.L',base)+position('中指先.L',base))*.5-position('手首.L',base)
  targets['L']=centerTarget+handRotation@(contact-center)-handRotation@blade
 # Keep the paired hands within physical reach without changing their shape.
 for attempt in range(100):
  if all((targets[side]-P['腕.'+side]).length < .331 for side in ['L','R']):break
  for side in ['L','R']:targets[side].z-=.002
 else:raise ValueError(('Paired hand reach failed',weapon,mode,phase,pitch))
 G={n:handRotation@base[n] for n in order if any(childof(n,'手首.'+side) for side in ['L','R'])}
 for side in ['L','R']:
  spread=.25 if weapon==2 and pitch>0 else (.45 if weapon==3 and pitch>0 else .29)
  guideY=(.90 if weapon==2 else .92) if weapon in [2,3] and side=='L' and pitch>0 else 1.05
  # The fan and cannon use low, outward elbow guides. The sword seal needs
  # additional head clearance while its two forearms cross.
  arm(side,targets[side],(-spread if side=='L' else .29,guideY,.22),G,maxroll=100,
      headSurface=head_surface(pitch) if weapon==1 else None)
 for n in order:
  if n not in G:
   parent=parents[n]
   G[n]=G[parent]@R[parent].inverted()@R[n] if parent in G else R[n]
 return G

# Record Blender actions first, then bake the evaluated Blender skeleton, not the design targets.
bpy.context.scene.render.fps=60
bpy.context.view_layer.objects.active=rig;rig.select_set(True)
rig.animation_data_create()
def install(G):
 for n in order:
  b=rig.pose.bones[n];p=parents[n]
  target=(C.transposed()@(G[n]@R[n].inverted()).to_matrix()@C).to_quaternion()@BR[n]
  parentTarget=(C.transposed()@(G[p]@R[p].inverted()).to_matrix()@C).to_quaternion()@BR[p] if p in BR and p in G else BR.get(p,Quaternion())
  localRest=(rig.data.bones[p].matrix_local.inverted()@b.bone.matrix_local).to_quaternion() if p in rig.data.bones else b.bone.matrix_local.to_quaternion()
  b.rotation_mode='QUATERNION';b.rotation_quaternion=localRest.inverted()@parentTarget.inverted()@target
 bpy.context.view_layer.update()
def bake():
 # Convert evaluated Blender world delta back into the production FBX's local axes.
 evaluated={}
 for n in order:
  delta=(rig.matrix_world@rig.pose.bones[n].matrix).to_quaternion()@BR[n].inverted()
  evaluated[n]=(C@delta.to_matrix()@C.transposed()).to_quaternion()@R[n]
 output=[]
 for n in tracks:
  p=parents[n];q=evaluated[p].inverted()@evaluated[n]
  output.extend([round(q.x,7),round(q.y,7),round(q.z,7),round(q.w,7)])
 return output
def author_motion(weapon,name,seconds,pitch=0):
 motion={'weapon':weapon,'name':name,'fireSeconds':seconds}
 if pitch:motion['pitch']=pitch
 for mode,frames in [('pose',1),('fire',round(seconds*60)+1),('reload',61)]:
  preserve=EXPORT_ONLY or (REVISE_OTHERS and weapon==4)
  existing=bpy.data.actions.get(name+'_'+mode)
  if REVISE_OTHERS and not preserve and existing:bpy.data.actions.remove(existing)
  action=existing if preserve else bpy.data.actions.new(name+'_'+mode)
  if action is None:raise ValueError('Missing source action: '+name+'_'+mode)
  action.use_fake_user=True;rig.animation_data.action=action
  values=[]
  for f in range(frames):
   bpy.context.scene.frame_set(f+1)
   if not preserve:
    phase=f/(frames-1) if frames>1 else 0
    install(make_pitched_pose(weapon,phase,mode,pitch) if pitch else make_pose(weapon,phase,mode))
    for n in tracks:rig.pose.bones[n].keyframe_insert('rotation_quaternion',frame=f+1,group=n)
   values.extend(bake())
  motion[mode]=values;motion[mode+'Frames']=frames
 print('AUTHORED',name,flush=True)
 return motion
motions=[]
for weapon,name,seconds in [(1,'Rifle_ThunderSeal',.18),(2,'Revolver_SingleActionFan',.42),(3,'Shotgun_Rupture',.6),(4,'Sniper_ClosedViewfinder',.7)]:
 motion=author_motion(weapon,name,seconds)
 if weapon!=4:
  motion['aimPoses']=[author_motion(weapon,name+'_Pitch'+str(pitch),seconds,pitch) for pitch in [-80,-65,-50,-40,-20,15,30,45,65,80]]
 motions.append(motion)
if accepted is not None:
 assert accepted['bones']==tracks and accepted['motions'][3]==motions[3], 'Accepted sniper must remain identical; export saved source first.'
data={'version':1,'frameRate':60,'bones':tracks,'chestBindRotation':dict(zip(['x','y','z','w'],U['上半身2']['rotation'])),'motions':motions}
data['revision']=hashlib.sha256(json.dumps(motions,separators=(',',':')).encode()).hexdigest()[:12]
rig['bake_revision']=data['revision']
open(OUT,'w').write(json.dumps(data,ensure_ascii=False,separators=(',',':')))
if EXPORT_ONLY:
 print('Exported saved Blender actions:',data['revision'],flush=True)
 sys.exit(0)
# Open the revised sword seal for review; the accepted sniper Actions stay untouched.
rig.animation_data.action=bpy.data.actions['Rifle_ThunderSeal_pose' if REVISE_OTHERS else 'Sniper_ClosedViewfinder_pose'];bpy.context.scene.frame_set(1)
# Cameras are convenient authoring aids; Unity uses its own production cameras.
for name,loc,target in ([] if REVISE_OTHERS else [('Owner',(0,1.31,-.15),(0,1.235,.235)),('Front',(0,1.25,1.25),(0,1.18,.12)),('Side',(1.1,1.25,.1),(0,1.18,.12)),('Back',(0,1.25,-1.1),(0,1.18,.12))]):
 pos=C.transposed()@Vector(loc);tar=C.transposed()@Vector(target)
 camera=bpy.data.cameras.new(name);obj=bpy.data.objects.new(name,camera);bpy.context.collection.objects.link(obj);obj.location=pos;obj.rotation_euler=(tar-pos).to_track_quat('-Z','Y').to_euler();camera.lens=45
 if name=='Front':bpy.context.scene.camera=obj
rig['authoring_notes']='Whole arms and weighted twist chain. Exact-rig bake; no Humanoid muscle retargeting. Actions: pose / fire / reload. 60 fps.'
bpy.context.scene.frame_end=61
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/ArtSource/DollSinger/WeaponGestures.blend')
print('BAKED',len(tracks),'bones',os.path.getsize(OUT),'bytes',flush=True)
