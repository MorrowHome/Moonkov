from pathlib import Path
import json,re,struct,math,hashlib
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template');d=json.loads((p/'EffectiveSavedOwnership.json').read_text());source=json.loads((p/'Handoff/EXPORT_SOURCE_SNAPSHOT.json').read_text());signatures=json.loads((p/'Handoff/source_contracts/COMPONENT_OWNERSHIP_SIGNATURES.json').read_text())['objects']
def f32(v):return struct.unpack('<f',struct.pack('<f',v))[0]
def transform(matrix,v):
 # Source world matrix relative to the common authored root (2015,2130,0).
 m=[row[:] for row in matrix];m[0][3]-=2015;m[1][3]-=2130
 q=[f32(sum(m[i][j]*v[j] for j in range(3))+m[i][3]) for i in range(3)]
 return (q[0],q[2],q[1])
out=[]
for target in d['targets']:
 name=target['source'];raw=(r/target['effectiveMeshPath']).read_text();n=int(re.search(r'm_VertexCount: (\d+)',raw)[1]);data=bytes.fromhex(re.search(r'(?m)^\s+_typelessdata: (\w+)',raw)[1]);channels=[]
 for m in re.finditer(r'- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)',raw):channels.append(tuple(map(int,m.groups())))
 assert channels[0]==(0,0,0,3) and channels[4][0]==0 and channels[4][2:]==(0,2);stride=len(data)//n;assert stride*n==len(data)
 verts=[struct.unpack_from('<fff',data,i*stride+channels[0][1]) for i in range(n)];uv=[struct.unpack_from('<ff',data,i*stride+channels[4][1]) for i in range(n)]
 indexdata=bytes.fromhex(re.search(r'(?m)^  m_IndexBuffer: (\w+)',raw)[1]);fmt=int(re.search(r'm_IndexFormat: (\d+)',raw)[1]);width=4 if fmt==1 else 2
 sms=[tuple(map(int,m.groups())) for m in re.finditer(r'firstByte: (\d+)\s+indexCount: (\d+)\s+topology: (\d+)\s+baseVertex: (\d+)',raw)];triangles=[]
 for sm,(first,count,topology,base) in enumerate(sms):
  assert topology==0;ids=struct.unpack_from('<'+('I' if width==4 else 'H')*count,indexdata,first)
  for j in range(0,count,3):triangles.append({'submesh':sm,'triangleInSubmesh':j//3,'indices':ids[j:j+3],'positions':[verts[i+base] for i in ids[j:j+3]]})
 faces=[];used=set()
 for component in signatures[name]:
  for face in component['face_records']:
   pts=[transform(source[name]['matrix'],x) for x in face['canonical_local_vertex_cycle']];assert len(pts)==4
   selected=[];max_error=0;max_uv_error=0
   for ti,tri in enumerate(triangles):
    assignments=[];errors=[]
    for v in tri['positions']:
     distances=[math.dist(v,q) for q in pts];k=min(range(4),key=distances.__getitem__);assignments.append(k);errors.append(distances[k])
    if max(errors)<=.00025 and len(set(assignments))==3:
     selected.append((ti,tri,assignments));max_error=max(max_error,max(errors))
     suv=next(iter(face['canonical_uv_cycle'].values()))
     for vi,corner in zip(tri['indices'],assignments):max_uv_error=max(max_uv_error,max(abs(uv[vi][a]-suv[corner][a]) for a in range(2)))
   unique=len(selected)==2 and len({k for _,_,a in selected for k in a})==4 and not any(ti in used for ti,_,_ in selected)
   if unique:used.update(ti for ti,_,_ in selected)
   faces.append({'component':component['inspection_component_id'],'sourceFace':face['source_face_index'],'candidateTriangles':len(selected),'uniqueQuadTriangulation':unique,'maxPositionErrorMetres':max_error,'maxSourceUVDifference':max_uv_error,'sourceMaterial':face['material'],'triangleRefs':[{'submesh':tri['submesh'],'triangleInSubmesh':tri['triangleInSubmesh'],'indices':list(tri['indices'])} for _,tri,_ in selected]})
 out.append({'source':name,'effectiveMesh':target['effectiveMeshPath'],'effectiveMeshSHA256':hashlib.sha256((r/target['effectiveMeshPath']).read_bytes()).hexdigest(),'vertexCount':n,'submeshTriangleCounts':[x[1]//3 for x in sms],'selectedFaces':len(faces),'uniqueGeometryQuads':sum(x['uniqueQuadTriangulation'] for x in faces),'exactSourceUVFaces':sum(x['uniqueQuadTriangulation'] and x['maxSourceUVDifference']<1e-6 for x in faces),'provisionalOnly':True,'note':'Coordinate candidate review is not by itself approved ownership; effective scene hierarchy, UV/material provenance and unique ownership must remain established.','faces':faces})
(p/'TriangleCorrespondenceReview.json').write_text(json.dumps(out,indent=2));print(json.dumps([{k:v for k,v in x.items() if k!='faces'} for x in out],indent=2))
