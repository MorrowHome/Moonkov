"""Extend room reflection volumes to the actual floor and ceiling.
Radiance captures and their positions do not change. Work one chunk at a time.
"""
import pathlib,json,numpy as np,time
ROOT=pathlib.Path(__file__).resolve().parent;PROJECT=ROOT.parents[2];WORK=PROJECT/'LocalData/SourceFidelity20261028/World'
def run():
 path=WORK/'WorldReflectionPlan.json';plan=json.loads(path.read_text());geometry=json.loads((WORK/'WorldGeometry.json').read_text());rows=[p for p in plan['probes'] if p['importance']>=50];points=np.array([[r['position'][k] for k in 'xyz'] for r in rows],dtype=np.float64);limits=np.array([12.,64.,12.]);lower=np.full(points.shape,-np.inf);upper=np.full(points.shape,np.inf);start=time.monotonic()
 for chunk in geometry['chunks']:
  b=(PROJECT/chunk['file']).read_bytes();vertices=np.frombuffer(b,dtype='<f4',count=chunk['vertices']*34,offset=12).reshape(-1,34)[:,:3].astype(np.float64);vertices+=np.array([chunk['position'][k] for k in 'xyz']);minimum=vertices.min(axis=0);maximum=vertices.max(axis=0)
  if not np.any(np.all((points>=minimum-limits)&(points<=maximum+limits),axis=1)):continue
  indices=np.frombuffer(b,dtype='<u4',offset=12+chunk['vertices']*136).reshape(-1,3);triangle=vertices[indices];origin=triangle[:,0];a=triangle[:,1]-origin;d=triangle[:,2]-origin
  for axis in range(3):
   j,k=[v for v in range(3) if v!=axis];selected=np.flatnonzero((points[:,j]>=minimum[j]-.001)&(points[:,j]<=maximum[j]+.001)&(points[:,k]>=minimum[k]-.001)&(points[:,k]<=maximum[k]+.001)&(points[:,axis]>=minimum[axis]-limits[axis])&(points[:,axis]<=maximum[axis]+limits[axis]))
   if not len(selected):continue
   det=a[:,j]*d[:,k]-a[:,k]*d[:,j];valid=np.abs(det)>1e-12;o=origin[valid];aa=a[valid];dd=d[valid];det=det[valid]
   for index in selected:
    delta=points[index]-o;u=(delta[:,j]*dd[:,k]-delta[:,k]*dd[:,j])/det;v=(aa[:,j]*delta[:,k]-aa[:,k]*delta[:,j])/det;inside=(u>=-1e-7)&(v>=-1e-7)&(u+v<=1+1e-7);hits=(o[:,axis]+u*aa[:,axis]+v*dd[:,axis])[inside];difference=hits-points[index,axis];lo=hits[(difference<-.08)&(difference>=-limits[axis])];hi=hits[(difference>.08)&(difference<=limits[axis])]
    if len(lo):lower[index,axis]=max(lower[index,axis],lo.max())
    if len(hi):upper[index,axis]=min(upper[index,axis],hi.min())
 changes=[]
 for index,row in enumerate(rows):
  before=[list(row['boxMin'].values()),list(row['boxMax'].values())]
  for axis,key in enumerate('xyz'):
   row['boxMin'][key]=float(lower[index,axis]-.08 if np.isfinite(lower[index,axis]) else points[index,axis]-(12 if axis!=1 else 3))
   row['boxMax'][key]=float(upper[index,axis]+.08 if np.isfinite(upper[index,axis]) else points[index,axis]+(12 if axis!=1 else 3))
  after=[list(row['boxMin'].values()),list(row['boxMax'].values())]
  if np.max(np.abs(np.array(before)-np.array(after)))>.01:changes.append({'id':row['id'],'before':before,'after':after})
 plan['boundsVersion']=2;temp=path.with_suffix('.json.tmp');temp.write_text(json.dumps(plan,ensure_ascii=False,indent=2));temp.replace(path);report={'sourceSha256':plan['sourceSha256'],'rooms':len(rows),'changed':changes,'seconds':time.monotonic()-start};(WORK/'ReflectionBoundsValidation.json').write_text(json.dumps(report,indent=2));print('SOURCE_REFLECTION_BOUNDS',len(rows),'changed',len(changes),'seconds',round(report['seconds'],2),flush=True);return report
if __name__=='__main__':run()
