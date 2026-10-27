"""Compare candidate source names with saved scene names; no geometry claim."""
import json
import re
from collections import Counter,defaultdict
from pathlib import Path

root=Path(__file__).resolve().parent
manifest=json.loads((root/'DisplayStaging/ExportManifest.json').read_text())
text=Path('Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity').read_text()
scene_names=[]
for m in re.finditer(r'^--- !u!1 &\d+\n(.*?)(?=^--- |\Z)',text,re.M|re.S):
    name=re.search(r'^  m_Name: (.*)$',m.group(1),re.M)
    if name:scene_names.append(name.group(1).strip('"'))
counts=Counter(scene_names)
dedup=set();groups=defaultdict(Counter);examples=defaultdict(lambda:defaultdict(list))
for o in manifest['objects']:
    key=(o['source'],tuple(round(v,5) for row in o['matrix_world'] for v in row),tuple(o['materials']))
    if key in dedup:continue
    dedup.add(key)
    matches=counts[o['source']]
    status='missing_exact_name' if matches==0 else 'one_exact_name' if matches==1 else 'multiple_exact_names'
    groups[o['group']][status]+=1
    if len(examples[o['group']][status])<12:examples[o['group']][status].append(o['source'])
result={'scope':'exact GameObject name comparison only; scene aggregates, visibility, transforms and mesh content are unverified',
        'scene_object_names':len(scene_names),'candidate_instances_after_exact_duplicate_removal':len(dedup),
        'groups':{k:dict(v) for k,v in groups.items()},'examples':{k:dict(v) for k,v in examples.items()}}
(root/'SceneNameOverlap.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print(json.dumps({k:v for k,v in result.items() if k!='examples'},ensure_ascii=False))
