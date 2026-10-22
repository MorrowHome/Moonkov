from pathlib import Path
import json,hashlib,struct,sys,math
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template');sys.path.insert(0,str(p.parent/'R22/Handoff'));import validate_local_ring_capture as v
sha=lambda b:hashlib.sha256(b).hexdigest()
manifest=json.loads((p/'Handoff/MANIFEST.json').read_text())
for name,row in manifest['files'].items():
 b=(p/'Handoff'/name).read_bytes();assert len(b)==row['bytes'] and sha(b)==row['sha256'],name
capture=json.loads((p.parent/'R22/SavedCapture.json').read_text());proposal=json.loads((p.parent/'R22/SavedProposal.json').read_text());assert proposal['status']=='VALID_PROPOSAL_ONLY'
e=json.loads((p/'Handoff/R23_TARGET_RING_STATIC_EVIDENCE.json').read_text());faces=e['complete_owner_faces'];assert len(faces)==1604
for f in faces:f['material']=f['material_R21']
mapped=v.match_inventory(faces,capture['triangles'],.001,1e-5)
raw=(r/capture['owner']['mesh_asset_path']).read_text();vd=bytes.fromhex(__import__('re').search(r'(?m)^\s+_typelessdata: (\w+)',raw)[1]);maxne=maxpe=maxuve=0.
for f in faces:
 for ti in mapped[f['source_face_index']]:
  t=capture['triangles'][ti]
  for k,point in enumerate(t['xyz']):
   corner=min(range(len(f['xyz'])),key=lambda j:math.dist(point,f['xyz'][j]));pe=math.dist(point,f['xyz'][corner]);maxpe=max(maxpe,pe)
   vi=t['vertex_indices'][[0,2,1][k]];n=struct.unpack_from('<fff',vd,vi*48+12);nn=[n[0],n[2],n[1]];maxne=max(maxne,math.dist(nn,f['corner_normals'][corner]));maxuve=max(maxuve,max(abs(a-b) for a,b in zip(t['uv']['UVMap'][k],f['uv']['UVMap'][corner])))
assert maxne<=.0001 and maxpe<=.001 and maxuve<=.00001,(maxne,maxpe,maxuve)
targetfaces={i for c in e['target_components'] for i in c['diagnostic_source_face_indices']};assert targetfaces==set(range(1070,1076))|set(range(1082,1088))
targetindices=[i for f in targetfaces for i in mapped[f]];assert len(targetindices)==24
protected=set(next(x for x in capture['protected_prior_r21'] if x['source_object']==v.SOURCE)['triangle_ids']);assert all(capture['triangles'][i]['triangle_id'] in protected for i in targetindices)
assert not set(targetfaces)&set(range(1334,1346))
out={'status':'SAVED_FULL_GEOMETRY_UV_NORMAL_WINDING_MATERIAL_BIJECTION_PASS','sourceFaces':1604,'triangles':2604,'oldNormalMaxVectorError':maxne,'maxPositionErrorMetres':maxpe,'maxUVDifference':maxuve,'targetFaces':sorted(targetfaces),'targetTriangles':24,'allTargetTrianglesInsideProtectedR21Set':True,'R22TargetDisjoint':True,'owner':capture['owner'],'currentMeshSHA256':capture['mesh_asset_sha256'],'currentSceneSHA256':capture['scene_asset_sha256'],'R23SourceEvidenceSHA256':sha((p/'Handoff/R23_TARGET_RING_STATIC_EVIDENCE.json').read_bytes()),'R22CurrentCaptureSHA256':sha((p.parent/'R22/SavedCapture.json').read_bytes()),'manifestFilesVerified':len(manifest['files']),'liveOwnerAndWorldTransformRequiredBeforeApply':True}
(p/'SavedR23Ownership.json').write_text(json.dumps(out,indent=2));(p/'SourceFaceToTriangleMap.json').write_text(json.dumps(mapped,separators=(',',':')));print(json.dumps(out,indent=2))
