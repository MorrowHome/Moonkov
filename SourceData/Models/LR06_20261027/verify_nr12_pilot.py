"""Re-import the small FBX and verify geometry without opening the source blend."""
import bpy
import bmesh
import json
import sys
from mathutils import Vector
from pathlib import Path

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'NR12Pilot_Corrected'
expected=json.loads((OUT/'PilotManifest.json').read_text())
assert bpy.ops.import_scene.fbx(filepath=str(OUT/'NR12_corridor_pilot.fbx'),use_custom_normals=True,
                                use_image_search=False)=={'FINISHED'}
bpy.context.view_layer.update()
sys.path.insert(0,'/Users/morrow/.codex/skills/blender-asset-qa/scripts')
import qa_check
import review_shots
report={'status':'blender_roundtrip_only_not_Unity','objects':[]}
test_cube=bmesh.new()
bmesh.ops.create_cube(test_cube,size=1)
report['reference_cube_signed_volume_m3']=test_cube.calc_volume(signed=True)
test_cube.free()
selected=[]
for entry in expected['objects']:
    obj=bpy.data.objects.get(entry['object'])
    assert obj and obj.type=='MESH',entry['object']
    selected.append(obj.name)
    points=[obj.matrix_world @ Vector(c) for c in obj.bound_box]
    actual=[[min(v[i] for v in points) for i in range(3)],[max(v[i] for v in points) for i in range(3)]]
    error=max(abs(actual[j][i]-entry['source_world_bounds'][j][i]) for i in range(3) for j in range(2))
    bm=bmesh.new()
    bm.from_mesh(obj.data)
    # The standard QA signed-volume check uses world coordinates around 2000 m.
    # Recenter first to prevent cancellation of float32 cross products.
    center=sum((v.co for v in bm.verts),Vector())/len(bm.verts)
    for v in bm.verts:v.co-=center
    volume=bm.calc_volume(signed=True)
    bm.free()
    saved=obj.matrix_world.copy()
    obj.matrix_world.translation=Vector((0,0,0))
    bpy.context.view_layer.update()
    qa=qa_check.run(objects=[obj.name],profile='render')
    obj.matrix_world=saved
    bpy.context.view_layer.update()
    report['objects'].append({'name':obj.name,'bounds_before_export':entry['source_world_bounds'],
       'bounds_after_reimport':actual,'max_coordinate_error_m':error,'signed_volume_local_m3':volume,
       'matrix_determinant':obj.matrix_world.to_3x3().determinant(),
       'vertices':len(obj.data.vertices),'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),
       'uv_maps':[u.name for u in obj.data.uv_layers],'materials':[s.material.name if s.material else None for s in obj.material_slots],
       'recentered_qa':qa})
    assert error<0.003, f'{obj.name}: exceeds 3 mm round-trip bound tolerance'
    assert volume>0, f'{obj.name}: closed mesh faces inward after recentering'
for mode in ['random','wire']:
    report[mode]=review_shots.run(objects=selected,out=str(OUT/('review_'+mode+'.png')),
                                   views=['front','right','top','three_quarter'],mode=mode,res=384)
(OUT/'RoundTripQA.json').write_text(json.dumps(report,ensure_ascii=False,indent=2,default=str))
print('@@JSON@@'+json.dumps({'objects':[{'name':x['name'],'error_m':x['max_coordinate_error_m'],
    'signed_volume':x['signed_volume_local_m3'],'matrix_determinant':x['matrix_determinant'],'uv_maps':x['uv_maps'],
    'errors':x['recentered_qa']['errors'],'warnings':x['recentered_qa']['warnings']} for x in report['objects']],
    'random_sheet':report['random']['sheet'],'wire_sheet':report['wire']['sheet']},ensure_ascii=False),flush=True)
