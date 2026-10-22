"""Independent exact-rig melee authoring; never writes an accepted firearm source/bake.
Blender --background --python Tools/Blender/melee_gestures.py [-- --export-only]
"""
import bpy,bmesh,json,math,sys,hashlib
from pathlib import Path
from mathutils import Vector,Quaternion,Matrix
ROOT=Path(__file__).resolve().parents[2]
EXPORT='--export-only' in sys.argv
DEST=ROOT/'ArtSource/DollSinger/MeleeGestures.blend'
OUT=ROOT/'Assets/DollSinger/Animation/Melee/Resources/MoonkovMeleeGestures.json'
ART=ROOT/'Assets/DollSinger/Art/Melee/Resources/MoonkovMeleeWeapons.json'
protected=[ROOT/'ArtSource/DollSinger/WeaponGestures.blend',ROOT/'ArtSource/DollSinger/HaloExpansion.blend',ROOT/'Assets/DollSinger/Animation/Blender/WeaponGestures.json']
sha={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected if p.exists()}
# Load existing bind-axis/weighted-twist authoring functions, without executing its action loop.
source=(ROOT/'Tools/Blender/weapon_gestures.py').read_text().split('def make_pose')[0]
if '--export-only' not in sys.argv:sys.argv.append('--export-only')
if EXPORT:source=source.replace('/ArtSource/DollSinger/WeaponGestures.blend','/ArtSource/DollSinger/MeleeGestures.blend')
exec(compile(source,'accepted-bind-helpers','exec'),globals())
export_helpers='def install(G):'+(Path(ROOT)/'Tools/Blender/weapon_gestures.py').read_text().split('def install(G):')[1].split('def author_motion')[0]
exec(compile(export_helpers,'accepted-export-helpers','exec'),globals())
bpy.context.scene.render.fps=60;rig.animation_data_create()
ROOT=Path(ROOT)
OUT=ROOT/'Assets/DollSinger/Animation/Melee/Resources/MoonkovMeleeGestures.json'
# Each timeline is source time only; runtime retimes its three segments to the shared rules.
TIMINGS={0:[(.14,.12,.24),(.12,.10,.22),(.20,.14,.30),(.36,.16,.48)],1:[(.15,.10,.20),(.13,.09,.18),(.21,.12,.26),(.35,.12,.40)],2:[(.22,.14,.30),(.20,.12,.28),(.28,.16,.36),(.52,.18,.60)]}
KINDS=['Fists','Dagger','EnergySword']
def smooth(x):x=max(0,min(1,x));return x*x*(3-2*x)
def vlerp(a,b,t):return Vector(a).lerp(Vector(b),t)
def orient(f,x):return frame(f,x)
def key(wr,fr,xr,wl=(-.105,1.13,.17),fl=(0,0,1),xl=(0,1,0)):
 return [Vector(wr),orient(fr,xr),Vector(wl),orient(fl,xl)]
def blend(a,b,t):return [a[0].lerp(b[0],t),a[1].slerp(b[1],t),a[2].lerp(b[2],t),a[3].slerp(b[3],t)]
def idle(kind):
 if kind==0:return key((.115,1.15,.17),(0,0,1),(0,1,0))
 if kind==1:return key((.115,1.115,.20),(.65,.1,.75),(0,.92,.40),(-.11,1.12,.17),(.45,.05,1),(-.2,1,0))
 return key((.105,1.135,.19),(.85,-.10,-.50),(.50,.60,.62),(-.12,1.10,.18),(.85,.2,.25),(0,1,-.1))
