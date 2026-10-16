import bpy, sys, json, array, struct, hashlib
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parent
PACKAGE=ROOT/'CompatWork511v2/R29_R35'
OUT=ROOT/'PreparedR35v2'
assert not OUT.exists(), 'Preserve existing export'
OUT.mkdir()
sys.path.insert(0,str(PACKAGE))
import combined_display_common as C
import apply_combined_display as A
mapping=C.mapping()
origin=Vector((2015,2130,0))

def export(ob,key,patch=None):
    deps=bpy.context.evaluated_depsgraph_get()
    evaluated=ob.evaluated_get(deps)
    mesh=evaluated.to_mesh(preserve_all_data_layers=True,depsgraph=deps)
    try:
        mesh.calc_loop_triangles()
        layers=[u for u in mesh.uv_layers if u.name!=C.UV]
        assert layers
        uvsets=[array.array('f') for _ in layers]
        verts,norms=array.array('f'),array.array('f')
        indices=[array.array('i') for _ in mesh.materials]
        normals=evaluated.matrix_world.to_3x3().inverted().transposed()
        count=0
        for tri in mesh.loop_triangles:
            for li in [tri.loops[0],tri.loops[2],tri.loops[1]]:
                v=evaluated.matrix_world@mesh.vertices[mesh.loops[li].vertex_index].co-origin
                n=(normals@mesh.corner_normals[li].vector).normalized()
                verts.extend((v.x,v.z,v.y));norms.extend((n.x,n.z,n.y))
                for channel,u in enumerate(layers):
                    source=mesh.uv_layers[C.UV] if channel==0 and patch and tri.polygon_index in patch['faces'] else u
                    uvsets[channel].extend(source.data[li].uv)
                indices[tri.material_index].append(count);count+=1
        path=OUT/(key+'.bin')
        with path.open('xb') as f:
            f.write(struct.pack('<i',count));verts.tofile(f);norms.tofile(f)
            f.write(struct.pack('<i',len(layers)))
            for i,values in enumerate(uvsets):
                f.write(struct.pack('<i',0 if i==0 else i+1));values.tofile(f)
            uvsets[0].tofile(f);f.write(struct.pack('<i',len(indices)))
            for values in indices:f.write(struct.pack('<i',len(values)));values.tofile(f)
        return {'name':ob.name,'key':key,'vertices':count,'triangles':count//3,'materials':[m.name for m in mesh.materials],
                'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'uvChannels':[0]+list(range(2,len(layers)+1))}
    finally:evaluated.to_mesh_clear()

host=bpy.data.objects[mapping['patches'][0]['object']]
baseline=export(host,'R35BaselineShroud')
receipt=A.apply()
C.verify_applied(mapping)
rows=[]
specs={p['object']:p for p in mapping['patches']}
for rec in mapping['added_objects']:
    ob=bpy.data.objects[rec['name']]
    if ob.type!='MESH':continue
    row=export(ob,'R35New_'+str(len(rows)).zfill(2),specs.get(ob.name));row['kind']='New';rows.append(row)
row=export(host,'R35Patch_00',specs[host.name]);row['kind']='Patch';row['baselineKey']=baseline['key'];rows.append(row)
assert len(rows)==12 and sum(r['kind']=='New' for r in rows)==11
result={'objects':rows,'baseline':baseline,'patches':mapping['patches'],'receipt':receipt,
        'sourceBlendSaved':False,'sourceBlend':bpy.data.filepath,'geometryAxis':'XZY, reverse winding, world minus (2015,2130,0)',
        'UnityRoot':[2015,0,2130],'lightmapChannelReserved':1,'sourceGuardsAfterExport':True}
C.verify_applied(mapping)
(OUT/'Manifest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('R35_PREPARED '+json.dumps({'newMeshes':11,'patches':2,'originalMeshPatchCopies':1,'sourceSaved':False,'out':str(OUT)}))
