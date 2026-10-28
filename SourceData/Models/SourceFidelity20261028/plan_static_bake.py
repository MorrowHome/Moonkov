"""Plan source-faithful static bake jobs by visible surface area, not object count."""
import bpy,hashlib,json,math,time,collections,pathlib,numpy as np
from mathutils import Vector
root=pathlib.Path(__file__).resolve().parent
local=root.parents[2]/'LocalData'/'SourceFidelity20261028';local.mkdir(parents=True,exist_ok=True)
scene=bpy.context.scene;dg=bpy.context.evaluated_depsgraph_get();allowed=set()
def walk(c,hidden=False):
 hidden=hidden or c.hide_render
 if not hidden:allowed.add(c.name)
 for child in c.children:walk(child,hidden)
walk(scene.collection)
instances=[];seen=set();rejected=collections.Counter()
for ins in dg.object_instances:
 o=ins.object.original
 if o.type not in {'MESH','CURVE','FONT'}:continue
 if o.hide_render or not ins.show_self:rejected['render_hidden_or_not_shown']+=1;continue
 if ins.is_instance:
  if ins.parent and ins.parent.original.hide_render:rejected['hidden_instance_parent']+=1;continue
 elif not any(c.name in allowed for c in o.users_collection):rejected['hidden_collection']+=1;continue
 matrix=ins.matrix_world.copy();materials=[s.material.name if s.material else None for s in o.material_slots]
 key=(o.name,tuple(round(x,6) for r in matrix for x in r),tuple(materials))
 if key in seen:rejected['identical_duplicate']+=1;continue
 seen.add(key)
 instances.append({'source':o.name,'matrix':[list(r) for r in matrix],'is_instance':ins.is_instance,'materials':materials})
cache={};start=time.monotonic()
for i,name in enumerate(sorted({x['source'] for x in instances})):
 o=bpy.data.objects[name];e=o.evaluated_get(dg);mesh=e.to_mesh(preserve_all_data_layers=True,depsgraph=dg)
 if mesh is None or not mesh.polygons:cache[name]={'empty':True};continue
 mesh.calc_loop_triangles()
 coords=np.empty(len(mesh.vertices)*3,dtype=np.float32);mesh.vertices.foreach_get('co',coords);coords=coords.reshape((-1,3))
 tri=np.empty(len(mesh.loop_triangles)*3,dtype=np.int32);mesh.loop_triangles.foreach_get('vertices',tri);tri=tri.reshape((-1,3))
 a=coords[tri[:,1]]-coords[tri[:,0]];b=coords[tri[:,2]]-coords[tri[:,0]];area_vectors=np.cross(a,b)*.5
 abs_area=float(np.linalg.norm(area_vectors,axis=1).sum())
 cache[name]={'area':abs_area,'triangle_count':len(tri),'vertex_count':len(coords),'uv_layers':len(mesh.uv_layers),'bounds':[coords.min(axis=0).tolist(),coords.max(axis=0).tolist()]}
 e.to_mesh_clear()
 if i%1000==0:print('GEOMETRY_AUDIT',i,'elapsed',round(time.monotonic()-start,1),flush=True)
rows=[];large=[]
for index,row in enumerate(instances):
 info=cache[row['source']]
 if info.get('empty'):continue
 from mathutils import Matrix
 matrix=Matrix(row['matrix']);scale=matrix.to_scale();area=info['area']*max(abs(scale.x*scale.y),abs(scale.y*scale.z),abs(scale.z*scale.x))
 text=row['source']+' '+' '.join(x for x in row['materials'] if x)
 ground=any(s in text.upper() for s in ['TERRAIN','REGOLITH','SHOULDER','BACKGROUND','R37_WEST_SEAM','GROUND_SURFACE'])
 density=16 if ground else 128
 capacity=(4096/density)**2*.65
 center=matrix@Vector([(info['bounds'][0][i]+info['bounds'][1][i])*.5 for i in range(3)])
 row.update({'id':index,'area_m2_conservative':area,'triangles':info['triangle_count'],'texels_per_metre':density,'ground':ground,'cell':[math.floor(center.x/32),math.floor(center.y/32),math.floor(center.z/16)]})
 if area>capacity:large.append({'source':row['source'],'area':area,'required_side':math.ceil(math.sqrt(area/.65)*density),'ground':ground})
 rows.append(row)
groups=collections.defaultdict(list)
for row in rows:groups[(row['ground'],*row['cell'])].append(row)
jobs=[]
for key,bucket in sorted(groups.items()):
 capacity=(4096/(16 if key[0] else 128))**2*.65
 batch=[];area=0
 for row in sorted(bucket,key=lambda r:(r['source'],r['id'])):
  if batch and area+row['area_m2_conservative']>capacity:
   jobs.append({'id':len(jobs),'objects':[r['id'] for r in batch],'area_m2_conservative':area,'density':batch[0]['texels_per_metre']});batch=[];area=0
  batch.append(row);area+=row['area_m2_conservative']
 if batch:jobs.append({'id':len(jobs),'objects':[r['id'] for r in batch],'area_m2_conservative':area,'density':batch[0]['texels_per_metre']})
plan={'source_sha256':hashlib.sha256(pathlib.Path(bpy.data.filepath).read_bytes()).hexdigest(),'source_saved':False,'visible_instances':len(rows),'unique_source_meshes':len(cache),'triangles':sum(r['triangles'] for r in rows),'area_m2_conservative':sum(r['area_m2_conservative'] for r in rows),'rejected':dict(rejected),'jobs':jobs,'objects':rows,'large_objects_requiring_split':large,'policy':'All static source lights; original MC00 control design abandoned by user. 128 texels/m manmade, 16 texels/m large ground, 4096 atlas, conservative 65% packing. Planner only; no bake or export yet. Scale surface estimate uses maximum pairwise scale product.'}
(local/'BakePlan.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2))
summary={k:plan[k] for k in ['source_sha256','source_saved','visible_instances','unique_source_meshes','triangles','area_m2_conservative','rejected','policy']};summary['job_count_before_large_mesh_split']=len(jobs);summary['large_objects_requiring_split']=large;summary['estimated_uncompressed_half_hdr_gi_GiB']=len(jobs)*4096*4096*8/1024**3
(root/'BakePlanSummary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2))
print('PLAN_COMPLETE',json.dumps({k:v for k,v in summary.items() if k!='large_objects_requiring_split'},ensure_ascii=False),flush=True)
print('LARGE_OBJECTS',len(large),flush=True)
