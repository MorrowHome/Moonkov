"""Bounded static surface-intersection check, no scene or physics mutation."""
from pathlib import Path
import json,struct,math,re
p=Path(__file__).resolve().parent;near=json.loads((p/'NearbyStatic.json').read_text());manifest=json.loads((p/'NativePrepared/Manifest.json').read_text())
def sub(a,b):return [x-y for x,y in zip(a,b)]
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def cross(a,b):return [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]]
def intersects(a,b):
 ea=[sub(a[(i+1)%3],a[i]) for i in range(3)];eb=[sub(b[(i+1)%3],b[i]) for i in range(3)];na=cross(ea[0],ea[1]);nb=cross(eb[0],eb[1]);axes=[na,nb]+[cross(x,y) for x in ea for y in eb]
 if dot(cross(na,nb),cross(na,nb))<1e-14*max(dot(na,na)*dot(nb,nb),1e-30):axes += [cross(na,e) for e in ea+eb]
 for axis in axes:
  n=math.sqrt(dot(axis,axis))
  if n<1e-12:continue
  pa=[dot(x,axis)/n for x in a];pb=[dot(x,axis)/n for x in b]
  if max(pa)<min(pb)-1e-5 or max(pb)<min(pa)-1e-5:return False
 return True
new=[]
for row in manifest['objects']:
 if row['kind']!='R23':continue
 b=(p/'NativePrepared'/row['bin']).read_bytes();n=struct.unpack_from('<i',b)[0];vs=[struct.unpack_from('<fff',b,4+i*12) for i in range(n)];q=row['rotation'];offset=row['position']
 def world(v):
  v=[a*s for a,s in zip(v,row['scale'])];u=q[:3];uv=cross(u,v);uuv=cross(u,uv);return [v[i]+2*(q[3]*uv[i]+uuv[i])+offset[i] for i in range(3)]
 vertices=[world(v) for v in vs]
 for i in range(0,n,3):
  t=vertices[i:i+3];lo=[min(v[k] for v in t) for k in range(3)];hi=[max(v[k] for v in t) for k in range(3)];new.append((row['object'],i//3,t,lo,hi))
results=[];capture=json.loads((p.parent/'R22/SavedCapture.json').read_text());face_map=json.loads((p/'SourceFaceToTriangleMap.json').read_text());source_by_indices={tuple(capture['triangles'][i]['vertex_indices']):int(fid) for fid,indices in face_map.items() for i in indices}
for row in near['renderers']:
 hits=[];oldfaces=set()
 for old in row['triangles']:
  t=old['xyz'];lo=[min(v[k] for v in t) for k in range(3)];hi=[max(v[k] for v in t) for k in range(3)]
  for name,i,nt,nlo,nhi in new:
   if any(hi[k]<nlo[k]-1e-5 or nhi[k]<lo[k]-1e-5 for k in range(3)):continue
   if intersects(t,nt):
    hits.append({'oldSubmesh':old['sm'],'oldTriangle':old['triangle'],'newObject':name,'newTriangle':i})
    if row['mesh'].endswith('/R2_MODULE_RING_SECTOR_00.asset'):oldfaces.add(source_by_indices[tuple(old['indices'])])
 containment=0
 if not row['mesh'].endswith('/R2_MODULE_RING_SECTOR_00.asset'):
  raw=(Path(r'E:\UnityProjects\FPS_Template')/row['mesh']).read_text();vd=bytes.fromhex(re.search(r'(?m)^\s+_typelessdata: (\w+)',raw)[1]);count=int(re.search(r'm_VertexCount: (\d+)',raw)[1]);stride=len(vd)//count;assert count==36 and stride==48,'Expected bounded convex box neighbor; otherwise stop'
  matrix=row['worldMatrix'];vs=[]
  for i in range(count):
   v=struct.unpack_from('<fff',vd,i*stride);vs.append([sum(matrix[j*4+k]*v[j] for j in range(3))+matrix[12+k] for k in range(3)])
  ib=bytes.fromhex(re.search(r'(?m)^  m_IndexBuffer: (\w+)',raw)[1]);ids=struct.unpack('<'+'I'*(len(ib)//4),ib);center=[sum(v[k] for v in vs)/len(vs) for k in range(3)];planes=[]
  for i in range(0,len(ids),3):
   a,b,c=[vs[j] for j in ids[i:i+3]];normal=cross(sub(b,a),sub(c,a));length=math.sqrt(dot(normal,normal));normal=[x/length for x in normal];sign=1 if dot(normal,sub(center,a))>=0 else -1;planes.append((a,normal,sign))
  assert all(all(sign*dot(n,sub(point,a))>=-1e-4 for a,n,sign in planes) for point in vs),'Neighbor is not a convex box'
  for _,_,tri,_,_ in new:
   for point in tri:
    if all(sign*dot(n,sub(point,a))>1e-5 for a,n,sign in planes):containment+=1
 results.append({'name':row['name'],'id':row['id'],'mesh':row['mesh'],'triangleAABBCandidates':row['hits'],'surfaceIntersectionPairs':len(hits),'sourceOwnerFaceIntersections':sorted(oldfaces),'newCornerSamplesInsideForeignClosedBox':containment,'firstPairs':hits[:12]})
outside=[x for x in results if (x['surfaceIntersectionPairs'] or x['newCornerSamplesInsideForeignClosedBox']) and not x['mesh'].endswith('/R2_MODULE_RING_SECTOR_00.asset')]
targets=set(range(1070,1076))|set(range(1082,1088));unexpected_source=set(next(x for x in results if x['mesh'].endswith('/R2_MODULE_RING_SECTOR_00.asset'))['sourceOwnerFaceIntersections'])-targets
terrain_clear=all(x['maxWorldHeight']<x['newNodeMinY']-.02 for x in near['terrains'])
report={'scope':'Four neighboring renderers and 2512 new triangles only. Triangle SAT, complete convex-box containment for three foreign neighbors, and 169 height samples per terrain. This is static evidence, not physics/navigation/performance acceptance.','results':results,'extraNeighborSurfaceContactsRequireSourceDesignEvidence':outside,'unexpectedSourceOwnerFaces':sorted(unexpected_source),'terrainSamples':near['terrains'],'terrainSampleClearancePassed':terrain_clear,'instantiationAllowedByThisCheck':not outside and not unexpected_source and terrain_clear}
(p/'InterferenceReview.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
