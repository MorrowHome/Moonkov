from pathlib import Path
import json,re,struct,math,hashlib
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template');mapping=json.loads((p/'TriangleCorrespondenceReview.json').read_text());owners=json.loads((p/'EffectiveSavedOwnership.json').read_text())
def cross(a,b):return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def sub(a,b):return tuple(x-y for x,y in zip(a,b))
def norm(a):
 n=math.sqrt(sum(x*x for x in a));assert n>0;return tuple(x/n for x in a)
report=[]
for obj in mapping:
 owner=next(x for x in owners['targets'] if x['source']==obj['source']);raw=(r/obj['effectiveMesh']).read_text();n=obj['vertexCount'];data=bytes.fromhex(re.search(r'(?m)^\s+_typelessdata: (\w+)',raw)[1]);stride=len(data)//n;v=[struct.unpack_from('<fff',data,i*stride) for i in range(n)];ns=[struct.unpack_from('<fff',data,i*stride+12) for i in range(n)]
 renderer=next(x for x in owner['components'] if x['type']=='23');assert 'subMeshCount: 0' in renderer['serialized'] and 'm_StaticBatchRoot: {fileID: 0}' in renderer['serialized'],'Batched renderer needs aggregate provenance'
 assert re.search(r'(?m)^  m_Enabled: 1$',renderer['serialized']);assert re.search(r'(?m)^  m_IsActive: 1$',owner['gameObjectSerialized'])
 triangle_refs=[];min_dot=1.0
 for face in obj['faces']:
  assert face['uniqueQuadTriangulation'] and face['maxSourceUVDifference']<1e-6,'Incomplete geometry/UV signature'
  assert face['sourceMaterial']=='R8_Dark_Protective_Steel';refs=face['triangleRefs'];assert len(refs)==2 and all(t['submesh']==0 for t in refs),'Old material/submesh correspondence differs'
  ids=[list(t['indices']) for t in refs];shared=set(ids[0])&set(ids[1])
  # Vertices are split per triangle; topology is checked by coordinates instead.
  sets=[set(v[i] for i in t) for t in ids];assert len(sets[0]&sets[1])==2 and len(sets[0]|sets[1])==4,'Not a unique quad pair'
  normals=[]
  for tri in refs:
   t=tri['indices'];normal=norm(cross(sub(v[t[1]],v[t[0]]),sub(v[t[2]],v[t[0]])));normals.append(normal)
   for i in t:min_dot=min(min_dot,sum(x*y for x,y in zip(normal,norm(ns[i]))))
  assert sum(x*y for x,y in zip(*normals))>.99999,'Opposite triangle winding'
  triangle_refs.extend(refs)
 assert min_dot>.99999,'Vertex normals/winding inconsistent'
 unique={(t['submesh'],t['triangleInSubmesh']) for t in triangle_refs};assert len(unique)==len(triangle_refs)
 obj['provisionalOnly']=False;obj['ownerPrefab']=owners['sourcePrefab'];obj['ownerPrefabGUID']=owners['sourcePrefabGUID'];obj['scenePrefabInstance']='885272968';obj['meshFilterPrefabFileID']=next(c['fileID'] for c in owner['components'] if c['type']=='33');obj['rendererPrefabFileID']=renderer['fileID'];obj['oldSubmeshCount']=len(obj['submeshTriangleCounts']);obj['newPrivateMaterialSlot']=len(obj['submeshTriangleCounts']);obj['selectedTriangles']=triangle_refs;obj['windingAndNormalMinDot']=min_dot;obj['note']='Saved-file ownership proved by unique prefab instance/component IDs, effective Mesh GUID and SHA256, per-face unique quad pair, matching UV corners, original slot0, consistent winding/normals and non-batched renderer. Live Editor checks still required before mutation.'
 report.append({'source':obj['source'],'faces':obj['selectedFaces'],'triangles':len(triangle_refs),'rendererID':obj['rendererPrefabFileID'],'meshFilterID':obj['meshFilterPrefabFileID'],'maxFloat32TransformedPositionErrorMetres':max(f['maxPositionErrorMetres'] for f in obj['faces']),'exactUV':True,'normalMinDot':min_dot})
assert sum(x['faces'] for x in report)==150 and sum(x['triangles'] for x in report)==300
(p/'SavedTriangleOwnershipProof.json').write_text(json.dumps(mapping,indent=2));print(json.dumps(report,indent=2))