def attack_keys(kind,index):
 base=idle(kind)
 if kind==0:
  if index==0:return key((.12,1.15,.16),(0,0,1),(0,1,0),(-.16,1.14,.12),(0,0,1),(0,1,0)),key((.12,1.15,.16),(0,0,1),(0,1,0),(-.055,1.17,.365),(0,0,1),(0,1,0))
  if index==1:return key((.15,1.13,.12),(0,0,1),(0,1,0)),key((.055,1.16,.375),(0,0,1),(0,1,0))
  if index==2:return key((.115,1.14,.16),(0,0,1),(0,1,0),(-.20,1.16,.17),(.50,0,.86),(0,1,0)),key((.115,1.14,.16),(0,0,1),(0,1,0),(-.025,1.20,.30),(.90,0,.43),(0,1,0))
  return key((.15,1.08,.10),(0,.05,1),(0,1,0),(-.13,1.16,.16),(0,0,1),(0,1,0)),key((.025,1.18,.375),(0,.08,1),(0,1,0),(-.15,1.13,.15),(0,0,1),(0,1,0))
 if kind==1:
  if index==0:return key((.18,1.17,.15),(.9,0,.4),(.05,.8,.6)),key((-.065,1.13,.34),(.9,.05,.25),(.4,-.05,.92))
  if index==1:return key((-.07,1.14,.25),(.85,.05,.3),(.3,.8,.5)),key((.18,1.12,.32),(.85,-.05,-.2),(-.35,0,.94))
  if index==2:return key((.12,1.13,.12),(.9,0,.1),(0,.2,1)),key((.055,1.16,.375),(.9,0,-.1),(0,.05,1))
  return key((.15,1.19,.13),(.95,0,.1),(0,-.25,.97)),key((.035,1.125,.38),(.95,0,-.1),(0,-.15,.99),(-.12,1.15,.18),(.2,0,1),(0,1,0))
 if index==0:return key((.18,1.235,.135),(.85,0,.4),(-.2,.95,.25)),key((-.065,1.12,.33),(.75,.1,-.5),(.45,-.20,.87),(-.15,1.12,.18),(.9,.15,.2),(0,1,0))
 if index==1:return key((-.06,1.19,.23),(.7,.15,.5),(.2,.9,.3)),key((.19,1.14,.32),(.8,-.05,-.4),(-.4,-.1,.91))
 if index==2:return key((.10,1.14,.12),(.9,.05,.1),(0,.25,.96)),key((.04,1.17,.375),(.95,0,-.1),(0,.05,1))
 return key((.12,1.265,.13),(.85,0,.35),(0,.99,.10),(-.14,1.17,.18),(.9,.2,.15),(0,1,-.1)),key((.025,1.10,.365),(.9,0,.1),(0,-.35,.94),(-.15,1.11,.20),(.8,.1,.35),(0,1,-.1))
def controls(kind,action,time,timing):
 base=idle(kind)
 if action=='idle':return base
 lowered=blend(base,key((.14,1.015,.13),(.55,-.25,.8),(0,1,0),(-.14,1.015,.13),(.55,-.25,.8),(0,1,0)),1)
 if action=='equip':return blend(lowered,base,smooth(time/.28))
 if action=='holster':return blend(base,lowered,smooth(time/.24))
 if action=='recover':return blend(attack_keys(kind,3)[1],base,smooth(time/.30))
 index=3 if action=='heavy' else int(action[-1]);ready,end=attack_keys(kind,index)
 w,a,r=timing
 if time<w:return blend(base,ready,smooth(time/w))
 if time<w+a:return blend(ready,end,smooth((time-w)/a))
 return blend(end,base,smooth((time-w-a)/r))
def make_melee(kind,action,time,timing,pitch=0):
 wr,qr,wl,ql=controls(kind,action,time,timing)
 if kind==0:
  wr+=Vector((0,.035,.040));wl+=Vector((0,.035,.040))
 if pitch:
  # Author the complete chain offline at pitch variants; runtime never solves wrists.
  turn=Quaternion(Vector((1,0,0)),math.radians(pitch));pivot=Vector((0,1.23,.02))
  offset=Vector((0,.05*max(0,pitch)/70,.20*(max(0,pitch)/70)**2+.12*(max(0,-pitch)/70)**4))
  wr=pivot+turn@(wr-pivot)+offset;wl=pivot+turn@(wl-pivot)+offset;qr=turn@qr;ql=turn@ql
 for side,target in [('R',wr),('L',wl)]:
  shoulder=P['腕.'+side];delta=target-shoulder
  maximum=(P['ひじ.'+side]-shoulder).length+(P['手首.'+side]-P['ひじ.'+side]).length-.012
  if delta.length>maximum:target[:]=shoulder+delta.normalized()*maximum
 G={}
 for side,q in [('R',qr),('L',ql)]:
  h='手首.'+side;m='中指１.'+side;i='人指１.'+side;l='小指１.'+side
  G[h]=q@frame(P[m]-P[h],P[i]-P[l]).inverted()@R[h]
  if kind==0:
   fingers(side,'grip',G,overrides={d:[75,95,55] for d in ['人指','中指','薬指','小指']})
   h='手首.'+side;basis=G[h]@R[h].inverted();f=(basis@(P['中指１.'+side]-P[h])).normalized()
   x=basis@(P['人指１.'+side]-P['小指１.'+side]);x=(x-f*x.dot(f)).normalized();p=x.cross(f).normalized()*(1 if side=='L' else -1)
   for k,d in enumerate([(x*.1+f*.3+p).normalized(),(-x*.9+f*.2+p*.5).normalized(),(-x*.9+p*.4).normalized()]):
    n='親指'+['０','１','２'][k]+'.'+side;end='親指'+['１','２','先'][k]+'.'+side;parent=parents[n];G[n]=G[parent]@R[parent].inverted()@R[n];setdir(n,end,d,G)
  elif side=='R':fingers(side,'grip',G,overrides={d:[58,80,52] for d in ['人指','中指','薬指','小指']})
  else:fingers(side,'open',G,overrides={d:[18,22,12] for d in ['人指','中指','薬指','小指']})
 # Targets use reachable sub-maximal extensions and a stable lateral elbow guide.
 arm('R',wr,(.31,1.07,.06),G,maxroll=85,headSurface=head_surface(pitch) if pitch< -35 else None);arm('L',wl,(-.31,1.05,.06),G,maxroll=85,headSurface=head_surface(pitch) if pitch< -35 else None)
 for n in order:
  if n not in G:
   p=parents[n];G[n]=G[p]@R[p].inverted()@R[n] if p in G else R[n]
 return G

