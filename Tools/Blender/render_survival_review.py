"""Render continuous source motion from model and production arm-camera views.
No changes to the saved source. Optional --stills-only checks shading/framing first.
"""
import bpy,bmesh,json,math,sys
from pathlib import Path
from mathutils import Matrix,Vector
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'LocalData/Actions/Blender';C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/DollSinger/SurvivalActions.blend'));scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE');body=next(o for o in scene.objects if o.type=='MESH' and len(o.vertex_groups)>100)
scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='STUDIO';scene.display.shading.color_type='TEXTURE';scene.display.shading.show_cavity=True;scene.display.shading.show_shadows=False
scene.render.resolution_x=800;scene.render.resolution_y=600;scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
for name in ('ration_Sealed','ration_Opened','ration_TearStrip'):
 mesh=bpy.data.objects[name].data
 if mesh.uv_layers.get('PrintUV'):
  mesh.uv_layers.active=mesh.uv_layers['PrintUV'];mesh.uv_layers['PrintUV'].active_render=True
for material in body.data.materials:
 if material and material.use_nodes:
  nodes=[n for n in material.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
  if nodes:material.node_tree.nodes.active=nodes[0]
# Use the same >=50% arm weights and any-vertex triangle rule as the real overlay.
arm_groups={g.index for g in body.vertex_groups if g.name in rig.data.bones and any(b.name in ('腕.L','腕.R') for b in [rig.data.bones[g.name]]+list(rig.data.bones[g.name].parent_recursive))}
mask=[sum(g.weight for g in v.groups if g.group in arm_groups)>=.5 for v in body.data.vertices]
preview=body.copy();preview.data=body.data.copy();preview.name='Review only - exact production arm mask';scene.collection.objects.link(preview)
bm=bmesh.new();bm.from_mesh(preview.data);bm.faces.ensure_lookup_table();bmesh.ops.delete(bm,geom=[f for f in bm.faces if not any(mask[v.index] for v in f.verts)],context='FACES_ONLY');bm.to_mesh(preview.data);bm.free();preview.hide_render=True
camera=bpy.data.objects.new('Review_FirstPerson_65',bpy.data.cameras.new('Review_FirstPerson_65'));scene.collection.objects.link(camera);camera.data.sensor_fit='VERTICAL';camera.data.sensor_height=24;camera.data.lens=12/math.tan(math.radians(65/2));camera.data.clip_start=.015
characters=[o for o in scene.objects if o.type=='MESH' and o!=preview and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
results=[]
for kind,start in [('Search',1),('Eat',211),('Drink',421),('Inject',631)]:
 if '--actions' in sys.argv and kind not in sys.argv[sys.argv.index('--actions')+1:]:continue
 scene.frame_set(start+90);bpy.context.view_layer.update()
 eye=C@((rig.matrix_world@rig.pose.bones['目.L'].matrix).translation+(rig.matrix_world@rig.pose.bones['目.R'].matrix).translation)*.5
 pitch=math.radians(55 if kind=='Search' else 0);direction=Vector((0,-math.sin(pitch),math.cos(pitch)));offset=Vector((0,-.08*math.cos(pitch)+.14*math.sin(pitch),-.08*math.sin(pitch)-.14*math.cos(pitch)))
 camera.location=C.transposed()@(eye+direction*.015+offset);camera.rotation_euler=(C.transposed()@direction).to_track_quat('-Z','Y').to_euler()
 for view in ('Review_ThreeQuarter','Review_FirstPerson_65'):
  first_person=view=='Review_FirstPerson_65';scene.camera=camera if first_person else bpy.data.objects[view]
  for obj in characters:obj.hide_render=first_person
  preview.hide_render=not first_person
  folder=OUT/(kind+'-Motion-'+view);folder.mkdir(exist_ok=True)
  indices=[36] if '--stills-only' in sys.argv else range(73)
  for i in indices:
   value=start+i*2.5;scene.frame_set(int(value),subframe=value%1);bpy.context.view_layer.update();scene.render.filepath=str(folder/('%03d.png'%i));bpy.ops.render.render(write_still=True)
  results.append(str(folder))
print('@@JSON@@'+json.dumps({'rendered':results,'sourceUnchanged':True,'fps':24,'duration':3,'armProjection':'65 degrees; same production eye bones, overlay offset and mesh cutout. Search review looks down 55 degrees.'}))
