"""Create compact, reversible LR06 display-only FBX chunks from the source scene.

This is a layout preview. Complex materials use their authored viewport color;
no gameplay, terrain, collision, switch, probe or final-lighting data is changed.
"""
import bpy
import bmesh
import hashlib
import json
import math
import sys
import time
from collections import Counter
from pathlib import Path
from mathutils import Matrix, Vector

root = Path(__file__).resolve().parent
out = root / 'FullPreviewStaging'
out.mkdir(exist_ok=True)
source = root / 'Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend'
source_sha = 'cdb003ad58e2c081076cc874eb9f1c4fdf3c216e056321257dabcf5d6f817054'
assert Path(bpy.data.filepath) == source and hashlib.sha256(source.read_bytes()).hexdigest() == source_sha
staged = json.loads((root / 'DisplayStaging' / 'ExportManifest.json').read_text())['objects']
texture_inventory = json.loads((root / 'PackedTextureManifest.json').read_text())
images = {row['source_image']: row for row in texture_inventory['images']}
assert texture_inventory['source_sha256'] == source_sha

# Remove only provably identical source/matrix/material copies from the earlier
# visible-instance inventory. Distinct transforms remain present.
seen, rows, duplicate_names = set(), [], []
for row in staged:
    key = (row['source'], tuple(tuple(v) for v in row['matrix_world']), tuple(row['materials']))
    if key in seen:
        duplicate_names.append(row['name'])
        continue
    seen.add(key)
    rows.append(row)
assert len(staged) == 30845 and len(duplicate_names) == 633 and len(rows) == 30212


def material_recipe(mat):
    result = {'name': mat.name, 'base_color': list(mat.diffuse_color), 'metallic': mat.metallic,
              'roughness': mat.roughness, 'emission_color': [0, 0, 0, 1], 'emission_strength': 0,
              'base_texture': None, 'status': 'viewport_color_approximation'}
    if not mat.node_tree:
        result['status'] = 'authored_constant'
        return result
    nodes = mat.node_tree.nodes
    bsdfs = [n for n in nodes if n.type == 'BSDF_PRINCIPLED']
    if bsdfs:
        b = bsdfs[0]
        for name, field in [('Metallic', 'metallic'), ('Roughness', 'roughness'),
                            ('Emission Color', 'emission_color'), ('Emission Strength', 'emission_strength')]:
            socket = b.inputs.get(name)
            if socket and not socket.is_linked:
                value = socket.default_value
                result[field] = list(value) if hasattr(value, '__len__') else float(value)
        base = b.inputs['Base Color']
        if not base.is_linked:
            result['base_color'] = list(base.default_value)
            result['status'] = 'authored_constant'
        elif len(base.links) == 1 and base.links[0].from_node.type == 'TEX_IMAGE':
            tex = base.links[0].from_node
            if tex.image and tex.image.name in images:
                result['base_color'] = [1, 1, 1, 1]
                result['base_texture'] = images[tex.image.name]
                result['status'] = 'direct_base_texture_other_nodes_may_be_approximate'
        if any(b.inputs[n].is_linked for n in ('Metallic', 'Roughness', 'Normal', 'Emission Color', 'Emission Strength')):
            result['status'] += '_other_nodes_approximate'
    else:
        emission = next((n for n in nodes if n.type == 'EMISSION'), None)
        if emission:
            if not emission.inputs['Color'].is_linked:
                result['emission_color'] = list(emission.inputs['Color'].default_value)
            if not emission.inputs['Strength'].is_linked:
                result['emission_strength'] = float(emission.inputs['Strength'].default_value)
            result['status'] = 'emission_approximation'
    return result


material_names = sorted({name for row in rows for name in row['materials'] if name})
materials = []
for name in material_names:
    material = bpy.data.materials.get(name)
    if material:
        materials.append(material_recipe(material))
    else:
        materials.append({'name': name, 'status': 'missing_source_material', 'base_color': [.5, .5, .5, 1],
                          'metallic': 0, 'roughness': .5, 'emission_color': [0, 0, 0, 1],
                          'emission_strength': 0, 'base_texture': None})
recipe = {'source_sha256': source_sha, 'scope': 'display-only approximate materials; complex nodes are not baked',
          'materials': materials, 'status_counts': dict(Counter(m['status'] for m in materials))}
