"""Fail-closed 4.3/5.1.1 diagnostic. No save/export/render or input-file changes.
Payload probes OPEN libraries in separate disposable Blender processes; no append
or link operation is performed in the user's scene. Only JSON reports are written.
"""
import array, hashlib, importlib, json, subprocess, sys
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
import compat_semantics as S

REFERENCE='COMPAT_REFERENCE_432.json'
REPORT='COMPAT_PREFLIGHT_REPORT.json'
PASS='COMPAT_PREFLIGHT_PASS.json'

def sha(p):
    h=hashlib.sha256()
    with open(p,'rb') as f:
        for b in iter(lambda:f.read(1048576),b''):h.update(b)
    return h.hexdigest()

def digest(v):return hashlib.sha256(json.dumps(v,sort_keys=True,separators=(',',':')).encode()).hexdigest()

def runtime_guard():
    if not __debug__ or sys.flags.optimize:
        raise RuntimeError('Assertions are disabled; refusing all guarded operations.')
    v=bpy.app.version[:3]
    assert v[:2]==(4,3) or v==(5,1,1),'Unsupported Blender version; only legacy 4.3 and exact 5.1.1 diagnostic gate.'
    S.enabled()
    S.check_loaded(Path(__file__).resolve().parent)
    assert sha(__file__)==COMPAT_SOURCE_SHA256,'Cached readonly helper changed on disk; restart Blender.'
    assert bpy.context.mode=='OBJECT','Leave Edit/Sculpt mode first.'
    assert sys.byteorder=='little' and array.array('i').itemsize==4 and array.array('f').itemsize==4,'Incompatible signature array encoding.'
    for owner,name in [(bpy.data.libraries,'load'),(bpy.data,'user_map'),(bpy.context.view_layer,'update'),(bpy.path,'abspath'),(bpy.path,'relpath')]:
        assert callable(getattr(owner,name,None)),'Required API unavailable: '+name
    for cls,props,funcs in [
        ('Mesh',['vertices','edges','loops','polygons','uv_layers','materials','attributes','has_custom_normals'],[]),
        ('MeshUVLoopLayer',['data','active_render','active_clone'],[]),
        ('MeshUVLoop',['uv'],[]),('MeshPolygon',['material_index','loop_start','loop_total','use_smooth'],[]),
        ('Object',['matrix_world','matrix_parent_inverse','modifiers','constraints','material_slots'],['hide_get','copy']),
        ('Material',['use_nodes','node_tree'],['copy']),('Image',['packed_file','filepath','colorspace_settings'],[])]:
        c=getattr(bpy.types,cls,None);assert c is not None,'Required RNA type unavailable: '+cls
        for p in props:assert p in c.bl_rna.properties,'Required RNA property unavailable: '+cls+'.'+p
        for f in funcs:assert f in c.bl_rna.functions,'Required RNA function unavailable: '+cls+'.'+f
    assert not bpy.types.MeshUVLoop.bl_rna.properties['uv'].is_readonly
    assert not bpy.types.MeshPolygon.bl_rna.properties['material_index'].is_readonly
    for o in bpy.data.objects:
        if o.type=='MESH':
            for seq in [o.data.vertices,o.data.edges,o.data.loops,o.data.polygons]:
                assert callable(seq.foreach_get) and callable(seq.foreach_set),'Required bulk RNA access unavailable.'
            assert callable(o.data.uv_layers.new) and callable(o.data.uv_layers.remove)
            assert callable(o.data.materials.append) and callable(o.data.materials.clear)
            for u in o.data.uv_layers:assert callable(u.data.foreach_get) and callable(u.data.foreach_set)
            break

def module(root):
    root=Path(root).resolve();sys.path.insert(0,str(root))
    if (root/'combined_display_common.py').exists():name,stage='combined_display_common','R29_R35'
    elif (root/'supplement_common.py').exists():name,stage='supplement_common','R36_R38'
    else:name,stage='r39_common','R39'
    mod=importlib.import_module(name)
    assert Path(mod.__file__).resolve()==root/(name+'.py'),'Cached module from another package; restart Blender after overlay.'
    assert getattr(mod,'COMPAT_SOURCE_SHA256',None)==sha(root/(name+'.py')),'Cached or unpatched common module; restart Blender after overlay.'
    S.check_loaded(root)
    assert sha(root/'compat_readonly.py')==COMPAT_SOURCE_SHA256,'Readonly helper differs between package stages.'
    for key in ('project_values','clone_flags','sockets','socket_index','socket_name','legacy_nh'):
        assert getattr(mod,key,None) is getattr(S,key),'Cached common/helper function binding changed; restart Blender.'
    return mod,stage

