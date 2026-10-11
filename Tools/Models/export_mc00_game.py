"""Blender export adapter for the authored MC00 derivative. Never saves the blend.

blender --background --disable-autoexec source.blend --python this.py -- output_dir
Keep triangulation OFF: exporter triangulation can destroy shared mesh references.
"""
import bpy
import hashlib
import json
import os
import re
import sys
import contextlib
import numpy as np
from collections import Counter
from mathutils import Vector

out = os.path.abspath(sys.argv[sys.argv.index('--') + 1])
os.makedirs(os.path.join(out, 'Textures'), exist_ok=True)
os.makedirs(os.path.join(out, 'AuthorNotes'), exist_ok=True)
def write_json(name, value):
    with open(os.path.join(out, name), 'w', encoding='utf-8') as f:
        json.dump(value, f, ensure_ascii=False, indent=2)
def safe(name):
    return re.sub(r'[^a-zA-Z0-9_.-]', '_', name)
def read_text(name):
    return json.loads(bpy.data.texts[name].as_string())
for t in bpy.data.texts:
    # Embedded recipes are provenance only, never execute them.
    if t.name.endswith(('.json', '.txt', '.csv')):
        with open(os.path.join(out, 'AuthorNotes', t.name), 'w', encoding='utf-8') as f:
            f.write(t.as_string())

image_paths = {}
for img in list(bpy.data.images):
    if not img.packed_file:
        raise RuntimeError('Expected a self-contained packed image: ' + img.name)
    data = bytes(img.packed_file.data)
    extension = '.png' if data.startswith(b'\x89PNG') else os.path.splitext(img.filepath)[1]
    if not extension:
        raise RuntimeError('Unknown packed image format: ' + img.name)
    path = 'Textures/' + safe(img.name) + '_' + hashlib.sha256(data).hexdigest()[:10] + extension
    with open(os.path.join(out, path), 'wb') as f:
        f.write(data)
    image_paths[img.name] = path

materials = []
packed_orm = {}
for mat in bpy.data.materials:
    if not mat.node_tree:
        raise RuntimeError('Material has no node tree: ' + mat.name)
    bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if not bsdf:
        raise RuntimeError('No Principled BSDF: ' + mat.name)
    def value(name):
        v = bsdf.inputs[name].default_value
        return list(v) if hasattr(v, '__len__') else float(v)
    def image_input(socket):
        if not socket.is_linked:
            return None
        n = socket.links[0].from_node
        if n.type == 'TEX_IMAGE':
            return n.image
        if n.type == 'NORMAL_MAP':
            return image_input(n.inputs['Color'])
        if n.type == 'SEPARATE_COLOR':
            return image_input(n.inputs['Color'])
        raise RuntimeError(f'Unsupported material link {mat.name}: {n.type}')
    base = image_input(bsdf.inputs['Base Color'])
    normal = image_input(bsdf.inputs['Normal'])
    rough = image_input(bsdf.inputs['Roughness'])
    metallic = image_input(bsdf.inputs['Metallic'])
    emission = image_input(bsdf.inputs['Emission Color'])
    mask = ''
    if rough or metallic:
        if rough != metallic or not rough:
            raise RuntimeError('Expected shared ORM texture: ' + mat.name)
        if rough.name not in packed_orm:
            w, h = rough.size
            pixels = np.empty(w*h*4, dtype=np.float32)
            rough.pixels.foreach_get(pixels)
            pixels = pixels.reshape((-1, 4))
            rgba = np.empty_like(pixels)
            rgba[:, 0] = pixels[:, 2]  # URP metallic
            rgba[:, 1] = pixels[:, 0]  # URP occlusion
            rgba[:, 2] = 0
            rgba[:, 3] = 1 - pixels[:, 1]  # URP smoothness
            generated = bpy.data.images.new('UnityMask_' + rough.name, width=w, height=h, alpha=True)
            generated.colorspace_settings.name = 'Non-Color'
            generated.pixels.foreach_set(rgba.ravel())
            mask_path = 'Textures/' + safe(rough.name) + '_UnityMask.png'
            generated.filepath_raw = os.path.join(out, mask_path)
            generated.file_format = 'PNG'
            generated.save()
            packed_orm[rough.name] = mask_path
        mask = packed_orm[rough.name]
    materials.append(dict(name=mat.name, baseColor=value('Base Color'), metallic=value('Metallic'),
        roughness=value('Roughness'), emissionColor=value('Emission Color'), emissionStrength=value('Emission Strength'),
        alpha=value('Alpha'), transmission=value('Transmission Weight'),
        baseMap=image_paths[base.name] if base else '', normalMap=image_paths[normal.name] if normal else '',
        maskMap=mask, emissionMap=image_paths[emission.name] if emission else ''))

