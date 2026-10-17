"""Render saved Blender pose using cameras/body placement captured from the playing Unity lab.
Arms retain the source action's local rotations. Only body placement and the common shoulder
look transform come from Unity. Also report independent joint-position/projection differences.
Usage: blender -b -P Tools/Blender/render_gesture_comparison.py -- snapshot.json output_folder
"""
import bpy,json,os,sys,math
from mathutils import Vector,Quaternion,Matrix
root=os.path.abspath(os.path.join(os.path.dirname(__file__),'../..'))
args=sys.argv[sys.argv.index('--')+1:];snapshot=json.load(open(args[0]));out=args[1];os.makedirs(out,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=root+'/ArtSource/DollSinger/WeaponGestures.blend')
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');mesh=next(o for o in bpy.data.objects if o.type=='MESH')
rig.animation_data.action=bpy.data.actions['Sniper_ClosedViewfinder_pose'];bpy.context.scene.frame_set(1)
basis={p.name:p.matrix_basis.copy() for p in rig.pose.bones};rig.animation_data.action=None
for n,m in basis.items():rig.pose.bones[n].matrix_basis=m
bpy.context.view_layer.update()
U={r['name']:r for r in json.load(open(root+'/Tools/Blender/DollSingerBind.json'))['bones']}
bake=json.load(open(root+'/Assets/DollSinger/Animation/Blender/WeaponGestures.json'));tracks=set(bake['bones'])
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
def qr(v):return Quaternion((v[3],v[0],v[1],v[2]))
actual={r['name']:r for r in snapshot['bones']}
for b in rig.data.bones:
 n=b.name
 if n not in actual or n not in U or (n in tracks and not n.startswith('肩P')):continue
 row=actual[n];rest=(rig.matrix_world@b.matrix_local).to_quaternion()
 q=(C.transposed()@(qr(row['rotation'])@qr(U[n]['rotation']).inverted()).to_matrix()@C).to_quaternion()@rest
 m=Matrix.LocRotScale(C.transposed()@Vector(row['position']),q,Vector((1,1,1)))
 rig.pose.bones[n].matrix=rig.matrix_world.inverted()@m;bpy.context.view_layer.update()
errors=[((C@(rig.matrix_world@rig.pose.bones[n].head)-Vector(actual[n]['position'])).length,n) for n in tracks]
report={'revision':bake.get('revision'),'maxJointPositionDifferenceMetres':max(errors)[0],'worstBone':max(errors)[1]}
scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH';scene.render.resolution_x=1280;scene.render.resolution_y=720;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.display.shading.light='STUDIO';scene.display.shading.color_type='SINGLE';scene.display.shading.single_color=(.67,.72,.8);scene.display.shading.show_shadows=True;scene.display.shading.show_cavity=True;scene.display.shading.background_type='WORLD';scene.world.color=(.07,.07,.07)
original=mesh.data
for row in snapshot['cameras']:
 data=bpy.data.cameras.new('Matched '+row['name']);cam=bpy.data.objects.new(data.name,data);bpy.context.collection.objects.link(cam)
 cam.location=C.transposed()@Vector(row['position']);cam.rotation_euler=(C.transposed()@qr(row['rotation']).to_matrix()@Matrix.Diagonal((1,1,-1))).to_euler()
 data.sensor_fit='VERTICAL';data.sensor_height=32;data.lens=16/math.tan(math.radians(row['fov']/2));data.clip_start=.015;scene.camera=cam
 if row['name']=='Owner':
  import bmesh
  armNames=set()
  for p in rig.pose.bones:
   ancestor=p
   while ancestor:
    if ancestor.name in ['腕.L','腕.R']:armNames.add(p.name);break
    ancestor=ancestor.parent
  indices={g.index for g in mesh.vertex_groups if g.name in armNames}
  owned={v.index for v in original.vertices if sum(g.weight for g in v.groups if g.group in indices)>=.5}
  clone=original.copy();bm=bmesh.new();bm.from_mesh(clone)
  remove=[f for f in bm.faces if not any(v.index in owned for v in f.verts)]
  bmesh.ops.delete(bm,geom=remove,context='FACES');bm.to_mesh(clone);bm.free();mesh.data=clone
 scene.render.filepath=os.path.join(out,'Blender-Matched-'+row['name']+'.png');bpy.ops.render.render(write_still=True)
 mesh.data=original
open(os.path.join(out,'source-comparison.json'),'w').write(json.dumps(report,indent=2));print('SOURCE COMPARISON',report,flush=True)
