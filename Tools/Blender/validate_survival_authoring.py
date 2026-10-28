"""Validate saved Blender actions, actual skinned surfaces and inventory geometry."""
import bpy,bmesh,json,math,sys
from pathlib import Path
from mathutils import Matrix,Vector,Quaternion
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'LocalData/Actions/Blender';SOURCE=ROOT/'ArtSource/DollSinger/SurvivalActions.blend'
sys.path.insert(0,str(ROOT/'Tools/Blender'));from retarget_hatsune_miku import Rig
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE');body=next(o for o in scene.objects if o.type=='MESH' and len(o.vertex_groups)>100)
bind=Rig(json.loads((ROOT/'Tools/Blender/DollSingerBind.json').read_text()));C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)));BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
def desc(name,root):
    while name in bind.parent:
        if name==root:return True
        name=bind.parent[name]
    return False
def group(n):
    if desc(n,'首'):return 'head'
    if not n.endswith(('.L','.R')):return 'body'
    s=n[-1]
    if n.startswith(('人指','中指','薬指','小指','親指')):return n[:2]+'.'+s
    if n.startswith('手首'):return 'palm.'+s
    if n.startswith(('ひじ','手捩')):return 'forearm.'+s
    if n.startswith(('腕','肩')):return 'upper.'+s
    return 'body'
dominant=[max(((body.vertex_groups[g.group].name,g.weight) for g in v.groups),key=lambda p:p[1])[0] if v.groups else '' for v in body.data.vertices];labels=[group(n) for n in dominant]
faces={}
for p in body.data.polygons:
    names={labels[i] for i in p.vertices}
    if len(names)==1:faces.setdefault(next(iter(names)),[]).append(tuple(p.vertices))
props=[o for o in scene.objects if o.get('source_part')]
# Solid-colour inventory variants have no texture dependency. Give each a valid
# non-overlapping review/export UV map, including the new seal and needle sheath.
for obj in props:
    for other in scene.objects:other.select_set(False)
    obj.hide_set(False);obj.hide_viewport=False;obj.select_set(True);bpy.context.view_layer.objects.active=obj
    uv=obj.data.uv_layers.get('ExportUV') or obj.data.uv_layers.new(name='ExportUV');obj.data.uv_layers.active=uv
    with bpy.context.temp_override(object=obj,active_object=obj,selected_objects=[obj],selected_editable_objects=[obj]):
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.005);bpy.ops.object.mode_set(mode='OBJECT')
    obj.select_set(False)
