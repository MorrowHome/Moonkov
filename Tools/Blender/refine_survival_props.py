"""Finish the source interaction variants: flexible film and conformal printing.
Keeps inventory dimensions, colours and markings; stock inventory assets are not edited.
"""
import bpy,bmesh,json,math
from pathlib import Path
from mathutils import Matrix,Vector
ROOT=Path(__file__).resolve().parents[2];SOURCE=ROOT/'ArtSource/DollSinger/SurvivalActions.blend';C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene
source=next(p for p in json.loads((ROOT/'SourceData/Animations/Survival/ReviewProps.json').read_text())['items'] if p['code']=='ration')
def depth(x,y):return .002+.014*max(0,1-(x/.06)**2)**.65*max(0,math.sin(math.pi*max(0,min(1,y/.19))))**.4
image=bpy.data.images.load(str(ROOT/'ArtSource/DollSinger/SurvivalActions/RationPrint.png'),check_existing=True)
print_material=bpy.data.materials.get('Ration inventory print') or bpy.data.materials.new('Ration inventory print');print_material.use_nodes=True;print_material.diffuse_color=(.18,.32,.23,1)
nodes=print_material.node_tree.nodes;shader=next(n for n in nodes if n.type=='BSDF_PRINCIPLED');texture=next((n for n in nodes if n.type=='TEX_IMAGE'),None) or nodes.new('ShaderNodeTexImage');texture.image=image;uvnode=next((n for n in nodes if n.type=='UVMAP'),None) or nodes.new('ShaderNodeUVMap');uvnode.uv_map='PrintUV';print_material.node_tree.links.new(uvnode.outputs['UV'],texture.inputs['Vector']);print_material.node_tree.links.new(texture.outputs['Color'],shader.inputs['Base Color']);shader.inputs['Roughness'].default_value=.72
def pouch(part,ymin,ymax,opened):
    verts=[];faces=[];materials=[];nx=24;ny=max(3,round((ymax-ymin)/.005));loops=[]
    def grid(inner=False):
        layers=[]
        for sign in (1,-1):
            ids=[]
            for j in range(ny+1):
                y=ymin+(ymax-ymin)*j/ny
                if inner and j==0 and ymin==0:y+=.0006
                row=[]
                for i in range(nx+1):
                    x=(i/nx*2-1)*.06*(.95+.05*math.sin(math.pi*y/.19))
                    if inner:x*=.989
                    z=sign*(depth(x,y)-(.0006 if inner else 0));row.append(len(verts));verts.append(Vector((x,y,z)))
                ids.append(row)
            layers.append(ids)
        start=len(faces)
        for side,ids in enumerate(layers):
            for j in range(ny):
                for i in range(nx):
                    q=[ids[j][i],ids[j][i+1],ids[j+1][i+1],ids[j+1][i]]
                    if side==1:q.reverse()
                    faces.append(q);materials.append(0 if side==0 and not inner else 1)
        a,b=layers
        for j in range(ny):
            for i in (0,nx):faces.append([a[j][i],b[j][i],b[j+1][i],a[j+1][i]]);materials.append(1)
        # Both grids make the connected film shell. At the open mouth or torn
        # lower cut the wall is capped by a thin rim, never by a filled box face.
        closed=[0] if ymin==0 else [ny]
        if not opened:closed=[0,ny]
        for j in closed:
            for i in range(nx):faces.append([a[j][i],b[j][i],b[j][i+1],a[j][i+1]]);materials.append(1)
        if inner:
            for k in range(start,len(faces)):faces[k].reverse()
        return layers
    outer=grid(False)
    if opened:
        inner=grid(True);j=ny if ymin==0 else 0
        for side in range(2):
            for i in range(nx):faces.append([outer[side][j][i],outer[side][j][i+1],inner[side][j][i+1],inner[side][j][i]]);materials.append(1)
        for i in (0,nx):faces.append([outer[0][j][i],outer[1][j][i],inner[1][j][i],inner[0][j][i]]);materials.append(1)
    mats=[print_material,bpy.data.materials['Survival_Item_green']]
    mesh=bpy.data.meshes.new('FlexibleRation_'+part);mesh.from_pydata([C.transposed()@p for p in verts],[],faces);mesh.update()
    for mat in mats:mesh.materials.append(mat)
    for face,index in zip(mesh.polygons,materials):face.material_index=index;face.use_smooth=True
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.0000005);bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.0000002);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free();mesh.update();mesh['flexible_film_v2']=True
    uv=mesh.uv_layers.new(name='PrintUV')
    for face in mesh.polygons:
        for index in face.loop_indices:
            p=C@mesh.vertices[mesh.loops[index].vertex_index].co;uv.data[index].uv=((.06-p.x)/.12,p.y/.19)
    uv.active_render=True
    bpy.data.objects['ration_'+part].data=mesh
pouch('Sealed',0,.19,False);pouch('Opened',0,.172,True);pouch('TearStrip',.172,.19,True)
# Project medical scale marks / text onto the real barrel, retaining solid parts.
obj=bpy.data.objects['medkit_Body'];bm=bmesh.new();bm.from_mesh(obj.data);seen=set(bm.verts) if obj.data.get('conformal_inventory_markings') else set();labels=0
for vertex in list(bm.verts):
    if vertex in seen:continue
    island=set([vertex]);todo=[vertex]
    while todo:
        v=todo.pop()
        for edge in v.link_edges:
            other=edge.other_vert(v)
            if other not in island:island.add(other);todo.append(other)
    seen|=island;faces={f for v in island for f in v.link_faces}
    if not any(e.is_boundary for v in island for e in v.link_edges):continue
    if not all((C@v.co).z>.015 for v in island):continue
    for v in island:
        p=C@v.co;theta=p.x/.018;p.x=.01835*math.sin(theta);p.z=.01835*math.cos(theta);v.co=C.transposed()@p
    for face in faces:
        face.normal_update()
        if (C@face.normal).z<0:face.normal_flip()
    labels+=1
bm.to_mesh(obj.data);bm.free();obj.data.update();obj.data['conformal_inventory_markings']=True
scene.frame_set(301);bpy.context.view_layer.update();bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE),compress=True)
print('@@JSON@@'+json.dumps({'pouch':'flexible, open mouth, thin rim, conformal inventory printing','medicalMarkings':labels}))
