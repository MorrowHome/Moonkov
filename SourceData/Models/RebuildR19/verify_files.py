from pathlib import Path
import json,hashlib,re,subprocess
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template')
def sha(f):
 h=hashlib.sha256()
 with f.open('rb') as s:
  for b in iter(lambda:s.read(1024*1024),b''):h.update(b)
 return h.hexdigest()
def records(raw):
 ms=list(re.finditer(rb'(?m)^--- !u!(\d+) &(-?\d+)[^\r\n]*',raw));return {m[2].decode():{'type':m[1].decode(),'bytes':raw[m.start():ms[i+1].start() if i+1<len(ms) else len(raw)]} for i,m in enumerate(ms)}
def guid(path):return re.search(r'(?m)^guid: (\w+)',(r/(path+'.meta')).read_text())[1].encode()
plan=json.loads((p/'ImportPlan.json').read_text());base=json.loads((p/'repository-before.json').read_text());app=json.loads((p/'UnityApplication.json').read_text());formal=plan['formal'];prefab=plan['priorPrefab']
a=records((p/'BeforeR09R18.prefab').read_bytes());b=records((r/prefab).read_bytes());assert set(a)==set(b),'Prior prefab record added/removed'
allowed=set();mapping=[];native=json.loads((p/'NativePrepared/Manifest.json').read_text());mat={x['identity']:x['path'] for x in plan['materials']}
for row in plan['replacements']:
 old=guid(row['priorMesh']);new=guid(plan['output']+'/Meshes/'+row['key']+'.asset')
 fs=[k for k,x in a.items() if x['type']=='33' and b'm_Mesh:' in x['bytes'] and old in x['bytes']];assert len(fs)==1
 f=fs[0];assert a[f]['bytes'].replace(old,new,1)==b[f]['bytes'],'Non-reference MeshFilter change'
 go=re.search(rb'(?m)^  m_GameObject: \{fileID: (-?\d+)\}',a[f]['bytes'])[1]
 renderer=[k for k,x in a.items() if x['type']=='23' and re.search(rb'(?m)^  m_GameObject: \{fileID: '+go+rb'\}',x['bytes'])];assert len(renderer)==1
 rid=renderer[0];regex=rb'(?m)^  m_Materials:\n(?:  - [^\n]*\n)+';ob=re.search(regex,a[rid]['bytes'])[0];nb=re.search(regex,b[rid]['bytes'])[0];assert a[rid]['bytes'].replace(ob,nb,1)==b[rid]['bytes'],'Other renderer property changed'
 target=next(x for x in native['objects'] if x['key']==row['key']);expected=[guid(mat[x]) for x in target['materials']];actual=re.findall(rb'guid: (\w+)',nb);assert actual==expected,'Wrong material slots'
 allowed|={f,rid};mapping.append({'GX':row['GX'],'meshFilterFileID':f,'rendererFileID':rid,'onlyMeshAndMaterialReferencesChanged':True})
changed={k for k in a if a[k]['bytes']!=b[k]['bytes']};assert changed==allowed and len(changed)==18
old_scene=(p/'BeforeFormal.unity').read_bytes();new_scene=(r/formal).read_bytes();sa,sb=records(old_scene),records(new_scene);assert set(sa)<=set(sb)
assert [k for k in sa if sa[k]['bytes']!=sb[k]['bytes']]==['9223372036854775807'],'Unexpected scene record change'
added=set(sb)-set(sa);assert len(added)==1 and all(sb[k]['type']=='1001' for k in added)
head_scene=subprocess.check_output(['git','-C',str(r),'show','HEAD:'+formal]);sh=records(head_scene);candidate=new_scene
assert sa['145744447']['bytes'].replace(b'  maximumBosses: 1\n',b'')==sh['145744447']['bytes']
candidate=candidate.replace(sb['145744447']['bytes'],sh['145744447']['bytes'],1);sc=records(candidate)
assert [k for k in sh if sh[k]['bytes']!=sc[k]['bytes']]==['9223372036854775807'];(p/'SceneCommitCandidate.blob').write_bytes(candidate)
changed_files=[x for x,s in base['protectedFiles'].items() if sha(r/x)!=s['sha256']]
assert set(changed_files)=={formal,prefab},changed_files
types=set(re.findall(rb'(?m)^--- !u!(\d+)',(r/plan['output']/'RebuildR19_Additive.prefab').read_bytes()));assert types<={b'1',b'4',b'23',b'33'}
report={'protectedExistingFiles':len(base['protectedFiles']),'changedOldFilesOnlyPriorPrefabAndScene':changed_files,'priorPrefabChangedRecords':18,'priorPrefabOnly9MeshAnd9MaterialReferencesChanged':True,'oldPrefabRecordsAddedOrRemoved':0,'GXToPrefabComponentMapping':mapping,'sceneOldRecordsRemoved':0,'sceneOldChangedOnlySceneRoots':True,'newScenePrefabInstances':1,'existingUncommittedDefaultFieldExcludedFromSceneCommit':True,'sceneCommitCandidateSHA256':hashlib.sha256(candidate).hexdigest(),'existingMaterialsTexturesShadersDEMPhysicsLightmapsLODCullingBatchMeshesUnchanged':True,'priorPrefabBeforeSHA256':sha(p/'BeforeR09R18.prefab'),'priorPrefabAfterSHA256':sha(r/prefab),'formalWorkingSHA256':sha(r/formal),'R20Applied':False}
(p/'FileVerification.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='GXToPrefabComponentMapping'},indent=2))
