from pathlib import Path
import json,re,struct,hashlib,subprocess,sys
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template')
sys.path.insert(0,str(p/'Handoff'));import validate_local_ring_capture as v
sha=lambda b:hashlib.sha256(b).hexdigest()
def write(name,obj):
 b=json.dumps(obj,sort_keys=True,separators=(',',':'),ensure_ascii=False,allow_nan=False).encode();(p/name).write_bytes(b);return sha(b)
manifest=json.loads((p/'Handoff/MANIFEST.json').read_text())
for name,row in manifest['files'].items():
 f=p/'Handoff'/name
 if not f.exists():
  matches=[x for x in (p/'Handoff').iterdir() if x.is_file() and sha(x.read_bytes())==row['sha256']]
  assert len(matches)==1,name
  matches[0].rename(f)
 b=f.read_bytes();assert len(b)==row['bytes'] and sha(b)==row['sha256'],name
contract=json.loads((p/'Handoff/RING_12_FACE_DELTA_CONTRACT.json').read_text())
priorpath=r/'Docs/AI/Moonkov_Rebuild_R21_RingFinish_Delivery.json';prior=json.loads(priorpath.read_text());app=prior['application']
proof=json.loads((p.parent/'R21/SavedTriangleOwnershipProof.json').read_text())
scene=r/app['formal'];sb=scene.read_bytes();st=sb.decode();scene_guid=re.search(r'^guid: (\w+)',Path(str(scene)+'.meta').read_text(),re.M)[1]
def records(raw):
 ms=list(re.finditer(r'(?m)^--- !u!(\d+) &(-?\d+)[^\n]*',raw));return {m[2]:(m[1],raw[m.start():ms[i+1].start() if i+1<len(ms) else len(raw)]) for i,m in enumerate(ms)}
