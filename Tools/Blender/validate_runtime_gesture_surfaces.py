"""Validate snapshots from export_runtime_gesture_surfaces.cs in a playing lab.
The export must finish (complete.txt) before starting this check.
"""
import bpy,json,os,glob
from mathutils.bvhtree import BVHTree
root=os.path.abspath(os.path.join(os.path.dirname(__file__),'../../Temp/GestureRuntimeSurfaces'))
if not os.path.exists(root+'/complete.txt'):raise RuntimeError('Runtime surface export has not finished')
data=json.load(open(root+'/topology.json'));labels=data['labels'];inds=data['triangles'];faces={}
for i in range(0,len(inds),3):
 tri=inds[i:i+3];ls=[labels[t] for t in tri]
 if len(set(ls))==1:faces.setdefault(ls[0],[]).append(tri)
report={}
for path in sorted(glob.glob(root+'/*.json')):
 if path.endswith('topology.json'):continue
 verts=json.load(open(path))['vertices'];trees={k:BVHTree.FromPolygons(verts,fs,all_triangles=True) for k,fs in faces.items()};keys=sorted(trees);hits={}
 for i,a in enumerate(keys):
  for b in keys[i+1:]:
   if a.startswith('upper') or b.startswith('upper'):continue
   if a[-1]==b[-1] and (a.startswith(('palm','forearm')) or b.startswith(('palm','forearm'))):continue
   n=len(trees[a].overlap(trees[b]))
   if n:hits[a+' / '+b]=n
 report[os.path.basename(path)]=hits;print(os.path.basename(path),hits,flush=True)
try: revision=json.load(open(root+'/complete.txt'))['revision']
except (ValueError,KeyError): revision='unrecorded'
result={'revision':revision,'passed':not any(report.values()),'poses':report,'scope':'Actual production skinned mesh, including animated head/cloth. Same-hand palm/forearm attachment seams and upper arms excluded.'}
open(root+'/report.txt','w').write(json.dumps(result,ensure_ascii=False,indent=2))
if not result['passed']:raise RuntimeError('Actual player surfaces intersect; inspect '+root+'/report.txt')
