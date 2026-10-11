"""Read-only inventory; run with Blender --background --disable-autoexec --python."""
import bpy
import json
import os
import sys
from collections import Counter

out = sys.argv[sys.argv.index('--') + 1]
os.makedirs(out, exist_ok=True)
def props(item):
    return {k: str(v) for k, v in item.items() if k != '_RNA_UI'}
report = {
    'blender': bpy.app.version_string,
    'units': {'system': bpy.context.scene.unit_settings.system, 'scale': bpy.context.scene.unit_settings.scale_length},
    'collections': [{'name': c.name, 'objects': len(c.objects), 'recursive': len(c.all_objects), 'types': dict(Counter(o.type for o in c.all_objects)), 'children': [x.name for x in c.children], 'properties': props(c)} for c in bpy.data.collections],
    'texts': {t.name: t.as_string() for t in bpy.data.texts},
    'materials': [],
    'images': [{'name': i.name, 'path': i.filepath, 'packed': bool(i.packed_file), 'size': list(i.size), 'colorspace': i.colorspace_settings.name} for i in bpy.data.images],
    'roots': [{'name': o.name, 'type': o.type, 'matrix': [list(r) for r in o.matrix_world], 'properties': props(o)} for o in bpy.data.objects if not o.parent],
    'special': [{'name': o.name, 'parent': o.parent.name if o.parent else None, 'type': o.type, 'properties': props(o), 'uv': [u.name for u in o.data.uv_layers] if o.type == 'MESH' else []} for o in bpy.data.objects if any(x in o.name for x in ('back_pad', 'fitted_seat', 'LIGHT', 'Light', 'ANCHOR'))][:140],
}
for m in bpy.data.materials:
    nodes = []
    if m.node_tree:
        for n in m.node_tree.nodes:
            nodes.append({'name': n.name, 'type': n.type, 'image': n.image.name if n.type == 'TEX_IMAGE' and n.image else None, 'uv_map': getattr(n, 'uv_map', None), 'inputs': {s.name: str(s.default_value) for s in n.inputs if hasattr(s, 'default_value') and not s.is_linked}})
    report['materials'].append({'name': m.name, 'nodes': nodes, 'links': [f'{l.from_node.name}.{l.from_socket.name} -> {l.to_node.name}.{l.to_socket.name}' for l in m.node_tree.links] if m.node_tree else []})
with open(os.path.join(out, 'blend-inventory.json'), 'w', encoding='utf-8') as f:
    json.dump(report, f, ensure_ascii=False, indent=2)
print('MC00_INVENTORY', json.dumps({'collections': len(report['collections']), 'materials': len(report['materials']), 'images': len(report['images']), 'text_names': list(report['texts']), 'output': out}))
