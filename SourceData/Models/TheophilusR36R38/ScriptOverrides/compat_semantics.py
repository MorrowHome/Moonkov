"""Narrow, read-only 4.3.2 projection for the audited official Blender 5.1.1 build.

Never edits a datablock. Original expected hashes remain unchanged. Projection
is accepted only together with the full reference descriptor and content guards.
See SEMANTIC_COMPATIBILITY_ZH.md for pinned upstream migration evidence.
"""
import bpy, hashlib, json

BUILD = 'b70da489d7f4'

def enabled():
    if bpy.app.version[:3] != (5, 1, 1):
        return False
    assert bpy.app.build_hash.decode() == BUILD, 'Unaudited 5.1.1 build; semantic projection blocked.'
    return True

def node_proof(n):
    """All these formerly unreported capability fields are read, never assumed."""
    assert n.id_data.animation_data is None, 'Animated or driven node tree is outside this compatibility proof.'
    assert isinstance(n.bl_static_type, str), 'Legacy node identity is not readable.'
    assert not hasattr(n.bl_rna.properties['bl_static_type'],'is_array'), 'Legacy string serialization schema changed.'
    assert n.bl_rna.properties['bl_static_type'].is_readonly, 'Unexpected node identity RNA.'
    assert n.parent is None, 'Parented node needs separate absolute-location evidence: '+n.name
    assert list(n.location_absolute) == list(n.location), 'Unexpected absolute node location: '+n.name
    result={'type':n.bl_idname,'bl_static_type':n.bl_static_type,'static_type_readonly':True,'static_type_has_is_array':False,
            'parent':None,'location_absolute':list(n.location_absolute)}
    if n.bl_idname == 'ShaderNodeNormalMap':
        assert n.base == 'DISPLACED', 'Normal Map base must preserve old-file DISPLACED behavior.'
        assert n.convention == 'OPENGL', 'Normal Map convention changes normal texture interpretation.'
        result.update(base=n.base,convention=n.convention)
    if n.bl_idname == 'ShaderNodeGroup':
        assert n.bl_width_max == 700.0, 'Unknown group-node width metadata.'
        result['bl_width_max']=n.bl_width_max
    return result

def project_values(value, result, complete=False):
    """Project only explicitly audited RNA deltas; retain every other field."""
    if not enabled():return result
    out=dict(result)
    if isinstance(value,bpy.types.Material):
        assert value.animation_data is None, 'Animated or driven material is outside this compatibility proof.'
        key='use_backface_culling_lightprobe_volume'
        assert key in out and type(out[key]) is bool, 'Missing lightprobe culling field.'
        out[key]=not out[key]  # upstream old-file XOR + reversed engine interpretation
    if isinstance(value,bpy.types.Node):
        node_proof(value)
        assert out.get('location_absolute') == list(value.location_absolute), 'Uncaptured node canvas location.'
        del out['location_absolute']
        if complete:
            assert 'bl_static_type' not in out, 'Unexpected editable legacy node identity.'
            out['bl_static_type']=value.bl_static_type
        if value.bl_idname=='ShaderNodeNormalMap':
            for key in ('base','convention'):
                if complete:assert key in out, 'Missing Normal Map field: '+key
                out.pop(key,None)  # values proved by node_proof, not ignored
        if value.bl_idname=='ShaderNodeGroup':
            assert out.get('bl_width_max')==700.0
            out['bl_width_max']=400.0
    return out

def clone_proof(mesh):
    layers=list(mesh.uv_layers)
    selected=mesh.uv_layer_clone
    explicit=selected.name if selected else None
    assert not layers or mesh.uv_layers.active is not None, 'UV layers have no active layer.'
    active=mesh.uv_layers.active
    effective=selected if selected else active
    flags=[bool(u.active_clone) for u in layers]
    if selected:
        assert flags==[u.name==selected.name for u in layers], 'Inconsistent explicit clone selection.'
    else:
        assert not any(flags), 'Missing clone pointer but an active clone flag is set.'
    return {'mesh':mesh.name,'explicit_clone':explicit,'effective_clone':effective.name if effective else None,
            'active':active.name if active else None,'active_index':mesh.uv_layers.active_index,
            'layers':[u.name for u in layers],'raw_clone_flags':flags}

def clone_flags(mesh):
    if not enabled():return [bool(u.active_clone) for u in mesh.uv_layers]
    p=clone_proof(mesh)
    # Canonicalize the actual effective clone layer. Full original UV signatures
    # still require the exact expected layer, active index, order, UV hashes and
    # render flags, including multi-UV meshes and the new R34 surface UV layer.
    return [u.name==p['effective_clone'] for u in mesh.uv_layers]