def props(v):
    # Full descriptor with checked, narrow migration projections; all other fields remain exact.
    out={}
    for p in v.bl_rna.properties:
        if p.identifier in {'rna_type','name','session_uid','is_override_data','execution_time'} or p.type in {'POINTER','COLLECTION'} or p.is_readonly:continue
        val=getattr(v,p.identifier)
        out[p.identifier]=list(val) if getattr(p,'is_array',False) else sorted(val) if isinstance(val,set) else val
    return S.project_values(v,out,complete=True)

def material_details(mat):
    def tree(t):
        nodes={}
        for n in t.nodes:
            row={'type':n.bl_idname,'properties':props(n)}
            for k in ('inputs','outputs'):
                row[k]=[]
                for _,s,sname in S.sockets(n,k):
                    v=getattr(s,'default_value',None)
                    if v is not None and not isinstance(v,(str,int,float,bool)):v=list(v) if hasattr(v,'__len__') else str(type(v))
                    row[k].append([sname,s.identifier,s.bl_idname,v])
            if hasattr(n,'image') and n.image:
                im=n.image;row['image']={'name':im.name,'sha256':hashlib.sha256(im.packed_file.data).hexdigest() if im.packed_file else sha(bpy.path.abspath(im.filepath)),'colorspace':im.colorspace_settings.name,'alpha_mode':im.alpha_mode}
            if n.type=='GROUP' and n.node_tree:row['group']=tree(n.node_tree)
            if hasattr(n,'color_ramp'):row['ramp']={'properties':props(n.color_ramp),'elements':[[e.position,list(e.color)] for e in n.color_ramp.elements]}
            if hasattr(n,'mapping') and n.mapping:row['mapping']={'properties':props(n.mapping),'curves':[[[list(p.location),p.handle_type] for p in c.points] for c in n.mapping.curves]}
            if hasattr(n,'texture_mapping'):row['texture_mapping']=props(n.texture_mapping)
            nodes[n.name]=row
        return {'nodes':nodes,'links':[[l.from_node.name,S.socket_index(l.from_node,'outputs',l.from_socket),l.to_node.name,S.socket_index(l.to_node,'inputs',l.to_socket)] for l in t.links]}
    result={'properties':props(mat),'nodes':tree(mat.node_tree) if mat.use_nodes else None}
    json.dumps(result,allow_nan=False)
    return result

def payload_state(root):
    C,stage=module(root)
    object_fn=C.exact_object if stage=='R36_R38' else C.object_signature if stage=='R29_R35' else C.object_sig
    mat_fn=C.mat_sig if stage=='R39' else C.material_signature
    return {'objects':{o.name:object_fn(o) for o in bpy.data.objects},'materials':{m.name:{'original_signature':mat_fn(m),'descriptor':material_details(m)} for m in bpy.data.materials},'images':sorted(bpy.data.images.keys())}

def diffs(a,b,path=''):
    if isinstance(a,dict) and isinstance(b,dict):
        for k in sorted(set(a)|set(b)):
            p=path+'/'+str(k)
            if k not in a:yield {'path':p,'expected':'<absent>','actual':b[k]}
            elif k not in b:yield {'path':p,'expected':a[k],'actual':'<absent>'}
            else:yield from diffs(a[k],b[k],p)
    elif isinstance(a,list) and isinstance(b,list):
        if len(a)!=len(b):yield {'path':path+'/length','expected':len(a),'actual':len(b)}
        for i,(x,y) in enumerate(zip(a,b)):yield from diffs(x,y,path+'/'+str(i))
        for i in range(min(len(a),len(b)),max(len(a),len(b))):yield {'path':path+'/'+str(i),'expected':a[i] if i<len(a) else '<absent>','actual':b[i] if i<len(b) else '<absent>'}
    elif a!=b:yield {'path':path,'expected':a,'actual':b}

