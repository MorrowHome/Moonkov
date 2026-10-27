"""Compare original and evaluated source winding using centered local geometry."""
import bpy
import bmesh
import json
from pathlib import Path
from mathutils import Vector

OUT=Path(__file__).resolve().parent/'NR12Pilot'
names=['NR01_V5_EAST_Full_Height_Wall','NR01_V5_SOUTH_TRANSFER_Worklight_Housing',
       'NR01_V5_SOUTH_TRANSFER_Worklight_Diffuser']
depsgraph=bpy.context.evaluated_depsgraph_get()
rows=[]
def sample(mesh):
    bm=bmesh.new();bm.from_mesh(mesh)
    center=sum((v.co for v in bm.verts),Vector())/len(bm.verts)
    for v in bm.verts:v.co-=center
    value={'volume_m3':bm.calc_volume(signed=True),'verts':len(bm.verts),'faces':len(bm.faces),
           'boundary_edges':sum(not e.is_manifold for e in bm.edges)}
    bm.free();return value
for name in names:
    ob=bpy.data.objects[name]
    evaluated=ob.evaluated_get(depsgraph)
    mesh=evaluated.to_mesh(preserve_all_data_layers=True,depsgraph=depsgraph)
    try:rows.append({'name':name,'source':sample(ob.data),'evaluated':sample(mesh),
                     'matrix_determinant':ob.matrix_world.to_3x3().determinant()})
    finally:evaluated.to_mesh_clear()
(OUT/'SourceNormals.json').write_text(json.dumps(rows,indent=2))
print('@@JSON@@'+json.dumps(rows),flush=True)
