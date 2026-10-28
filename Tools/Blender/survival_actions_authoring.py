"""Create an editable Blender review of the actual rig and inventory models.

This is an authoring starting point. Further edits happen in the saved .blend;
Unity integration stays separate until the complete motion has been reviewed.
Run Blender -b -P this_file.py -- [--rebuild] [--render-contact]
"""
import bpy, sys, json, math
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/Blender'))
from retarget_hatsune_miku import Rig

OUT=ROOT/'ArtSource/DollSinger/SurvivalActions.blend'
REVIEW=ROOT/'LocalData/Actions/Blender';REVIEW.mkdir(parents=True,exist_ok=True)
if OUT.exists() and '--rebuild' not in sys.argv:
    raise RuntimeError('Saved authoring exists. Use its Actions; --rebuild explicitly replaces this task source.')
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'ArtSource/DollSinger/WeaponGestures.blend'))
scene=bpy.context.scene
rig=next(o for o in scene.objects if o.type=='ARMATURE')
rig.name='DollSinger_Survival_Authoring'
body=next(o for o in scene.objects if o.type=='MESH' and len(o.vertex_groups)>100)
characters=[o for o in scene.objects if o.type=='MESH' and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
for o in characters:o.hide_render=False;o.hide_set(False)
rig.animation_data_clear();rig.animation_data_create()
for bone in rig.pose.bones:bone.matrix_basis=Matrix.Identity(4)
scene.render.fps=60;scene.frame_start=1;scene.frame_end=810
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
bind=Rig(json.loads((ROOT/'Tools/Blender/DollSingerBind.json').read_text()))
data=json.loads((ROOT/'SourceData/Animations/Survival/AuthoringSeed.json').read_text())
geometry=json.loads((ROOT/'SourceData/Animations/Survival/ReviewProps.json').read_text())
BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
def qr(q):return Quaternion((q[3],q[0],q[1],q[2]))
def install(rotations):
    for n in bind.order:
        if n not in rig.pose.bones:continue
        bone=rig.pose.bones[n];parent=bind.parent[n]
        target=(C.transposed()@(qr(rotations[n])@qr(bind.r[n]).inverted()).to_matrix()@C).to_quaternion()@BR[n]
        parent_target=(C.transposed()@(qr(rotations[parent])@qr(bind.r[parent]).inverted()).to_matrix()@C).to_quaternion()@BR[parent] if parent in rotations and parent in BR else BR.get(parent,Quaternion())
        local_rest=(rig.data.bones[parent].matrix_local.inverted()@bone.bone.matrix_local).to_quaternion() if parent in rig.data.bones else bone.bone.matrix_local.to_quaternion()
        bone.rotation_mode='QUATERNION';bone.rotation_quaternion=local_rest.inverted()@parent_target.inverted()@target
    bpy.context.view_layer.update()

collection=bpy.data.collections.new('Survival inventory props');scene.collection.children.link(collection)
props={}
for item in geometry['items']:
    root=bpy.data.objects.new('Using_'+item['code'],None);collection.objects.link(root)
    root.empty_display_size=.025;root['inventory_code']=item['code'];root['grip_unity']=item['grip'];root['contact_unity']=item['contact']
    parts=[]
    for part in item['parts']:
        vertices=[C.transposed()@Vector(v) for v in part['vertices']];faces=[];indices=[]
        for s,submesh in enumerate(part['submeshes']):
            ids=submesh['triangles']
            for t in range(0,len(ids),3):faces.append(tuple(reversed(ids[t:t+3])));indices.append(s)
        mesh=bpy.data.meshes.new(item['code']+'_'+part['name']);mesh.from_pydata(vertices,[],faces);mesh.update()
        for submesh in part['submeshes']:
            mat=bpy.data.materials.get('Survival_'+submesh['material'])
            if not mat:mat=bpy.data.materials.new('Survival_'+submesh['material']);mat.diffuse_color=submesh['color']
            mesh.materials.append(mat)
        for polygon,index in zip(mesh.polygons,indices):polygon.material_index=index;polygon.use_smooth=True
        if part['uv']:
            uv=mesh.uv_layers.new(name='InventoryUV')
            for polygon in mesh.polygons:
                for loop in polygon.loop_indices:uv.data[loop].uv=part['uv'][mesh.loops[loop].vertex_index]
        obj=bpy.data.objects.new(item['code']+'_'+part['name'],mesh);collection.objects.link(obj);obj.parent=root
        # Unity's generated action meshes duplicate triangle corners. Weld their
        # geometric seams while retaining loop UVs/material boundaries in Blender.
        import bmesh
        bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.0000005);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free();mesh.update()
        obj['source_part']=part['name'];parts.append(obj)
    props[item['code']]={'root':root,'parts':parts,'grip':Vector(item['grip'])}

