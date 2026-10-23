"""Offline authoring contact sheets. These are Blender previews, not Unity validation.
Blender --background --python Tools/Blender/render_melee_authoring.py
"""
import bpy,json,math,sys
from pathlib import Path
from mathutils import Vector,Matrix,Quaternion
ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'ArtSource/DollSinger/MeleeGestures.blend'
OUT=ROOT/'Library/MeleeAuthoring';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
data=json.loads((ROOT/'Assets/DollSinger/Animation/Melee/Resources/MoonkovMeleeGestures.json').read_text())
bind=json.loads((ROOT/'Tools/Blender/DollSingerBind.json').read_text())['bones'];bind={b['name']:b for b in bind}
def qread(q):return Quaternion((q[3],q[0],q[1],q[2]))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
originals=[o for o in bpy.data.objects if o.type=='MESH' and not o.name.startswith('MeleeArt_')]
body=next(o for o in originals if len(o.vertex_groups)>100)
right=rig.data.bones['手首.R'];BR=(rig.matrix_world@right.matrix_local).to_quaternion();R=qread(bind['手首.R']['rotation'])
socket=Vector(tuple(data['gripPosition'][a] for a in ['x','y','z']));srot=qread([data['gripRotation'][a] for a in ['x','y','z','w']])
props={kind:[o for o in bpy.data.objects if o.name.startswith('MeleeArt_'+kind+'_')] for kind in ['Dagger','EnergySword']}
matrices={o.name:(o.matrix_basis.copy() if o.parent and o.parent.name.startswith('MeleeGripRoot_') else o.matrix_world.copy()) for parts in props.values() for o in parts}
for parts in props.values():
 for o in parts:o.parent=None;o.hide_set(False)
scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH';scene.render.resolution_x=960;scene.render.resolution_y=540;scene.render.resolution_percentage=100
scene.display.shading.light='STUDIO';scene.display.shading.color_type='MATERIAL';scene.display.shading.show_shadows=False;scene.display.shading.show_cavity=True
scene.display.shading.background_type='WORLD';scene.world.color=(.045,.06,.08);scene.render.image_settings.file_format='PNG'
camera=bpy.data.objects.new('MeleeAuthorPreview',bpy.data.cameras.new('MeleeAuthorPreview'));scene.collection.objects.link(camera);scene.camera=camera
camera.data.sensor_fit='VERTICAL';camera.data.sensor_height=32;camera.data.clip_start=.015;camera.data.clip_end=20

def aim(pos,target,fov):
 camera.location=C.transposed()@Vector(pos);direction=C.transposed()@(Vector(target)-Vector(pos));camera.rotation_euler=direction.to_track_quat('-Z','Y').to_euler();camera.data.lens=32/(2*math.tan(math.radians(fov)/2))

def arm_mesh():
 names=[g.name for g in body.vertex_groups]
 def arm(name):
  while name in bind:
   if name in ['腕.L','腕.R']:return True
   name=bind[name]['parent']
  return False
 groups={i for i,n in enumerate(names) if arm(n)}
 keep=[sum(g.weight for g in v.groups if g.group in groups)>=.5 for v in body.data.vertices]
 ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh();polys=[p for p in mesh.polygons if any(keep[v] for v in p.vertices)]
 result=bpy.data.meshes.new('OwnerArmPreview');result.from_pydata([v.co for v in mesh.vertices],[],[tuple(p.vertices) for p in polys]);result.update()
 for material in body.data.materials:result.materials.append(material)
 for target,original in zip(result.polygons,polys):target.material_index=original.material_index
 o=bpy.data.objects.new('OwnerArmPreview',result);scene.collection.objects.link(o);o.matrix_world=body.matrix_world.copy();ev.to_mesh_clear();return o

for kind in [a.split('=',1)[1] for a in sys.argv if a.startswith('--weapon=')] or ['Fists','Dagger','EnergySword']:
 for action in ['idle','light0','heavy']:
  clip=next(c for c in data['clips'] if c['weapon']==kind and c['action']==action)
  rig.animation_data.action=bpy.data.actions['Melee_'+kind+'_'+action]
  scene.frame_set(1 if action=='idle' else round((clip['windup']+clip['active']*.85)*60)+1)
  bpy.context.view_layer.update()
  hand=(rig.matrix_world@rig.pose.bones['手首.R'].matrix);delta=hand.to_quaternion()@BR.inverted();unity=(C@delta.to_matrix()@C.transposed()).to_quaternion()@R
  center=C@hand.translation+unity@socket;rotation=unity@srot;assembly=Matrix.Translation(C.transposed()@center)@(C.transposed()@rotation.to_matrix()@C).to_4x4()
  for weapon,parts in props.items():
   for o in parts:o.hide_render=weapon!=kind;o.matrix_world=assembly@matrices[o.name]
  arms=arm_mesh();arms.hide_render=True
  eye=(C@(rig.matrix_world@rig.pose.bones['目.L'].matrix).translation+C@(rig.matrix_world@rig.pose.bones['目.R'].matrix).translation)*.5+Vector((0,0,.015))
  offset=Vector((0,.14,-.38)) if kind=='EnergySword' else Vector((0,.08,-.28)) if kind=='Dagger' else Vector((0,.06,-.16))
  views=[('owner',eye+offset,eye+offset+Vector((0,0,1)),75),('front',(0,1.16,1.9),(0,1.15,.06),60),('side',(1.9,1.16,.06),(0,1.15,.06),60),('back',(0,1.16,-1.9),(0,1.15,.06),60)]
  for label,pos,target,fov in views:
   for o in originals:o.hide_render=label=='owner'
   arms.hide_render=label!='owner';aim(pos,target,fov)
   scene.render.filepath=str(OUT/(kind+'-'+action+'-'+label+'.png'));bpy.ops.render.render(write_still=True)
  bpy.data.objects.remove(arms,do_unlink=True)
  print('PREVIEW',kind,action,flush=True)
print('BLENDER_AUTHOR_PREVIEWS',OUT,flush=True)
