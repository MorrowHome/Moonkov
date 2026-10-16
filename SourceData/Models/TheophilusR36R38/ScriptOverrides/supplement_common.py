"""Exact R35-baseline R36+R38 display supplement; no scene/material patching."""
import bpy, array, hashlib, json
from pathlib import Path
from compat_semantics import project_values,clone_flags,sockets,socket_index,socket_name,legacy_nh
ROOT = Path(__file__).resolve().parent
MAPPING = 'R36_R38_DISPLAY_MAPPING.json'
MARKER = 'MOONKOV_R36_R38_DISPLAY_RECEIPT'

def sha(path):
    h=hashlib.sha256()
    with open(path,'rb') as f:
        for chunk in iter(lambda:f.read(1024*1024),b''): h.update(chunk)
    return h.hexdigest()

def digest(value):
    return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':')).encode()).hexdigest()

def values(value):
    result={}
    for p in value.bl_rna.properties:
        if p.identifier in {'rna_type','name','session_uid'} or p.type in {'POINTER','COLLECTION'} or p.is_readonly: continue
        try:
            v=getattr(value,p.identifier)
            result[p.identifier]=list(v) if p.is_array else v
        except: pass
    return project_values(value,result)

def array_hash(seq,attr,n,typ):
    a=array.array(typ,[0])*n; seq.foreach_get(attr,a)
    return hashlib.sha256(a.tobytes()).hexdigest()

def geometry(o):
    m=o.data
    return {key:array_hash(seq,attr,n,t) for key,seq,attr,n,t in [
      ('vertices',m.vertices,'co',len(m.vertices)*3,'f'),
      ('edges',m.edges,'vertices',len(m.edges)*2,'i'),
      ('loops',m.loops,'vertex_index',len(m.loops),'i'),
      ('polygon_starts',m.polygons,'loop_start',len(m.polygons),'i'),
      ('polygon_lengths',m.polygons,'loop_total',len(m.polygons),'i'),
      ('smooth',m.polygons,'use_smooth',len(m.polygons),'b')]}

def uv_signature(m):
    flags=clone_flags(m)
    return {'layers':[[u.name,array_hash(u.data,'uv',len(u.data)*2,'f'),u.active_render,flags[i]] for i,u in enumerate(m.uv_layers)], 'active':m.uv_layers.active_index}

def face_materials(o):
    return [o.data.materials[p.material_index].name if len(o.data.materials)>p.material_index and o.data.materials[p.material_index] else None for p in o.data.polygons]

def object_signature(o):
    result={'type':o.type,'matrix_world':[list(r) for r in o.matrix_world],
      'matrix_parent_inverse':[list(r) for r in o.matrix_parent_inverse],
      'parent':o.parent.name if o.parent else None,'hide_render':o.hide_render,'hide_viewport':o.hide_viewport,'hide_set':o.hide_get(),
      'modifiers':[[m.name,m.type,values(m)] for m in o.modifiers],
      'constraints':[[c.name,c.type,values(c)] for c in o.constraints],
      'rigid_body':o.rigid_body is not None,'rigid_body_constraint':o.rigid_body_constraint is not None}
    if o.type=='MESH':
        result.update({'geometry':geometry(o),'uv':uv_signature(o.data),'slots':[m.name if m else None for m in o.data.materials],
          'face_material_indices':array_hash(o.data.polygons,'material_index',len(o.data.polygons),'i'),
          'face_material_names':digest(face_materials(o))})
    return result

def image_identity(im):
    assert im.source=='FILE', 'Unsupported shared image type: '+im.name
    h=hashlib.sha256(im.packed_file.data).hexdigest() if im.packed_file else sha(bpy.path.abspath(im.filepath))
    return {'name':im.name,'sha256':h,'colorspace':im.colorspace_settings.name,'alpha_mode':im.alpha_mode}

def material_signature(mat):
    def tree(t):
        nodes=[]
        for n in t.nodes:
            ins=[]
            for i,s,sname in sockets(n,'inputs'):
                if hasattr(s,'default_value'):
                    v=s.default_value
                    if isinstance(v,(str,int,float,bool)):ins.append([i,sname,v])
                    elif hasattr(v,'__len__'):ins.append([i,sname,list(v)])
            rec={'name':n.name,'type':n.bl_idname,'properties':values(n),'inputs':ins}
            if hasattr(n,'image') and n.image: rec['image']=image_identity(n.image)
            if n.type=='GROUP' and n.node_tree: rec['group']=tree(n.node_tree)
            nodes.append(rec)
        return {'nodes':nodes,'links':[[l.from_node.name,socket_index(l.from_node,'outputs',l.from_socket),l.to_node.name,socket_index(l.to_node,'inputs',l.to_socket)] for l in t.links]}
    return digest({'properties':values(mat),'nodes':tree(mat.node_tree) if mat.use_nodes else None})


