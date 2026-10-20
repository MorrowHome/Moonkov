"""Append six independently authored actions without regenerating the accepted four.
Blender --background --python Tools/Blender/halo_expansion_gestures.py [-- --export-only]
"""
import bpy,json,math,sys,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
export_only='--export-only' in sys.argv
only=next((a.split('=',1)[1] for a in sys.argv if a.startswith('--only=')),None)
old=json.loads((ROOT/'Assets/DollSinger/Animation/Blender/WeaponGestures.json').read_text())
old_motions=old['motions'][:4];old_sha=hashlib.sha256(json.dumps(old_motions,sort_keys=True).encode()).hexdigest()
original_source=ROOT/'ArtSource/DollSinger/WeaponGestures.blend'
source_sha=hashlib.sha256(original_source.read_bytes()).hexdigest()
# Load the original rig and exact bind-axis helpers, never run its authoring loop.
prefix=(ROOT/'Tools/Blender/weapon_gestures.py').read_text().split('motions=[]')[0]
# Additional fan spread is confined to new seals; the accepted library is untouched.
prefix=prefix.replace('overrides=None,thumb_drop=0):','overrides=None,thumb_drop=0,fan=False):')
prefix=prefix.replace("if kind=='open':spread={'人指':-8,'中指':0,'薬指':7,'小指':13}[digit]","if kind=='open':spread=({'人指':-18,'中指':0,'薬指':18,'小指':36} if fan else {'人指':-8,'中指':0,'薬指':7,'小指':13})[digit]*( -1 if fan and side=='R' else 1)")
if export_only or only:prefix=prefix.replace('/ArtSource/DollSinger/WeaponGestures.blend','/ArtSource/DollSinger/HaloExpansion.blend')
if '--export-only' not in sys.argv:sys.argv.append('--export-only')
exec(compile(prefix,str(ROOT/'Tools/Blender/weapon_gestures.py'),'exec'),globals())
ROOT=Path(ROOT)
EXPORT_ONLY=export_only;REVISE_OTHERS=False
burst_digit=0

def seal_thumb(side,G):
 h='手首.'+side;basis=G[h]@R[h].inverted()
 f=norm(basis@(P['中指１.'+side]-P[h]));x=basis@(P['人指１.'+side]-P['小指１.'+side]);x=norm(x-f*x.dot(f))
 # Fold across the palm plane, never send the thumb back toward the lens.
 for k,d in enumerate([norm(x*.55+f*.83),norm(-x*.90+f*.44),norm(-x*.92-f*.30)]):
  n='親指'+['０','１','２'][k]+'.'+side;end='親指'+['１','２','先'][k]+'.'+side
  parent=parents[n];G[n]=G[parent]@R[parent].inverted()@R[n];setdir(n,end,d,G)

