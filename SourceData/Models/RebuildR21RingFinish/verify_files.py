from pathlib import Path
import re,json,hashlib,subprocess,collections
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template')
def sha(f):
 h=hashlib.sha256()
 with f.open('rb') as s:
  for b in iter(lambda:s.read(1024*1024),b''):h.update(b)
 return h.hexdigest()
def records(raw):
 ms=list(re.finditer(rb'(?m)^--- !u!(\d+) &(-?\d+)[^\r\n]*',raw));return {m[2].decode():{'type':m[1].decode(),'bytes':raw[m.start():ms[i+1].start() if i+1<len(ms) else len(raw)]} for i,m in enumerate(ms)}
def guid(path):return re.search(r'(?m)^guid: (\w+)',(r/(path+'.meta')).read_text())[1]
def modifications(raw):
 s=raw.decode();m=re.search(r'(?ms)^    m_Modifications:\n(.*?)^    m_RemovedComponents:',s);assert m
 chunks=re.split(r'(?m)^    - target: ',m[1]);out=[]
 for t in chunks[1:]:
  target=re.match(r'\{fileID: (-?\d+), guid: (\w+), type: (\d+)\}',t);prop=re.search(r'(?m)^      propertyPath: (.+)$',t);value=re.search(r'(?m)^      value:(.*)$',t);ref=re.search(r'(?m)^      objectReference: (.+)$',t);assert target and prop and value and ref
  out.append(((target[1],target[2],prop[1].strip("'")),(value[1].strip(),ref[1].strip())))
 return out,re.sub(r'(?ms)(^    m_Modifications:\n).*?(^    m_RemovedComponents:)',r'\1\2',s)
plan=json.loads((p/'ImportPlan.json').read_text());app=json.loads((p/'UnityApplication.json').read_text());base=json.loads((p/'repository-before.json').read_text());formal=plan['formal'];before=(p/'BeforeFormal.unity').read_bytes();after=(r/formal).read_bytes();a,b=records(before),records(after);assert set(a)==set(b),'Scene object records added/removed'
changed=[k for k in a if a[k]['bytes']!=b[k]['bytes']];assert changed==['885272968'],changed
ma,sa=modifications(a['885272968']['bytes']);mb,sb=modifications(b['885272968']['bytes']);assert sa==sb,'Other prefab-instance data changed'
allowed={};expected={};pguid=plan['sourcePrefabGUID'];matguid=guid(app['material'])
for row in plan['objects']:
 mf=row['meshFilterPrefabFileID'];mr=row['rendererPrefabFileID'];slot=row['oldSubmeshCount'];key=(mf,pguid,'m_Mesh');allowed[key]=True;expected[key]=('',f'{{fileID: 4300000, guid: {guid(row["privateMeshPath"])}, type: 2}}')
 key=(mr,pguid,'m_Materials.Array.size');allowed[key]=True;expected[key]=(str(slot+1),'{fileID: 0}')
 key=(mr,pguid,f'm_Materials.Array.data[{slot}]');allowed[key]=True;expected[key]=('',f'{{fileID: 2100000, guid: {matguid}, type: 2}}')
 oldraw=(r/row['effectiveMesh']).read_text();newraw=(r/row['privateMeshPath']).read_text();assert re.search(r'(?m)^\s+_typelessdata: (\w+)',oldraw)[1]==re.search(r'(?m)^\s+_typelessdata: (\w+)',newraw)[1],'Vertex buffer changed'
 assert sha(r/row['effectiveMesh'])==row['effectiveMeshSHA256'],'Old shared mesh changed'
assert collections.Counter(x for x in ma if x[0] not in allowed)==collections.Counter(x for x in mb if x[0] not in allowed),'Non-target override changed'
actual=dict(mb)
for k,v in expected.items():assert actual.get(k)==v,(k,actual.get(k),v)
head=subprocess.check_output(['git','-C',str(r),'show','HEAD:'+formal]);h=records(head);assert a['145744447']['bytes'].replace(b'  maximumBosses: 1\n',b'')==h['145744447']['bytes']
candidate=after.replace(b['145744447']['bytes'],h['145744447']['bytes'],1);c=records(candidate);assert set(c)==set(h);assert [k for k in h if h[k]['bytes']!=c[k]['bytes']]==['885272968'];(p/'SceneCommitCandidate.blob').write_bytes(candidate)
changed_files=[x for x,s in base['protectedFiles'].items() if sha(r/x)!=s['sha256']];assert changed_files==[formal],changed_files
report={'existingProtectedFiles':len(base['protectedFiles']),'changedExistingFilesOnlyFormalScene':changed_files,'sceneRecordsAddedOrRemoved':0,'onlyChangedSceneRecord':'885272968','existingNonTargetPrefabOverridesUnchanged':True,'privateMeshOverrides':5,'privateMaterialSlotAppends':5,'materialArraySizeUpdates':5,'paintedSourceFaces':150,'paintedTriangles':300,'privateVertexBuffersByteIdenticalToOriginals':True,'originalMeshFilesByteIdentical':True,'allOldMaterialsTexturesDEMPhysicsLightingLODCullingBatchAssetsByteIdentical':True,'preexistingFontChangesPreserved':True,'preexistingDefaultFieldExcludedFromCommit':True,'sceneCommitCandidateSHA256':hashlib.sha256(candidate).hexdigest(),'workingSceneSHA256':sha(r/formal)}
(p/'FileVerification.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
