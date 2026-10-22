"""Fresh-background Blender static audit only; no Unity/project APIs or source .blend.
Run: blender -b --factory-startup --threads 2 --python-exit-code 1 --python verify_fbx_static.py
Writes FBX_STATIC_READBACK.json next to this file. Do not run in an editing session.
"""
import bpy,json,hashlib,math
from pathlib import Path
from mathutils import Vector,Matrix
from io_scene_fbx import parse_fbx
assert bpy.app.background,'Run only in a NEW background Blender process.'
P=Path(__file__).resolve().parent;C=json.loads((P/'R23_NEW_GEOMETRY_CONTRACT.json').read_text());F=P/'R23_RING01_JOINT_DISPLAY_ONLY.fbx';records=C['objects']
def sha(p):
 with open(p,'rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def elem(x,k):return next(e for e in x.elems if e.id==k)
def optional(x,k):return next((e for e in x.elems if e.id==k),None)
def chunks(a,n):return [list(a[i:i+n]) for i in range(0,len(a),n)]
def nstring(x):return x.split(b'\x00\x01')[0].decode()
def eprop(x):return {e.props[0].decode():e.props[-1] for e in elem(x,b'Properties70').elems if e.id==b'P'}
def coords(r,t):return [r['source_vertices_local'][i] for i in t['vertex_indices']]
def key(v):return tuple(sorted(tuple(round(float(x),5) for x in p) for p in v))
def compare(r,rows,normals_required=True,normal_threshold=0.0001):
 refs=r['export_triangles'];assert len(rows)==len(refs);lookup={}
 for j,t in enumerate(rows):lookup.setdefault(key(t['xyz']),[]).append(j)
 used=set();maxg=maxn=0.
 for si,t in enumerate(refs):
  xyz=coords(r,t);found=[]
  for j in lookup.get(key(xyz),[]):
   if j in used:continue
   u=rows[j]
   assert u['material']==r['material_slots'][t['material_slot']]
   assert not u['uv'],'Approved source has no UV; do not fabricate UV layers'
   for k in range(3):
    order=[(i+k)%3 for i in range(3)]
    ge=max((Vector(xyz[i])-Vector(u['xyz'][order[i]])).length for i in range(3))
    ne=max((Vector(t['corner_normals'][i])-Vector(u['corner_normals'][order[i]])).length for i in range(3))
    if ge<1e-6:found.append((j,ge,ne))
  assert len(found)==1,('triangle unique oriented match failure',r['object'],si,found)
  j,ge,ne=found[0];used.add(j);maxg=max(maxg,ge);maxn=max(maxn,ne)
  if normals_required:assert ne<normal_threshold,('normal mismatch',r['object'],si,ne)
 assert len(used)==len(refs)
 return {'unique_oriented_triangle_bijection':True,'triangles':len(refs),'max_local_position_error_m':maxg,'max_corner_normal_vector_error':maxn,'max_normal_angle_degrees_bound':math.degrees(2*math.asin(min(1,maxn/2))),'normal_check_threshold':normal_threshold,'normal_threshold_pass':maxn<normal_threshold,'material_slot_binding_matches':True,'source_UV_layers':0,'readback_UV_layers':0,'UV_status':'ABSENCE_PRESERVED; no UV created or lost'}
root,version=parse_fbx.parse(str(F));objects=elem(root,b'Objects');counts={}
for e in objects.elems:counts[e.id.decode()]=counts.get(e.id.decode(),0)+1
assert counts.get('Geometry')==2 and counts.get('Model')==2 and counts.get('Material')==2
assert not any(counts.get(n,0) for n in ['Texture','Video','NodeAttribute','AnimationStack','AnimationLayer','AnimationCurve','AnimationCurveNode','Deformer','Pose'])
props=eprop(elem(root,b'GlobalSettings'));props={k:v.decode() if isinstance(v,bytes) else v for k,v in props.items()};assert props['UnitScaleFactor']==100.
models={e.props[0]:e for e in objects.elems if e.id==b'Model'};geoms={e.props[0]:e for e in objects.elems if e.id==b'Geometry'};mats={e.props[0]:nstring(e.props[1]) for e in objects.elems if e.id==b'Material'}
cons=[e.props for e in elem(root,b'Connections').elems if e.id==b'C'];raw_results={}
for mid,model in models.items():
 name=nstring(model.props[1]);assert name in records and model.props[2]==b'Mesh';r=records[name]
 gids=[c[1] for c in cons if c[0]==b'OO' and c[2]==mid and c[1] in geoms];mids=[c[1] for c in cons if c[0]==b'OO' and c[2]==mid and c[1] in mats]
 assert len(gids)==1 and [mats[i] for i in mids]==r['material_slots'];g=geoms[gids[0]]
 verts=chunks(elem(g,b'Vertices').props[0],3);assert len(verts)==len(r['source_vertices_local']);assert not any(e.id==b'LayerElementUV' for e in g.elems)
 pi=list(elem(g,b'PolygonVertexIndex').props[0]);ln=elem(g,b'LayerElementNormal');assert elem(ln,b'MappingInformationType').props[0]==b'ByPolygonVertex'
 ns=chunks(elem(ln,b'Normals').props[0],3);ref=elem(ln,b'ReferenceInformationType').props[0]
 nis=list(elem(ln,b'NormalsIndex').props[0]) if ref==b'IndexToDirect' else list(range(len(ns)));assert ref in [b'IndexToDirect',b'Direct']
 sm=elem(g,b'LayerElementSmoothing');assert all(v==0 for v in elem(sm,b'Smoothing').props[0])
 lm=elem(g,b'LayerElementMaterial');assert elem(lm,b'MappingInformationType').props[0]==b'AllSame' and list(elem(lm,b'Materials').props[0])==[0]
 rows=[];vids=[];lis=[]
 for li,v in enumerate(pi):
  vids.append(v if v>=0 else -v-1);lis.append(li)
  if v<0:
   assert len(vids)==3;rows.append({'xyz':[verts[i] for i in vids],'corner_normals':[ns[nis[i]] for i in lis],'uv':{},'material':mats[mids[0]]});vids=[];lis=[]
 assert not vids and len(rows)==len(r['export_triangles']);raw_results[name]=compare(r,rows)
 assert all(math.isfinite(v) for p in verts+ns for v in p)

def probe(custom):
 bpy.ops.wm.read_factory_settings(use_empty=True);assert bpy.ops.import_scene.fbx(filepath=str(F),use_custom_normals=custom)=={'FINISHED'};bpy.context.view_layer.update()
 obs=list(bpy.context.scene.objects);assert set(o.name for o in obs)==set(records) and all(o.type=='MESH' for o in obs)
 assert len(bpy.data.meshes)==2 and len(bpy.data.materials)==2 and not bpy.data.images and not bpy.data.lights and not bpy.data.cameras and not bpy.data.actions and not bpy.data.armatures
 result={}
 for o in obs:
  r=records[o.name];me=o.data;me.calc_loop_triangles();assert not me.uv_layers and not o.modifiers and o.parent is None;assert [m.name for m in me.materials]==r['material_slots']
  assert len(me.vertices)==len(r['source_vertices_local']) and len(me.polygons)==len(r['export_triangles']) and all(len(p.vertices)==3 for p in me.polygons)
  rows=[{'xyz':[list(me.vertices[i].co) for i in t.vertices],'corner_normals':[list(me.corner_normals[i].vector) for i in t.loops],'uv':{},'material':me.materials[t.material_index].name} for t in me.loop_triangles]
  rr=compare(r,rows,normals_required=custom,normal_threshold=0.000175);materr=max(abs(r['matrix_world_blender'][i][j]-o.matrix_world[i][j]) for i in range(4) for j in range(4));assert materr<0.001
  expected=Matrix(r['matrix_world_blender']);worlderr=max((o.matrix_world@v.co-expected@Vector(r['source_vertices_local'][v.index])).length for v in me.vertices);assert worlderr<.001
  assert all(not p.use_smooth for p in me.polygons)
  rr.update(readback_vertices=len(me.vertices),readback_polygons=len(me.polygons),max_world_matrix_element_error=materr,max_world_vertex_error_m=worlderr,parent=None,all_original_flat_flags_preserved=True,has_custom_normals=me.has_custom_normals)
  result[o.name]=rr
 return result
custom=probe(True);flat=probe(False)
report={'status':'PASS_STATIC_WITH_IMPORTED_NORMALS_AND_DISCLOSED_PRECISION','FBX_file':F.name,'FBX_sha256':sha(F),'FBX_bytes':F.stat().st_size,'FBX_version':version,'FBX_object_types':counts,'FBX_global_settings':props,'blender_version':bpy.app.version_string,'raw_binary_checks':raw_results,'custom_normal_import_checks':custom,'recomputed_flat_normal_diagnostic':flat,'all_recomputed_flat_normals_also_within_import_threshold':all(x['normal_threshold_pass'] for x in flat.values()),'recomputed_flat_normals_accepted':False,'normal_policy':'REQUIRE imported FBX corner normals. Binary max vector error must be <1e-4; custom-normal roundtrip <0.000175 (about 0.0101 degrees). This explicitly accounts for small export/import normal encoding precision, not exact bit identity. Recalculated flat normals are a rejected diagnostic; plate error about 17.51 degrees.','total_source_vertices':1308,'total_exported_triangles':2512,'exported_meshes':2,'materials':2,'UV_layers':0,'UV_status':'Approved new source meshes have no UV. Binary and imported absence verified; no unwrap or UV fabrication.','images_or_texture_references':0,'cameras_lights_animation_armatures_collision_objects':0,'physics_limit':'Only exact two visual meshes exported; no collision meshes or physics data. Unity Generate Colliders must remain off and actual imported hierarchy must be checked locally.','full_source_scene_loaded':False,'source_saved_or_rendered':False,'Unity_import_compile_display_collision_navigation_runtime_performance':'NOT_RUN; local gate remains closed','normal_tangent_limit':'Source per-corner normals and flat flags preserved. No tangents exported because source has no UV and no tangent-space maps. Local Unity importer must preserve normals, not silently smooth/recalculate them.'}
(P/'FBX_STATIC_READBACK.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n');print(json.dumps({'status':report['status'],'bytes':F.stat().st_size,'sha256':sha(F),'flat_also_pass':report['all_recomputed_flat_normals_also_within_import_threshold']},indent=2),flush=True)
