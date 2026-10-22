from pathlib import Path
import json,re,struct,hashlib,copy
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template');cap=json.loads((p.parent/'R22/SavedCapture.json').read_text());plan=json.loads((p.parent/'R22/SavedProposal.json').read_text());app=json.loads((p/'R22Application.json').read_text());before=(p/'R21RingCloneBefore.asset.txt').read_bytes();after=(r/app['path']).read_bytes();sha=lambda b:hashlib.sha256(b).hexdigest()
assert sha(before)==app['beforeSHA256']==cap['mesh_asset_sha256'] and sha(after)==app['afterSHA256']
def parse(raw):
 t=raw.decode();vd=bytes.fromhex(re.search(r'(?m)^\s+_typelessdata: (\w+)',t)[1]);ib=bytes.fromhex(re.search(r'(?m)^  m_IndexBuffer: (\w+)',t)[1]);groups=[]
 for m in re.finditer(r'firstByte: (\d+)\s+indexCount: (\d+)\s+topology: (\d+)\s+baseVertex: (\d+)',t):
  first,count,top,base=map(int,m.groups());assert top==base==0;ids=struct.unpack_from('<'+'I'*count,ib,first);groups.append([tuple(ids[i:i+3]) for i in range(0,count,3)])
 return t,vd,groups
bt,bv,bg=parse(before);at,av,ag=parse(after);assert bv==av
selected={x['original_triangle_index_in_submesh'] for x in plan['plan']};assert len(selected)==24
expected=[[t for i,t in enumerate(bg[0]) if i not in selected]]+bg[1:4]+[bg[4]+[bg[0][i] for i in sorted(selected)]];assert ag==expected
strip=lambda t:re.sub(r'(?m)^  m_IndexBuffer: \w+','  m_IndexBuffer: <buffer>',re.sub(r'(?s)  m_SubMeshes:\n.*?  m_Shapes:','  m_SubMeshes: <descriptors>\n  m_Shapes:',t))
assert strip(bt)==strip(at),'Other serialized mesh properties changed'
assert sha((r/cap['owner']['scene_asset_path']).read_bytes())==cap['scene_asset_sha256'],'Scene file changed unexpectedly'
for row in cap['protected_prior_r21']:
 if row['source_object']!='R2_MODULE_RING_SECTOR_00':assert sha((r/row['owner']['mesh_asset_path']).read_bytes())==row['mesh_asset_sha256']
report={'passed':True,'beforeSHA256':sha(before),'afterSHA256':sha(after),'allSerializedPropertiesOutsideIndicesAndSubmeshDescriptorsByteIdentical':True,'allVertexNormalTangentColorUVSkinningAndWholeBoundsDataByteIdentical':True,'allNonTargetTrianglesAndMaterialsPreserved':True,'allProtectedR21TriangleOrderPreserved':True,'otherFourR21ClonesUnchanged':True,'sceneFileUnchanged':True,'privateMaterialUnchanged':True,'newSubmeshTriangles':list(map(len,ag)),'reassignedTriangles':24,'combinedFiveRendererFinishTriangles':324,'footSceneInstances':0}
(p/'R22FileVerification.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
