"""Bake the existing inventory pouch graphics to a surface texture in Blender."""
import bpy,json,math
from pathlib import Path
from mathutils import Matrix,Vector
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'ArtSource/DollSinger/SurvivalActions';OUT.mkdir(parents=True,exist_ok=True)
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)));part=next(p for p in next(i for i in json.loads((ROOT/'SourceData/Animations/Survival/ReviewProps.json').read_text())['items'] if i['code']=='ration')['parts'] if p['name']=='Sealed')
scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH';scene.display.shading.light='FLAT';scene.display.shading.color_type='MATERIAL';scene.display.shading.show_shadows=False;scene.display.shading.show_cavity=False;scene.display.shading.show_specular_highlight=False
# Factory-startup contents belong only to this baking process.
for obj in list(scene.objects):obj.hide_render=True
mesh=bpy.data.meshes.new('ExistingInventoryPrint');faces=[];indices=[]
for k,sub in enumerate(part['submeshes']):
 mat=bpy.data.materials.new(sub['material']);mat.diffuse_color=sub['color'];mesh.materials.append(mat)
 for t in range(0,len(sub['triangles']),3):faces.append(tuple(reversed(sub['triangles'][t:t+3])));indices.append(k)
mesh.from_pydata([C.transposed()@Vector(v) for v in part['vertices']],[],faces);mesh.update()
for f,m in zip(mesh.polygons,indices):f.material_index=m
obj=bpy.data.objects.new('ExistingInventoryPrint',mesh);scene.collection.objects.link(obj)
cam=bpy.data.objects.new('PrintBakeCamera',bpy.data.cameras.new('PrintBakeCamera'));scene.collection.objects.link(cam);cam.location=C.transposed()@Vector((0,.095,.5));cam.rotation_euler=(C.transposed()@Vector((0,0,-1))).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=.19;scene.camera=cam
scene.render.resolution_x=768;scene.render.resolution_y=1216;scene.render.resolution_percentage=100;scene.view_settings.view_transform='Standard';scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False;scene.render.filepath=str(OUT/'RationPrint.png');bpy.ops.render.render(write_still=True)
print('@@JSON@@'+json.dumps({'texture':str(OUT/'RationPrint.png'),'source':'Unmodified inventory geometry and colours, orthographic flat render'}))
