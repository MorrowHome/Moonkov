
import json,re,subprocess,pathlib
root=pathlib.Path(__file__).parent
repo=pathlib.Path(r'E:\UnityProjects\FPS_Template')
prefab='Assets/MoonEnvironment/Variants/MC00L14GI01/Prefabs/MC00_L14v4_ADDITIONS.prefab'
old=subprocess.check_output(['git','show','HEAD:'+prefab],cwd=repo).decode('utf-8-sig').replace('\r\n','\n')
new=(repo/prefab).read_text(encoding='utf-8-sig').replace('\r\n','\n')
plan=json.loads((root/'CompatibilityIsolationPlan.json').read_text())
def records(text):
    starts=list(re.finditer(r'^--- !u!(\d+) &(-?\d+).*$',text,re.M));return {m[2]:(m[1],text[m.start():starts[i+1].start() if i+1<len(starts) else len(text)]) for i,m in enumerate(starts)}
a,b=records(old),records(new)
assert a.keys()==b.keys(),'Serialized object membership changed'
changed=[]
for key,(kind,before) in a.items():
    after=b[key][1]
    if before==after:continue
    assert kind=='1',f'Non-GameObject changed {kind}/{key}'
    name=re.search(r'^  m_Name: (.*)$',before,re.M)[1]
    assert name in plan['names'],f'Unplanned object changed {name}'
    assert re.sub(r'^  m_IsActive: 1$','  m_IsActive: 0',before,flags=re.M)==after,f'Extra serialized change {name}'
    changed.append(name)
assert set(changed)==set(plan['names']) and len(changed)==220
result={'passed':True,'changedGameObjects':220,'onlyChange':'m_IsActive: 1 -> 0','allOtherSerializedRecordsIdentical':True,'formalSceneChanged':bool(subprocess.check_output(['git','diff','--name-only','--','Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity'],cwd=repo).strip()),'oldAggregateMeshesEdited':False}
assert not result['formalSceneChanged']
(root/'CompatibilitySerializedDiff.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result))