def mesh_box(name,center,size,color,emission=0,bevel=.003):
 bpy.ops.mesh.primitive_cube_add(size=1,location=C.transposed()@Vector(center));o=bpy.context.object;o.name=name
 # Mesh stored in canonical Unity space through the same exact coordinate map.
 for v in o.data.vertices:v.co=C.transposed()@Vector((v.co.x*size[0],v.co.y*size[1],v.co.z*size[2]))
 if bevel:
  mod=o.modifiers.new('Small edge bevel','BEVEL');mod.width=bevel;mod.segments=1
 o['melee_color']=color;o['melee_emission']=emission;return o

def make_props():
 for o in list(bpy.data.objects):
  if o.name.startswith('MeleeArt_'):bpy.data.objects.remove(o,do_unlink=True)
 # No imported third party models/textures; original low-poly solids.
 for kind in ['EnergySword','Dagger']:
  prefix='MeleeArt_'+kind+'_'
  length=.15 if kind=='EnergySword' else .115
  mesh_box(prefix+'Grip',(0,0,0),(.027,length,.033),[.055,.075,.10,1],bevel=.004)
  mesh_box(prefix+'Pommel',(0,-length*.55,0),(.041,.020,.045),[.36,.42,.48,1])
  mesh_box(prefix+'Guard',(0,length*.55,0),(.12 if kind=='EnergySword' else .080,.020,.037),[.38,.44,.51,1])
  if kind=='EnergySword':
   mesh_box(prefix+'Emitter',(0,.11,0),(.037,.070,.040),[.15,.21,.26,1])
   mesh_box(prefix+'Blade',(0,.495,0),(.028,.72,.009),[.40,.92,1,1],1,bevel=.003)
   mesh_box(prefix+'BrightCore',(0,.495,-.005),(.009,.70,.002),[.93,1,1,1],1,bevel=0)
  else:
   # Pointed solid blade with a central ridge; local +Y points along the blade.
   verts=[(-.018,.075,0),(.018,.075,0),(-.015,.22,0),(.015,.22,0),(0,.305,0),(0,.10,-.006),(0,.22,-.004),(0,.10,.006),(0,.22,.004)]
   faces=[(0,5,6,2),(2,6,4),(5,1,3,6),(6,3,4),(7,0,2,8),(8,2,4),(1,7,8,3),(3,8,4),(0,7,1,5)]
   data=bpy.data.meshes.new(prefix+'Blade');data.from_pydata([C.transposed()@Vector(v) for v in verts],[],faces);data.update()
   o=bpy.data.objects.new(prefix+'Blade',data);bpy.context.collection.objects.link(o);o['melee_color']=[.69,.75,.79,1];o['melee_emission']=0
 props=[]
 for kind in ['EnergySword','Dagger']:
  parts=[]
  for o in sorted((o for o in bpy.data.objects if o.name.startswith('MeleeArt_'+kind+'_')),key=lambda o:o.name):
   bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
   material=bpy.data.materials.new(o.name+'_Material');material.diffuse_color=tuple(o['melee_color']);material.use_nodes=True
   principled=material.node_tree.nodes.get('Principled BSDF');principled.inputs['Base Color'].default_value=tuple(o['melee_color']);principled.inputs['Roughness'].default_value=.32
   if o['melee_emission']:
    principled.inputs['Emission Color'].default_value=tuple(o['melee_color']);principled.inputs['Emission Strength'].default_value=1.2
   o.data.materials.clear();o.data.materials.append(material)
   dg=bpy.context.evaluated_depsgraph_get();ev=o.evaluated_get(dg);mesh=ev.to_mesh();mesh.calc_loop_triangles()
   vv=[];nn=[];indices=[]
   for tri in mesh.loop_triangles:
    for li in reversed(tri.loops):
     vi=mesh.loops[li].vertex_index;vv.extend(C@(o.matrix_world@mesh.vertices[vi].co));nn.extend((C@mesh.polygons[tri.polygon_index].normal).normalized());indices.append(len(indices))
   parts.append({'name':o.name.split('_')[-1],'vertices':[round(x,7) for x in vv],'normals':[round(x,7) for x in nn],'triangles':indices,'color':list(o['melee_color']),'emission':o['melee_emission']})
   ev.to_mesh_clear()
  props.append({'weapon':kind,'parts':parts})
 ART.parent.mkdir(parents=True,exist_ok=True);ART.write_text(json.dumps({'version':1,'weapons':props},separators=(',',':')))

