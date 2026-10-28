"""Keep the four editable source takes and stable, unweighted end helpers."""
import bpy,bmesh,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];SOURCE=ROOT/'ArtSource/DollSinger/SurvivalActions.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE')
for image in bpy.data.images:
 if not image.filepath:continue
 candidate=ROOT/'Assets/DollSinger/Art/Textures'/Path(image.filepath).name
 if candidate.is_file():image.filepath=bpy.path.relpath(str(candidate));image.reload()
 if image.name.startswith('RationPrint'):image.filepath='//SurvivalActions/RationPrint.png';image.reload()
for material in bpy.data.materials:
 if material.use_nodes:
  textures=[n for n in material.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
  if textures:material.node_tree.nodes.active=textures[0]
for name in ('ration_Sealed','ration_Opened','ration_TearStrip'):
 mesh=bpy.data.objects[name].data
 if mesh.uv_layers.get('PrintUV'):
  uv=mesh.uv_layers['PrintUV'];mesh.uv_layers.active=uv;uv.active_render=True
  for face in mesh.polygons:
   for index in face.loop_indices:
    p=mesh.vertices[mesh.loops[index].vertex_index].co;uv.data[index].uv=(.5+p.x/.12,p.z/.19)
mesh=bpy.data.objects['medkit_Body'].data;bm=bmesh.new();bm.from_mesh(mesh);seen=set()
for vertex in list(bm.verts):
 if vertex in seen:continue
 island={vertex};todo=[vertex]
 while todo:
  v=todo.pop()
  for edge in v.link_edges:
   other=edge.other_vert(v)
   if other not in island:island.add(other);todo.append(other)
 seen|=island;faces={f for v in island for f in v.link_faces}
 if any(not edge.is_manifold for v in island for edge in v.link_edges):continue
 volume=sum(f.verts[0].co.dot(f.verts[i].co.cross(f.verts[i+1].co))/6 for f in faces for i in range(1,len(f.verts)-1))
 if volume < -1e-11:bmesh.ops.reverse_faces(bm,faces=list(faces))
bm.to_mesh(mesh);bm.free();mesh.update()
used={strip.action for t in rig.animation_data.nla_tracks for strip in t.strips}
for track in rig.animation_data.nla_tracks:
 for strip in track.strips:
  strip.action.name='Survival_'+track.name
for action in used:
 for layer in action.layers:
  for strip in layer.strips:
   for bag in strip.channelbags:
    for curve in bag.fcurves:
     if '先.L' in curve.data_path or '先.R' in curve.data_path:
      for point in curve.keyframe_points:point.co.y=1 if curve.array_index==0 else 0
for obj in bpy.data.objects:
 if obj.animation_data and obj.animation_data.action:used.add(obj.animation_data.action)
hemisphere=0
for action in used:
 for layer in action.layers:
  for strip in layer.strips:
   for bag in strip.channelbags:
    paths={c.data_path for c in bag.fcurves if c.data_path.endswith('rotation_quaternion')}
    for path in paths:
     curves={c.array_index:c for c in bag.fcurves if c.data_path==path}
     if set(curves)!={0,1,2,3}:continue
     previous=None
     for index in range(len(curves[0].keyframe_points)):
      values=[curves[k].keyframe_points[index].co.y for k in range(4)]
      if previous and sum(a*b for a,b in zip(previous,values))<0:
       values=[-v for v in values];hemisphere+=1
       for k in range(4):curves[k].keyframe_points[index].co.y=values[k]
      previous=values
removed=0
for action in list(bpy.data.actions):
 if action not in used:bpy.data.actions.remove(action);removed+=1
text=bpy.data.texts.get('READ ME - Survival Actions') or bpy.data.texts.new('READ ME - Survival Actions');text.clear();text.write('正式角色和库存物品的 Blender 动作源。\n时间线（60 帧/秒）：搜刮 1–181，进食 211–391，喝水 421–601，注射 631–811。\n每段 3 秒。NLA 轨道与动作可继续编辑。默认循环预览喝水。\n握持、开盖、撕封条、按钮与针尖接触均在这里处理。封口条与针帽是独立物体。箱框仅作定位参考，不导出到游戏。\n贴图沿用库存图形。食物包装改为薄壁软袋，字样烘焙到表面贴图。角色网格和蒙皮权重未修改。\n动作参考 Quaternius（CC0）与 BURNER（CC BY 4.0）。原瞄准动作源 WeaponGestures.blend 保持不变。\n此文件用于源动作审阅。Unity 三套角色回灌与运行时验收尚未完成。\n')
rig['authoring_status']='Four editable source takes; source review and Unity integration are separate.'
scene.frame_start=1;scene.frame_end=811;scene.use_preview_range=True;scene.frame_preview_start=421;scene.frame_preview_end=601;scene.frame_set(511);scene.camera=bpy.data.objects['Review_ThreeQuarter'];bpy.context.view_layer.update()
scene.display.shading.color_type='TEXTURE'
for screen in bpy.data.screens:
 for area in screen.areas:
  if area.type=='VIEW_3D':
   area.spaces.active.region_3d.view_perspective='CAMERA';area.spaces.active.overlay.show_overlays=False;area.spaces.active.shading.color_type='TEXTURE'
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE),compress=True)
print('@@JSON@@'+json.dumps({'source':str(SOURCE),'sourceActions':[a.name for a in used if a.name.startswith('Survival_')],'unusedActionsRemoved':removed,'quaternionHemisphereCorrections':hemisphere,'bytes':SOURCE.stat().st_size}))
