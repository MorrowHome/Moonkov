"""Exact source-baseline correction. Preflight is read-only; apply is a separate explicit call."""
import bpy,os,json,hashlib,importlib.util

def _sha(path):return hashlib.sha256(open(path,'rb').read()).hexdigest()
def _module(path,name):
 spec=importlib.util.spec_from_file_location(name,path);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

def _corrected_module(industrial_folder, correction_folder=None):
 folder=correction_folder or os.path.dirname(os.path.abspath(__file__))
 F=json.load(open(os.path.join(folder,'R29_Industrial_UV_Guard_Correction.json')))
 mp=os.path.join(industrial_folder,'R29_Industrial_Final_Mapping.json');sp=os.path.join(industrial_folder,'apply_r29_industrial_final.py')
 assert _sha(mp)==F['original_industrial_mapping_sha256'],'Original industrial mapping differs'
 assert _sha(sp)==F['original_industrial_entry_sha256'],'Original industrial entry differs'
 assert _sha(os.path.join(industrial_folder,F['original_industrial_face_UV_data_file']))==F['original_industrial_face_UV_data_sha256'],'Original final face/UV data differs'
 ep=os.path.join(folder,'R29_Authoritative_Source_Face_Material_Names.json');ap=os.path.join(folder,'R29_Exact_R13_Alias_Equivalence.json');fp=os.path.join(folder,'r13_material_fingerprint.py')
 assert _sha(ep)==F['authoritative_material_names_sha256'],'Authoritative face-name list differs'
 assert _sha(ap)==F['R13_alias_equivalence_sha256'],'R13 authority definition differs'
 assert _sha(fp)==F['R13_fingerprint_helper_sha256'],'R13 fingerprint implementation differs'
 E={r['object']:r for r in json.load(open(ep))['objects']};A=json.load(open(ap));finger=_module(fp,'_r29_scoped_fingerprint').fingerprint
 module=_module(sp,'_r29_exact_original_industrial_entry');original_read=module._read;alias_report={}
 def corrected_read(path):
  M,D=original_read(path);records={r['object']:r for r in M['objects']}
  assert len(F['guards'])==16 and len(records)==19 and set(records)==set(E),'Unexpected correction scope'
  for g in F['guards']:
   r=records[g['object']];assert r['UVMap_float32_sha256']==g['old_guard'],('Old UV guard differs',g['object'])
   assert (r['vertex_float32_sha256'],r['topology_sha256'])==(g['vertex_float32_sha256'],g['topology_sha256']),('Geometry guard differs',g['object'])
   r['UVMap_float32_sha256']=g['correct_guard']
  alias_verified=False
  for name,r in records.items():
   o=bpy.data.objects.get(name);assert o and o.type=='MESH','Missing object '+name
   actual=[o.data.materials[p.material_index].name for p in o.data.polygons]
   effective=[o.material_slots[p.material_index].material.name for p in o.data.polygons]
   assert actual==effective,('Unexpected OBJECT material override',name)
   restored=list(actual)
   for pi,old,new in D[name]['face_material_changes']:
    assert actual[pi] in [old,new],('Affected source face differs',name,pi,actual[pi],old,new)
    restored[pi]=old
   want=E[name]['face_material_names'];assert len(restored)==len(want),('Polygon count differs',name)
   assert E[name]['expected_source_face_material_names_sha256']==r['source_face_material_names_sha256'],('Original face guard differs',name)
   count=0
   for pi,(got,expected) in enumerate(zip(restored,want)):
    if got==expected:continue
    assert name in A['allowed_objects'] and expected==A['canonical_material'] and got==A['only_allowed_alias'],('Unapproved material difference',name,pi,expected,got)
    if not alias_verified:
     alias=bpy.data.materials.get(A['only_allowed_alias']);assert alias,'Required exact alias material missing'
     actual_fp,definition=finger(alias)
     assert actual_fp==A['expected_fingerprint_sha256'],('R13 alias does not match cloud-authoritative node/scalar/image fingerprint',actual_fp,A['expected_fingerprint_sha256'])
     alias_verified=True
    count+=1
   if count:
    # Exact per-face equivalence was already proved; keep actual unchanged alias names in the original strict hash check.
    r['source_face_material_names_sha256']=hashlib.sha256(json.dumps(restored,separators=(',',':')).encode()).hexdigest();alias_report[name]=count
  return M,D
 module._read=corrected_read
 return module,F,A,alias_report

