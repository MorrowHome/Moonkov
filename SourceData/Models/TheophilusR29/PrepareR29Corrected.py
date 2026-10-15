import bpy, json, pathlib, sys, struct, hashlib, array
from mathutils import Vector

p=pathlib.Path(__file__).parent
out=p/'Prepared'
assert not (out/'Manifest.json').exists()
out.mkdir(exist_ok=True)
G=json.loads((p/'geometry/R29_Final_Geometry_Manifest.json').read_text())
I=json.loads((p/'industrial_patch/R29_Industrial_Final_Mapping.json').read_text())
T=json.loads((p/'local_ground/R29_Unity_Local_Ground_Texture_Settings.json').read_text())
assert len(G['objects'])==34 and len(I['objects'])==19
for t in I['textures']:
    assert hashlib.sha256((p/'industrial_patch'/t['path']).read_bytes()).hexdigest()==t['sha256']
for t in T['files']:
    assert hashlib.sha256((p/'local_ground'/t['file']).read_bytes()).hexdigest()==t['sha256']

source=p.parent/'R21/Moonkov_R21_Open_Deep_Shaft_Latest_Candidate.blend'
base_collection=bpy.data.collections['B_26_R24_LOWER_MACHINE_AND_RIM_HERO_FINISH']
bpy.context.scene.collection.children.link(base_collection)
required=[r['object'] for r in I['objects']]+[r['object'] for r in G['hide_old_renderer_only']]
missing=[name for name in required if name not in bpy.data.objects]
with bpy.data.libraries.load(str(source),link=False) as (src,dst):
    assert all(name in src.objects for name in missing)
    dst.objects=missing
inputs=bpy.data.collections.new('R29_BASELINE_EXPORT_INPUTS')
bpy.context.scene.collection.children.link(inputs)
for o in dst.objects:
    current=o
    while current:
        if not current.users_collection:inputs.objects.link(current)
        current=current.parent
bpy.context.view_layer.update()

# Reconstruct only the requested source objects' already-installed R23 state.
R=json.loads((p.parent/'R23/R23_R21_Material_UV_Handoff.json').read_text())
U=json.loads((p.parent/'R23/R23_R21_Metric_UV_Overrides.json').read_text())
records={r['canonical_R21_display_name']:r for r in R['objects']}
for name in required:
    if name.startswith('R24_'):continue
    o=bpy.data.objects[name];r=records[name];mesh=o.data
    assert hashlib.sha256(b''.join(struct.pack('<3f',*v.co) for v in mesh.vertices)).hexdigest()==r['vertex_coordinate_sha256'], name+' R21 coordinate guard'
    assert [len(mesh.vertices),len(mesh.polygons),len(mesh.loops)]==[r['vertex_count'],r['polygon_count'],r['loop_count']]
    for s in r['material_slots']:
        assert mesh.materials[s['slot_index']].name==s['old_material']
        material=bpy.data.materials.get(s['new_material']) or bpy.data.materials.new(s['new_material'])
        mesh.materials[s['slot_index']]=material
    layer=mesh.uv_layers.active
    for loop,u,v in U[r['uv_rows_key']]:layer.data[loop].uv=(u,v)

baseline_slots={name:[m.name for m in bpy.data.objects[name].data.materials] for name in required}
fix=p/'Repairs/ExactSourceBaseline/industrial_uv_baseline_fix'
F=json.loads((fix/'R29_Industrial_UV_Guard_Correction.json').read_text())
assert hashlib.sha256((p.parent/'R23/R23_R21_Metric_UV_Overrides.json').read_bytes()).hexdigest()==F['source_R23_JSON_sha256']
assert hashlib.sha256((p.parent/'R23/R23_R21_Material_UV_Handoff.json').read_bytes()).hexdigest()==F['source_R23_handoff_mapping_sha256']
sys.path.insert(0,str(fix))
from apply_r29_with_r23_serialized_uv_guard import preflight_r29_source_baseline, apply_r29_with_r23_serialized_uv_guard
preflight=preflight_r29_source_baseline(str(p/'industrial_patch'),str(fix))
assert preflight['objects_materials_UV_visibility_written']==0 and len(preflight['ready_objects'])==19
(out/'OfflineExportPreflight.json').write_text(json.dumps(preflight,indent=2))
result=apply_r29_with_r23_serialized_uv_guard(str(p/'industrial_patch'),str(fix))
assert len(result['applied_objects'])==19 and result['changed_faces']==4639
(out/'OfflineIndustrialApplication.json').write_text(json.dumps(result,indent=2))
def geomhash(mesh):
    h=hashlib.sha256()
    for v in mesh.vertices:h.update(struct.pack('<3f',*v.co))
    for f in mesh.polygons:
        h.update(struct.pack('<I',len(f.vertices)));h.update(struct.pack('<%dI'%len(f.vertices),*f.vertices))
    return h.hexdigest()

for r in G['hide_old_renderer_only']:
    assert geomhash(bpy.data.objects[r['object']].data)==r['vertex_topology_float32_sha256']