(out / 'MaterialPlan.json').write_text(json.dumps(recipe, ensure_ascii=False, indent=2))
print('PREVIEW_MATERIAL_PLAN', len(materials), recipe['status_counts'], flush=True)

depsgraph = bpy.context.evaluated_depsgraph_get()
source_names = sorted({row['source'] for row in rows})
mesh_cache, qa_rows = {}, []
started = time.monotonic()
for index, name in enumerate(source_names):
    obj = bpy.data.objects.get(name)
    assert obj and obj.type in {'MESH', 'CURVE', 'FONT'}, name
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
    if not mesh or not mesh.vertices or not mesh.polygons:
        qa_rows.append({'source': name, 'status': 'empty_evaluated_mesh'})
        continue
    # Common UV layer names preserve each source's UV set by index after joins.
    for i, layer in enumerate(mesh.uv_layers):
        layer.name = 'LR06_UV' + str(i)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    visited, corrected, opened, degenerate = set(), 0, 0, 0
    for face in bm.faces:
        if face.index in visited:
            continue
        pending, component, closed = [face], [], True
        while pending:
            current = pending.pop()
            if current.index in visited:
                continue
            visited.add(current.index)
            component.append(current)
            for edge in current.edges:
                if not edge.is_manifold:
                    closed = False
                pending.extend(other for other in edge.link_faces if other.index not in visited)
        if not closed:
            opened += 1
            continue
        vertices = {v for face in component for v in face.verts}
        center = sum((v.co for v in vertices), Vector()) / len(vertices)
        volume = 0.0
        for f in component:
            for i in range(1, len(f.verts) - 1):
                a, b, c = (f.verts[j].co - center for j in (0, i, i + 1))
                volume += a.dot(b.cross(c)) / 6.0
        if volume < -1e-9:
            bmesh.ops.reverse_faces(bm, faces=component)
            corrected += 1
        elif abs(volume) <= 1e-9:
            degenerate += 1
    if corrected:
        bm.to_mesh(mesh)
        mesh.update()
    bm.free()
    mesh_cache[name] = mesh
    if corrected or opened or degenerate:
        qa_rows.append({'source': name, 'corrected_closed_components': corrected,
                        'open_or_nonmanifold_components': opened, 'ambiguous_zero_volume_components': degenerate})
    if (index + 1) % 2000 == 0:
        print('PREVIEW_GEOMETRY_PREPARED', index + 1, '/', len(source_names),
              'elapsed_s', round(time.monotonic() - started, 1), flush=True)

empty = [row for row in qa_rows if row.get('status') == 'empty_evaluated_mesh']
assert not empty, ('Visible candidates with empty evaluated mesh require inspection', empty[:10])
qa = {'scope': 'evaluated source copies; only unambiguously inward closed components are reversed',
      'sources': len(source_names), 'sources_with_corrected_closed_components': sum(r.get('corrected_closed_components', 0) > 0 for r in qa_rows),
      'sources_with_open_or_nonmanifold_components': sum(r.get('open_or_nonmanifold_components', 0) > 0 for r in qa_rows),
      'sources_with_zero_volume_components': sum(r.get('ambiguous_zero_volume_components', 0) > 0 for r in qa_rows),
      'rows': qa_rows}
(out / 'GeometryPreparation.json').write_text(json.dumps(qa, ensure_ascii=False, indent=2))

slot_overrides = {name: [(slot.link, slot.material) for slot in bpy.data.objects[name].material_slots]
                  for name in source_names}
scene = bpy.data.scenes.new('LR06_FullDisplay_Staging')
bpy.context.window.scene = scene
# The opened source is never saved. Remove its original in-memory object and
# mesh datablocks after taking evaluated copies, so Blender does not scan tens
# of thousands of unrelated IDs every time a temporary join object is freed.
# This does not delete or rewrite any file, project asset or source data.
bpy.data.batch_remove(ids=list(bpy.data.objects))
kept_meshes = {mesh.as_pointer() for mesh in mesh_cache.values()}
discard_meshes = [mesh for mesh in bpy.data.meshes if mesh.as_pointer() not in kept_meshes]
bpy.data.batch_remove(ids=discard_meshes)
print('PREVIEW_MEMORY_TRIMMED', len(source_names), 'evaluated meshes retained', flush=True)
parts = []
manifest_path = out / 'ExportManifest.json'
previous_parts = {}
if manifest_path.exists():
    previous = json.loads(manifest_path.read_text())
    for part in previous.get('parts', []):
        candidate = root / part['path']
        if candidate.is_file() and candidate.stat().st_size == part['bytes'] and \
                hashlib.sha256(candidate.read_bytes()).hexdigest() == part['sha256']:
            previous_parts[part['path']] = part
