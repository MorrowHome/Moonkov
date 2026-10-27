"""Stage LR06 display geometry without saving the source or touching Assets."""
raise RuntimeError('UNVERIFIED ARCHIVE: this interrupted exporter duplicates text/curves and does not correct inward faces. Do not use it for Unity import.')
import bpy
import hashlib
import json
import re
import sys
from pathlib import Path
from collections import Counter
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
OUT = ROOT / 'DisplayStaging'
OUT.mkdir(exist_ok=True)
SOURCE_SHA = 'cdb003ad58e2c081076cc874eb9f1c4fdf3c216e056321257dabcf5d6f817054'
assert hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest() == SOURCE_SHA

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def safe(name):
    return re.sub(r'[^a-zA-Z0-9_.-]', '_', name)

scene = bpy.context.scene
dg = bpy.context.evaluated_depsgraph_get()
allowed_collections = set()
def walk(collection, hidden=False):
    hidden = hidden or collection.hide_render
    if not hidden:
        allowed_collections.add(collection.name)
    for child in collection.children:
        walk(child, hidden)
walk(scene.collection)

def hidden(ob):
    if ob.hide_render:
        return True
    parent = ob.parent
    while parent:
        if parent.hide_render:
            return True
        parent = parent.parent
    return False

def excluded(ob):
    names = [c.name for c in ob.users_collection]
    # Keep the project's actual DEM/TerrainData and all existing Collider ownership.
    return (any(s in ob.name.upper() for s in ['TERRAIN', 'THEOPHILUS_SHOULDER', 'THEOPHILUS_BACKGROUND'])
            or ob.name.startswith(('COL_', 'COLLISION_', 'REFERENCE_'))
            or any(any(s in name.upper() for s in ['COLLISION', 'NOT_EXPORT', 'REFERENCE_REAL_DEM']) for name in names))

def group(ob):
    names = ' '.join(c.name for c in ob.users_collection)
    root = ob
    while root.parent:
        root = root.parent
    text = ob.name + ' ' + root.name + ' ' + names
    if 'GI01' in text:
        return 'GI01'
    if any(s in text for s in ['MC00', 'L14_', 'GAME_', 'MC_L', 'MC_N', 'MC_W', 'MC_E']):
        return 'MC00v5'
    if 'NR01' in text:
        return 'NR12'
    if any(s in text for s in ['WC_', 'WEST_NEXT']):
        return 'W18'
    if ob.name.startswith(('CS01', 'ES01', 'CM01', 'TH01', 'FS01', 'GH01', 'WH01', 'HU01')):
        return 'DetailRevisions'
    return 'Map'

instances = []
rejected = Counter()
for instance in dg.object_instances:
    ob = instance.object.original
    if ob.type not in {'MESH', 'CURVE', 'FONT'}:
        continue
    if hidden(ob) or excluded(ob):
        rejected['hidden_or_reference_or_terrain_or_collision'] += 1
        continue
    if instance.is_instance:
        if instance.parent and hidden(instance.parent.original):
            rejected['hidden_instance_parent'] += 1
            continue
    elif not any(c.name in allowed_collections for c in ob.users_collection):
        rejected['render_hidden_collection'] += 1
        continue
    if not instance.show_self:
        rejected['dependency_graph_not_shown'] += 1
        continue
    # Evaluated RNA references expire when another scene/data graph is changed.
    # Keep original IDs and value copies only across the snapshot boundary.
    instances.append((ob.name, instance.matrix_world.copy(), group(ob)))

report = {'source_sha256': SOURCE_SHA, 'status': 'OFFLINE_GEOMETRY_STAGING_NOT_UNITY_IMPORTED',
          'frame': 'Original Blender world metres, FBX -Z forward / Y up. Verify numeric bounds in Unity before scene binding.',
          'excluded': dict(rejected), 'parts': [], 'objects': [], 'images': [],
          'material_status': 'Original Blender node graphs are in SourceInspection.json. FBX material previews are not approved equivalents. Procedural and linked node inputs require explicit adaptation or baking before scene installation.',
          'unity_lighting_status': 'No Unity lights created. Source lighting is fully inventoried, not mapped 1:1 to realtime lights.',
          'source_modified': False}