sr=records(st);prefabpath=r/proof[0]['ownerPrefab'];pr=records(prefabpath.read_text());pguid=proof[0]['ownerPrefabGUID'];instance=sr['885272968'][1]
assert len([x for t,x in sr.values() if t=='1001' and 'm_SourcePrefab: {fileID: 100100000, guid: '+pguid in x])==1
colliders=sorted((i,t,x) for i,(t,x) in sr.items() if t in ('64','65','135','136','154'))
prefabcolliders=sorted((i,t,x) for i,(t,x) in pr.items() if t in ('64','65','135','136','154'))
collider_hash=sha(v.canonical({'scene':colliders,'basePrefab':prefabcolliders}))
matpath=app['material'];matguid=re.search(r'^guid: (\w+)',(r/(matpath+'.meta')).read_text(),re.M)[1]
normalization={'units':'meters','space':'blender_source_local','winding':'source_polygon','verified_from_local_receipt_and_scene_refs':True,'source_matrix_world_blender':contract['matrix_world_blender'],'mesh_local_to_blender_source_local':[[1,0,0,0],[0,0,1,0],[0,1,0,0],[0,0,0,1]],'mesh_uv_to_source_uv':[[1,0,0],[0,1,0],[0,0,1]],'normalized_corner_order':[0,2,1]}
protected=[];captures={};evidence=[]
for row in app['rows']:
 name=row['source'];oldproof=next(x for x in proof if x['source']==name);meshpath=row['newMesh'];f=r/meshpath;b=f.read_bytes();raw=b.decode();guid=re.search(r'^guid: (\w+)',Path(str(f)+'.meta').read_text(),re.M)[1]
 # Current files must still be the accepted R21 commit bytes, not an inferred baseline.
 headb=subprocess.check_output(['git','-C',str(r),'show','46169763:'+meshpath]);assert b==headb
 fid=oldproof['meshFilterPrefabFileID'];rid=oldproof['rendererPrefabFileID'];owner={'scene_asset_path':app['formal'],'scene_guid':scene_guid,'renderer_global_object_id':row['rendererId'],'mesh_asset_path':meshpath,'mesh_guid':guid,'mesh_local_file_id':re.search(r'--- !u!43 &(\d+)',raw)[1]}
 def overrides(component,prop):
  return [c for c in re.split(r'(?m)^    - target: ',instance)[1:] if c.startswith('{fileID: '+component+', guid: '+pguid) and (re.search(r'(?m)^      propertyPath: (.+)$',c)[1].strip("'")==prop)]
 mo=overrides(fid,'m_Mesh');assert len(mo)==1 and 'objectReference: {fileID: 4300000, guid: '+guid+', type: 2}' in mo[0]
 renderer=pr[rid][1];assert 'subMeshCount: 0' in renderer and 'm_StaticBatchRoot: {fileID: 0}' in renderer and 'm_Enabled: 1' in renderer
 for sm,material in enumerate(row['oldMaterials']+[matpath]):
  mg=re.search(r'^guid: (\w+)',(r/(material+'.meta')).read_text(),re.M)[1];oo=overrides(rid,'m_Materials.Array.data['+str(sm)+']')
  if oo:assert len(oo)==1 and 'guid: '+mg in oo[0]
  else:
   refs=re.search(r'  m_Materials:\n((?:  - .*\n)+)',renderer)[1];assert re.findall(r'guid: (\w+)',refs)[sm]==mg,(name,sm,material,mg,re.findall(r'guid: (\w+)',refs),[c[:320] for c in re.split(r'(?m)^    - target: ',instance)[1:] if c.startswith('{fileID: '+rid+', guid: '+pguid)])
 assert all(guid not in x for _,_,x in colliders+prefabcolliders)
 n=int(re.search(r'm_VertexCount: (\d+)',raw)[1]);data=bytes.fromhex(re.search(r'(?m)^\s+_typelessdata: (\w+)',raw)[1]);stride=len(data)//n;assert stride==48
 oldraw=(r/row['oldMesh']).read_text();assert data==bytes.fromhex(re.search(r'(?m)^\s+_typelessdata: (\w+)',oldraw)[1])
 positions=[struct.unpack_from('<fff',data,i*stride) for i in range(n)];uv=[struct.unpack_from('<ff',data,i*stride+40) for i in range(n)]
 ib=bytes.fromhex(re.search(r'(?m)^  m_IndexBuffer: (\w+)',raw)[1]);assert 'm_IndexFormat: 1' in raw
 sms=[tuple(map(int,m.groups())) for m in re.finditer(r'firstByte: (\d+)\s+indexCount: (\d+)\s+topology: (\d+)\s+baseVertex: (\d+)',raw)]
 slots=[Path(x).stem for x in row['oldMaterials']+[matpath]];triangles=[]
 for sm,(start,count,topology,base) in enumerate(sms):
  assert topology==0 and base==0;ids=struct.unpack_from('<'+'I'*count,ib,start)
  for j in range(0,count,3):
   indices=list(ids[j:j+3]);rp=[list(positions[i]) for i in indices];ru=[list(uv[i]) for i in indices];order=[0,2,1]
   triangles.append({'triangle_id':'vi_'+ '_'.join(map(str,indices)),'submesh':sm,'triangle_index_in_submesh':j//3,'vertex_indices':indices,'raw_xyz':rp,'raw_uv':{'UVMap':ru},'xyz':[[rp[k][0],rp[k][2],rp[k][1]] for k in order],'uv':{'UVMap':[ru[k] for k in order]},'material':slots[sm]})
 pts=sorted([t for t in triangles if t['submesh']==row['newMaterialSlot']],key=lambda t:t['triangle_id'])
 assert {tuple(t['vertex_indices']) for t in pts}=={tuple(t['indices']) for t in oldproof['selectedTriangles']}
 assert len(pts)==row['triangles']
 protected.append({'source_object':name,'owner':owner,'mesh_asset_sha256':sha(b),'source_face_count':row['sourceFaces'],'triangle_ids':[t['triangle_id'] for t in pts],'protected_triangle_records_sha256':sha(v.canonical(pts))})
 captures[name]={'owner':owner,'mesh_asset_sha256':sha(b),'vertex_streams_sha256':sha(data),'vertex_count':n,'submesh_count':len(sms),'material_slots':slots,'triangles':triangles}
 evidence.append({'source':name,'meshSHA256':sha(b),'commitBytesEqual':True,'sceneReferenceVerified':True,'materialReferencesVerified':True,'nonBatchedSavedRenderer':True,'oldVertexBufferEqual':True,'protectedTriangles':len(pts),'colliderReferencesToPrivateMesh':0})
assert len(list((r/Path(matpath).parent).glob('*.mat')))==1 and len(list((r/Path(app['rows'][0]['newMesh']).parent).glob('*.asset')))==5
for commit in ('fc975c6b','46169763'):subprocess.run(['git','-C',str(r),'merge-base','--is-ancestor',commit,'HEAD'],check=True)
target=captures[v.SOURCE];common={k:target[k] for k in ('owner','mesh_asset_sha256','vertex_streams_sha256','material_slots')}
common.update(source_object=v.SOURCE,source_mesh=contract['source_mesh'],scene_asset_sha256=sha(sb),collider_references_sha256=collider_hash,normalization=normalization,protected_prior_r21=protected,existing_finish_material={'name':v.NEW,'asset_path':matpath,'guid':matguid})
capture=dict(common,schema='r22-normalized-triangle-capture-v1',vertex_count=target['vertex_count'],submesh_count=target['submesh_count'],triangles=target['triangles']);ch=write('SavedCapture.json',capture)
receipt=dict(common,schema='r22-dot00-readonly-receipt-v1',verified_by='DOT00',verification_mode='read_only',contract_sha256=sha((p/'Handoff/RING_12_FACE_DELTA_CONTRACT.json').read_bytes()),capture_sha256=ch,r21_delivery_receipt={'path':str(priorpath).replace('\\','/'),'sha256':sha(priorpath.read_bytes())},verified_r21_commits=['fc975c6b','46169763'],existing_private_material_count=1,existing_private_mesh_clone_count=5)
rh=write('SavedReceipt.json',receipt);write('SavedOwnershipEvidence.json',{'scope':'Saved files only; no current Editor capture, no application authorization inferred from this report','rows':evidence,'receiptSHA256':rh,'sceneSHA256':sha(sb),'manifestFilesVerified':len(manifest['files']),'newPrivateMeshOrMaterial':False})
try:result=v.validate(contract,capture,receipt,receipt['contract_sha256'],ch,rh,rh)
except v.Blocked as error:result={'status':'BLOCKED','reason':str(error),'applied':False,'plan':[]}
write('SavedProposal.json',result);print(json.dumps({k:val for k,val in result.items() if k not in ('plan','owner','rollback')},indent=2))