def read(motion,phase):
    frame=max(0,min(1,phase))*(motion['frames']-1);a=int(frame);b=min(a+1,motion['frames']-1);stride=len(data['bones'])*4
    local={}
    for i,n in enumerate(data['bones']):
        qa=qr(motion['rotations'][a*stride+i*4:a*stride+i*4+4]);qb=qr(motion['rotations'][b*stride+i*4:b*stride+i*4+4]);q=qa.slerp(qb,frame-a)
        local[n]=(q.x,q.y,q.z,q.w)
    return bind.evaluate(local=local)

clips=[('Search','',1),('Eat','ration',211),('Drink','water',421),('Inject','medkit',631)]
for kind,code,start in clips:
    motion=next(m for m in data['motions'] if m['action']==('Treat' if kind=='Search' else kind) and (not code or code in m.get('items',[])))
    action=bpy.data.actions.new('Survival_'+kind);action.use_fake_user=True;action['source_motion']=motion['source'];action['inventory_code']=code
    rig.animation_data.action=action
    for f in range(181):
        scene.frame_set(f+1)
        phase=f/180;points,rotations=read(motion,phase);install(rotations)
        for name in data['bones']:rig.pose.bones[name].keyframe_insert('rotation_quaternion',frame=f+1,group=name)
    track=rig.animation_data.nla_tracks.new();track.name=kind;track.mute=True
    strip=track.strips.new('Survival_'+kind,start,action);strip.action_frame_start=1;strip.action_frame_end=181;strip.frame_end=start+180
    strip.extrapolation='NOTHING';strip.blend_type='REPLACE';strip.influence=1
    if len(action.slots):strip.action_slot=action.slots[0]
    rig.animation_data.action=None
    scene.timeline_markers.new(kind,frame=start)
    # The props are keyed in the shared review timeline. They retain their real
    # meshes, size, opening, needle and plunger, so grip problems stay visible.
    for f in range(181):
        phase=f/180;points,_=read(motion,phase)
        hand=Vector(points['手首.R']);middle=Vector(points['中指１.R']);across=(Vector(points['人指１.R'])-Vector(points['小指１.R'])).normalized();forward=(middle-hand).normalized();normal=across.cross(forward).normalized()
        right=across.cross(normal).normalized();rotation=Matrix(((right.x,across.x,normal.x),(right.y,across.y,normal.y),(right.z,across.z,normal.z))).to_quaternion()
        if motion.get('propRotation'):rotation=rotation@qr(motion['propRotation'])
        grip=hand.lerp(middle,.65)+normal*(.033 if code=='water' else .014)
        frame=start+f
        for item,p in props.items():
            for obj in p['parts']:
                part=obj['source_part'];visible=item==code
                if part=='Sealed':visible=visible and phase<.18
                elif part=='Opened':visible=visible and phase>=.18
                elif part=='Cap':visible=visible and phase<.18
                elif part=='Needle':visible=visible and phase>=.18
                obj.hide_render=not visible;obj.hide_viewport=not visible
                obj.keyframe_insert('hide_render',frame=frame);obj.keyframe_insert('hide_viewport',frame=frame)
                if part=='Plunger':
                    t=max(0,min(1,(phase-.46)/.26));obj.location=C.transposed()@Vector((0,-.022*t*t*(3-2*t),0));obj.keyframe_insert('location',frame=frame)
            if item==code:
                position=grip-rotation@p['grip'];p['root'].matrix_world=Matrix.Translation(C.transposed()@position)@(C.transposed()@rotation.to_matrix()@C).to_4x4()
                p['root'].rotation_mode='QUATERNION';p['root'].keyframe_insert('location',frame=frame);p['root'].keyframe_insert('rotation_quaternion',frame=frame)
    print('AUTHORING',kind,start,start+180,flush=True)