def material_descriptor(mat):
    # Preserve the prior handoff's full property/image-content check, and add
    # structured ramp/mapping/output checks that the older nh() omitted.
    def tree(t):
        result=[]
        for n in t.nodes:
            rec={'name':n.name,'type':n.bl_idname,'properties':values(n),'inputs':[],'outputs':[]}
            for key in ('inputs','outputs'):
                for i,s,sname in sockets(n,key):
                    if hasattr(s,'default_value'):
                        v=s.default_value
                        if isinstance(v,(str,int,float,bool)):rec[key].append([i,sname,v])
                        elif hasattr(v,'__len__'):rec[key].append([i,sname,list(v)])
            if hasattr(n,'image') and n.image:rec['image']=image_identity(n.image)
            if n.type=='GROUP' and n.node_tree:rec['group']=tree(n.node_tree)
            if hasattr(n,'color_ramp'):
                r=n.color_ramp;rec['color_ramp']={'properties':values(r),'elements':[[e.position,list(e.color)] for e in r.elements]}
            if hasattr(n,'mapping') and n.mapping:
                m=n.mapping;rec['mapping']={'properties':values(m),'curves':[[[list(p.location),p.handle_type] for p in c.points] for c in m.curves]}
            if hasattr(n,'texture_mapping'):rec['texture_mapping']=values(n.texture_mapping)
            result.append(rec)
        return {'nodes':result,'links':[[l.from_node.name,socket_index(l.from_node,'outputs',l.from_socket),l.to_node.name,socket_index(l.to_node,'inputs',l.to_socket)] for l in t.links]}
    return {'properties':values(mat),'nodes':tree(mat.node_tree) if mat.use_nodes else None}

def strict_material_signature(mat):return digest(material_descriptor(mat))

def attribute_signature(a):
    properties=[]
    if len(a.data):
        for p in a.data[0].bl_rna.properties:
            if p.identifier=='rna_type' or p.type in {'POINTER','COLLECTION'}:continue
            if p.type in {'FLOAT','INT','BOOLEAN'}:
                count=len(a.data)*(p.array_length if p.is_array else 1)
                properties.append([p.identifier,array_hash(a.data,p.identifier,count,'f' if p.type=='FLOAT' else 'i')])
            else:
                h=hashlib.sha256()
                for v in a.data:h.update(json.dumps(getattr(v,p.identifier),sort_keys=True).encode());h.update(b'\0')
                properties.append([p.identifier,h.hexdigest()])
    return [a.name,a.domain,a.data_type,properties]

def json_value(v):
    if v is None or isinstance(v,(str,int,float,bool)):return v
    if isinstance(v,bpy.types.ID):return {'id_type':v.bl_rna.identifier,'name':v.name_full,'library':v.library.filepath if v.library else None}
    if hasattr(v,'to_dict'):return {k:json_value(x) for k,x in v.to_dict().items()}
    if isinstance(v,dict):return {k:json_value(x) for k,x in v.items()}
    if hasattr(v,'to_list'):return [json_value(x) for x in v.to_list()]
    if isinstance(v,(list,tuple)):return [json_value(x) for x in v]
    raise TypeError('Unsupported custom property type: '+type(v).__name__)

_SOURCE_MODULE=None
def source_module():
    global _SOURCE_MODULE
    p=ROOT/'source_signature_compat.py';raw=p.read_bytes()
    assert hashlib.sha256(raw).hexdigest()=='60a319a2878c208ffb41fc5308d1e4c9a07e956255c489ae97705cf8950643fb','Original source signature code changed.'
    if _SOURCE_MODULE is None:
        import types
        mod=types.ModuleType('_moonkov_locked_source_signature_compat')
        mod.__file__=str(p)
        exec(compile(raw,str(p),'exec'),mod.__dict__)
        _SOURCE_MODULE=mod
    return _SOURCE_MODULE

def exact_object(o):
    legacy=source_module();semantic,object_sig=legacy.semantic,legacy.object_sig
    s=object_signature(o)
    s['source_legacy_signature']=semantic(object_sig(o))
    if o.type=='MESH':
        s['mesh_attributes']=[attribute_signature(a) for a in o.data.attributes]
        s['mesh_custom_properties']={k:json_value(o.data[k]) for k in o.data.keys()}
        s['mesh_animation_data']=o.data.animation_data is not None
        s['mesh_shape_keys']=o.data.shape_keys.name if o.data.shape_keys else None
        s['has_custom_normals']=o.data.has_custom_normals
        s['material_slots']=[[slot.link,slot.material.name if slot.material else None] for slot in o.material_slots]
        s['vertex_groups']=[[g.name,g.lock_weight] for g in o.vertex_groups]
    s.update({'data_name':o.data.name if o.data else None,'collections':sorted(c.name for c in o.users_collection),'custom_properties':{k:json_value(o[k]) for k in o.keys()},'animation_data':o.animation_data is not None})
    return s

