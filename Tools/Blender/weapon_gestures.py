"""Blender 5.x. Import the production FBX, author whole-chain actions and bake exact-rig rotations.
Run: Blender --background --python Tools/Blender/weapon_gestures.py
No Humanoid muscle conversion: preserve the MMD twist bones and original bind axes.
"""
import bpy, json, math, os, sys, hashlib
from mathutils import Vector, Quaternion, Matrix
ROOT=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
SOURCE=ROOT+'/Assets/DollSinger/Art/Model/LuoTianYi_DollSinger.fbx'
OUT=ROOT+'/Assets/DollSinger/Animation/Blender/WeaponGestures.json'
EXPORT_ONLY='--export-only' in sys.argv
if EXPORT_ONLY:
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

def fingers(side,kind,G):
 h='手首.'+side
 basis=G[h]@R[h].inverted()
 f=norm(basis@(P['中指１.'+side]-P[h]))
 x=basis@(P['人指１.'+side]-P['小指１.'+side]);x=norm(x-f*x.dot(f))
 # Palm flexion is toward the palm, independent of handedness.
 palm=norm(x.cross(f))*(1 if side=='L' else -1)
 for digit in ['人指','中指','薬指','小指']:
  straight=kind=='open' or (digit=='人指' and kind in ['gun','frame']) or (digit in ['人指','中指'] and kind=='sword')
  curl=[0,0,0] if straight else ([22,30,20] if kind=='cup' else [62,85,65])
  if kind=='cup': spread={'人指':-15,'中指':-5,'薬指':8,'小指':19}[digit]
  else: spread=0
  # Slight spread in the fan/slap hand; fingertips remain parallel on the viewfinder edges.
  if kind=='open':spread={'人指':-8,'中指':0,'薬指':7,'小指':13}[digit]
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
 elif kind=='gun': dirs=[norm(x*.95+f*.3),x,x]
 elif kind=='sword': dirs=[norm(x*.6+f*.3+palm*.4),norm(f*.35+palm*.9),norm(palm)]
 else:dirs=[norm(x*.8+f*.6),norm(x*.8+f*.6),norm(x*.65+f*.7)]
 for k,d in enumerate(dirs):
  n='親指'+['０','１','２'][k]+'.'+side;end='親指'+['１','２','先'][k]+'.'+side
  G[n]=frame(d,palm)@frame(P[end]-P[n],(P['人指１.'+side]-P['小指１.'+side]).cross(P['中指１.'+side]-P[h])).inverted()@R[n]

def arm(side,wrist,guide,G):
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
  applied=tq if raw<=85 else Quaternion(tq.axis,math.radians(85))
  remainder=G[h]@(applied@expected).inverted()
  if remainder.w<0:remainder.negate()
  wristAngle=math.degrees(remainder.angle)
  score=(wristAngle/42)**4+(raw/85)**4+.20*(degrees/55)**4
  # Reject an elbow folded through the ribs or raised into the head.
  score+=max(0,.115-abs(elbow.x))*200+max(0,elbow.y-1.31)*200
  if best is None or score<best[0]:best=(score,upperRot,baseLower,applied)
 _,upperRot,baseLower,tq=best
 G[upper]=upperRot
 G[ut]=(upperRot@R[upper].inverted())@R[ut]
 G[lower]=Quaternion().slerp(tq,.5)@baseLower
 G[twist]=tq@baseLower@R[lower].inverted()@R[twist]
 # Helpers not explicitly authored inherit their immediate parent's rest-relative orientation.