def sockets(node, direction):
    """Return unchanged sockets except the two observed upstream migrations."""
    seq=list(getattr(node,direction))
    if not enabled():return [(i,s,s.name) for i,s in enumerate(seq)]
    if node.bl_idname=='ShaderNodeBump' and direction=='inputs':
        expected=[('Strength','NodeSocketFloatFactor'),('Distance','NodeSocketFloat'),
                  ('Filter Width','NodeSocketFloat'),('Height','NodeSocketFloat'),('Normal','NodeSocketVector')]
        assert [(s.identifier,s.bl_idname) for s in seq]==expected, 'Unknown Bump input structure.'
        assert [s.name for s in seq]==[x[0] for x in expected], 'Unknown Bump input labels.'
        assert seq[2].default_value==1.0 and not seq[2].is_linked, 'Bump Filter Width must be legacy 1.0 and unlinked.'
        seq=seq[:2]+seq[3:]
    rows=[]
    for i,s in enumerate(seq):
        name=s.name
        # Only MixRGB input Fac occurs in the authorized local reports.
        if node.bl_idname=='ShaderNodeMixRGB' and direction=='inputs' and i==0:
            assert s.identifier=='Fac' and s.bl_idname=='NodeSocketFloatFactor' and s.name=='Factor', 'Unknown MixRGB factor socket.'
            name='Fac'
        rows.append((i,s,name))
    return rows

def socket_index(node,direction,socket):
    for i,s,name in sockets(node,direction):
        if s==socket:return i
    raise AssertionError('A link targets the new Bump Filter Width; no legacy equivalent.')

def socket_name(node,direction,socket):
    for i,s,name in sockets(node,direction):
        if s==socket:return name
    raise AssertionError('A link targets the new Bump Filter Width; no legacy equivalent.')

def legacy_nh(mat):
    """Unchanged original source_signature_compat.nh JSON schema and digest."""
    if not mat.use_nodes:return list(mat.diffuse_color)
    data=[]
    for n in mat.node_tree.nodes:
        ins=[]
        for _,s,name in sockets(n,'inputs'):
            if hasattr(s,'default_value'):
                v=s.default_value
                if isinstance(v,(int,float,bool,str)):ins.append([name,v])
                elif hasattr(v,'__len__'):ins.append([name,list(v)])
        data.append([n.name,n.bl_idname,ins])
    links=[(l.from_node.name,socket_name(l.from_node,'outputs',l.from_socket),
            l.to_node.name,socket_name(l.to_node,'inputs',l.to_socket)) for l in mat.node_tree.links]
    return hashlib.sha256(json.dumps([data,links],sort_keys=True).encode()).hexdigest()

def material_capabilities(mat):
    def tree(t):
        out={}
        for n in t.nodes:
            row=node_proof(n)
            if n.bl_idname=='ShaderNodeBump':
                sockets(n,'inputs')
                row['inputs']=[[s.name,s.identifier,s.bl_idname,
                    list(s.default_value) if hasattr(s.default_value,'__len__') else s.default_value,
                    s.is_linked] for s in n.inputs]
            if n.type=='GROUP' and n.node_tree:row['group']=tree(n.node_tree)
            out[n.name]=row
        return out
    return tree(mat.node_tree) if mat.use_nodes else None

def assert_clone_preserved(before, mesh):
    """Adding the requested display UV must not rewrite the existing clone state."""
    after=clone_proof(mesh)
    for key in ('explicit_clone','effective_clone','active','active_index'):
        assert after[key]==before[key], 'Display UV operation changed original clone state: '+key
    old=dict(zip(before['layers'],before['raw_clone_flags']))
    new=dict(zip(after['layers'],after['raw_clone_flags']))
    assert all(new.get(n)==flag for n,flag in old.items()), 'Display UV operation changed original clone flags.'
    assert not any(flag for n,flag in new.items() if n not in old), 'New display UV became explicit clone source.'
    return after

from pathlib import Path
COMPAT_SOURCE_SHA256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
def check_loaded(root):
    assert hashlib.sha256(Path(__file__).read_bytes()).hexdigest()==COMPAT_SOURCE_SHA256, 'Cached semantic helper changed on disk; restart Blender.'
    assert hashlib.sha256((Path(root)/'compat_semantics.py').read_bytes()).hexdigest()==COMPAT_SOURCE_SHA256, 'Semantic helper differs between package stages; restart after consistent overlay.'
