"""Inspect the immutable LR06 source. Does not save or change the blend file."""
import bpy
import sys
import json
from pathlib import Path
from collections import Counter
from mathutils import Vector

OUT = Path(__file__).resolve().parent
sys.path.insert(0, '/Users/morrow/.codex/skills/blender-core/scripts')
import env_report

def plain(value):
    if isinstance(value, (str, int, float, bool)) or value is None:
        return value
    try:
        return list(value)
    except TypeError:
        return str(value)

report = {'environment': env_report.run(), 'collections': [], 'objects': [], 'materials': [], 'images': [], 'texts': []}
for collection in bpy.data.collections:
    report['collections'].append({'name': collection.name, 'objects': len(collection.objects), 'all_objects': len(collection.all_objects), 'hide_render': collection.hide_render, 'properties': {k: plain(v) for k,v in collection.items()}})
for ob in bpy.context.scene.objects:
    row = {'name': ob.name, 'type': ob.type, 'parent': ob.parent.name if ob.parent else None, 'collections': [c.name for c in ob.users_collection], 'hide_render': ob.hide_render, 'hide_viewport': ob.hide_viewport, 'visible': ob.visible_get(), 'matrix_world': [list(r) for r in ob.matrix_world], 'dimensions': list(ob.dimensions), 'properties': {k: plain(v) for k,v in ob.items()}}
    if ob.type in {'MESH', 'FONT', 'CURVE'}:
        corners = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
        row['bounds'] = [[min(v[i] for v in corners) for i in range(3)], [max(v[i] for v in corners) for i in range(3)]]
        row['materials'] = [s.material.name if s.material else None for s in ob.material_slots]
        if ob.type == 'MESH':
            row.update(vertices=len(ob.data.vertices), polygons=len(ob.data.polygons), uv_maps=[uv.name for uv in ob.data.uv_layers], modifiers=[{'type': m.type, 'name': m.name, 'render': m.show_render} for m in ob.modifiers])
    if ob.type == 'LIGHT':
        l=ob.data
        row['light'] = {'type':l.type, 'energy': l.energy, 'color': list(l.color), 'shadow': l.use_shadow, 'size': getattr(l,'size',None), 'spot_size': getattr(l,'spot_size',None), 'cutoff_distance': l.cutoff_distance, 'custom_distance': l.use_custom_distance}
    report['objects'].append(row)
for mat in bpy.data.materials:
    nodes=[]
    if mat.node_tree:
        for n in mat.node_tree.nodes:
            inputs={}
            for s in n.inputs:
                if hasattr(s,'default_value'):
                    inputs[s.name]={'linked':s.is_linked,'value':plain(s.default_value)}
            nodes.append({'type':n.type, 'name':n.name, 'inputs':inputs, 'image':n.image.name if hasattr(n,'image') and n.image else None})
    report['materials'].append({'name':mat.name, 'diffuse_color':list(mat.diffuse_color), 'roughness':mat.roughness, 'metallic':mat.metallic, 'nodes':nodes, 'links':[{'from':l.from_node.name,'output':l.from_socket.name,'to':l.to_node.name,'input':l.to_socket.name} for l in mat.node_tree.links] if mat.node_tree else []})
for im in bpy.data.images:
    report['images'].append({'name':im.name,'size':list(im.size),'packed':bool(im.packed_file),'path':im.filepath,'source':im.source})
text_dir=OUT/'EmbeddedTexts'
text_dir.mkdir(exist_ok=True)
for i,t in enumerate(bpy.data.texts):
    path=text_dir/(str(i).zfill(3)+'_'+''.join(c if c.isalnum() or c in '._-' else '_' for c in t.name)+'.txt')
    path.write_text(t.as_string())
    report['texts'].append({'name':t.name,'path':str(path),'characters':len(t.as_string())})
(OUT/'SourceInspection.json').write_text(json.dumps(report,ensure_ascii=False,indent=2,default=str))
print('@@JSON@@'+json.dumps({'environment':report['environment'],'collections':report['collections'],'types':dict(Counter(o['type'] for o in report['objects'])),'materials':len(report['materials']),'images':len(report['images']),'texts':report['texts'],'report':str(OUT/'SourceInspection.json')},ensure_ascii=False))