def baseline_guard(root):
    C,stage=module(root)
    if stage=='R29_R35':C.preflight()
    elif stage=='R36_R38':
        d=C.mapping();C.runtime_guard();C.verify_payload(d);C.verify_baseline(d)
        assert C.MARKER not in bpy.context.scene,'Preflight expects a pre-supplement working copy.'
        for kind,names in d['expected_created'].items():
            for n in names:assert n not in getattr(bpy.data,kind),'Existing/conflicting '+kind+': '+n
    else:
        p=json.loads((Path(root)/'R39_Deck0176_Material_UV_Patch.json').read_text());o=bpy.data.objects.get(p['target_object'])
        assert o and o.type=='MESH','R39 original deck missing.'
        assert o.data.name==p['target_mesh'] and o.data.users==1,'R39 mesh identity/ownership changed.'
        assert C.object_sig(o)==p['before_object_signature'],'R39 target signature differs.'
        assert C.geom_sig(o.data)==p['target_geometry'],'R39 geometry differs.'
        assert p['new_material'] not in bpy.data.materials and not any(n in bpy.data.images for n in p['new_images']),'R39 resource conflict.'

def baseline_object_diffs(root):
    C,stage=module(root)
    if stage=='R29_R35':
        checks=[(n,C.object_signature,v) for n,v in C.mapping()['baseline_guards'].items()]
    elif stage=='R36_R38':
        d=C.mapping();checks=[(n,C.exact_object,v['exact']) for n,v in d['r35_support_and_bridge_guards'].items()]
        checks += [(n,C.object_signature,v) for n,v in d['predecessor_r29_to_r35_guards'].items()]
    else:
        p=json.loads((Path(root)/'R39_Deck0176_Material_UV_Patch.json').read_text())
        checks=[(p['target_object'],C.object_sig,p['before_object_signature'])]
    for n,fn,expected in checks:
        if n not in bpy.data.objects:yield {'path':'baseline_objects/'+n,'expected':'present','actual':'missing'}
        else:yield from diffs(expected,fn(bpy.data.objects[n]),'baseline_objects/'+n)

def package_fingerprint(root):
    root=Path(root);ref=json.loads((root/REFERENCE).read_text());result={}
    for n,h in ref['locked_original_files'].items():
        assert sha(root/n)==h,'Original payload/mapping changed: '+n
        result[n]=h
    for p in sorted(root.glob('*.py')):result[p.name]=sha(p)
    result[REFERENCE]=sha(root/REFERENCE)
    return digest(result)

def guarded_state(root):
    C,stage=module(root);ref=json.loads((Path(root)/REFERENCE).read_text())
    fn=C.exact_object if stage=='R36_R38' else C.object_signature if stage=='R29_R35' else C.object_sig
    return digest({'objects':{n:fn(bpy.data.objects[n]) for n in ref['baseline_objects']},'materials':{n:material_details(bpy.data.materials[n]) for n in ref['baseline_materials']},'raw_capabilities':capabilities(root)})

def require_511_precheck(root):
    runtime_guard()
    if bpy.app.version[:3]!=(5,1,1):return
    p=Path(root)/PASS;assert p.is_file(),'5.1.1 application blocked: run COMPAT_CHECK_ONLY.py and require PASS first.'
    rec=json.loads(p.read_text())
    assert rec.get('semantic_policy')=='audited-432-to-official-511-v2','Old preflight receipt; run v2 preflight.'
    assert rec.get('status')=='PASS' and rec['blender']==list(bpy.app.version[:3]),'No passing exact-version preflight.'
    assert rec['build_hash']==bpy.app.build_hash.decode() and rec['binary_sha256']==sha(bpy.app.binary_path),'Blender binary changed; rerun preflight.'
    assert rec['package_fingerprint']==package_fingerprint(root),'Package changed; rerun preflight.'
    assert rec['guarded_state']==guarded_state(root),'Guarded scene changed; rerun preflight.'

def capabilities(root,payload=False):
    if not S.enabled():return {'legacy_432':True}
    ref=json.loads((Path(root)/REFERENCE).read_text())
    objects=list(bpy.data.objects) if payload else [bpy.data.objects[n] for n in ref['baseline_objects'] if n in bpy.data.objects]
    materials=list(bpy.data.materials) if payload else [bpy.data.materials[n] for n in ref['baseline_materials'] if n in bpy.data.materials]
    result={'uv_clone':{},'nodes':{}}
    for o in objects:
        if o.type=='MESH':
            result['uv_clone'][o.name]=S.clone_proof(o.data)
    for m in materials:
        result['nodes'][m.name]=S.material_capabilities(m)
    return result

