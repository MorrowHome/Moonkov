import bpy, json, pathlib, hashlib, struct, array
from mathutils import Matrix, Vector

p = pathlib.Path(__file__).parent
contract = json.loads((p/'R24_R21_Base_Display_Mapping.json').read_text())
out = p/'Prepared'
assert not (out/'Manifest.json').exists()
out.mkdir(exist_ok=True)
(out/'PackedImages').mkdir(exist_ok=True)
collection = bpy.data.collections[contract['append_collection']]
if collection.name not in bpy.context.scene.collection.children:
    bpy.context.scene.collection.children.link(collection)
bpy.context.view_layer.update()
root = bpy.data.objects[contract['root_object']]
assert root in collection.all_objects.values()
assert not any(o.type in {'LIGHT', 'CAMERA'} for o in collection.all_objects)
assert len([o for o in collection.all_objects if o.type == 'MESH']) == 14
assert max(abs(root.matrix_world[i][j]-contract['coordinates']['root_blender_matrix_rows'][i][j]) for i in range(4) for j in range(4)) < 1e-6
deps = bpy.context.evaluated_depsgraph_get()
origin = Vector((2015,2130,0))
records, rows, used = [], [], set()
for i, entry in enumerate(contract['objects']):
    o = bpy.data.objects[entry['object']]
    raw = o.data
    assert o.name in collection.all_objects and o.type == 'MESH'
    assert o.parent and o.parent.name == entry['parent']
    assert [len(raw.vertices),len(raw.polygons),len(raw.loops)] == [entry['vertex_count'],entry['polygon_count'],entry['loop_count']]
    digest = hashlib.sha256(b''.join(struct.pack('<fff',*v.co) for v in raw.vertices)).hexdigest()
    assert digest == entry['vertex_coordinate_float32_le_sha256'], o.name+' source hash'
    assert max(abs(o.matrix_world[a][b]-entry['matrix_world_blender_rows'][a][b]) for a in range(4) for b in range(4)) < 1e-5, o.name+' matrix differs: '+str([list(r) for r in o.matrix_world])
    mats = [m.name for m in raw.materials]
    assert mats == [m['name'] for m in entry['materials']], o.name+' slots'
    assert [sum(p.material_index == j for p in raw.polygons) for j in range(len(mats))] == [m['polygon_count'] for m in entry['materials']]
    assert raw.uv_layers.active and raw.uv_layers.active.name == entry['uv_layer']
    evaluated = o.evaluated_get(deps)
    mesh = evaluated.to_mesh(preserve_all_data_layers=True,depsgraph=deps)
    try:
        assert [m.name for m in mesh.materials] == mats
        mesh.calc_loop_triangles()
        normal_matrix = evaluated.matrix_world.to_3x3().inverted().transposed()
        v,n,uv = array.array('f'),array.array('f'),array.array('f')
        indices = [array.array('i') for _ in mats]
        count = 0
        for tri in mesh.loop_triangles:
            for loop in [tri.loops[0],tri.loops[2],tri.loops[1]]:
                pos = evaluated.matrix_world@mesh.vertices[mesh.loops[loop].vertex_index].co-origin
                normal = (normal_matrix@mesh.corner_normals[loop].vector).normalized()
                v.extend((pos.x,pos.z,pos.y)); n.extend((normal.x,normal.z,normal.y))
                uv.extend(mesh.uv_layers.active.data[loop].uv)
                indices[tri.material_index].append(count);count += 1
        key = f'R24_Mesh_{i:02d}'
        with (out/(key+'.bin')).open('wb') as f:
            f.write(struct.pack('<i',count));v.tofile(f);n.tofile(f);uv.tofile(f)
            f.write(struct.pack('<i',len(indices)))
            for values in indices:f.write(struct.pack('<i',len(values)));values.tofile(f)
        rows.append('\t'.join([o.name,key]+mats));used.update(mats)
        records.append({'name':o.name,'key':key,'sourceCoordinateHash':digest,'sourceCounts':[len(raw.vertices),len(raw.polygons),len(raw.loops)],'evaluatedVertices':count,'triangles':count//3,'materials':mats,'modifiersEvaluated':[m.name for m in o.modifiers]})
    finally:evaluated.to_mesh_clear()
assert used == {m['name'] for m in contract['materials']}
images=[]
for entry in contract['packed_images']:
    image = bpy.data.images[entry['image']]
    assert image.packed_file and image.colorspace_settings.name == entry['color_space']
    data = image.packed_file.data
    (out/'PackedImages'/image.name).write_bytes(data)
    images.append({'name':image.name,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'colorSpace':image.colorspace_settings.name})
(out/'objects.tsv').write_text('\n'.join(rows))
(out/'Manifest.json').write_text(json.dumps({'objects':records,'images':images,'materials':sorted(used),'axis':'Blender XZY, one triangle winding reversal; source world minus (2015,2130,0), Unity root (2015,0,2130)','sourceSaved':False},indent=2))
print(json.dumps({'exportedMeshes':len(records),'materials':len(used),'images':len(images),'triangles':sum(r['triangles'] for r in records),'sourceSaved':False}))
