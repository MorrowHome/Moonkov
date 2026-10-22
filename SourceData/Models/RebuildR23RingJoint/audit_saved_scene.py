"""Read saved file references only. Never copies or modifies any scene file."""
from pathlib import Path
import json,re,hashlib,subprocess
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template');app=json.loads((p/'R23Application.json').read_text());sha=lambda b:hashlib.sha256(b).hexdigest();f=r/app['formal'];current=f.read_bytes();assert sha(current)==app['formalAfterUnitySaveSHA256'],'Saved scene changed after our scoped save'
before=subprocess.check_output(['git','-C',str(r),'show','HEAD:'+app['formal']]);assert sha(before)==app['formalBeforeSHA256']
def records(raw):
 text=raw.decode();ms=list(re.finditer(r'(?m)^--- !u!(\d+) &(-?\d+)[^\n]*',text));return {m[2]:text[m.start():ms[i+1].start() if i+1<len(ms) else len(text)] for i,m in enumerate(ms)}
a=records(before);b=records(current);added=b.keys()-a.keys();assert not a.keys()-b.keys() and len(added)==1
allowed={'145744447':['  maximumBosses: 1\n'],'145744448':['  m_MedicalSeconds: 3\n','  m_SearchSeconds: 2\n']};changed=[k for k in a.keys()&b.keys() if a[k]!=b[k]];assert set(changed)==set(allowed)|{'9223372036854775807'}
for key,lines in allowed.items():
 normalized=b[key]
 for line in lines:assert normalized.count(line)==1 and line not in a[key];normalized=normalized.replace(line,'',1)
 assert normalized==a[key],'Unexpected old component change'
# Compare the exact implementation defaults; no runtime script is edited.
cs=(r/'Assets/Scripts/Gameplay/Enemies/AlienEncounterSettings.cs').read_text();assert re.search(r'private int maximumBosses\s*=\s*1;',cs)
cs=(r/'Assets/Scripts/Gameplay/MoonRaidMap.cs').read_text();assert re.search(r'private float m_MedicalSeconds\s*=\s*3;',cs) and re.search(r'private float m_SearchSeconds\s*=\s*2;',cs)
newid=next(iter(added));assert b[newid].startswith('--- !u!1001 ');guid=re.search(r'(?m)^guid: (\w+)',(r/(app['prefabPath']+'.meta')).read_text())[1];assert 'guid: '+guid in b[newid]
getids=lambda s:re.findall(r'\{fileID: (-?\d+)\}',s)
assert getids(b['9223372036854775807'])==getids(a['9223372036854775807'])+[newid]
assert app['oldProtectedHashBefore']==app['oldProtectedHashAfter']
report={'passed':True,'formal':app['formal'],'beforeSHA256':sha(before),'afterSHA256':sha(current),'newPrefabInstanceID':newid,'newPrefabGUID':guid,'newSceneRecords':1,'removedSceneRecords':0,'existingRecordsChanged':{'9223372036854775807':'One appended R23 prefab instance root','145744447':'Unity serialized existing effective default maximumBosses=1','145744448':'Unity serialized existing effective defaults m_MedicalSeconds=3, m_SearchSeconds=2'},'threeDefaultFieldsRetained':True,'defaultsCheckedAgainstCurrentSourceInitializers':True,'allOtherExistingSavedSceneRecordsByteIdentical':True,'oldProtectedRenderersMeshFiltersCollidersLightsAndButtonsHashUnchanged':True,'sceneCopyCreated':False,'sceneFileWrittenByThisAudit':False,'dirtyFlagChangedByThisAudit':False,'directFieldRemovalDeniedAndAbandoned':True,'UnityPlayOrPerformanceValidation':False}
(p/'SavedSceneAudit.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