render_collection = bpy.data.collections['MC00_GAME_EXPORT']
proxies = list(bpy.data.collections['MC00_COLLISION_PROXIES'].all_objects)
render = [o for o in render_collection.all_objects if o.type in {'MESH','FONT','CURVE'} and not o.hide_render]
empties = [o for o in render_collection.all_objects if o.type == 'EMPTY']
excluded = [o.name for o in render_collection.all_objects if o.type in {'MESH','FONT','CURVE'} and o.hide_render]
known_sources = {
    '8e607a8b6b8a152aea980c0b834cc46c85817210a939d8dae04f170571281f9c': 9108,
    'c985ea16ff85e0ff53d8566860ec33374d8213bd4e35976fc200874056627fc0': 9122,
}
with open(bpy.data.filepath, 'rb') as source_file:
    source_hash = hashlib.sha256(source_file.read()).hexdigest()
def option(name):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else None

# New deliveries provide their measured contract explicitly; the original presets
# remain unchanged. Never accept an unknown file just because its filename matches.
expected_hash = option('--expected-sha256')
if expected_hash:
    assert source_hash == expected_hash.lower(), 'Delivered source hash mismatch'
    expected_render = int(option('--expected-render-objects'))
    expected_proxies = int(option('--expected-proxy-objects'))
    expected_excluded = int(option('--expected-excluded-objects'))
else:
    assert source_hash in known_sources, 'Supply the new delivery hash and measured object counts'
    expected_render, expected_proxies, expected_excluded = known_sources[source_hash], 277, 32
assert len(render) == expected_render and len(proxies) == expected_proxies and len(excluded) == expected_excluded

# Only these two materials use the authored second UV. Reorder their export-only
# layers so standard URP Lit and MikkTSpace both consume the baked coordinates.
uv_remapped = []
for name in ('MC105_REST_back_pad', 'MC105_REST_fitted_seat_cushion'):
    obj = bpy.data.objects[name]
    obj.data = obj.data.copy()
    mesh = obj.data
    layers = [(u.name, np.array([tuple(v.uv) for v in u.data], dtype=np.float32)) for u in mesh.uv_layers]
    assert len(layers) == 2 and layers[1][0] == 'MC106_Portable_BakeUV'
    for layer in list(mesh.uv_layers):
        mesh.uv_layers.remove(layer)
    for name_uv, coordinates in reversed(layers):
        layer = mesh.uv_layers.new(name=name_uv)
        layer.data.foreach_set('uv', coordinates.ravel())
    mesh.uv_layers.active_index = 0
    mesh.uv_layers[0].active_render = True
    uv_remapped.append(dict(name=obj.name, uv0=mesh.uv_layers[0].name, uv1=mesh.uv_layers[1].name))

coverage = read_text('MC00_COLLISION_REPLACEMENT_COVERAGE.json')
fully_covered = {r['render_output_object'] for r in coverage if r['covers_all_source_members']}
if any(not r['covers_all_source_members'] for r in coverage):
    raise RuntimeError('Partial collision coverage requires explicit residual meshes.')
