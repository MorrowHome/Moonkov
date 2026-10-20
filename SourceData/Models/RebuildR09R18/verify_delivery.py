from pathlib import Path
import json,hashlib,re
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template')
def sha(f):
 h=hashlib.sha256()
 with f.open('rb') as s:
  for b in iter(lambda:s.read(1024*1024),b''):h.update(b)
 return h.hexdigest()
def records(raw):
 ms=list(re.finditer(rb'(?m)^--- !u!(\d+) &(-?\d+)[^\r\n]*',raw))
 return {m[2].decode():{'type':m[1].decode(),'bytes':raw[m.start():ms[i+1].start() if i+1<len(ms) else len(raw)]} for i,m in enumerate(ms)}
plan=json.loads((p/'ImportPlan.json').read_text());base=json.loads((p/'repository-before.json').read_text());app=json.loads((p/'UnityFormalApplication.json').read_text())
before=(p/'BeforeFormal.unity').read_bytes();after=(r/plan['formal']).read_bytes();assert sha(r/plan['formal'])==app['formalAfterUnitySaveSHA256']
a,b=records(before),records(after);assert set(a)<=set(b),'Old record removed'
changes=[k for k in a if a[k]['bytes']!=b[k]['bytes']];normal=[]
candidate=after
for k in changes:
 if a[k]['type']=='1660057539':continue
 assert k=='145744447' and a[k]['type']=='114',('Unexpected existing record changed',k)
 assert b[k]['bytes'].replace(b'  maximumBosses: 1\n',b'')==a[k]['bytes'],'Unexpected normalization'
 candidate=candidate.replace(b[k]['bytes'],a[k]['bytes'],1);normal.append(k)
c=records(candidate);assert [k for k in a if a[k]['bytes']!=c[k]['bytes']]==['9223372036854775807']
added=set(b)-set(a);assert len(added)==1 and all(b[k]['type']=='1001' for k in added)
(p/'CanonicalFormal.unity').write_bytes(candidate)
changed=[]
for path,spec in base['protectedFiles'].items():
 if sha(r/path)!=spec['sha256']:changed.append(path)
assert changed==[plan['formal']],changed
for mat in plan['materials']:assert sha(r/mat['path'])==mat['beforeSHA256']
prefab=r/plan['output']/'RebuildR09R18_Display.prefab';types=set(re.findall(rb'(?m)^--- !u!(\d+)',prefab.read_bytes()));assert types<={b'1',b'4',b'23',b'33'}
report={'status':'R09_R18_DISPLAY_SAVED_STATIC_CHECKS_PASSED_WITH_EXPLICIT_R16_OMISSION','packageSHA256':sha(p/'handoff.zip'),'sourceSHA256':plan['sourceSHA256'],'baselineCommit':base['head'],'branch':base['branch'],'application':app,'oldSceneRecords':len(a),'oldSceneRecordsRemoved':0,'oldRecordsChangedInCanonicalSceneOnlySceneRoots':True,'newPrefabInstances':1,'defaultFieldNormalizationExcluded':normal,'canonicalSceneSHA256':hashlib.sha256(candidate).hexdigest(),'protectedExistingFiles':len(base['protectedFiles']),'existingFilesChangedOnlyFormalScene':changed,'allExistingMaterialsTexturesShadersDEMTerrainLightmapsLODCullingAndBatchAssetsByteIdentical':True,'newPrefabComponentTypes':['GameObject','Transform','MeshFilter','MeshRenderer'],'R16OldRodOverrideApplied':False,'R19Applied':False,'R20Applied':False,'qualityGatesFile':'QUALITY_GATES_EXACT.json','rollback':{'visible':'Disable or remove only REBUILD_R09_R18_DISPLAY_ROOT; no old renderer disabled.','git':'Revert the task scene-binding commit, optionally the preceding asset/source commit; never reset unrelated work.'},'limits':['EditMode/static verification, no Play/Host/runtime/performance acceptance.','R16 target is unique but combined renderer/submesh/triangle ownership is unproven; 14 old rods are not changed.','R14 image/ORM/normal maps not bound; 15 source identities use 9 existing local material assets. R19 is a separate handoff.','Current formal branch R08 has 0 increment colliders; earlier e0edffe3 collision commit remains on its separate local branch and is not automatically merged.','Source gates retained: 8 old text UV objects / 2394 collapsed-UV triangles; 3 tangent objects / 4 zero vectors; 4 near-degenerate objects / 9 triangles.']}
(p/'DeliveryVerification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({k:report[k] for k in ['status','oldSceneRecords','oldSceneRecordsRemoved','newPrefabInstances','defaultFieldNormalizationExcluded','protectedExistingFiles','existingFilesChangedOnlyFormalScene']},indent=2))
