"""Export the saved, reviewed Blender takes and geometry; never regenerate poses."""
import bpy,json,sys,hashlib
from pathlib import Path
from mathutils import Matrix,Vector,Quaternion
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'Tools/Blender'))
from retarget_hatsune_miku import Rig,qmul,qinverse
SOURCE=ROOT/'ArtSource/DollSinger/SurvivalActions.blend';bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE')
bind=Rig(json.loads((ROOT/'Tools/Blender/DollSingerBind.json').read_text()));C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)));BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones};bones=[n for n in bind.order if bind.child_of(n,'肩P.L') or bind.child_of(n,'肩P.R')]
def qlist(q):return [q.x,q.y,q.z,q.w]
def pose(obj):return list(C@obj.matrix_world.translation),qlist((C@obj.matrix_world.to_quaternion().to_matrix()@C.transposed()).to_quaternion())
profiles={'ration':{'grip':[.045,.115,0],'contact':[0,.172,0]},'water':{'grip':[0,.08204398,0],'contact':[0,.172799975,0]},'medkit':{'grip':[0,.1345,0],'contact':[0,-.014,0]}}
result={'version':2,'frameRate':60,'rig':'DollSinger','sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'bones':bones,'motions':[],'props':[]}
# Hidden viewport objects stop evaluating transforms. Keep them evaluated in
# this read-only process while preserving their authored render visibility.
for obj in scene.objects:
 if not obj.get('source_part'):continue
 obj.hide_viewport=False
 if obj.animation_data and obj.animation_data.action:
  for layer in obj.animation_data.action.layers:
   for strip in layer.strips:
    for bag in strip.channelbags:
     for curve in bag.fcurves:
      if curve.data_path=='hide_viewport':curve.mute=True
for action,start,code in [('Search',1,None),('Eat',211,'ration'),('Drink',421,'water'),('Inject',631,'medkit')]:
 root=bpy.data.objects.get('Using_'+str(code));parts=[o for o in scene.objects if o.name.startswith(str(code)+'_') and o.get('source_part')]
 motion={'action':action,'seconds':3,'frames':181,'contact':.55 if action=='Inject' else .5,'contactIn':.45 if action=='Eat' else .42,'contactOut':.64,'items':[code] if code else [],'source':'SurvivalActions.blend / Survival_'+action,'rotations':[],'root':{'positions':[],'rotations':[]},'parts':[{'name':o['source_part'],'positions':[],'rotations':[],'visible':[]} for o in parts]}
 for f in range(181):
  scene.frame_set(start+f);bpy.context.view_layer.update();world={}
  for n in bind.order:
   if n not in rig.pose.bones:continue
   current=(rig.matrix_world@rig.pose.bones[n].matrix).to_quaternion();delta=current@BR[n].inverted();u=(C@delta.to_matrix()@C.transposed()).to_quaternion();r=bind.r[n];u=u@Quaternion((r[3],r[0],r[1],r[2]));world[n]=qlist(u)
  for n in bones:motion['rotations'].extend(qmul(qinverse(world[bind.parent[n]]),world[n]) if bind.parent[n] in world else world[n])
  if root:
   p,q=pose(root);motion['root']['positions'].extend(p);motion['root']['rotations'].extend(q)
   for obj,track in zip(parts,motion['parts']):
    p,q=pose(obj);track['positions'].extend(p);track['rotations'].extend(q);track['visible'].append(not obj.hide_render)
 result['motions'].append(motion)
 if root:
  meshes=[]
  for obj in parts:
   mesh=obj.data;uv=mesh.uv_layers.get('PrintUV') or mesh.uv_layers.get('ExportUV') or mesh.uv_layers.active;vertices=[];normals=[];uvs=[];indices=[[] for _ in mesh.materials];mesh.calc_loop_triangles()
   for triangle in mesh.loop_triangles:
    for loop_index in reversed(triangle.loops):
     loop=mesh.loops[loop_index];vertex=mesh.vertices[loop.vertex_index];indices[triangle.material_index].append(len(vertices));vertices.append(list(C@vertex.co));normals.append(list(C@loop.normal));uvs.append(list(uv.data[loop_index].uv) if uv else [0,0])
   meshes.append({'name':obj['source_part'],'vertices':[v for p in vertices for v in p],'normals':[v for p in normals for v in p],'uv':[v for p in uvs for v in p],'submeshes':[{'material':m.name,'triangles':indices[i]} for i,m in enumerate(mesh.materials)]})
  result['props'].append({'code':code,**profiles[code],'parts':meshes})
out=ROOT/'SourceData/Animations/Survival/BlenderExport.json';out.write_text(json.dumps(result,separators=(',',':')))
print('@@JSON@@'+json.dumps({'path':str(out),'motions':len(result['motions']),'bones':len(bones),'props':len(result['props']),'source':result['sourceSha256']}))
