"""Read verified FBX increments into the project's native mesh format; never save the author blend."""
import bpy, array, struct, hashlib, json, math
from pathlib import Path
from mathutils import Matrix, Vector

ROOT=Path(__file__).resolve().parent
PACKAGE=ROOT/'Handoff'
OUT=ROOT/'NativePrepared'
SOURCE_SHA='b61cfb1c9e1b35fad0b3a1d287b3ddda1845c448397e0511fae00367f4c7acdc'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
manifest=json.loads((PACKAGE/'EXPORT_MANIFEST.json').read_text())
assert manifest['source_sha256']==SOURCE_SHA and len(manifest['objects'])==52
assert sha(PACKAGE/'EXPORT_MANIFEST.json')=='ffb7dd969e6ef4e53e42457ef281b802e46f27a1dda241b5a26c95a4aa904b1b'
inventory=json.loads((PACKAGE/'PACKAGE_CONTENTS.json').read_text())
for f in inventory['files']:
    p=PACKAGE/f['file']; assert p.stat().st_size==f['bytes'] and sha(p)==f['sha256'],f['file']
assert not OUT.exists(), 'Do not overwrite an earlier native export'
OUT.mkdir()
conversion=Matrix(((1,0,0,0),(0,0,1,0),(0,1,0,0),(0,0,0,1)))
result=[]
for partition in manifest['partitions']:
    source=PACKAGE/partition['file'];assert sha(source)==partition['sha256']
    for old in list(bpy.data.objects):bpy.data.objects.remove(old,do_unlink=True)
    for old in list(bpy.data.meshes):bpy.data.meshes.remove(old)
    for old in list(bpy.data.materials):bpy.data.materials.remove(old)
    bpy.ops.import_scene.fbx(filepath=str(source),use_anim=False,use_custom_normals=True)
    expected=[o for o in manifest['objects'] if o['partition']==partition['name']]
    imported={o.name:o for o in bpy.context.scene.objects if o.type=='MESH'}
    assert set(imported)=={o['export_object'] for o in expected},partition['name']
    for row in expected:
        ob=imported[row['export_object']]; mesh=ob.data
        assert len(mesh.vertices)==row['export_snapshot']['vertices']
        assert len(mesh.polygons)==row['export_snapshot']['polygons']
        coordinates=array.array('f',(c for v in mesh.vertices for c in v.co))
        vertex_sha=hashlib.sha256(coordinates.tobytes()).hexdigest()
        assert vertex_sha==row['export_snapshot']['local_vertex_sha256'], 'Local vertex bytes differ: '+ob.name
        mesh.calc_loop_triangles(); assert len(mesh.loop_triangles)==row['export_snapshot']['triangles']
        assert len(mesh.uv_layers)==1 and mesh.uv_layers[0].name==row['export_snapshot']['uv_layers'][0]['name']
        mats=[m.name for m in mesh.materials]
        assert set(mats)==set(row['export_snapshot']['material_slots']), 'Material identity differs: '+ob.name
        world=[ob.matrix_world@v.co for v in mesh.vertices]
        bounds=[[min(v[i] for v in world),max(v[i] for v in world)] for i in range(3)]
        expected_bounds=row['export_snapshot']['world_bounds']
        drift=max(abs(bounds[i][j]-expected_bounds[i][j]) for i in range(3) for j in range(2))
        assert drift<0.001, (ob.name,drift)
        unity_matrix=conversion@ob.matrix_world@conversion
        pos,rot,scale=unity_matrix.decompose()
        reconstructed=Matrix.LocRotScale(pos,rot,scale)
        assert max(abs(reconstructed[i][j]-unity_matrix[i][j]) for i in range(4) for j in range(4))<0.0001
        positions,normals,uv=array.array('f'),array.array('f'),array.array('f')
        indices=[array.array('i') for _ in mats];count=0
        for tri in mesh.loop_triangles:
            for li in (tri.loops[0],tri.loops[2],tri.loops[1]):
                v=mesh.vertices[mesh.loops[li].vertex_index].co
                n=mesh.corner_normals[li].vector
                positions.extend((v.x,v.z,v.y));normals.extend((n.x,n.z,n.y))
                uv.extend(mesh.uv_layers[0].data[li].uv)
                indices[tri.material_index].append(count);count+=1
        assert all(math.isfinite(v) for a in (positions,normals,uv) for v in a)
        key=row['export_object'].split('_')[1]
        path=OUT/(key+'.bin')
        with path.open('xb') as f:
            f.write(struct.pack('<i',count));positions.tofile(f);normals.tofile(f)
            f.write(struct.pack('<ii',1,0));uv.tofile(f);uv.tofile(f)
            f.write(struct.pack('<i',len(indices)))
            for ids in indices:f.write(struct.pack('<i',len(ids)));ids.tofile(f)
        result.append({'name':row['source_object'],'key':key,'exportObject':row['export_object'],
                       'partition':row['partition'],'vertices':count,'triangles':count//3,
                       'materials':mats,'sha256':sha(path),
                       'position':list(pos),'rotation':[rot.x,rot.y,rot.z,rot.w],'scale':list(scale),
                       'worldBounds':[[bounds[0][0],bounds[2][0],bounds[1][0]],
                                      [bounds[0][1],bounds[2][1],bounds[1][1]]],
                       'boundsDriftMetres':drift,'exactFBXLocalVertexBytes':True,
                       'sourceUVPreserved':True,'sourcePivotPreserved':True})
    print('NATIVE_R19 '+partition['name']+' '+str(len(expected)),flush=True)
report={'schema':'R19_native_display_v1','sourceSHA256':SOURCE_SHA,
        'packageSHA256':'7d1b2fb57b5fb00bb4f15c1fcf1365a3473cce12d6232c55b19419a4affaf724',
        'objects':result,'materials':manifest['materials'],
        'summary':{'objects':len(result),'triangles':sum(r['triangles'] for r in result),
                   'maxBoundsDriftMetres':max(r['boundsDriftMetres'] for r in result),
                   'baseGeometryExported':False,'authorBlendSaved':False,
                   'preservedUVChannels':[0],'reservedLightmapChannel':1,
                   'protectedLocalAlloyMaterialReused':True,'preservedLocalPivots':True}}
assert report['summary']['objects']==52 and report['summary']['triangles']==5272
(OUT/'Manifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('NATIVE_R19_COMPLETE '+json.dumps(report['summary']),flush=True)