def make_pose(weapon,phase=0,mode='pose',fan_scale=1):
 G={};hit=bell(phase,.25) if mode=='fire' else 0;gather=math.sin(math.pi*phase) if mode=='reload' else 0
 guideR=(.29,1.03,.08);guideL=(-.29,1.00,.08)
 if weapon==5:
  # Crossed forearms occupy different height/depth planes; both sword rays converge on aim.
  hand('R',(0,0,1),(1,.28,0),G);hand('L',(0,0,1),(-1,.20,0),G)
  fingers('R','sword',G,overrides={'中指':[8*hit,16*hit,8*hit]});fingers('L','sword',G)
  wr=Vector((-.035,1.15,.16))+Vector((-.012,.012,.030))*hit+Vector((.050,-.050,-.025))*gather
  wl=Vector((.035,1.095,.105))+Vector((.012,-.008,.022))*hit+Vector((-.050,.045,.030))*gather
  guideL=(-.29,.97,.06)
 elif weapon==6:
  hand('R',(0,1,-.12),(1,0,0),G);hand('L',(.95,.25,.12),(0,1,-.25),G)
  digit=['人指','中指','薬指'][burst_digit]
  curls={d:[55*gather,70*gather,40*gather] for d in ['人指','中指','薬指']};curls['小指']=[75,95,65];curls[digit]=[35*hit+55*gather,50*hit+70*gather,25*hit+40*gather]
  fingers('R','open',G,overrides=curls,fan=True);seal_thumb('R',G);fingers('L','cup',G)
  wr=Vector((.070,1.175,.20))+Vector((0,.012,-.016))*hit+Vector((-.025,-.020,-.015))*gather
  wl=Vector((-.025,1.09,.175))+Vector((0,-.008,.012))*hit+Vector((-.040,.050,.035))*gather
 elif weapon==7:
  hand('R',(.04,.99,-.10*gather),(1,0,-.05),G);hand('L',(.94,.20+.30*gather,.14),(0,1,-.20),G)
  fingers('R','open',G);fingers('L','open',G)
  wr=Vector((.090,1.175,.225))+Vector((.003,.018,-.028))*hit+Vector((.025,.015,-.055))*gather
  wl=Vector((-.10,1.08,.185))+Vector((-.003,-.005,-.014))*hit+Vector((-.030,.055,.025))*gather
  guideR=(.29,1.11,.10);guideL=(-.29,.96,.07)
 elif weapon==8:
  # Sword points forward while the closed left hand pulls an imaginary bowstring rearward.
  hand('R',(0,0,1),(1,.12,0),G);hand('L',(.85,.15,.24),(0,1,-.1),G)
  fingers('R','sword',G);fingers('L','grip',G,openness=.4*gather)
  wr=Vector((.095,1.16,.205))+Vector((0,.008,.027))*hit+Vector((-.030,-.025,-.040))*gather
  wl=Vector((-.095,1.20,.120))+Vector((-.010,.008,-.020))*hit+Vector((.040,-.065,.055))*gather
  guideL=(-.31,1.10,.02)
 elif weapon==11:
  hand('R',(0,0,1),(0,1,0),G);hand('L',(0,0,1),(0,1,0),G)
  fingers('R','gun',G);fingers('L','gun',G)
  right=hit if burst_digit==0 else bell(.5+phase*.5,.25) if mode=='fire' else 0
  left=hit if burst_digit==1 else 0
  wr=Vector((.095,1.14,.22))+Vector((0,.008,-.026))*right+Vector((-.035,.025,-.040))*gather
  wl=Vector((-.095,1.11,.18))+Vector((0,.008,-.026))*left+Vector((.035,.045,-.020))*gather
 elif weapon==9:
  hand('R',(.02,1,-.10),(1,0,0),G);hand('L',(.82,.42,.20),(0,1,-.3),G)
  claw=8*(1-hit)+57*gather
  fingers('R','open',G,overrides={d:[claw,claw*.85,claw*.5] for d in ['人指','中指','薬指','小指']},fan=True);fingers('L','cup',G)
  wr=Vector((.080,1.18,.205))+Vector((.005,.018,.032))*hit+Vector((-.015,-.025,-.035))*gather
  wl=Vector((-.005,1.095,.185))+Vector((-.008,-.008,-.015))*hit+Vector((-.035,.035,-.010))*gather
  guideL=(-.29,.96,.09)
 else:
  # An upper claw and a lower bowl enclose the solar core, then release together.
  hand('R',(-.35,0,.94),(-.94,0,-.35),G);hand('L',(.35,0,.94),(-.94,0,.35),G)
  curls={d:[22+18*gather,30+18*gather,20+10*gather] for d in ['人指','中指','薬指','小指']}
  fingers('R','cup',G,overrides=curls);fingers('L','cup',G,overrides=curls)
  wr=Vector((.070,1.19,.165))+Vector((.010,-.015,.050))*hit+Vector((-.012,.015,-.040))*gather
  wl=Vector((-.075,1.085,.185))+Vector((-.010,.015,.050))*hit+Vector((.012,-.015,-.040))*gather
  guideR=(.29,1.11,.07);guideL=(-.29,.97,.09)
 arm('R',wr,guideR,G);arm('L',wl,guideL,G)
 for n in order:
  if n not in G:
   p=parents[n];G[n]=G[p]@R[p].inverted()@R[n] if p in G else R[n]
 return G
