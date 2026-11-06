"""Read-only Blender QA: saved actions, unchanged reference mesh and review views."""
import bpy
import hashlib
import json
import sys
from array import array
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/DollSinger/SquadMotions.blend'
OUT = ROOT / 'LocalData/SquadMotionReview/Blender'
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(Path.home() / '.codex/skills/blender-asset-qa/scripts'))
import qa_check
import review_shots


def mesh_hash(mesh):
    coords = array('f', [0]) * (len(mesh.vertices) * 3)
    mesh.vertices.foreach_get('co', coords)
    indices = array('i', [0]) * len(mesh.loops)
    mesh.loops.foreach_get('vertex_index', indices)
    return hashlib.sha256(coords.tobytes() + indices.tobytes()).hexdigest()


before = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
body = next(o for o in scene.objects if o.type == 'MESH' and len(o.vertex_groups) > 100)
mesh = dict(name=body.name, hash=mesh_hash(body.data), triangles=sum(len(p.vertices) - 2 for p in body.data.polygons),
            materials=len(body.data.materials), dimensions=list(body.dimensions))
# Report the untouched reference mesh separately. The deliverable changes only
# animation; existing MMD seams, layers and skin weights are not repaired here.
qa = qa_check.run(objects=[body.name], profile='render', no_modifiers=True, uv=False)
specs = json.loads(scene['SquadMotions'])
checks = []
for spec in specs:
    rig.animation_data.action = bpy.data.actions[spec['action']]
    for frame in (1, 16, 31, 46, 61):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        evaluated = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
        surface = evaluated.to_mesh()
        try:
            assert len(surface.vertices) == len(body.data.vertices)
            assert all(all(abs(v) < 100 for v in vertex.co) for vertex in surface.vertices)
        finally:
            evaluated.to_mesh_clear()
        checks.append(dict(motion=spec['name'], frame=frame, passDeformation=True))
    scene.frame_set(16)
    review_shots.run(objects=[body.name], out=str(OUT / (spec['name'] + '.png')), views=['front', 'right', 'back', 'three_quarter'], mode='material', res=512)
rig.animation_data.action = bpy.data.actions[specs[2]['action']]
scene.frame_set(16)
review_shots.run(objects=[body.name], out=str(OUT / 'Heart-wire.png'), views=['front', 'right'], mode='wire', res=512)
bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'ArtSource/DollSinger/SurvivalActions.blend'))
original = bpy.data.objects.get(mesh['name'])
assert original is not None and mesh_hash(original.data) == mesh['hash'], 'Reference geometry changed'
report = dict(sourceSha256=before, sourceUnchanged=before == hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    geometryChanged=False, referenceGeometryUnchanged=True, meshes=[mesh], sampledDeformations=checks,
    referenceMeshQA=qa, acceptedReferenceFindings='Existing MMD reference topology is unchanged; no mesh is exported by this motion-only task.')
(OUT / 'Review.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
print('@@JSON@@' + json.dumps(dict(sourceUnchanged=report['sourceUnchanged'], geometryChanged=False,
    referenceErrors=qa['errors'], referenceWarnings=qa['warnings'], meshes=[mesh], deformationSamples=len(checks))))