# Export copies only. Neither the original file nor its data blocks are saved.
mesh_cache = {}
bound_cache = {}
for source_name, matrix, category in instances:
    if source_name in mesh_cache:
        continue
    source = bpy.data.objects[source_name]
    evaluated = source.evaluated_get(dg)
    bound_cache[source_name] = [Vector(c) for c in evaluated.bound_box]
    mesh_cache[source_name] = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=dg)
print('EXTRACTED_EVALUATED_MESHES',len(mesh_cache),flush=True)

stage_scene = bpy.data.scenes.new('LR06_Offline_Export_Only')
buckets = {}
for index, (source_name, matrix, category) in enumerate(instances):
    source = bpy.data.objects[source_name]
    mesh = mesh_cache[source_name]
    if not mesh or not mesh.vertices:
        continue
    clone = bpy.data.objects.new('LR06_' + str(index).zfill(5) + '_' + source.name, mesh)
    stage_scene.collection.objects.link(clone)
    clone.matrix_world = matrix
    # Preserve per-object material overrides such as LR06's warm diffuser.
    for slot, original_slot in zip(clone.material_slots, source.material_slots):
        slot.link = original_slot.link
        if original_slot.link == 'OBJECT':
            slot.material = original_slot.material
    corners = [matrix @ c for c in bound_cache[source_name]]
    bounds = [[min(v[i] for v in corners) for i in range(3)], [max(v[i] for v in corners) for i in range(3)]]
    mesh.calc_loop_triangles()
    row = {'name': clone.name, 'source': source.name, 'group': category,
           'vertices':len(mesh.vertices), 'triangles':len(mesh.loop_triangles),
           'source_world_bounds':bounds, 'materials':[s.material.name if s.material else None for s in clone.material_slots],
           'uv_maps':[u.name for u in mesh.uv_layers], 'matrix_world':[list(r) for r in matrix]}
    report['objects'].append(row)
    buckets.setdefault(category, []).append(clone)

bpy.context.window.scene = stage_scene
for category, objects in sorted(buckets.items()):
    for start in range(0, len(objects), 500):
        batch = objects[start:start+500]
        bpy.ops.object.select_all(action='DESELECT')
        for ob in batch:
            ob.select_set(True)
        bpy.context.view_layer.objects.active = batch[0]
        target = OUT / (category + '_' + str(start//500).zfill(2) + '.fbx')
        result = bpy.ops.export_scene.fbx(filepath=str(target), use_selection=True,
                    object_types={'MESH'}, use_mesh_modifiers=False, add_leaf_bones=False,
                    bake_anim=False, axis_forward='-Z', axis_up='Y', global_scale=1,
                    apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
                    use_space_transform=True, bake_space_transform=False,
                    mesh_smooth_type='OFF', use_triangles=True, path_mode='STRIP')
        assert result == {'FINISHED'} and target.stat().st_size > 0
        report['parts'].append({'path':str(target),'group':category,'objects':len(batch),
                                'bytes':target.stat().st_size,'sha256':digest(target)})
        (OUT/'ExportManifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
        print('STAGED', target.name, len(batch), flush=True)

texture_dir = OUT/'PackedTextures'
texture_dir.mkdir(exist_ok=True)
for image in bpy.data.images:
    if not image.packed_file:
        continue
    original = Path(image.filepath).name
    target = texture_dir/safe(image.name)
    payload = bytes(image.packed_file.data)
    suffix = Path(original).suffix or '.bin'
    if not target.suffix:
        target = target.with_suffix(suffix)
    target.write_bytes(payload)
    report['images'].append({'name':image.name,'path':str(target),'bytes':len(payload),'sha256':digest(target),'colorspace':image.colorspace_settings.name})
(OUT/'ExportManifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print('@@JSON@@'+json.dumps({'parts':len(report['parts']),'objects':len(report['objects']),
      'triangles':sum(o['triangles'] for o in report['objects']),'textures':len(report['images']),
      'status':report['status']},ensure_ascii=False),flush=True)
