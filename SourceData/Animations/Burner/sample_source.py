"""Read the CC-BY source animation, without editing its geometry or creating new motion."""
import bpy,json,pathlib
from mathutils import Vector,Matrix
root=pathlib.Path(__file__).resolve().parent
for name in ['Cube','Camera','Light']:
 obj=bpy.data.objects.get(name)
 if obj:bpy.data.objects.remove(obj,do_unlink=True)
bpy.ops.import_scene.gltf(filepath=str(root/'MedicalSyringe.glb'))
scene=bpy.context.scene;arm=next(o for o in scene.objects if o.type=='ARMATURE' and len(o.data.bones)>40)
# Blender right-handed Z-up to the production Unity character's left-handed Y-up.
conversion=Matrix(((-1,0,0,0),(0,0,1,0),(0,-1,0,0),(0,0,0,1)))
names=['mixamorig2:Spine2_01']
for side in ['Left','Right']:
 for part in ['Shoulder','Arm','ForeArm','Hand','HandThumb1','HandThumb2','HandThumb3','HandIndex1','HandIndex2','HandIndex3','HandMiddle1','HandMiddle2','HandMiddle3','HandRing1','HandRing2','HandRing3','HandPinky1','HandPinky2','HandPinky3']:
  prefix='mixamorig2:'+side+part+'_'
  match=[b.name for b in arm.data.bones if b.name.startswith(prefix)]
  if len(match)!=1:raise RuntimeError((prefix,match))
  names.append(match[0])
frames=[];start=1/30;end=172/30;rate=60
for f in range(round((end-start)*rate)+1):
 t=start+f/rate;frame=t*24;scene.frame_set(int(frame),subframe=frame%1);bpy.context.view_layer.update()
 dep=bpy.context.evaluated_depsgraph_get();ev=arm.evaluated_get(dep)
 row={}
 for name in names:
  b=ev.pose.bones[name];m=conversion@ev.matrix_world@b.matrix
  p=m.translation;direction=(m.to_3x3()@Vector((0,1,0))).normalized()
  row[name]={'p':[round(x,7) for x in p],'direction':[round(x,7) for x in direction]}
 frames.append(row)
result={'source':'MedicalSyringe.glb','author':'BURNER','license':'CC-BY-4.0','frameRate':rate,'seconds':end-start,'frames':frames}
(root/'Samples.json').write_text(json.dumps(result,separators=(',',':'))+'\n')
print('@@JSON@@'+json.dumps({'frames':len(frames),'joints':len(names),'seconds':end-start}))