def prerequisites(root):
    C,stage=module(root)
    if stage!='R36_R38':return []
    d=C.mapping();out=[]
    missing=[n for n in d['predecessor_r29_to_r35_guards'] if n not in bpy.data.objects]
    if missing:out.append({'reason':'R29_TO_R35_NOT_APPLIED','missing':missing})
    # A missing previous-stage marker is an explanation, never an exemption.
    if 'MOONKOV_R29_TO_R35_DISPLAY_RECEIPT' not in bpy.context.scene:
        out.append({'reason':'R29_TO_R35_RECEIPT_ABSENT','action':'Run and verify the first package before this supplement; all real guards remain blocking.'})
    return out

def run(root):
    root=Path(root);ref=json.loads((root/REFERENCE).read_text());errors=[];details=[];proof={};before={k:sorted(getattr(bpy.data,k).keys()) for k in ['objects','meshes','materials','images','collections']}
    def check(label,fn):
        try:fn()
        except Exception as e:errors.append({'check':label,'error':type(e).__name__+': '+str(e)})
    check('runtime_api',runtime_guard);check('package_hashes',lambda:package_fingerprint(root));check('original_baseline_guards',lambda:baseline_guard(root))
    check('semantic_capabilities',lambda:proof.update(capabilities(root)))
    check('baseline_object_details',lambda:details.extend(baseline_object_diffs(root)))
    for n,expected in ref['baseline_materials'].items():
        try:details.extend(diffs(expected,material_details(bpy.data.materials[n]),'baseline_materials/'+n))
        except Exception as e:errors.append({'check':'material '+n,'error':repr(e)})
    # Each library is opened read-only in a fresh process; current scene is never changed.
    for name,expected in ref['payload_states'].items():
        cmd=[bpy.app.binary_path,'--background','--disable-autoexec',str(root/name),'--python-exit-code','49','--python',str(root/'compat_readonly.py'),'--','--payload-root',str(root)]
        try:
            r=subprocess.run(cmd,capture_output=True,text=True,timeout=180)
            rows=[x[len('MOONKOV_PAYLOAD_JSON='):] for x in r.stdout.splitlines() if x.startswith('MOONKOV_PAYLOAD_JSON=')]
            if r.returncode or len(rows)!=1:raise RuntimeError('Payload probe failed '+name+'; '+(r.stdout+r.stderr)[-6000:])
            result=json.loads(rows[0]);proof['payload/'+name]=result['capabilities']
            details.extend(diffs(expected,result['state'],'payload/'+name))
        except Exception as e:errors.append({'check':'payload '+name,'error':repr(e)})
    assert before=={k:sorted(getattr(bpy.data,k).keys()) for k in before},'Read-only probe unexpectedly changed datablock membership.'
    report={'semantic_policy':'audited-432-to-official-511-v2','capabilities':proof,'prerequisite_blockers':prerequisites(root),'status':'BLOCKED' if errors or details else 'PASS','blender':list(bpy.app.version[:3]),'build_hash':bpy.app.build_hash.decode(),'errors':errors,'differences':details,'scene_datablock_membership_unchanged':True,'asset_files_written':0,'save_export_render_calls':0,'scope':'This package stage only; 4.3 regression is not 5.1.1 acceptance. Original expected hashes unchanged; only audited exact-version semantic projection, plus full reference descriptor comparison.'}
    if report['status']=='PASS':
        report.update({'binary_sha256':sha(bpy.app.binary_path),'package_fingerprint':package_fingerprint(root),'guarded_state':guarded_state(root)})
        (root/PASS).write_text(json.dumps(report,indent=2),encoding='utf-8')
    elif (root/PASS).exists():(root/PASS).unlink()
    (root/REPORT).write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
    print('MOONKOV_COMPAT_'+report['status']+': '+str(root/REPORT)+'; differences='+str(len(details))+'; errors='+str(len(errors)),flush=True)
    assert report['status']=='PASS','Compatibility blocked. Read the report; do not alter guards or assets to force a pass.'
    return report

COMPAT_SOURCE_SHA256=sha(__file__)

if __name__=='__main__':
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    if args[:1]==['--payload-root']:print('MOONKOV_PAYLOAD_JSON='+json.dumps({'state':payload_state(Path(args[1])),'capabilities':capabilities(Path(args[1]),payload=True)},separators=(',',':')),flush=True)
    else:run(Path(__file__).resolve().parent)