def make_pose(weapon,phase=0,mode='pose'):
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
  # Cocked finger gun + open offhand, poised above the imaginary hammer.
  hand('R',(.04,.12,1),(0,1,-.12),G);fingers('R','gun',G)
  hand('L',(.45,.15,.88),(.88,0,-.45),G);fingers('L','open',G)
  wr=Vector((.065,1.125,.245));wl=Vector((-.07,1.215,.175))
  if mode=='fire':
   # Complete single-action fan: open palm descends over thumb/hammer, then clears and recovers.
   sweep=math.sin(math.pi*min(1,phase*1.6)) if phase<.625 else 0
   recover=math.sin(math.pi*max(0,(phase-.625)/.375))
   wl+=Vector((.075,-.095,.025))*sweep+Vector((-.025,.012,-.015))*recover
   kick=math.sin(math.pi*min(1,phase*3))*math.exp(-phase*3)
   wr+=Vector((0,.012,-.018))*kick
  if mode=='reload':wl+=Vector((.02,-.065,.035))*math.sin(math.pi*phase)
  arm('R',wr,(.22,.99,.11),G);arm('L',wl,(-.24,1.10,.08),G)
 elif weapon==1:
  hand('R',(.15,.38,.91),(0,1,-.4),G);hand('L',(-.18,.72,.67),(-1,0,-.15),G)
  fingers('R','sword',G);fingers('L','sword',G)
  wr=Vector((.085,1.11,.24));wl=Vector((-.09,1.12,.22))
  if mode=='fire':
   k=math.sin(math.pi*min(1,phase*2))*math.exp(-phase*3);wr+=Vector((0,.008,-.012))*k;wl+=Vector((0,.004,-.006))*k
  if mode=='reload':wl+=Vector((-.025,-.035,-.02))*math.sin(math.pi*phase)
  arm('R',wr,(.22,.98,.08),G);arm('L',wl,(-.23,1.0,.08),G)
 else:
  hand('R',(-.2,.55,.82),(0,1,-.6),G);hand('L',(.2,.55,.82),(0,1,-.6),G)
  fingers('R','cup',G);fingers('L','cup',G)
  wr=Vector((.095,1.11,.22));wl=Vector((-.095,1.11,.22))
  if mode=='fire':
   k=math.sin(math.pi*phase)*math.exp(-phase*2);wr+=Vector((.012,.012,-.025))*k;wl+=Vector((-.012,.012,-.025))*k
  if mode=='reload':
   k=math.sin(math.pi*phase);wr+=Vector((.02,-.02,-.015))*k;wl+=Vector((-.02,-.02,.015))*k
  arm('R',wr,(.24,.98,.08),G);arm('L',wl,(-.24,.98,.08),G)
 # Fill all descendants in parent order, including zero-weight helper nodes.
 for n in order:
  if n not in G:
   p=parents[n]
   G[n]=G[p]@R[p].inverted()@R[n] if p in G else R[n]
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
motions=[]
for weapon,name,seconds in [(1,'Rifle_ThunderSeal',.18),(2,'Revolver_SingleActionFan',.38),(3,'Shotgun_Rupture',.6),(4,'Sniper_ClosedViewfinder',.7)]:
 motion={'weapon':weapon,'name':name,'fireSeconds':seconds}
 for mode,frames in [('pose',1),('fire',round(seconds*60)+1),('reload',61)]:
  action=bpy.data.actions.get(name+'_'+mode) if EXPORT_ONLY else bpy.data.actions.new(name+'_'+mode)
  if action is None:raise ValueError('Missing source action: '+name+'_'+mode)
  action.use_fake_user=True;rig.animation_data.action=action
  values=[]
  for f in range(frames):
   bpy.context.scene.frame_set(f+1)
   if not EXPORT_ONLY:
    install(make_pose(weapon, f/(frames-1) if frames>1 else 0,mode))
    for n in tracks:rig.pose.bones[n].keyframe_insert('rotation_quaternion',frame=f+1,group=n)
   values.extend(bake())
  motion[mode]=values;motion[mode+'Frames']=frames
 motions.append(motion)
 print('AUTHORED',name,flush=True)
data={'version':1,'frameRate':60,'bones':tracks,'chestBindRotation':dict(zip(['x','y','z','w'],U['上半身2']['rotation'])),'motions':motions}
data['revision']=hashlib.sha256(json.dumps(motions,separators=(',',':')).encode()).hexdigest()[:12]
rig['bake_revision']=data['revision']
open(OUT,'w').write(json.dumps(data,ensure_ascii=False,separators=(',',':')))
if EXPORT_ONLY:
 print('Exported saved Blender actions:',data['revision'],flush=True)
 sys.exit(0)
# Visible source opens on the closed frame with production mesh and a real preview camera.
rig.animation_data.action=bpy.data.actions['Sniper_ClosedViewfinder_pose'];bpy.context.scene.frame_set(1)
# Cameras are convenient authoring aids; Unity uses its own production cameras.
for name,loc,target in [('Owner',(0,1.31,-.15),(0,1.235,.235)),('Front',(0,1.25,1.25),(0,1.18,.12)),('Side',(1.1,1.25,.1),(0,1.18,.12)),('Back',(0,1.25,-1.1),(0,1.18,.12))]:
 pos=C.transposed()@Vector(loc);tar=C.transposed()@Vector(target)
 camera=bpy.data.cameras.new(name);obj=bpy.data.objects.new(name,camera);bpy.context.collection.objects.link(obj);obj.location=pos;obj.rotation_euler=(tar-pos).to_track_quat('-Z','Y').to_euler();camera.lens=45
 if name=='Front':bpy.context.scene.camera=obj
rig['authoring_notes']='Whole arms and weighted twist chain. Exact-rig bake; no Humanoid muscle retargeting. Actions: pose / fire / reload. 60 fps.'
bpy.context.scene.frame_end=61
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/ArtSource/DollSinger/WeaponGestures.blend')
print('BAKED',len(tracks),'bones',os.path.getsize(OUT),'bytes',flush=True)