geometry = []
depsgraph = bpy.context.evaluated_depsgraph_get()
minimum = Vector((float('inf'),)*3)
maximum = Vector((float('-inf'),)*3)
for obj in render:
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    mesh.calc_loop_triangles()
    per_material = Counter()
    degenerate_material = Counter()
    degenerates = 0
    for tri in mesh.loop_triangles:
        slot = obj.material_slots[tri.material_index]
        material_name = slot.material.name if slot.material else ''
        per_material[material_name] += 1
        if tri.area < 1e-12:
            degenerates += 1
            degenerate_material[material_name] += 1
    corners = [obj.matrix_world @ Vector(v) for v in evaluated.bound_box]
    for p in corners:
        for i in range(3):
            minimum[i] = min(minimum[i], p[i]); maximum[i] = max(maximum[i], p[i])
    geometry.append(dict(name=obj.name, sourceType=obj.type, triangles=len(mesh.loop_triangles),
        materials=[s.material.name if s.material else '' for s in obj.material_slots],
        materialTriangles=[dict(name=name, triangles=count, degenerate=degenerate_material[name]) for name, count in sorted(per_material.items())],
        degenerateTriangles=degenerates,
        coveredByProxy=obj.name in fully_covered,
        # Retain fallback only for authored solids; never add duplicate covered collision.
        collisionSolid=bool(obj.get('collision_solid', False)),
        position=list(obj.matrix_world.translation), scale=list(obj.matrix_world.to_scale())))
    evaluated.to_mesh_clear()

def export(filename, chosen):
    selection = set(chosen)
    for obj in list(selection):
        parent = obj.parent
        while parent:
            selection.add(parent); parent = parent.parent
    bpy.ops.object.select_all(action='DESELECT')
    for obj in selection:
        obj.hide_set(False); obj.hide_viewport = False; obj.hide_select = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = bpy.data.objects['MC00_ROOT']
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, filename), use_selection=True,
        object_types={'MESH','EMPTY','OTHER'}, global_scale=1, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        bake_space_transform=False, use_mesh_modifiers=True, mesh_smooth_type='OFF',
        use_triangles=False, use_tspace=False, bake_anim=False, use_custom_props=False,
        path_mode='STRIP', embed_textures=False, add_leaf_bones=False)
# Blender prints a warning even for repeated identical mesh/material bindings.
# Preserve its log for diagnosis; Unity checks actual per-object material slots.
if '--metadata-only' not in sys.argv:
    with open(os.path.join(out, 'fbx-export.log'), 'w', encoding='utf-8') as log:
        with contextlib.redirect_stdout(log):
            export('MC00_Render.fbx', render + empties)
            export('MC00_Collision.fbx', proxies)
write_json('unity-manifest.json', dict(source=os.path.basename(bpy.data.filepath),
    sourceSha256=hashlib.sha256(open(bpy.data.filepath, 'rb').read()).hexdigest(),
    blender=bpy.app.version_string, triangulateFaces=False, geometry=geometry,
    renderObjects=len(render), renderTriangles=sum(g['triangles'] for g in geometry),
    proxyObjects=len(proxies), excluded=excluded, uvRemapped=uv_remapped,
    boundsMin=list(minimum), boundsMax=list(maximum), materials=materials,
    lights=read_text('MC00_LIGHT_REFERENCE.json')['anchors']))
if 'MC00_LIGHT_SWITCH_INTERACTION_HINTS.json' in bpy.data.texts:
    # Flatten matrices for JsonUtility. These remain Blender world-space metres;
    # the Unity adapter applies the same (-X, Z, -Y) basis as the light anchors.
    hints = read_text('MC00_LIGHT_SWITCH_INTERACTION_HINTS.json')
    switch_records = {s['switch_id']: s for s in read_text('MC00_LIGHT_SWITCH_MAP_V2.json')['switches']}
    buttons = []
    for hint in hints['buttons']:
        switch = switch_records[hint['switch_id']]
        obj = bpy.data.objects[hint['button_object']]
        root = bpy.data.objects[hint['interaction_root']]
        corners = [obj.matrix_world @ Vector(v) for v in obj.bound_box]
        buttons.append(dict(switch_id=hint['switch_id'], button_object=obj.name,
            interaction_root=root.name, indicator_object=switch['indicator_object'],
            indicator_material_name=switch['indicator_material_name'],
            press_travel_m=hint['press_travel_m'],
            press_axis_world=list((root.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized()),
            position_world=list(obj.matrix_world.translation),
            bounds_min=[min(v[i] for v in corners) for i in range(3)],
            bounds_max=[max(v[i] for v in corners) for i in range(3)]))
    write_json('unity-switch-interactions.json', dict(sourceSha256=source_hash, buttons=buttons))
print('MC00_EXPORT_COMPLETE', json.dumps(dict(render=len(render), proxies=len(proxies),
    triangles=sum(g['triangles'] for g in geometry), images=len(image_paths), materials=len(materials), output=out)))
