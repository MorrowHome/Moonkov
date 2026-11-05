"""Read-only source review: actual saved bones, reference mesh, and material/wire views."""
import bpy
import hashlib
import json
import sys
from array import array
from pathlib import Path
from mathutils import Matrix,Vector

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'ArtSource/DollSinger/GrenadeGestures.blend'
OUT=ROOT/'LocalData/GrenadeReview/Blender';OUT.mkdir(parents=True,exist_ok=True)
before=hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE')
body=next(o for o in scene.objects if o.type=='MESH' and len(o.vertex_groups)>100)
prop=bpy.data.objects['Grenade_Grip_Reference']
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
def point(name):return C@(rig.matrix_world@rig.pose.bones[name].matrix).translation
checks=[]
for frame in (1,54):
    scene.frame_set(frame);bpy.context.view_layer.update()
    wrist=point('手首.R');middle=point('中指１.R');across=point('人指１.R')-point('小指１.R')
    palm=-across.cross(middle-wrist).normalized()
    center=C@(prop.matrix_world@(sum((Vector(p) for p in prop.bound_box),Vector())/8))
    dot=(center-wrist).dot(palm)
    assert dot>.02,(frame,'prop is on wrong side of palm',dot)
    checks.append(dict(frame=frame,palmOffsetMetres=dot))
guide=(point('人指２.L')-point('人指１.L')).normalized()
assert guide.z>.9,('empty hand must aim forward',list(guide))
sys.path.insert(0,str(Path.home()/'.codex/skills/blender-asset-qa/scripts'))
import qa_check,review_shots
qa=qa_check.run(objects=[prop.name],profile='game',uv=False)
def mesh_hash(mesh):
    coordinates=array('f',[0])*(len(mesh.vertices)*3);mesh.vertices.foreach_get('co',coordinates)
    indices=array('i',[0])*len(mesh.loops);mesh.loops.foreach_get('vertex_index',indices)
    return hashlib.sha256(coordinates.tobytes()+indices.tobytes()).hexdigest()
with bpy.data.libraries.load(str(ROOT/'SourceData/Models/PhysicalItems/energy_grenade.blend')) as (src,dst):dst.objects=['energy_grenade']
unchanged=mesh_hash(prop.data)==mesh_hash(dst.objects[0].data)
assert unchanged,'The action source must not modify the existing item mesh'
# The reference is the unchanged existing item mesh. It is not exported from this file.
review_shots.run(objects=[body.name,prop.name],out=str(OUT/'material'),
    views=['front','right','three_quarter'],mode='material',res=512)
# A thin overlay avoids the stock QA wire thickness exploding narrow MMD triangles.
wire=body.copy();wire.data=body.data.copy();scene.collection.objects.link(wire)
black=bpy.data.materials.new('Temporary QA wire');black.diffuse_color=(.005,.005,.005,1)
wire.data.materials.clear();wire.data.materials.append(black)
for face in wire.data.polygons:face.material_index=0
modifier=wire.modifiers.new('Temporary QA wire','WIREFRAME');modifier.thickness=.00015;modifier.use_even_offset=False
review_shots.run(objects=[body.name,prop.name,wire.name],out=str(OUT/'wire'),
    views=['front','right','three_quarter'],mode='material',res=512)
report=dict(sourceSha256=before,sourceUnchanged=before==hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    geometryChanged=False,referenceGeometryUnchanged=unchanged,aimDirection=list(guide),checks=checks,referenceMeshQA=qa,
    meshes=[dict(name=o.name,triangles=sum(len(p.vertices)-2 for p in o.data.polygons),materials=len(o.data.materials),dimensions=list(o.dimensions)) for o in (body,prop)])
(OUT/'Review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print('@@JSON@@'+json.dumps(dict(sourceUnchanged=report['sourceUnchanged'],checks=checks,
    referenceMeshPass=qa['pass'],referenceErrors=qa['errors'],referenceWarnings=qa['warnings'],meshes=report['meshes'])))
