"""Prepare unapplied simple floor proxies from exact current native geometry.
Reads repository files; writes this workspace only. Never opens/saves Unity scenes.
"""
from pathlib import Path
import collections, hashlib, itertools, json, math, re, struct

REPO = Path('E:/UnityProjects/FPS_Template')
OUT = Path(__file__).resolve().parent
BASE = 'Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements'
SCENE = 'Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity'

def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def records(text):
    matches = list(re.finditer(r'(?m)^--- !u!(\d+) &(-?\d+)[^\n]*', text))
    return {m[2]: (int(m[1]), text[m.start():matches[i+1].start() if i+1<len(matches) else len(text)]) for i,m in enumerate(matches)}
def reference(body, key):
    m = re.search(re.escape(key)+r': \{fileID: (-?\d+)', body)
    return m[1] if m else '0'
def property_text(body, key):
    return re.search(r'(?m)^  '+re.escape(key)+r': (.*)', body)[1]
def values(text): return [float(v) for v in re.findall(r': ([-+\d.eE]+)', text)]
def triple(v): return '{x: %.9g, y: %.9g, z: %.9g}' % tuple(v)
def native_positions(path):
    s = path.read_text(encoding='utf8'); n = int(re.search(r'm_VertexCount: (\d+)',s)[1])
    data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-fA-F]+)', s)[1])
    channels = re.findall(r'- stream: (\d+)\n\s+offset: (\d+)\n\s+format: (\d+)\n\s+dimension: (\d+)',s)
    assert channels and tuple(map(int,channels[0])) == (0,0,0,3)
    assert all(int(stream)==0 and int(fmt)==0 for stream,offset,fmt,dim in channels if int(dim)>0)
    stride = max(int(offset)+4*int(dim) for stream,offset,fmt,dim in channels if int(dim)>0)
    assert len(data)==n*stride
    pos = [struct.unpack_from('<fff',data,i*stride) for i in range(n)]
    fmt = '<H' if 'm_IndexFormat: 0' in s else '<I'
    indices = [x[0] for x in struct.iter_unpack(fmt,bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-fA-F]+)',s)[1]))]
    return pos,indices

def exact_box(pos, ids):
    # An enclosing AABB is accepted only if the actual closed mesh is that box.
    if len(ids)!=36: return None
    lo=[min(v[i] for v in pos) for i in range(3)];hi=[max(v[i] for v in pos) for i in range(3)]
    if min(hi[i]-lo[i] for i in range(3))<.005: return None
    tags=[]
    for v in pos:
        tag=tuple(int(abs(v[i]-hi[i])<abs(v[i]-lo[i])) for i in range(3))
        if max(abs(v[i]-(hi[i] if tag[i] else lo[i])) for i in range(3))>1e-5: return None
        tags.append(tag)
    if set(tags)!=set(itertools.product((0,1),repeat=3)): return None
    edges=collections.Counter();faces=collections.Counter();areas=collections.defaultdict(float)
    for a,b,c in zip(ids[0::3],ids[1::3],ids[2::3]):
        ts=[tags[a],tags[b],tags[c]]
        if len(set(ts))!=3:return None
        side=[(i,ts[0][i]) for i in range(3) if ts[0][i]==ts[1][i]==ts[2][i]]
        if len(side)!=1:return None
        i,_=side[0];axes=[j for j in range(3) if j!=i]
        twice=abs((pos[b][axes[0]]-pos[a][axes[0]])*(pos[c][axes[1]]-pos[a][axes[1]])-(pos[b][axes[1]]-pos[a][axes[1]])*(pos[c][axes[0]]-pos[a][axes[0]]))
        if twice<1e-8:return None
        faces[side[0]]+=1;areas[side[0]]+=twice/2
        for u,v in [(ts[0],ts[1]),(ts[1],ts[2]),(ts[2],ts[0])]:edges[tuple(sorted((u,v)))]+=1
    if len(faces)!=6 or any(x!=2 for x in faces.values()) or any(x!=2 for x in edges.values()):return None
    for (i,sign),area in areas.items():
        axes=[j for j in range(3) if j!=i];expected=(hi[axes[0]]-lo[axes[0]])*(hi[axes[1]]-lo[axes[1]])
        if abs(area-expected)>max(1e-6,expected*1e-5):return None
    return {'center':[(a+b)/2 for a,b in zip(lo,hi)],'size':[b-a for a,b in zip(lo,hi)],'proof':'8 exact AABB corners; 12 nondegenerate triangles; 6 complete faces; closed edge incidence'}

