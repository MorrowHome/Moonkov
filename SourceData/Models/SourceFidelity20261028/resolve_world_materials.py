"""Resolve implicit coordinates and cache original AO before full-world ray work."""
import bpy,pathlib,json,sys,shutil
ROOT=pathlib.Path(__file__).resolve().parent;PROJECT=ROOT.parents[2];WORK=PROJECT/'LocalData/SourceFidelity20261028/World';sys.path.insert(0,str(ROOT))
from freeze_object_inputs import freeze_material
scene=next(s for s in bpy.data.scenes if s.name.startswith('SourceWorldStatic'));bpy.context.window.scene=scene
for mat in {m for o in scene.objects if o.type=='MESH' for m in o.data.materials if m}:freeze_material(mat)
count=0
for obj in scene.objects:
 if obj.type!='MESH' or 'source_atlas_id' not in obj:continue
 atlas=int(obj['source_atlas_id']);stats=WORK/'Bakes'/f'SurfaceStatistics_{atlas:03d}.png';cache=WORK/'Bakes'/f'SourceAO_{atlas:03d}.png';shutil.copyfile(stats,cache)
 image=None
 for index,mat in enumerate(list(obj.data.materials)):
  if not mat or not any(n.type=='AMBIENT_OCCLUSION' for n in mat.node_tree.nodes):continue
  if image is None:
   image=bpy.data.images.load(str(cache),check_existing=False);image.colorspace_settings.name='Non-Color';image.pack()
  copy=mat.copy();obj.data.materials[index]=copy;tree=copy.node_tree
  texture=tree.nodes.new('ShaderNodeTexImage');texture.image=image;texture.extension='EXTEND';uv=tree.nodes.new('ShaderNodeUVMap');uv.uv_map='SourceStaticBake';tree.links.new(uv.outputs['UV'],texture.inputs['Vector']);separate=tree.nodes.new('ShaderNodeSeparateColor');separate.mode='RGB';tree.links.new(texture.outputs['Color'],separate.inputs['Color'])
  for node in list(tree.nodes):
   if node.type!='AMBIENT_OCCLUSION':continue
   assert not node.inputs['Color'].is_linked and all(abs(v-1)<1e-6 for v in node.inputs['Color'].default_value)
   for socket in node.outputs:
    for link in list(socket.links):tree.links.new(separate.outputs['Red'],link.to_socket)
   tree.nodes.remove(node);count+=1
bpy.data.libraries.write(str(WORK/'PreparedWorldResolved.blend'),{scene},fake_user=True,compress=True)
(WORK/'ResolvedMaterials.json').write_text(json.dumps({'version':2,'originalCoordinatesExplicit':True,'cachedAONodes':count,'sourceSha256':json.loads((WORK/'PreparedWorld.json').read_text())['source_sha256']},indent=2));print('ALL_WORLD_MATERIALS_RESOLVED',count,flush=True)
