"""Multi-angle object-colour and wire review of the interaction meshes.
Freeze evaluated props in isolated scenes so visibility keys cannot reveal other
items. Disable wire offset miters on very thin printed-label triangles.
"""
import bpy,sys,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'LocalData/Actions/Blender'
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/DollSinger/SurvivalActions.blend'));source_scene=bpy.context.scene
sys.path.insert(0,str(Path.home()/'.codex/skills/blender-asset-qa/scripts'));import review_shots
results=[]
def narrow_wire(scene):
 for obj in scene.objects:
  if obj.get('review_wire_ready'):continue
  for modifier in obj.modifiers:
   if modifier.name=='QA_Wire':modifier.use_even_offset=False;modifier.thickness*=.15;obj['review_wire_ready']=True
bpy.app.handlers.render_pre.append(narrow_wire)
for name,frame,objects in [('RationSealed',211,['ration_Sealed']),('RationOpened',301,['ration_Opened']),('RationSeal',254,['ration_TearStrip']),('Bottle',421,['water_Body','water_Cap']),('Injector',631,['medkit_Body','medkit_Plunger','medkit_Needle','medkit_NeedleSheath'])]:
 bpy.context.window.scene=source_scene;source_scene.frame_set(frame);bpy.context.view_layer.update();depsgraph=bpy.context.evaluated_depsgraph_get();frozen=[]
 for object_name in objects:
  obj=bpy.data.objects[object_name];evaluated=obj.evaluated_get(depsgraph);mesh=bpy.data.meshes.new_from_object(evaluated,depsgraph=depsgraph);frozen.append((object_name,mesh,obj.matrix_world.copy()))
 scene=bpy.data.scenes.new('Geometry review '+name);scene.world=source_scene.world.copy();scene.world.color=(.055,.065,.08);bpy.context.window.scene=scene;names=[]
 for object_name,mesh,matrix in frozen:
  obj=bpy.data.objects.new('Review_'+object_name,mesh);scene.collection.objects.link(obj);obj.matrix_world=matrix;names.append(obj.name)
 for mode in ('random','wire'):
  path=OUT/(name+'-Geometry-'+mode+'.png');review_shots.run(objects=names,out=str(path),views=['front','right','top','three_quarter'],mode=mode,res=320);results.append(str(path))
bpy.context.window.scene=source_scene;bpy.app.handlers.render_pre.remove(narrow_wire)
print('@@JSON@@'+json.dumps({'sheets':results,'sourceUnchanged':True}))
