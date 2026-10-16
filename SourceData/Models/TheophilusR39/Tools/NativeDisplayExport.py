import bpy, array, struct, hashlib
from mathutils import Vector

def export(ob,out,key,channels,patch_faces=None):
    deps=bpy.context.evaluated_depsgraph_get();ev=ob.evaluated_get(deps)
    mesh=ev.to_mesh(preserve_all_data_layers=True,depsgraph=deps)
    try:
        mesh.calc_loop_triangles();layers=list(mesh.uv_layers)
        arrays={u.name:array.array('f') for u in layers}
        vertices,normals=array.array('f'),array.array('f')
        indices=[array.array('i') for _ in mesh.materials]
        nm=ev.matrix_world.to_3x3().inverted().transposed();origin=Vector((2015,2130,0));count=0
        for tri in mesh.loop_triangles:
            for li in [tri.loops[0],tri.loops[2],tri.loops[1]]:
                v=ev.matrix_world@mesh.vertices[mesh.loops[li].vertex_index].co-origin
                n=(nm@mesh.corner_normals[li].vector).normalized()
                vertices.extend((v.x,v.z,v.y));normals.extend((n.x,n.z,n.y))
                for u in layers:arrays[u.name].extend(u.data[li].uv)
                indices[tri.material_index].append(count);count+=1
        path=out/(key+'.bin')
        with path.open('xb') as f:
            f.write(struct.pack('<i',count));vertices.tofile(f);normals.tofile(f)
            f.write(struct.pack('<i',len(layers)))
            for u in layers:f.write(struct.pack('<i',channels[u.name]));arrays[u.name].tofile(f)
            arrays[layers[0].name].tofile(f);f.write(struct.pack('<i',len(indices)))
            for ids in indices:f.write(struct.pack('<i',len(ids)));ids.tofile(f)
        return {'name':ob.name,'key':key,'vertices':count,'triangles':count//3,'materials':[m.name for m in mesh.materials],
                'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'uvChannels':{u.name:channels[u.name] for u in layers}}
    finally:ev.to_mesh_clear()