def hold_authored_props(rig,grip_position,grip_rotation):
 # Store a real bone-parented grip in the author file, not a prop floating at the origin.
 bone=rig.pose.bones['手首.R'];world=rig.matrix_world@bone.matrix
 bind=(rig.matrix_world@bone.bone.matrix_local).to_quaternion();delta=world.to_quaternion()@bind.inverted()
 unity=(C@delta.to_matrix()@C.transposed()).to_quaternion()@R['手首.R']
 position=C@world.translation+unity@grip_position;rotation=unity@grip_rotation
 assembly=Matrix.Translation(C.transposed()@position)@(C.transposed()@rotation.to_matrix()@C).to_4x4()
 for kind in ['Dagger','EnergySword']:
  root=bpy.data.objects.new('MeleeGripRoot_'+kind,None);bpy.context.collection.objects.link(root)
  root.parent=rig;root.parent_type='BONE';root.parent_bone='手首.R';bpy.context.view_layer.update();root.matrix_world=assembly
  for o in list(bpy.data.objects):
   if o.name.startswith('MeleeArt_'+kind+'_'):
    local=o.matrix_world.copy();o.parent=root;o.matrix_parent_inverse=Matrix.Identity(4);o.matrix_basis=local
    o.hide_render=kind!='EnergySword';o.hide_set(kind!='EnergySword')

clips=[]
for kind in range(3):
 for action in ['idle','equip','light0','light1','light2','heavy','recover','holster']:
  timing=TIMINGS[kind][3 if action=='heavy' else int(action[-1])] if action.startswith('light') or action=='heavy' else (0,0,0)
  seconds=sum(timing) if sum(timing)>0 else {'idle':0,'equip':.28,'recover':.30,'holster':.24}[action]
  poses=[]
  for pitch in [0,-70,-40,40,70]:
   name='Melee_'+KINDS[kind]+'_'+action+('_Pitch'+str(pitch) if pitch else '')
   act=bpy.data.actions.get(name)
   if not EXPORT:
    if act:bpy.data.actions.remove(act)
    act=bpy.data.actions.new(name)
   if not act:raise ValueError('Missing saved Action '+name)
   act.use_fake_user=True;rig.animation_data.action=act
   frames=round(seconds*60)+1;values=[]
   for f in range(frames):
    bpy.context.scene.frame_set(f+1)
    if not EXPORT:
     install(make_melee(kind,action,min(seconds,f/60),timing,pitch))
     for n in tracks:rig.pose.bones[n].keyframe_insert('rotation_quaternion',frame=f+1,group=n)
    values.extend(bake())
   poses.append({'pitch':pitch,'frames':frames,'values':values})
  clips.append({'weapon':KINDS[kind],'action':action,'seconds':seconds,'windup':timing[0],'active':timing[1],'recovery':timing[2],'poses':sorted(poses,key=lambda p:p['pitch'])})
  print('AUTHORED_MELEE',KINDS[kind],action,flush=True)
# A socket calibrated once from the original wrist axes. Props never chase an IK target.
h='手首.R';f=(P['中指１.R']-P[h]).normalized();x=P['人指１.R']-P['小指１.R'];x=(x-f*x.dot(f)).normalized();p=-x.cross(f).normalized()
center=sum((P[d+'１.R'] for d in ['人指','中指','薬指','小指']),Vector())/4+f*.016+p*.018
socket=R[h].inverted()@(center-P[h]);q=R[h].inverted()@frame(x,f) # Canonical +Y is blade axis; +X is the finger direction.
data={'version':1,'frameRate':60,'bones':tracks,'chestBindRotation':dict(zip(['x','y','z','w'],U['上半身2']['rotation'])),'gripPosition':dict(zip(['x','y','z'],socket)),'gripRotation':dict(zip(['x','y','z','w'],[q.x,q.y,q.z,q.w])),'clips':clips}
data['revision']=hashlib.sha256(json.dumps(clips,separators=(',',':')).encode()).hexdigest()[:12]
OUT.parent.mkdir(parents=True,exist_ok=True);OUT.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')))
if not EXPORT:
 make_props();rig.animation_data.action=bpy.data.actions['Melee_EnergySword_idle'];bpy.context.scene.frame_set(1);hold_authored_props(rig,socket,q)
 rig['melee_revision']=data['revision'];bpy.ops.wm.save_as_mainfile(filepath=str(DEST))
for path,digest in sha.items():
 if hashlib.sha256(Path(path).read_bytes()).hexdigest()!=digest:raise RuntimeError('Protected firearm source changed: '+path)
print('MELEE_EXPORTED',data['revision'],'independent clips',len(clips),flush=True)
