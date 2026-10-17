"""Validate the saved authoring Actions against actual skinned triangle surfaces.
Run in Blender background, optionally `-- /absolute/report.json`.
Uses dominant bone weights to partition surfaces. This is a conservative check
of interior triangles; attachment seams and adjacent palm/forearm are excluded.
It complements visual inspection and Unity's runtime bake/projection checks.
"""
import bpy,sys,math,json
from mathutils import Matrix,Vector,Quaternion
from mathutils.bvhtree import BVHTree
import os
root=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
report={'scope':'Saved Blender Actions; rifle, revolver, shotgun; 60fps surface intersections. Adjacent same-hand palm/forearm seams, upper-arm attachment and head excluded. Head look/cloth are checked on actual Unity mesh snapshots.', 'motions':[]}
failed=False
bpy.ops.wm.open_mainfile(filepath=root+'/ArtSource/DollSinger/WeaponGestures.blend')
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
report['revision']=rig.get('bake_revision','unknown')
mesh=next(o for o in bpy.data.objects if o.type=='MESH' and len(o.vertex_groups)>100)
# Separate surfaces by dominant weights; count actual triangle intersections, not only joint proximity.
def group(n):
 bone=rig.data.bones.get(n)
 if bone and (bone.name=='首' or any(p.name=='頭' for p in [bone]+list(bone.parent_recursive))):return 'head'
 s=n[-1:] if n.endswith(('.L','.R')) else ''
 if not s:return 'body'
 if any(n.startswith(d) for d in ['人指','中指','薬指','小指','親指']):return n[:2]+'.'+s
 if n.startswith('手首'):return 'palm.'+s
 if n.startswith(('ひじ','手捩')):return 'forearm.'+s
 if n.startswith(('腕','肩')):return 'upper.'+s
 return 'body'
labels=[group(max([(mesh.vertex_groups[g.group].name,g.weight) for g in v.groups],key=lambda a:a[1])[0]) if v.groups else 'body' for v in mesh.data.vertices]
faces={}
for p in mesh.data.polygons:
 ls=[labels[i] for i in p.vertices]
 # Only interior triangles: skip seams, which naturally share vertices.
 if len(set(ls))==1:faces.setdefault(ls[0],[]).append(tuple(p.vertices))
for w,name,pitch in [(w,n+('_Pitch'+str(p) if p else ''),p) for w,n in [(1,'Rifle_ThunderSeal'),(2,'Revolver_SingleActionFan'),(3,'Shotgun_Rupture')] for p in [0,-80,-65,-50,-40,-20,15,30,45,65,80]]:
 result={}
 anatomy={}
 samples=0
 angular={'minimumFlexion':360,'maximumFlexion':0,'maximumWristBend':0}
 for mode in ['pose','fire','reload']:
  rig.animation_data.action=bpy.data.actions[name+'_'+mode]
  end=int(max(fc.range()[1] for layer in rig.animation_data.action.layers for strip in layer.strips for cb in strip.channelbags for fc in cb.fcurves))
  for f in range(1,end+1):
   samples+=1
   bpy.context.scene.frame_set(f);bpy.context.view_layer.update()
   for side in ['L','R']:
    world=lambda n:(rig.matrix_world@rig.pose.bones[n+'.'+side].matrix).translation
    rest=lambda n:rig.matrix_world@rig.data.bones[n+'.'+side].head_local
    wrist=rig.pose.bones['手首.'+side]
    delta=(rig.matrix_world@wrist.matrix).to_quaternion()@(rig.matrix_world@wrist.bone.matrix_local).to_quaternion().inverted()
    forward=(delta@(rest('中指１')-rest('手首'))).normalized()
    across=delta@(rest('人指１')-rest('小指１'));across=(across-forward*across.dot(forward)).normalized()
    # Reflection into Unity reverses the cross product. Signs here are Blender world.
    palm=across.cross(forward)*(-1 if side=='L' else 1)
    for digit in ['人指','中指','薬指','小指']:
     angles=[]
     for j,(a,b) in enumerate(zip(['１','２','３'],['２','３','先'])):
      d=(world(digit+b)-world(digit+a)).normalized()
      angle=math.degrees(math.atan2(d.dot(palm),d.dot(forward)))
      if angle<-.05:angle+=360
      if angles and angle<angles[-1]-.05:angle+=360
      angles.append(angle)
     bends=[angles[0],angles[1]-angles[0],angles[2]-angles[1]]
     angular['minimumFlexion']=min(angular['minimumFlexion'],*bends)
     angular['maximumFlexion']=max(angular['maximumFlexion'],*bends)
     if min(bends)<-.1 or max(bends)>96:anatomy['finger.'+side+'.'+digit]=[mode,f,bends]
    # Compare the wrist against its actual bind orientation relative to the weighted twist parent.
    q=wrist.rotation_quaternion
    if math.degrees(min(q.angle,2*math.pi-q.angle))>80:anatomy['wrist.'+side]=[mode,f,math.degrees(min(q.angle,2*math.pi-q.angle))]
    angular['maximumWristBend']=max(angular['maximumWristBend'],math.degrees(min(q.angle,2*math.pi-q.angle)))
   ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());m=ev.to_mesh();verts=[ev.matrix_world@v.co for v in m.vertices]
   trees={k:BVHTree.FromPolygons(verts,fs,all_triangles=True) for k,fs in faces.items()}
   keys=sorted(trees)
   for i,a in enumerate(keys):
    for b in keys[i+1:]:
     if a.startswith('upper') or b.startswith('upper') or a=='head' or b=='head':continue
     if a=='body' and b=='body':continue
     if a[-1]==b[-1] and (a.startswith(('palm','forearm')) or b.startswith(('palm','forearm'))):continue
     hits=len(trees[a].overlap(trees[b]))
     if hits:
      k=a+' / '+b
      if hits>result.get(k,[0])[0]:result[k]=[hits,mode,f]
   ev.to_mesh_clear()
 report['motions'].append({'weapon':w,'pitch':pitch,'intersections':result,'anatomyFailures':anatomy,'anglesDegrees':angular,'frames':samples})
 failed=failed or bool(result) or bool(anatomy)
 print('COLLISION',w,pitch,json.dumps({'intersections':result,'anatomy':anatomy},ensure_ascii=False),flush=True)
report['passed']=not failed
out=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else root+'/Temp/GestureValidation/Blender/SourceSurfaces.json'
os.makedirs(os.path.dirname(out),exist_ok=True)
open(out,'w').write(json.dumps(report,ensure_ascii=False,indent=2))
if failed:raise RuntimeError('Gesture surfaces intersect; inspect '+out)
