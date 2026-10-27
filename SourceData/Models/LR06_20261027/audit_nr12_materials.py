"""Classify NR12 staged objects by UV and connected material coordinate inputs."""
import json
from collections import Counter
from pathlib import Path

root=Path(__file__).resolve().parent
source=json.loads((root/'SourceInspection.json').read_text())
staged=json.loads((root/'DisplayStaging/ExportManifest.json').read_text())
materials={m['name']:m for m in source['materials']}
classified={}
for name,m in materials.items():
    nodes={n['name']:n for n in m['nodes']}
    todo=[n['name'] for n in m['nodes'] if n['type']=='OUTPUT_MATERIAL']
    connected=set()
    while todo:
        n=todo.pop()
        if n in connected:continue
        connected.add(n)
        todo.extend(l['from'] for l in m['links'] if l['to']==n)
    connected_links=[l for l in m['links'] if l['from'] in connected and l['to'] in connected]
    inputs=[]
    for l in connected_links:
        kind=nodes[l['from']]['type']
        if kind=='NEW_GEOMETRY' and l['output'] in {'Position','Normal'}:
            inputs.append('geometry_'+l['output'].lower())
        if kind=='OBJECT_INFO' and l['output']=='Random':inputs.append('object_random')
        if kind=='UVMAP':inputs.append('uv_map')
        if kind=='TEX_COORD':inputs.append('texcoord_'+l['output'].lower())
    types={nodes[n]['type'] for n in connected}
    classified[name]={'coordinate_inputs':sorted(set(inputs)),
                      'connected_image_count':sum(nodes[n]['type']=='TEX_IMAGE' for n in connected),
                      'connected_procedural':bool(types & {'TEX_NOISE','TEX_WHITE_NOISE','AMBIENT_OCCLUSION','GROUP','BUMP'}),
                      'constant_principled':bool(types <= {'BSDF_PRINCIPLED','OUTPUT_MATERIAL'})}
rows=[]
for obj in staged['objects']:
    if obj['group']!='NR12':continue
    slots=[{'name':name,**classified.get(name,{})} for name in obj['materials'] if name]
    rows.append({'source':obj['source'],'export_name':obj['name'],'has_uv':bool(obj['uv_maps']),'slots':slots})
summary={'staged_instances':len(rows),'unique_source_objects':len({r['source'] for r in rows}),
         'without_uv':sum(not r['has_uv'] for r in rows),
         'with_image_in_a_connected_material_slot':sum(any(s.get('connected_image_count',0)>0 for s in r['slots']) for r in rows),
         'with_procedural_in_a_connected_material_slot':sum(any(s.get('connected_procedural') for s in r['slots']) for r in rows),
         'with_world_position_in_a_connected_material_slot':sum(any('geometry_position' in s.get('coordinate_inputs',[]) for s in r['slots']) for r in rows),
         'with_object_random_in_a_connected_material_slot':sum(any('object_random' in s.get('coordinate_inputs',[]) for s in r['slots']) for r in rows),
         'all_slots_constant_principled':sum(bool(r['slots']) and all(s.get('constant_principled') for s in r['slots']) for r in rows),
         'objects_without_slots':sum(not r['slots'] for r in rows)}
result={'scope':'NR12 staged object slots and connected shader nodes; not per-face material usage',
        'warning':'Candidate export list is not yet proven complete or free of duplicates.',
        'summary':summary,'objects':rows}
(root/'NR12MaterialAudit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print(json.dumps(summary,ensure_ascii=False))