for track in rig.animation_data.nla_tracks:track.mute=False
for action in bpy.data.actions:
    if not action.name.startswith(('Survival_','Using_','ration_','water_','medkit_')):continue
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    for key in curve.keyframe_points:key.interpolation='CONSTANT' if curve.data_path.startswith('hide_') else 'LINEAR'

scene.render.engine='BLENDER_WORKBENCH';scene.render.resolution_x=960;scene.render.resolution_y=720;scene.render.resolution_percentage=100
scene.display.shading.light='STUDIO';scene.display.shading.color_type='MATERIAL';scene.display.shading.show_shadows=False;scene.display.shading.show_cavity=True;scene.display.shading.background_type='WORLD';scene.world.color=(.055,.065,.08)
scene.render.image_settings.file_format='PNG'
views={}
for name,pos,target in [('Review_Front',(0,1.26,1.4),(0,1.18,.08)),('Review_Side',(1.4,1.26,.08),(0,1.18,.08)),('Review_Back',(0,1.26,-1.4),(0,1.18,.08)),('Review_ThreeQuarter',(.76,1.30,.86),(0,1.16,.14))]:
    camera=bpy.data.objects.new(name,bpy.data.cameras.new(name));scene.collection.objects.link(camera);camera.location=C.transposed()@Vector(pos);camera.rotation_euler=(C.transposed()@(Vector(target)-Vector(pos))).to_track_quat('-Z','Y').to_euler();camera.data.lens=45;camera.data.clip_start=.01;views[name]=camera
rig['authoring_status']='Contact review blockout. Validate and edit full motions here before Unity integration.'
rig['motion_credits']='Quaternius CC0; BURNER / Medical syringe healing FPS Animation CC BY 4.0.'
text=bpy.data.texts.new('READ ME - Survival Actions')
text.write('正式角色与库存模型。时间线：1 搜刮，211 进食，421 饮水，631 注射。每段 3 秒。\n先检查完整动作，尤其是握持、左手开盖、拇指按钮、针尖接触。当前文件是 Blender 接触检查的起点，尚未验收。\n原瞄准源文件保持不变。\n')
scene.camera=views['Review_Front'];scene.frame_set(721);bpy.context.view_layer.update()
bpy.context.preferences.filepaths.save_version=0
OUT.parent.mkdir(parents=True,exist_ok=True);bpy.ops.wm.save_as_mainfile(filepath=str(OUT),compress=True)
if '--render-contact' in sys.argv:
    for kind,code,start in clips:
        scene.frame_set(start+90);bpy.context.view_layer.update()
        for label in ('Review_Front','Review_Side','Review_Back'):
            scene.camera=views[label];scene.render.filepath=str(REVIEW/(kind+'-'+label+'.png'));bpy.ops.render.render(write_still=True)
print('@@JSON@@'+json.dumps({'blend':str(OUT),'bytes':OUT.stat().st_size,'actions':[x[0] for x in clips],'frames':scene.frame_end,'bodyTris':sum(len(p.vertices)-2 for p in body.data.polygons),'review':str(REVIEW)}))