# Reuse whole-chain pitch authoring, with full pitch tracking for these directional signs.
pitched=(ROOT/'Tools/Blender/weapon_gestures.py').read_text().split('def make_pitched_pose')[1].split('# Record Blender actions')[0]
pitched='def make_pitched_pose'+pitched.replace('limit=-30 if weapon==1 else -60','limit=-90 if weapon>=5 else (-30 if weapon==1 else -60)')
exec(compile(pitched,'pitch-helper','exec'),globals())
entries=[(5,'Cicada_TwinBlades',.065),(6,'Trine_ThreefoldSeal',.085),(7,'Aegis_AnchorPalms',.11),(8,'Vesper_PrecisionSword',.24),(9,'Thorn_FiveNeedles',.36),(10,'Sundown_SolarRelease',.60),(11,'Rondo_TwinRefrain',.14)]
# Idempotent re-authoring is confined to the six new action name prefixes.
if not export_only:
 for action in list(bpy.data.actions):
  if any(action.name.startswith(name+'_') and (only is None or name==only) for _,name,_ in entries):bpy.data.actions.remove(action)
new=[]
for weapon,name,seconds in entries:
 if only is not None and name!=only:
  new.append(old['motions'][weapon-1]);continue
 variants=[]
 for pitch in [0,-80,-65,-40,-20,20,40,65,80]:
  burst_digit=0;label=name if pitch==0 else name+'_Pitch'+str(pitch)
  motion=author_motion(weapon,label,seconds,pitch)
  if weapon in [6,11]:
   motion['burstFires']=[{'values':motion['fire']}]
   for digit in range(1,3 if weapon==6 else 2):
    burst_digit=digit;action_name=label+'_fire'+str(digit);action=bpy.data.actions.get(action_name)
    if not export_only:action=bpy.data.actions.new(action_name)
    if not action:raise ValueError('Missing '+action_name)
    action.use_fake_user=True;rig.animation_data.action=action;values=[]
    for f in range(motion['fireFrames']):
     bpy.context.scene.frame_set(f+1)
     if not export_only:
      phase=f/(motion['fireFrames']-1)
      install(make_pitched_pose(weapon,phase,'fire',pitch) if pitch else make_pose(weapon,phase,'fire'))
      for n in tracks:rig.pose.bones[n].keyframe_insert('rotation_quaternion',frame=f+1,group=n)
     values.extend(bake())
    motion['burstFires'].append({'values':values})
  if pitch==0:main=motion
  else:variants.append(motion)
 main['aimPoses']=sorted(variants,key=lambda m:m['pitch']);new.append(main)
assert old['bones']==tracks
old['motions']=old_motions+new
assert hashlib.sha256(json.dumps(old['motions'][:4],sort_keys=True).encode()).hexdigest()==old_sha
old['revision']=hashlib.sha256(json.dumps(old['motions'],separators=(',',':')).encode()).hexdigest()[:12]
Path(OUT).write_text(json.dumps(old,ensure_ascii=False,separators=(',',':')))
if not export_only:
 rig.animation_data.action=bpy.data.actions['Cicada_TwinBlades_pose'];bpy.context.scene.frame_set(1)
 rig['halo_expansion_old_actions_sha256']=old_sha;rig['bake_revision']=old['revision']
 bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ArtSource/DollSinger/HaloExpansion.blend'))
assert hashlib.sha256(original_source.read_bytes()).hexdigest()==source_sha
print('HALO_EXPANSION_EXPORTED',old['revision'],'old four preserved',old_sha,flush=True)
