"""Classify original NR12 mesh winding. Reads the original blend; does not save it."""
import bpy
import bmesh
import json
from collections import Counter
from pathlib import Path
from mathutils import Vector

root=Path(__file__).resolve().parent
material_audit=json.loads((root/'NR12MaterialAudit.json').read_text())
rows=[]
for source in material_audit['objects']:
    ob=bpy.data.objects.get(source['source'])
    if not ob or ob.type!='MESH':
        rows.append({'name':source['source'],'status':'not_mesh_or_missing'})
        continue
    bm=bmesh.new();bm.from_mesh(ob.data)
    bm.verts.ensure_lookup_table();bm.faces.ensure_lookup_table()
    visited=set();components=[]
    for initial in bm.faces:
        if initial.index in visited:continue
        pending=[initial];faces=[];closed=True
        while pending:
            face=pending.pop()
            if face.index in visited:continue
            visited.add(face.index);faces.append(face)
            for edge in face.edges:
                if not edge.is_manifold:closed=False
                pending.extend(other for other in edge.link_faces if other.index not in visited)
        verts={v for f in faces for v in f.verts}
        center=sum((v.co for v in verts),Vector())/len(verts)
        # Translation avoids float32 cancellation for positions far from the origin.
        volume=0.0
        for face in faces:
            for k in range(1,len(face.verts)-1):
                a=face.verts[0].co-center;b=face.verts[k].co-center;c=face.verts[k+1].co-center
                volume+=a.dot(b.cross(c))/6.0
        components.append({'closed':closed,'faces':len(faces),'signed_volume':volume})
    bad=sum(c['closed'] and c['signed_volume']<-1e-9 for c in components)
    good=sum(c['closed'] and c['signed_volume']>1e-9 for c in components)
    opened=sum(not c['closed'] for c in components)
    rows.append({'name':ob.name,'raw_vertices':len(bm.verts),'raw_faces':len(bm.faces),
                 'closed_outward_parts':good,'closed_inward_parts':bad,'open_parts':opened,
                 'world_determinant':ob.matrix_world.to_3x3().determinant()})
    bm.free()
summary={'objects':len(rows),'with_inward_closed_parts':sum(r.get('closed_inward_parts',0)>0 for r in rows),
         'only_inward_closed_parts':sum(r.get('closed_inward_parts',0)>0 and not r.get('closed_outward_parts',0) and not r.get('open_parts',0) for r in rows),
         'with_open_parts':sum(r.get('open_parts',0)>0 for r in rows),
         'negative_world_determinant':sum(r.get('world_determinant',0)<0 for r in rows),
         'not_mesh_or_missing':sum(r.get('status')=='not_mesh_or_missing' for r in rows)}
result={'scope':'original mesh geometry before modifiers; not evaluated export or Unity',
        'source_unmodified':True,'summary':summary,'objects':rows}
(root/'NR12SourceWindingAudit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print('@@JSON@@'+json.dumps(summary),flush=True)
