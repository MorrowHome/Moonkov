"""Remove zero-area export triangles and repair undefined source corner normals.
Only the generated runtime binary is changed. The authoritative blend is read-only.
"""
import json,pathlib,struct,numpy as np
ROOT=pathlib.Path(__file__).resolve().parent;PROJECT=ROOT.parents[2];WORK=PROJECT/'LocalData/SourceFidelity20261028/World'
def run():
 path=WORK/'WorldGeometry.json';world=json.loads(path.read_text());removed=normals=0;changed=[]
 for c in world['chunks']:
  target=PROJECT/c['file'];raw=target.read_bytes();v=np.frombuffer(raw,dtype='<f4',count=c['vertices']*34,offset=12).reshape(-1,34).copy();tri=np.frombuffer(raw,dtype='<u4',offset=12+c['vertices']*136).reshape(-1,3).copy()
  edges=v[tri[:,1],:3].astype('float64')-v[tri[:,0],:3];other=v[tri[:,2],:3].astype('float64')-v[tri[:,0],:3];cross=np.cross(edges,other);area2=np.linalg.norm(cross,axis=1);valid=area2>1e-12;bad=np.linalg.norm(v[:,3:6],axis=1)<.5
  if valid.all() and not bad.any():continue
  if not valid.any():raise ValueError('Empty source chunk '+str(c['id']))
  removed+=int((~valid).sum());tri=tri[valid];cross=cross[valid];area2=area2[valid]
  # Index winding already compensates for the Blender->Unity axis reflection.
  for face,normal in zip(tri,cross/area2[:,None]):
   for index in face:
    if bad[index]:v[index,3:6]=normal;bad[index]=False;normals+=1
  used,inverse=np.unique(tri.ravel(),return_inverse=True);v=np.ascontiguousarray(v[used],dtype='<f4');tri=np.ascontiguousarray(inverse,dtype='<u4')
  if np.any(np.linalg.norm(v[:,3:6],axis=1)<.5):raise ValueError('Undefined referenced normal '+str(c['id']))
  target.write_bytes(b'SMW1'+struct.pack('<II',len(v),len(tri))+v.tobytes()+tri.tobytes());c['vertices']=len(v);c['indices']=len(tri);c['bytes']=target.stat().st_size;changed.append(c['id'])
 world['triangles']=sum(c['indices']//3 for c in world['chunks']);world['vertices']=sum(c['vertices'] for c in world['chunks']);path.write_text(json.dumps(world,indent=2))
 report={'changedChunks':changed,'zeroAreaTrianglesRemoved':removed,'undefinedNormalsRepaired':normals,'triangles':world['triangles'],'vertices':world['vertices']};(WORK/'BinaryRepair.json').write_text(json.dumps(report,indent=2));print('SOURCE_BINARY_REPAIR',json.dumps(report));return report
if __name__=='__main__':run()