for group in sorted({row['group'] for row in rows}):
    selected = [row for row in rows if row['group'] == group]
    # Nearby pieces stay together, limiting the cost of coarse culling.
    def spatial(row):
        bounds = row['source_world_bounds']
        center = [(bounds[0][i] + bounds[1][i]) / 2 for i in range(3)]
        return tuple(math.floor(center[i] / (80 if i < 2 else 50)) for i in range(3)) + (row['name'],)
    selected.sort(key=spatial)
    for start in range(0, len(selected), 500):
        chunk = selected[start:start + 500]
        target = out / (group + '_' + str(start // 500).zfill(2) + '.fbx')
        relative_target = str(target.relative_to(root))
        old = previous_parts.get(relative_target)
        if old and old['group'] == group and old['source_names'] == [item['source'] for item in chunk]:
            parts.append(old)
            print('PREVIEW_REUSED', group, len(parts), 'instances', len(chunk), flush=True)
            continue
        anchor_mesh = bpy.data.meshes.new('LR06_JoinAnchor')
        anchor = bpy.data.objects.new('LR06_' + group + '_' + str(start // 500).zfill(2), anchor_mesh)
        scene.collection.objects.link(anchor)
        clones = []
        for entry in chunk:
            clone = bpy.data.objects.new(entry['name'], mesh_cache[entry['source']])
            scene.collection.objects.link(clone)
            clones.append(clone)
            clone.matrix_world = Matrix(entry['matrix_world'])
            for slot, (link, material) in zip(clone.material_slots, slot_overrides[entry['source']]):
                slot.link = link
                if link == 'OBJECT':
                    slot.material = material
        with bpy.context.temp_override(object=anchor, active_object=anchor,
                                       selected_objects=[anchor, *clones], selected_editable_objects=[anchor, *clones]):
            assert bpy.ops.object.join() == {'FINISHED'}
        bpy.ops.object.select_all(action='DESELECT')
        anchor.select_set(True)
        bpy.context.view_layer.objects.active = anchor
        assert bpy.ops.export_scene.fbx(filepath=str(target), use_selection=True, object_types={'MESH'},
            use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False, axis_forward='-Z', axis_up='Y',
            global_scale=1, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
            use_space_transform=True, bake_space_transform=False, use_triangles=True, path_mode='STRIP') == {'FINISHED'}
        assert target.stat().st_size > 0
        anchor.data.calc_loop_triangles()
        part = {'path': str(target.relative_to(root)), 'group': group, 'instances': len(chunk),
                'source_names': [item['source'] for item in chunk], 'vertices': len(anchor.data.vertices),
                'triangles': len(anchor.data.loop_triangles),
                'materials': [slot.material.name if slot.material else None for slot in anchor.material_slots],
                'bytes': target.stat().st_size, 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()}
        parts.append(part)
        manifest_path.write_text(json.dumps({'status': 'export_in_progress', 'parts': parts}, ensure_ascii=False, indent=2))
        print('PREVIEW_EXPORTED', group, len(parts), 'instances', len(chunk),
              'elapsed_s', round(time.monotonic() - started, 1), flush=True)
        mesh = anchor.data
        bpy.data.objects.remove(anchor, do_unlink=True)
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)

report = {'status': 'full_geometry_staged_not_yet_Unity_validated', 'source_sha256': source_sha,
          'source_file_saved': False, 'visible_instances': len(rows), 'removed_exact_duplicates': len(duplicate_names),
          'groups': dict(Counter(row['group'] for row in rows)), 'parts': parts,
          'triangles_before_join': sum(row['triangles'] for row in rows),
          'notes': ['Display-only preview. Old formal scene, terrain, collision, interactions and switch IDs are untouched.',
                    'Material recipes mark procedural approximations; final lightmaps and probes are not installed.',
                    'Every FBX is hash-bound and must be verified in Unity before display.']}
manifest_path.write_text(json.dumps(report, ensure_ascii=False, indent=2))
print('@@JSON@@' + json.dumps({'status': report['status'], 'parts': len(parts), 'instances': len(rows),
      'triangles_before_join': report['triangles_before_join']}, ensure_ascii=False), flush=True)