library=p/'geometry'/G['geometry_library']
assert hashlib.sha256(library.read_bytes()).hexdigest()==G['library_sha256']
with bpy.data.libraries.load(str(library),link=False) as (src,dst):dst.collections=[G['collection']]
bpy.context.scene.collection.children.link(dst.collections[0])
bpy.context.view_layer.update()
for r in G['objects']:
    o=bpy.data.objects[r['object']];mesh=o.data
    assert geomhash(mesh)==r['vertex_topology_float32_sha256'],o.name+' geometry guard'
    assert [len(mesh.vertices),len(mesh.polygons),len(mesh.loops)]==[r['vertices'],r['polygons'],r['loops']]
    assert max(abs(o.matrix_world[i][j]-r['matrix_world'][i][j]) for i in range(4) for j in range(4))<1e-5,o.name+' world matrix'
    assert [u.name for u in mesh.uv_layers]==[u['name'] for u in r['uv_layers']]
    for u in r['uv_layers']:
        digest=hashlib.sha256(b''.join(struct.pack('<2f',*d.uv) for d in mesh.uv_layers[u['name']].data)).hexdigest()
        assert digest==u['loop_float32_sha256'],o.name+' '+u['name']+' UV guard'
    for slot in r['material_slots']:
        source_material=mesh.materials[slot['slot']]
        assert source_material.get('required_canonical_material')==slot['canonical_material']
        material=bpy.data.materials.get(slot['canonical_material']) or bpy.data.materials.new(slot['canonical_material'])
        mesh.materials[slot['slot']]=material

channels={'UVMap':0,'R26_Finish_UV':2,'R27_Industrial_Finish_UV':3,'R28_Large_Surface_UV':4,'R28_Metric_Detail_UV':5,'R8_Regolith_3p7m':6,'R8_Terrain_Global_4096m':7}
contracts={r['name']:r['shader_contract'] for r in I['materials']}
origin=Vector((2015,2130,0));deps=bpy.context.evaluated_depsgraph_get()
exported=[]
for kind,entries in [('Patch',I['objects']),('New',G['objects'])]:
    for i,r in enumerate(entries):
        o=bpy.data.objects[r['object']];evaluated=o.evaluated_get(deps)
        mesh=evaluated.to_mesh(preserve_all_data_layers=True,depsgraph=deps)
        try:
            mesh.calc_loop_triangles();materials=[m.name for m in mesh.materials]
            layers={u.name:array.array('f') for u in mesh.uv_layers}
            assert all(name in channels for name in layers),o.name+' unknown UV layer'
            vertices,normals,tangent_uv=array.array('f'),array.array('f'),array.array('f')
            indices=[array.array('i') for _ in materials]
            nm=evaluated.matrix_world.to_3x3().inverted().transposed();count=0
            for tri in mesh.loop_triangles:
                material=materials[tri.material_index];contract=contracts.get(material,{})
                basis=contract.get('detail_UV','UVMap') if material.startswith('R26V2_') else contract.get('atlas_UV','UVMap')
                assert basis in layers,o.name+' missing tangent UV '+basis
                for loop in [tri.loops[0],tri.loops[2],tri.loops[1]]:
                    pos=evaluated.matrix_world@mesh.vertices[mesh.loops[loop].vertex_index].co-origin
                    normal=(nm@mesh.corner_normals[loop].vector).normalized()
                    vertices.extend((pos.x,pos.z,pos.y));normals.extend((normal.x,normal.z,normal.y))
                    for name,values in layers.items():values.extend(mesh.uv_layers[name].data[loop].uv)
                    tangent_uv.extend(mesh.uv_layers[basis].data[loop].uv)
                    indices[tri.material_index].append(count);count+=1
            key=f'R29{kind}_{i:02d}'
            with (out/(key+'.bin')).open('wb') as f:
                f.write(struct.pack('<i',count));vertices.tofile(f);normals.tofile(f)
                f.write(struct.pack('<i',len(layers)))
                for name,values in layers.items():f.write(struct.pack('<i',channels[name]));values.tofile(f)
                tangent_uv.tofile(f);f.write(struct.pack('<i',len(indices)))
                for values in indices:f.write(struct.pack('<i',len(values)));values.tofile(f)
            exported.append({'name':o.name,'key':key,'kind':kind,'vertices':count,'triangles':count//3,'materials':materials,'baselineMaterialSlots':baseline_slots.get(o.name,[]),'UVChannels':{name:channels[name] for name in layers},'reservedLightmapChannel':1,'normalTangentUVPerMaterial':True})
        finally:evaluated.to_mesh_clear()
(out/'Manifest.json').write_text(json.dumps({'objects':exported,'industrialChangedFaces':4639,'newMeshes':34,'patchedMeshes':19,'imageHashesVerified':43,'UVChannelMap':channels,'axis':'XZY once with reversed triangle winding; source world minus (2015,2130,0), Unity root at (2015,0,2130)','sourceSaved':False,'rendered':False},indent=2))
(out/'objects.tsv').write_text('\n'.join('\t'.join([r['kind'],r['name'],r['key']]+r['materials']) for r in exported))
print(json.dumps({'new':34,'patch':19,'changedFaces':4639,'newTriangles':sum(r['triangles'] for r in exported if r['kind']=='New'),'patchTriangles':sum(r['triangles'] for r in exported if r['kind']=='Patch'),'verifiedTextureHashes':43,'sourcesSaved':False}))