def preflight_r29_source_baseline(industrial_folder, correction_folder=None):
 module,F,A,aliases=_corrected_module(industrial_folder,correction_folder);M,D=module._read(industrial_folder);ready=[];already=[]
 for r in M['objects']:
  o=bpy.data.objects[r['object']];me=o.data;patch=D[o.name]
  assert module._geom(me)==(r['vertex_float32_sha256'],r['topology_sha256']),('Geometry mismatch',o.name)
  assert 'UVMap' in me.uv_layers and module._uvhash(me.uv_layers['UVMap'])==r['UVMap_float32_sha256'],('UVMap mismatch',o.name)
  assert all(abs(o.matrix_world[i][j]-r['source_matrix_world'][i][j])<1e-5 for i in range(4) for j in range(4)),('Transform mismatch',o.name)
  current=[me.materials[p.material_index].name for p in me.polygons];restored=list(current);states=[]
  for pi,old,new in patch['face_material_changes']:
   assert current[pi] in [old,new],('Source face differs',o.name,pi);states.append(current[pi]==new);restored[pi]=old
  assert hashlib.sha256(json.dumps(restored,separators=(',',':')).encode()).hexdigest()==r['source_face_material_names_sha256'],('Complete source material hash differs',o.name)
  assert all(states) or not any(states),('Partially applied object',o.name)
  if all(states):
   for uvname,rows in patch['new_uv_loop_rows'].items():
    layer=me.uv_layers.get(uvname);assert layer
    assert all(tuple(layer.data[li].uv)==module._float2(u,v) for li,u,v in rows),('Final UV differs',o.name,uvname)
   already.append(o.name)
  else:
   assert all(n not in me.uv_layers for n in patch['new_uv_loop_rows']),('Unexpected existing final UV layer',o.name);ready.append(o.name)
 for t in M['textures']:assert _sha(os.path.join(industrial_folder,t['path']))==t['sha256'],'Texture differs '+t['path']
 assert _sha(os.path.join(industrial_folder,M['material_libraries'][0]))==M['library_sha256'],'Material library differs'
 return {'read_only':True,'ready_objects':ready,'already_applied':already,'exact_alias_faces_by_object':dict(aliases),'R13_authoritative_fingerprint':A['expected_fingerprint_sha256'],'corrected_UV_guards':16,'unchanged_R24_UV_guards':3,'objects_materials_UV_visibility_written':0,'scene_saved':False}

def apply_r29_with_r23_serialized_uv_guard(industrial_folder, correction_folder=None):
 preflight=preflight_r29_source_baseline(industrial_folder,correction_folder)
 module,F,A,aliases=_corrected_module(industrial_folder,correction_folder)
 result=module.apply_r29_industrial_final(industrial_folder)
 result['source_UV_guard']='Exact delivered R23 JSON float32 baseline for 16; original compact R24 guard for 3'
 result['source_UV_values_written_by_correction']=0;result['exact_R13_alias_faces_by_object']=preflight['exact_alias_faces_by_object']
 result['shared_materials_renamed_or_changed_by_correction']=False
 return result
if __name__=='__main__':
 import sys
 args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
 apply='--apply' in args;args=[a for a in args if a!='--apply']
 if len(args)!=1:raise SystemExit('Usage: blender <source.blend> -P this_script.py -- /path/to/industrial_patch [--apply]. Default is read-only preflight.')
 result=apply_r29_with_r23_serialized_uv_guard(args[0]) if apply else preflight_r29_source_baseline(args[0]);print(json.dumps(result,indent=2))
