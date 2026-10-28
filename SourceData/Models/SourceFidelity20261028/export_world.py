"""Export source shader payload and lightmap UVs to bounded runtime mesh chunks.
The binary payload is imported once by the Editor, not parsed during gameplay.
"""
import bpy,numpy as np,pathlib,json,struct,time,math
ROOT=pathlib.Path(__file__).resolve().parent;PROJECT=ROOT.parents[2];WORK=PROJECT/'LocalData/SourceFidelity20261028/World';OUT=WORK/'Geometry';OUT.mkdir(parents=True,exist_ok=True)
world=json.loads((WORK/'PreparedWorld.json').read_text());compiled=json.loads((ROOT/'MaterialIR/CompiledMaterials.json').read_text());rows={r['id']:r for r in compiled['materials']};scene=next(s for s in bpy.data.scenes if s.name.startswith('SourceWorldStatic'));manifest=[];start=time.monotonic()

def attribute(mesh,name,count,size=3):
 a=mesh.attributes.get(name);output=np.zeros((count,size),dtype=np.float32)
 if a:
  key='value' if a.data_type in {'FLOAT','INT','BOOLEAN'} else 'color' if a.data_type in {'FLOAT_COLOR','BYTE_COLOR'} else 'vector'
  components=1 if key=='value' else 4 if key=='color' else 3
  values=np.empty(len(a.data)*components,dtype=np.float32);a.data.foreach_get(key,values);values=values.reshape(-1,components)
  if a.domain=='CORNER':output[:,:min(size,components)]=values[:,:min(size,components)]
  elif a.domain=='POINT':
   ids=np.empty(count,dtype=np.int32);mesh.loops.foreach_get('vertex_index',ids);output[:,:min(size,components)]=values[ids,:min(size,components)]
  elif a.domain=='FACE':
   for p in mesh.polygons:output[p.loop_start:p.loop_start+p.loop_total,:min(size,components)]=values[p.index,:min(size,components)]
 return output
for atlas in world['atlases']:
 obj=scene.objects[atlas['object']];mesh=obj.data;mesh.calc_loop_triangles();count=len(mesh.loops)
 positions=np.empty(len(mesh.vertices)*3,dtype=np.float32);mesh.vertices.foreach_get('co',positions);positions=positions.reshape(-1,3)
 loops=np.empty(count,dtype=np.int32);mesh.loops.foreach_get('vertex_index',loops)
 triangles=np.empty(len(mesh.loop_triangles)*3,dtype=np.int32);mesh.loop_triangles.foreach_get('loops',triangles);triangles=triangles.reshape(-1,3)
 material_indices=np.empty(len(mesh.loop_triangles),dtype=np.int32);mesh.loop_triangles.foreach_get('material_index',material_indices)
 bake_uv=np.empty(count*2,dtype=np.float32);mesh.uv_layers['SourceStaticBake'].data.foreach_get('uv',bake_uv);bake_uv=bake_uv.reshape(-1,2)
 local=attribute(mesh,'SourceLocal',count);generated=attribute(mesh,'SourceGenerated',count);random=attribute(mesh,'SourceRandom',count,1);normal=attribute(mesh,'SourceWorldNormal',count)
 data=np.zeros((count,34),dtype=np.float32);data[:,:3]=positions[loops][:,[0,2,1]];data[:,3:6]=normal[:,[0,2,1]];data[:,14:16]=bake_uv;data[:,18:21]=local;data[:,21:22]=random;data[:,22:25]=generated;data[:,25]=atlas['id']
 # Spatial chunks retain frustum culling without recreating tens of thousands
 # of source objects. A material cannot share a vertex with another payload layout.
 centers=data[triangles,:3].mean(axis=1);cell_size=256 if atlas['density']<=4 else 64
 cells=np.floor(centers/cell_size).astype(np.int32)
 keys=np.column_stack([material_indices,cells]);unique_keys,groups=np.unique(keys,axis=0,return_inverse=True)
 for key_index,key in enumerate(unique_keys):
  selection=np.flatnonzero(groups==key_index);loop_ids=triangles[selection].ravel();material_name=mesh.materials[int(key[0])].name;material_id=int(material_name.split('SourceMaterial_')[1].split('.')[0]);row=rows[material_id]
  payload=data[loop_ids].copy()
  for slot,name in enumerate(row['uv_names']):
   values=attribute(mesh,'SourcePrimaryUV' if name=='$ACTIVE' else 'SourceUV_'+name,count,3)[loop_ids,:2]
   if slot==0:payload[:,10:12]=values
   elif slot==1:payload[:,12:14]=values
   elif slot==2:payload[:,16:18]=values
  tangent=attribute(mesh,'SourceTangent_'+row['tangent_uv'],count)[loop_ids];bitangent=attribute(mesh,'SourceBitangent_'+row['tangent_uv'],count)[loop_ids]
  payload[:,6:9]=tangent;payload[:,9]=np.where((np.cross(normal[loop_ids],tangent)*bitangent).sum(axis=1)<0,-1,1)
  for name,layout in row['attribute_layout'].items():
   values=attribute(mesh,name,count,layout['size'])[loop_ids];offset=26+layout['offset'];payload[:,offset:offset+layout['size']]=values
  center=(payload[:,:3].min(axis=0)+payload[:,:3].max(axis=0))*.5;payload[:,:3]-=center
  # Preserve all source discontinuities: normals, UVs, random IDs and named attrs.
  bytes_view=np.ascontiguousarray(payload).view(np.dtype((np.void,payload.dtype.itemsize*34))).ravel();_,first,inverse=np.unique(bytes_view,return_index=True,return_inverse=True)
  vertices=np.ascontiguousarray(payload[first],dtype='<f4');indices=inverse.reshape(-1,3)[:,[0,2,1]].astype('<u4').ravel()
  part=len(manifest);path=OUT/f'Chunk_{part:05d}.bytes'
  with path.open('wb') as stream:stream.write(b'SMW1');stream.write(struct.pack('<II',len(vertices),len(indices)));stream.write(vertices.tobytes());stream.write(indices.tobytes())
  manifest.append({'id':part,'atlas':atlas['id'],'material':material_id,'position':{'x':float(center[0]),'y':float(center[1]),'z':float(center[2])},'vertices':len(vertices),'indices':len(indices),'file':str(path.relative_to(PROJECT)),'bytes':path.stat().st_size})
 print('SOURCE_MESH_ATLAS_EXPORTED',atlas['id'],'chunks',len(manifest),'elapsed',round(time.monotonic()-start,1),flush=True)
result={'sourceSha256':world['source_sha256'],'triangles':sum(r['indices']//3 for r in manifest),'vertices':sum(r['vertices'] for r in manifest),'chunks':manifest,'materialIds':sorted({r['material'] for r in manifest}),'atlasCount':len(world['atlases'])}
(WORK/'WorldGeometry.json').write_text(json.dumps(result,indent=2));print('FULL_WORLD_GEOMETRY_EXPORTED',len(manifest),result['triangles'],result['vertices'],round(time.monotonic()-start,1),flush=True)

# Validate the actual float32 runtime payload after the axis conversion.
import importlib.util
spec=importlib.util.spec_from_file_location("source_binary_repair",ROOT/"repair_world_binary.py");module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);module.run()
