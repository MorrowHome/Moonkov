"""Compare stable source light/switch IDs with the saved Unity scene, read only."""
from pathlib import Path
import json
import re

root=Path(__file__).resolve().parent
scene=Path('Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity')
raw=scene.read_text(encoding='utf-8')
object_types={ident:kind for kind,ident in re.findall(r'^--- !u!(\d+) &(\d+)',raw,re.M)}
blocks=list(re.finditer(r'^--- !u!114 &(\d+)\n(.*?)(?=^--- |\Z)',raw,re.M|re.S))
controls=[]
buttons=[]
for block in blocks:
    ident,body=block.groups()
    if 'guid: c2f4b5e65dc2ded4eb0adc8a653f11a1' in body:
        controls.append({'file_id':ident,'light_ids':re.findall(r'^  - lightId: (.+)$',body,re.M),
                         'fixture_ids':re.findall(r'^    fixture: \{fileID: (\d+)\}',body,re.M),
                         'renderers':re.findall(r'^    - renderer: \{fileID: (\d+)\}',body,re.M),
                         'baked_lighting':re.search(r'^  bakedLighting: \{fileID: (\d+)\}',body,re.M).group(1)})
    if 'guid: b490bb18ed7b3a143a3427eaeecdc0e6' in body:
        def field(name):
            m=re.search(r'^  '+name+r': (.*)$',body,re.M)
            return m.group(1) if m else None
        targets=re.search(r'^  targets:\n(.*?)(?=^  [a-zA-Z]|\Z)',body,re.M|re.S)
        buttons.append({'file_id':ident,'switch_id':field('switchId'),'control':field('controls'),
                        'targets':re.findall(r'^  - (.+)$',targets.group(1),re.M) if targets else []})
source=json.loads((root/'SourceLightInventory.json').read_text())['functional_map']
source_lights={x['target_light_id'] for x in source['lights']}
source_buttons={x['switch_id']:x['target_light_ids'] for x in source['switches']}
unity_lights={x for c in controls for x in c['light_ids']}
unity_buttons={x['switch_id']:x['targets'] for x in buttons}
result={'scope':'saved_scene_serialization_only_no_runtime_toggle',
        'source_light_count':len(source_lights),'unity_light_count':len(unity_lights),
        'source_button_count':len(source_buttons),'unity_button_count':len(unity_buttons),
        'controls':controls,'buttons':buttons,
        'lights_missing_in_unity':sorted(source_lights-unity_lights),
        'lights_only_in_unity':sorted(unity_lights-source_lights),
        'buttons_missing_in_unity':sorted(set(source_buttons)-set(unity_buttons)),
        'buttons_only_in_unity':sorted(set(unity_buttons)-set(source_buttons)),
        'target_mismatches':[k for k in source_buttons.keys() & unity_buttons.keys()
                             if set(source_buttons[k])!=set(unity_buttons[k])],
        'duplicate_light_ids':sum(len(c['light_ids']) for c in controls)-len(unity_lights),
        'duplicate_button_ids':len(buttons)-len(unity_buttons),
        'unresolved_button_targets':sorted({x for b in buttons for x in b['targets'] if x not in unity_lights}),
        'null_fixture_count':sum(i=='0' for c in controls for i in c['fixture_ids']),
        'null_emissive_renderer_count':sum(i=='0' for c in controls for i in c['renderers']),
        'missing_or_wrong_fixture_objects':[i for c in controls for i in c['fixture_ids'] if object_types.get(i)!='108'],
        'missing_or_wrong_emissive_renderer_objects':[i for c in controls for i in c['renderers']
                                                      if object_types.get(i) not in {'23','137'}],
        'buttons_with_wrong_controller':[b['switch_id'] for b in buttons
                                        if b['control'] not in {f"{{fileID: {c['file_id']}}}" for c in controls}]}
(root/'BaselineLightContract.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print(json.dumps({k:v for k,v in result.items() if k not in {'controls','buttons'}},ensure_ascii=False))
