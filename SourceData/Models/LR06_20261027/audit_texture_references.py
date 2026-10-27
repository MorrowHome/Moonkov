"""Resolve source shader image-node names to the exact extracted packed bytes."""
import json
from collections import Counter, defaultdict
from pathlib import Path

root=Path(__file__).resolve().parent
inspection=json.loads((root/'SourceInspection.json').read_text())
packed=json.loads((root/'PackedTextureManifest.json').read_text())
images={i['source_image']:i for i in packed['images']}
uses=[];missing=[]
for material in inspection['materials']:
    connected=set()
    queue=[n['name'] for n in material['nodes'] if n['type']=='OUTPUT_MATERIAL']
    while queue:
        name=queue.pop()
        if name in connected:continue
        connected.add(name)
        queue.extend(l['from'] for l in material['links'] if l['to']==name)
    for node in material['nodes']:
        if node['type']!='TEX_IMAGE' or not node['image']:continue
        image=images.get(node['image'])
        row={'material':material['name'],'node':node['name'],'image':node['image'],
             'connected_to_output':node['name'] in connected}
        if image:
            row.update({'path':image['path'],'sha256':image['sha256'],'colorspace':image['colorspace'],
                        'alpha_mode':image['alpha_mode']})
            uses.append(row)
        else:missing.append(row)
by_digest=defaultdict(list)
for image in packed['images']:by_digest[image['sha256']].append(image)
conflicts=[digest for digest,rows in by_digest.items() if len({(x['colorspace'],x['alpha_mode']) for x in rows})>1]
result={'scope':'source shader image nodes; no Unity import',
        'source_image_nodes':len(uses)+len(missing),'connected_image_nodes':sum(u['connected_to_output'] for u in uses),
        'resolved':len(uses),'missing':missing,'same_bytes_with_different_color_or_alpha_settings':conflicts,
        'image_uses':uses}
(root/'TextureUsageAudit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print(json.dumps({k:v for k,v in result.items() if k!='image_uses'},ensure_ascii=False))
