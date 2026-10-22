"""Use the established R19 native display pipeline with imported FBX normals.
Fresh background Blender only, no author blend loaded/saved, no normal recompute.
"""
import bpy,struct,array,json,math,hashlib,sys,io
from pathlib import Path
from mathutils import Matrix,Vector
p=Path(__file__).resolve().parent;out=p/'NativePrepared';assert bpy.app.background and not (out/'Manifest.json').exists();out.mkdir(exist_ok=True)
sys.path.insert(0,str(p.parent/'R22/Handoff'));import validate_local_ring_capture as validator
conversion=Matrix(((1,0,0,0),(0,0,1,0),(0,1,0,0),(0,0,0,1)));result=[]
def key(xyz):return tuple(sorted(tuple(round(float(x),5) for x in v) for v in xyz))
def run(fbx,expected,kind):
 for ob in list(bpy.data.objects):bpy.data.objects.remove(ob,do_unlink=True)
 for me in list(bpy.data.meshes):bpy.data.meshes.remove(me)
 for ma in list(bpy.data.materials):bpy.data.materials.remove(ma)
 bpy.ops.import_scene.fbx(filepath=str(fbx),use_anim=False,use_custom_normals=True)
 obs={o.name:o for o in bpy.context.scene.objects if o.type=='MESH'};assert set(obs)==set(expected) and len(bpy.context.scene.objects)==len(expected)
 for name,ref in expected.items():
  ob=obs[name];me=ob.data;me.calc_loop_triangles();assert not ob.modifiers and ob.parent is None
  tris=ref['export_triangles'] if kind=='R23' else ref['triangles'];assert len(me.loop_triangles)==len(tris)
  assert [m.name for m in me.materials]==ref['material_slots']
  assert len(me.uv_layers)==(0 if kind=='R23' else 1)
  xyzfor=lambda t:[ref['source_vertices_local'][i] for i in t['vertex_indices']] if kind=='R23' else t['xyz']
  lookup={}
  for j,t in enumerate(me.loop_triangles):lookup.setdefault(key([me.vertices[i].co for i in t.vertices]),[]).append((j,t))
  used=set();maxn=maxg=0.
  checked=tris
  if kind=='R22FOOT':
   actual=[]
   for t in me.loop_triangles:actual.append({'xyz':[list(me.vertices[i].co) for i in t.vertices],'uv':{'UVMap':[list(me.uv_layers[0].data[li].uv) for li in t.loops]},'material':ref['source_material_name']})
   mapped=validator.match_inventory(ref['polygons'],actual,.000001,.000001);checked=[]
   for face in ref['polygons']:
    for ti in mapped[face['source_face_index']]:
     at=actual[ti];corners=[min(range(len(face['xyz'])),key=lambda j:(Vector(point)-Vector(face['xyz'][j])).length) for point in at['xyz']]
     checked.append({'xyz':at['xyz'],'corner_normals':[face['corner_normals'][j] for j in corners]})
  for rt in checked:
   xyz=xyzfor(rt);hits=[]
   for j,t in lookup.get(key(xyz),[]):
    if j in used:continue
    for k in range(3):
     order=[(i+k)%3 for i in range(3)];ge=max((Vector(xyz[i])-me.vertices[t.vertices[order[i]]].co).length for i in range(3));ne=max((Vector(rt['corner_normals'][i])-me.corner_normals[t.loops[order[i]]].vector).length for i in range(3))
     if ge<1e-6:hits.append((j,ge,ne))
   assert len(hits)==1,(name,hits);j,ge,ne=hits[0];assert ne<.000175,(name,ne);used.add(j);maxn=max(maxn,ne);maxg=max(maxg,ge)
  assert len(used)==len(tris)
  expectedmatrix=Matrix(ref['matrix_world_blender']);worlderr=max((ob.matrix_world@v.co-expectedmatrix@Vector(ref['source_vertices_local'][v.index] if kind=='R23' else ref['vertices_local'][v.index])).length for v in me.vertices);assert worlderr<.001
  um=conversion@ob.matrix_world@conversion;pos,rot,scale=um.decompose();assert max(abs(um[i][j]-Matrix.LocRotScale(pos,rot,scale)[i][j]) for i in range(4) for j in range(4))<.0001
  positions=array.array('f');normals=array.array('f');uv=array.array('f');indices=array.array('i');count=0
  for t in me.loop_triangles:
   for li in (t.loops[0],t.loops[2],t.loops[1]):
    v=me.vertices[me.loops[li].vertex_index].co;n=me.corner_normals[li].vector;positions.extend((v.x,v.z,v.y));normals.extend((n.x,n.z,n.y))
    if me.uv_layers:uv.extend(me.uv_layers[0].data[li].uv)
    indices.append(count);count+=1
  target=out/(name+'.bin')
  with io.BytesIO() as f:
   f.write(struct.pack('<i',count));positions.tofile(f);normals.tofile(f);f.write(struct.pack('<i',len(me.uv_layers)))
   if uv:uv.tofile(f)
   f.write(struct.pack('<ii',1,len(indices)));f.write(indices.tobytes());payload=f.getvalue()
  if target.exists():assert target.read_bytes()==payload,'Partial export differs; inspect before replacing'
  else:target.write_bytes(payload)
  result.append({'object':name,'kind':kind,'bin':target.name,'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'vertices':count,'triangles':count//3,'UVChannels':len(me.uv_layers),'materials':ref['material_slots'],'position':list(pos),'rotation':[rot.x,rot.y,rot.z,rot.w],'scale':list(scale),'maxLocalPositionError':maxg,'maxWorldPositionError':worlderr,'maxImportedNormalVectorError':maxn,'allImportedCornerNormalsCopied':True,'normalsRecalculated':False})
contract=json.loads((p/'Handoff/R23_NEW_GEOMETRY_CONTRACT.json').read_text());run(p/'Handoff/R23_RING01_JOINT_DISPLAY_ONLY.fbx',contract['objects'],'R23')
foot=json.loads((p.parent/'R22/Handoff/FOOT0541_GEOMETRY_AND_LOCAL_GATE.json').read_text());foot['material_slots']=[foot['source_material_name']];run(p.parent/'R22/Handoff/R22_FOOT0541_DISPLAY_ONLY.fbx',{foot['object']:foot},'R22FOOT')
(out/'Manifest.json').write_text(json.dumps({'pipeline':'Established R19 FBX custom-normal to native mesh pipeline','normalMode':'Import stored FBX corner normals; no RecalculateNormals/Smooth/Weld','objects':result,'fullAuthorBlendLoadedOrSaved':False,'UnityAcceptance':False},indent=2));print(json.dumps(result,indent=2),flush=True)
