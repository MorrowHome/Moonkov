"""Find exact duplicate display candidates in the interrupted export manifest."""
import collections
import json
from pathlib import Path

root=Path(__file__).resolve().parent
candidate=json.loads((root/'DisplayStaging/ExportManifest.json').read_text())
source=json.loads((root/'SourceInspection.json').read_text())
types={o['name']:o['type'] for o in source['objects']}
groups=collections.defaultdict(list)
for o in candidate['objects']:
    # These keys identify identical source, world placement and material slots.
    key=(o['source'],tuple(round(v,5) for row in o['matrix_world'] for v in row),tuple(o['materials']))
    groups[key].append(o)
duplicates=[v for v in groups.values() if len(v)>1]
result={'scope':'interrupted display candidate list; no Unity assets or source changes',
        'status':'NOT_APPROVED_FOR_IMPORT',
        'candidate_instances':len(candidate['objects']),
        'unique_source_names':len({x['source'] for x in candidate['objects']}),
        'exact_duplicate_groups':len(duplicates),
        'exact_duplicate_excess_instances':sum(len(v)-1 for v in duplicates),
        'duplicate_types':dict(collections.Counter(types.get(v[0]['source'],'not_in_scene') for v in duplicates)),
        'duplicate_groups':dict(collections.Counter(v[0]['group'] for v in duplicates)),
        'duplicates':[{'source':v[0]['source'],'group':v[0]['group'],'export_names':[x['name'] for x in v],
                       'matrix_world':v[0]['matrix_world'],'materials':v[0]['materials']} for v in duplicates],
        'notes':['The interrupted exporter cached one evaluated mesh per source name; identical keys therefore export identical geometry twice.',
                 'Remove only a proven duplicate from the next display export. Preserve each source object and all distinct instance transforms.',
                 'This check does not establish that all other overlapping or hidden candidates are safe.']}
(root/'DisplayCandidateAudit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print(json.dumps({k:v for k,v in result.items() if k not in {'duplicates','notes'}},ensure_ascii=False))