def main():
    rows=[];boxes=[];protected={SCENE:sha(REPO/SCENE)}
    for version in ['RebuildR08','RebuildR09R18']:
        manifest_path=REPO/'SourceData/Models'/version/'NativeManifest.json'
        manifest=json.loads(manifest_path.read_text(encoding='utf8'))
        prefab_path=REPO/BASE/version/(version+'_Display.prefab');prefab=records(prefab_path.read_text(encoding='utf8'))
        protected[prefab_path.relative_to(REPO).as_posix()]=sha(prefab_path)
        source_go={fid:body for fid,(typ,body) in prefab.items() if typ==1}
        source_tr={reference(body,'m_GameObject'):body for typ,body in prefab.values() if typ==4}
        filters=[(fid,body) for fid,(typ,body) in prefab.items() if typ==33]
        for obj in manifest['objects']:
            name=obj['name']
            if not re.search(r'Deck|Floor|Tread|Landing|Apron|Ramp|Toe_Transition|Threshold|_Path|_Link$',name,re.I):continue
            if re.search(r'Rail|Guard|Pier|Trestle|Truss|Stringer|Bearer|Girder|Support|Seam|Bolt|Anchor|Mark|Stripe|Drain|Lip|Panel|Detail|Frame|Foot_|Footing|Haunch|Mount|Shield|Low_Edge|_Boot',name,re.I):continue
            bounds=obj['worldBounds']
            if bounds[1][0]-bounds[0][0]<.8 or bounds[1][2]-bounds[0][2]<.8:continue
            mesh=REPO/BASE/version/'Meshes'/(obj['key']+'.asset')
            guid=re.search(r'guid: (\w+)',Path(str(mesh)+'.meta').read_text())[1]
            matches=[(fid,b) for fid,b in filters if 'guid: '+guid+',' in b]
            assert len(matches)==1,(name,'nonunique source mesh filter')
            fid,body=matches[0];go=reference(body,'m_GameObject');tr=source_tr[go]
            own_cols=[b for t,b in prefab.values() if t in (64,65,135,136) and reference(b,'m_GameObject')==go]
            assert not own_cols
            pos,ids=native_positions(mesh);box=exact_box(pos,ids)
            row={'sourceVersion':version,'key':obj['key'],'name':name,'sourcePrefab':prefab_path.relative_to(REPO).as_posix(),'mesh':mesh.relative_to(REPO).as_posix(),'meshGUID':guid,'meshSHA256':sha(mesh),'meshFilterFileID':fid,'gameObjectFileID':go,'worldBoundsManifest':bounds,'ownColliderCount':0,'otherProxyCoverage':'UNVERIFIED: no connected Editor Pipeline; no raycast/scene query run','simpleBox':box,'sourcePosition':property_text(tr,'m_LocalPosition'),'sourceRotation':property_text(tr,'m_LocalRotation'),'sourceScale':property_text(tr,'m_LocalScale')}
            rows.append(row)
            if box:
                # Only near-horizontal, sufficiently wide floor boxes are prepared.
                q=values(row['sourceRotation']);cx,cy,cz,cw=q
                up_y=1-2*(cx*cx+cz*cz)
                if up_y>.95 and box['size'][1]<=.6 and box['size'][0]>.8 and box['size'][2]>.8:boxes.append(row)
    OUT.mkdir(exist_ok=True)
    root_id,root_tr=100000,100001; chunks=['%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n']
    def go_record(go,tr,name,collider=None):
        components=f'  - component: {{fileID: {tr}}}\n'+(f'  - component: {{fileID: {collider}}}\n' if collider else '')
        return f'--- !u!1 &{go}\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  serializedVersion: 6\n  m_Component:\n{components}  m_Layer: 0\n  m_Name: {name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n'
    children=''.join(f'  - {{fileID: {200001+i*10}}}\n' for i in range(len(boxes)))
    chunks.append(go_record(root_id,root_tr,'OuterBuildings_Collision_DRAFT_NOT_APPLIED'))
    chunks.append(f'--- !u!4 &{root_tr}\nTransform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {root_id}}}\n  serializedVersion: 2\n  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}\n  m_LocalPosition: {{x: 0, y: 0, z: 0}}\n  m_LocalScale: {{x: 1, y: 1, z: 1}}\n  m_ConstrainProportionsScale: 0\n  m_Children:\n{children}  m_Father: {{fileID: 0}}\n  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}\n')
    for i,row in enumerate(boxes):
        go,tr,col=200000+i*10,200001+i*10,200002+i*10;box=row['simpleBox'];chunks.append(go_record(go,tr,row['key']+'_'+row['name'],col))
        chunks.append(f'--- !u!4 &{tr}\nTransform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n  m_LocalRotation: {row["sourceRotation"]}\n  m_LocalPosition: {row["sourcePosition"]}\n  m_LocalScale: {row["sourceScale"]}\n  m_ConstrainProportionsScale: 0\n  m_Children: []\n  m_Father: {{fileID: {root_tr}}}\n  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}\n')
        chunks.append(f'--- !u!65 &{col}\nBoxCollider:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {go}}}\n  m_Material: {{fileID: 0}}\n  m_IncludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n  m_ExcludeLayers:\n    serializedVersion: 2\n    m_Bits: 0\n  m_LayerOverridePriority: 0\n  m_IsTrigger: 0\n  m_ProvidesContacts: 0\n  m_Enabled: 1\n  serializedVersion: 3\n  m_Size: {triple(box["size"])}\n  m_Center: {triple(box["center"])}\n')
    draft=OUT/'OuterBuildings_Collision_DRAFT_NOT_APPLIED.prefab';draft.write_text(''.join(chunks),encoding='utf8')
    plan={'scope':'Workspace-only draft. Not imported, instantiated, committed, or verified by Unity.','rows':rows,'draftBoxCount':len(boxes),'draftKeys':[x['key'] for x in boxes],'protectedFileHashes':protected,'applicationGates':['Current formal prefab parent chains and final world matrices match manifests.','Editor connected, not Play/compiling/building and no concurrent scene writer.','Query all current proxies at candidate surface samples; omit already covered surfaces.','Validate Layer0 with actual controller layers and check source collision material.','Complex aperture/stair surfaces need separate reduced proxies; no enclosing boxes.','Preserve ExpeditionClockPresentation and existing protected settings.'],'editorBlocker':'No Pipeline instance found for project: E:/UnityProjects/FPS_Template','actualSceneChanges':0,'runtimeChecks':0}
    for rel,h in protected.items():assert sha(REPO/rel)==h,'Protected file changed concurrently: '+rel
    (OUT/'CollisionPreparation.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf8')
    print(json.dumps({'candidateSurfaces':len(rows),'exactClosedBoxDrafts':len(boxes),'draft':str(draft),'otherProxyCoverage':'unverified','sceneModified':False,'candidateExamples':[x['name'] for x in rows[:8]]},ensure_ascii=False))
if __name__=='__main__':main()