def mapping():return json.loads((ROOT/MAPPING).read_text(encoding='utf-8'))

def verify_payload(data):
    for name,rec in data['payload'].items():
        p=ROOT/name
        assert p.is_file() and p.stat().st_size==rec['bytes'] and sha(p)==rec['sha256'],'Payload missing or changed: '+name

def runtime_guard():
    from compat_readonly import runtime_guard as compat_runtime_guard
    compat_runtime_guard()
    from compat_readonly import module
    module(ROOT)
    assert bpy.app.version[:3] in {(4,3,2),(5,1,1)},'Supplement permits exact 4.3.2 or gated 5.1.1 only.'
    assert bpy.context.mode=='OBJECT','Leave Edit/Sculpt mode first.'

def verify_baseline(data):
    legacy=source_module();semantic,object_sig=legacy.semantic,legacy.object_sig
    for n,rec in data['r35_support_and_bridge_guards'].items():
        o=bpy.data.objects.get(n);assert o is not None,'R35 support/bridge object missing: '+n
        assert semantic(object_sig(o))==semantic(rec['legacy']),'R35 support/bridge source signature differs: '+n
        assert exact_object(o)==rec['exact'],'R35 support/bridge detailed signature differs: '+n
    for n,expected in data['predecessor_r29_to_r35_guards'].items():
        o=bpy.data.objects.get(n);assert o is not None,'Apply the R29-to-R35 handoff first; predecessor object missing: '+n
        assert object_signature(o)==expected,'R29-to-R35 predecessor display differs: '+n
    for n,rec in data['shared_material_guards'].items():
        m=bpy.data.materials.get(n);assert m is not None,'R35 shared material missing: '+n
        assert legacy_nh(m)==rec['legacy_nh'],'Existing shared material source signature differs: '+n
        assert material_signature(m)==rec['prior_strict'],'Existing shared material properties/texture content differ: '+n
        assert strict_material_signature(m)==rec['strict'],'Existing shared material graph/ramp/output differs: '+n

def ids():
    return {k:set(getattr(bpy.data,k).keys()) for k in ['objects','meshes','collections','materials','images','node_groups','scenes','worlds','cameras','lights','actions','texts']}

def receipt(data):
    raw=bpy.context.scene.get(MARKER)
    assert raw is not None,'No supplement receipt. Do not remove individually installed or detached objects with this combined script.'
    rec=json.loads(raw)
    assert rec['mapping_sha256']==sha(ROOT/MAPPING),'Mapping differs from the applied supplement.'
    assert rec['created']==data['expected_created'],'Applied ownership receipt differs.'
    return rec

def same_applied_object_signature(actual, expected, mesh):
    if actual == expected:return True
    from compat_semantics import enabled
    if not enabled() or mesh is None:return False
    a=actual.get('mesh_attributes');e=expected.get('mesh_attributes')
    if not isinstance(a,list) or not isinstance(e,list):return False
    for rows in (a,e):
        assert len(rows)==len({row[0] for row in rows}), 'Duplicate mesh attribute names.'
    am=[row for row in a if row[0]=='material_index']
    em=[row for row in e if row[0]=='material_index']
    if len(am)!=1 or am!=em or am[0][:3]!=['material_index','FACE','INT']:return False
    if [row for row in a if row[0]!='material_index'] != [row for row in e if row[0]!='material_index']:return False
    i=mesh.attributes.active_index;active=mesh.attributes.active
    if i == -1:
        if active is not None:return False
    elif not (0<=i<len(a) and i<len(e) and a[i][0]==e[i][0] and active is not None and active.name==a[i][0]):return False
    projected=dict(actual);projected['mesh_attributes']=e
    return projected == expected

def verify_applied(data):
    receipt(data)
    for n,names in data['collections'].items():
        c=bpy.data.collections.get(n);assert c is not None,'Display collection missing: '+n
        assert set(o.name for o in c.objects)==set(names) and not c.children,'Display collection membership changed: '+n
        assert not c.hide_viewport and not c.hide_render,'Display collection visibility changed: '+n
        assert c.name in bpy.context.scene.collection.children,'Display collection was moved: '+n
    for rec in data['added_objects']:
        o=bpy.data.objects.get(rec['name']);assert o is not None,'Display object missing: '+rec['name']
        assert same_applied_object_signature(exact_object(o),rec['applied_signature'],o.data if o.type=='MESH' else None),'Display object has subsequent edits: '+rec['name']
        assert not o.library and not o.override_library,'Linked/overridden display object: '+rec['name']
        if o.type=='MESH':assert o.data.users==1 and not o.data.library,'Display mesh has new linked users: '+rec['name']
        assert set(c.name for c in o.children).issubset(data['expected_created']['objects']),'Display object has external child users: '+rec['name']
    return True

# Detect stale cached code after a script overlay. Restart Blender before preflight.
COMPAT_SOURCE_SHA256=sha(__file__)