scene.frame_set(511);bpy.context.view_layer.update();bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE),compress=True)
sys.path.insert(0,str(Path.home()/'.codex/skills/blender-asset-qa/scripts'));import qa_check
asset_reports={o.name:qa_check.run(objects=[o.name],profile='game',eps=.0002,tol=.0006) for o in props if not o.name.startswith('medkit_')}
scene.frame_set(631);bpy.context.view_layer.update()
medical=[o for o in props if o.name.startswith('medkit_')];saved_matrices={o:o.matrix_world.copy() for o in medical}
origin=bpy.data.objects['Using_medkit'].matrix_world.translation.copy()
for obj in medical:obj.matrix_world=Matrix.Translation(-origin)@saved_matrices[obj]
bpy.context.view_layer.update()
asset_reports['medkit_assembly']=qa_check.run(objects=[o.name for o in medical],profile='game',eps=.0002,tol=.0006)
asset_reports['medkit_assembly']['measurement']='Rigid translation near origin avoids single-precision signed-volume cancellation; geometry, scale and relative transforms are unchanged.'
for obj,matrix in saved_matrices.items():obj.matrix_world=matrix
bpy.context.view_layer.update()
result={'scope':'Saved production Blender source. Source body topology and weights are unchanged. Inventory variants, exact-rig FK, loop seam, skinned contact and nonadjacent surface intersections are inspected.','assets':asset_reports,'motions':{}}
hand_vertices={s:[i for i,n in enumerate(dominant) if n.endswith('.'+s) and n.startswith(('手首','人指','中指','薬指','小指','親指'))] for s in ('L','R')}
for kind,start,code in [('Search',1,None),('Eat',211,'ration'),('Drink',421,'water'),('Inject',631,'medkit')]:
    entry={'maximumFkErrorMetres':0,'maximumJointStepDegrees':0,'surfaceIntersections':{},'handPropPenetration':{},'sampledFrames':181};previous=None;first=None;last=None
    for f in range(181):
        scene.frame_set(start+f);bpy.context.view_layer.update();world={};positions={}
        for name in bind.order:
            if name not in rig.pose.bones:continue
            q=(rig.matrix_world@rig.pose.bones[name].matrix).to_quaternion();delta=q@BR[name].inverted();u=(C@delta.to_matrix()@C.transposed()).to_quaternion();rest=bind.r[name];u=u@Quaternion((rest[3],rest[0],rest[1],rest[2]));world[name]=(u.x,u.y,u.z,u.w);positions[name]=C@(rig.matrix_world@rig.pose.bones[name].matrix).translation
        expected,_=bind.evaluate(world=world)
        entry['maximumFkErrorMetres']=max(entry['maximumFkErrorMetres'],max((positions[n]-Vector(expected[n])).length for n in positions))
        if first is None:first={n:rig.pose.bones[n].rotation_quaternion.copy() for n in bind.order if n in rig.pose.bones}
        last={n:rig.pose.bones[n].rotation_quaternion.copy() for n in first}
        if previous:
            entry['maximumJointStepDegrees']=max(entry['maximumJointStepDegrees'],max(min(math.degrees(Quaternion((q[3],q[0],q[1],q[2])).rotation_difference(Quaternion((previous[n][3],previous[n][0],previous[n][1],previous[n][2]))).angle),360-math.degrees(Quaternion((q[3],q[0],q[1],q[2])).rotation_difference(Quaternion((previous[n][3],previous[n][0],previous[n][1],previous[n][2]))).angle)) for n,q in world.items()))
        previous=world
        if f not in (0,18,29,43,54,67,81,99,117,135,153,171,180):continue
        ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh();vertices=[ev.matrix_world@v.co for v in mesh.vertices];trees={k:BVHTree.FromPolygons(vertices,fs,all_triangles=True) for k,fs in faces.items()};keys=sorted(trees)
        for i,a in enumerate(keys):
            for b in keys[i+1:]:
                if a.startswith('upper') or b.startswith('upper'):continue
                if a=='body' and b=='body':continue
                if a[-1:]==b[-1:] and (a.startswith(('palm','forearm')) or b.startswith(('palm','forearm'))):continue
                hits=len(trees[a].overlap(trees[b]))
                if hits:
                    key=a+' / '+b
                    if hits>entry['surfaceIntersections'].get(key,{}).get('triangles',0):entry['surfaceIntersections'][key]={'triangles':hits,'frame':start+f}
        if code:
            for prop in props:
                if not prop.name.startswith(code+'_') or prop.hide_render:continue
                pe=prop.evaluated_get(bpy.context.evaluated_depsgraph_get());pm=pe.to_mesh();minimum=Vector(tuple(min(v.co[i] for v in pm.vertices) for i in range(3)));maximum_bounds=Vector(tuple(max(v.co[i] for v in pm.vertices) for i in range(3)));inverse=pe.matrix_world.inverted();pv=[pe.matrix_world@v.co for v in pm.vertices];tree=BVHTree.FromPolygons(pv,[tuple(p.vertices) for p in pm.polygons],all_triangles=True)
                for side in ('L','R'):
                    maximum=0;count=0;nearest=.1;digit_distances={}
                    for i in hand_vertices[side]:
                        p=vertices[i];hit,normal,_,distance=tree.find_nearest(p)
                        if hit is None:continue
                        nearest=min(nearest,distance);digit=dominant[i].split('.')[0][:2];digit_distances[digit]=min(digit_distances.get(digit,.1),distance)
                        local_point=inverse@p
                        in_bounds=all(minimum[j]-.0001<=local_point[j]<=maximum_bounds[j]+.0001 for j in range(3))
                        if in_bounds and (p-hit).dot(normal)<-.0001 and distance<.025 and distance>.001:
                            maximum=max(maximum,distance);count+=1
                    key=side+' / '+prop.name;old=entry['handPropPenetration'].get(key,{'maximumDepth':0,'minimumGap':.1})
                    if maximum>old['maximumDepth']:old.update({'maximumDepth':maximum,'vertices':count,'frame':start+f})
                    old['minimumGap']=min(old['minimumGap'],nearest);entry['handPropPenetration'][key]=old
                pe.to_mesh_clear()
        ev.to_mesh_clear()
    if kind=='Search':entry['loopSeamDegrees']=max(math.degrees(a.rotation_difference(last[n]).angle) for n,a in first.items())
    result['motions'][kind]=entry
(OUT/'AuthoringValidation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print('@@JSON@@'+json.dumps({'assets':{n:{'pass':r.get('pass'),'errors':r.get('errors'),'warnings':r.get('warnings')} for n,r in asset_reports.items()},'motions':result['motions']},ensure_ascii=False))
