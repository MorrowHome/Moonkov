"""Record LR06 authoring light properties without assigning Unity intensity."""
import bpy
import json
from collections import Counter
from pathlib import Path

root=Path(__file__).resolve().parent
rows=[]
for obj in bpy.context.scene.objects:
    if obj.type!='LIGHT':continue
    l=obj.data
    if obj.name.startswith('LR01_'):group='LR01'
    elif obj.name.startswith('LR02_'):group='LR02'
    elif obj.name.startswith('NR01_'):group='NR01'
    elif obj.name.startswith('WC_'):group='W18'
    else:group='other_or_preview'
    node_types=[n.type for n in l.node_tree.nodes] if l.node_tree else []
    links=[{'from':x.from_node.name,'output':x.from_socket.name,'to':x.to_node.name,'input':x.to_socket.name}
           for x in l.node_tree.links] if l.node_tree else []
    nodes=[]
    if l.node_tree:
        for n in l.node_tree.nodes:
            inputs={}
            for s in n.inputs:
                if not hasattr(s,'default_value'):continue
                v=s.default_value
                try:value=list(v)
                except TypeError:value=v
                inputs[s.name]={'value':value,'linked':s.is_linked}
            nodes.append({'type':n.type,'name':n.name,'inputs':inputs})
    rows.append({'name':obj.name,'group':group,'collections':[c.name for c in obj.users_collection],
                 'hide_render':obj.hide_render,'parent':obj.parent.name if obj.parent else None,
                 'matrix_world':[list(r) for r in obj.matrix_world],
                 'type':l.type,'color':list(l.color),'energy_watts':l.energy,'shape':getattr(l,'shape',None),
                 'size':getattr(l,'size',None),'size_y':getattr(l,'size_y',None),
                 'spot_size_radians':getattr(l,'spot_size',None),'spot_blend':getattr(l,'spot_blend',None),
                 'use_shadow':l.use_shadow,'cutoff_distance':l.cutoff_distance,
                 'use_custom_distance':l.use_custom_distance,
                 'light_node_types':node_types,'light_nodes':nodes,'light_node_links':links})
result={'scope':'saved Blender light values in world frame, not Unity light intensity or gameplay binding',
        'source_lights':len(rows),'groups':dict(Counter(r['group'] for r in rows)),
        'types':dict(Counter(r['type'] for r in rows)),'lights_with_node_graph':sum(bool(r['light_nodes']) for r in rows),
        'lights':rows}
(root/'LightConversionInventory.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print('@@JSON@@'+json.dumps({k:v for k,v in result.items() if k!='lights'}),flush=True)
