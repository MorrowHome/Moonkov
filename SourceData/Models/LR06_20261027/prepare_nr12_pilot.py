"""Prepare a small LR06 corridor pilot in memory; do not save the source blend."""
import bpy
import bmesh
import json
import hashlib
import sys
import numpy as np
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'NR12Pilot_Corrected'
OUT.mkdir(exist_ok=True)
SOURCE_SHA='cdb003ad58e2c081076cc874eb9f1c4fdf3c216e056321257dabcf5d6f817054'
assert hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest()==SOURCE_SHA

# One UV-less procedural wall, one procedural housing, and LR06's warm diffuser.
names=['NR01_V5_EAST_Full_Height_Wall',
       'NR01_V5_SOUTH_TRANSFER_Worklight_Housing',
       'NR01_V5_SOUTH_TRANSFER_Worklight_Diffuser']
objects=[bpy.data.objects[name] for name in names]
source_matrices={o.name:o.matrix_world.copy() for o in objects}
depsgraph=bpy.context.evaluated_depsgraph_get()
meshes={}
source_bounds={}
flipped={}
for obj in objects:
    evaluated=obj.evaluated_get(depsgraph)
    source_bounds[obj.name]=[[min((source_matrices[obj.name] @ Vector(c))[i] for c in evaluated.bound_box) for i in range(3)],
                            [max((source_matrices[obj.name] @ Vector(c))[i] for c in evaluated.bound_box) for i in range(3)]]
    mesh=bpy.data.meshes.new_from_object(evaluated,preserve_all_data_layers=True,depsgraph=depsgraph)
    # Correct confirmed closed, inward-facing meshes on the export copy only.
    # Open meshes and uncertain islands are not automatically changed.
    bm=bmesh.new();bm.from_mesh(mesh)
    assert all(e.is_manifold for e in bm.edges),f'{obj.name}: pilot requires a closed mesh'
    tmp=bm.copy()
    center=sum((v.co for v in tmp.verts),Vector())/len(tmp.verts)
    for v in tmp.verts:v.co-=center
    volume=tmp.calc_volume(signed=True);tmp.free()
    assert abs(volume)>1e-9
    flipped[obj.name]=volume<0
    if volume<0:
        bmesh.ops.reverse_faces(bm,faces=bm.faces[:])
        bm.to_mesh(mesh);mesh.update()
    bm.free()
    meshes[obj.name]=mesh

pilot=bpy.data.scenes.new('LR06_NR12_Pilot_Offline')
for obj in objects:
    # Use the original object's identity in this disposable process: the wall
    # shader uses Object Info Random. Neither the source file nor its assets are saved.
    pilot.collection.objects.link(obj)
    matrix=source_matrices[obj.name]
    obj.parent=None
    obj.matrix_world=matrix
    obj.data=meshes[obj.name]
    for mod in obj.modifiers:
        mod.show_render=False
        mod.show_viewport=False

# Include nearby original geometry as AO occluders during the pilot bake. These
# are not selected for FBX export. This is still a small local context, not a
# full-scene bake or a claim of final AO equivalence.
for obj in list(bpy.context.scene.objects):
    if obj in objects or obj.type!='MESH' or obj.hide_render or not obj.visible_get():
        continue
    if not obj.name.startswith('NR01_'):
        continue
    midpoint=obj.matrix_world.translation
    if (midpoint-Vector((1998.175,2468.3,256.04))).length<16:
        pilot.collection.objects.link(obj)

bpy.context.window.scene=pilot
pilot.render.engine='CYCLES'
pilot.cycles.device='CPU'
pilot.cycles.samples=8
pilot.render.bake.use_clear=True
pilot.render.bake.margin=8
pilot.render.bake.use_selected_to_active=False
pilot.render.image_settings.file_format='PNG'

def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj

def make_uv(obj):
    assert len(obj.data.uv_layers)==0, 'Pilot will not replace a source UV map'
    obj.data.uv_layers.new(name='LR06_BakeUV')
    activate(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    result=bpy.ops.uv.smart_project(island_margin=0.025)
    bpy.ops.object.mode_set(mode='OBJECT')
    assert result=={'FINISHED'}

def bake_emit(obj, material, socket_name, out_name, is_color):
    tree=material.node_tree
    bsdf=next(n for n in tree.nodes if n.type=='BSDF_PRINCIPLED')
    output=next(n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output)
    surface=output.inputs['Surface']
    prior=[(l.from_socket,l.to_socket) for l in list(surface.links)]
    for link in list(surface.links):tree.links.remove(link)
    emit=tree.nodes.new('ShaderNodeEmission')
    source=bsdf.inputs[socket_name]
    if source.is_linked:
        tree.links.new(source.links[0].from_socket,emit.inputs['Color'])
    else:
        val=source.default_value
        emit.inputs['Color'].default_value=tuple(val) if hasattr(val,'__len__') else (val,val,val,1)
    tree.links.new(emit.outputs['Emission'],surface)
    image=bpy.data.images.new(out_name,512,512,alpha=True)
    image.colorspace_settings.name='sRGB' if is_color else 'Non-Color'
    node=tree.nodes.new('ShaderNodeTexImage')
    node.image=image
    tree.nodes.active=node
    activate(obj)
    try:
        assert bpy.ops.object.bake(type='EMIT')=={'FINISHED'}
        image.filepath_raw=str(OUT/(out_name+'.png'))
        image.file_format='PNG'
        image.save()
    finally:
        tree.nodes.remove(node)
        tree.nodes.remove(emit)
        for a,b in prior:tree.links.new(a,b)
    return {'path':str((OUT/(out_name+'.png')).relative_to(ROOT)),'colorspace':image.colorspace_settings.name,
            'bytes':(OUT/(out_name+'.png')).stat().st_size}

def bake_normal(obj, material, out_name, disable_bump=False, exaggerate_bump=False):
    tree=material.node_tree
    bsdf=next(n for n in tree.nodes if n.type=='BSDF_PRINCIPLED')
    source=bsdf.inputs['Normal']
    prior=[(l.from_socket,l.to_socket) for l in list(source.links)]
    bump=next((n for n in tree.nodes if n.type=='BUMP'),None)
    bump_values=None
    if exaggerate_bump and bump:
        bump_values=(bump.inputs['Strength'].default_value,bump.inputs['Distance'].default_value)
        bump.inputs['Strength'].default_value=1
        bump.inputs['Distance'].default_value=0.1
    if disable_bump:
        for link in list(source.links):tree.links.remove(link)
    image=bpy.data.images.new(out_name,512,512,alpha=True,float_buffer=True)
    image.colorspace_settings.name='Non-Color'
    node=tree.nodes.new('ShaderNodeTexImage');node.image=image;tree.nodes.active=node
    activate(obj)
    try:
        assert bpy.ops.object.bake(type='NORMAL',normal_space='TANGENT')=={'FINISHED'}
        pixels=np.empty(512*512*4,dtype=np.float32)
        image.pixels.foreach_get(pixels)
        image.filepath_raw=str(OUT/(out_name+'.exr'));image.file_format='OPEN_EXR';image.save()
    finally:
        tree.nodes.remove(node)
        if bump_values:
            bump.inputs['Strength'].default_value,bump.inputs['Distance'].default_value=bump_values
        if disable_bump:
            for a,b in prior:tree.links.new(a,b)
    return {'path':str((OUT/(out_name+'.exr')).relative_to(ROOT)),'colorspace':'Non-Color',
            'bytes':(OUT/(out_name+'.exr')).stat().st_size},pixels.reshape((-1,4))

material_rows=[]
for i,obj in enumerate(objects):
    originals=[s.material for s in obj.material_slots]
    assert len(originals)==1 and originals[0]
    source_mat=originals[0]
    mat=source_mat.copy()
    obj.data.materials[0]=mat
    bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    ins=bsdf.inputs
    row={'object':obj.name,'source_material':source_mat.name,
         'source_has_uv':bool(obj.data.uv_layers),
         'reversed_closed_mesh_in_export_copy':flipped[obj.name],
         'source_world_bounds':source_bounds[obj.name],
         'base_color':list(ins['Base Color'].default_value),
         'metallic':ins['Metallic'].default_value,
         'roughness':ins['Roughness'].default_value,
         'emission_color':list(ins['Emission Color'].default_value),
         'emission_strength':ins['Emission Strength'].default_value,
         'linked_inputs':[k for k in ('Base Color','Metallic','Roughness','Normal','Emission Color','Emission Strength') if ins[k].is_linked],
         'textures':{}}
    if row['linked_inputs']:
        make_uv(obj)
        for socket,tag,is_color in [('Base Color','basecolor',True),('Roughness','roughness',False)]:
            if ins[socket].is_linked:
                row['textures'][tag]=bake_emit(obj,mat,socket,f'pilot_{i}_{tag}',is_color)
                print('BAKED',obj.name,tag,flush=True)
        if ins['Normal'].is_linked:
            row['textures']['normal'],bumped=bake_normal(obj,mat,f'pilot_{i}_normal')
            row['textures']['normal_control'],flat=bake_normal(obj,mat,f'pilot_{i}_normal_control',True)
            mask=(bumped[:,3]>.5)&(flat[:,3]>.5)
            diff=np.abs(bumped[mask,:3]-flat[mask,:3])
            row['normal_status']='bump_bake_changed' if diff.size and float(diff.max())>0.002 else 'bump_bake_not_proven'
            row['normal_max_channel_difference']=float(diff.max()) if diff.size else 0
            if i==0:
                row['textures']['normal_strong_control'],strong=bake_normal(obj,mat,f'pilot_{i}_normal_strong_control',exaggerate_bump=True)
                mask2=(strong[:,3]>.5)&(flat[:,3]>.5)
                delta=np.abs(strong[mask2,:3]-flat[mask2,:3])
                row['normal_strong_control_max_difference']=float(delta.max()) if delta.size else 0
            print('NORMAL',obj.name,row['normal_status'],row['normal_max_channel_difference'],flush=True)
        else:
            row['normal_status']='constant'
    else:
        row['normal_status']='constant'
    material_rows.append(row)

for obj in objects:obj.select_set(False)
bpy.ops.object.select_all(action='DESELECT')
for obj in objects:obj.select_set(True)
bpy.context.view_layer.objects.active=objects[0]
fbx=OUT/'NR12_corridor_pilot.fbx'
assert bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},
    use_mesh_modifiers=False,bake_anim=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',
    global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_NONE',
    use_space_transform=True,bake_space_transform=False,use_triangles=True,path_mode='STRIP')=={'FINISHED'}

sys.path.insert(0,'/Users/morrow/.codex/skills/blender-asset-qa/scripts')
import qa_check
qa={o.name:qa_check.run(objects=[o.name],profile='render',max_tris=300000) for o in objects}
(OUT/'GeometryQA.json').write_text(json.dumps(qa,ensure_ascii=False,indent=2,default=str))
report={'status':'offline_pilot_not_Unity_validated','source_sha256':SOURCE_SHA,
        'source_preserved_on_disk':True,'objects':material_rows,
        'fbx':{'path':str(fbx.relative_to(ROOT)),'bytes':fbx.stat().st_size,'sha256':hashlib.sha256(fbx.read_bytes()).hexdigest()},
        'limitations':['Normal-map material wiring and visual equivalence require Unity verification.',
                       'AO used a small local scene context; compare with the complete source.',
                       'Unity coordinate, lighting, material assignment and visuals remain unverified.']}
(OUT/'PilotManifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2,default=str))
print('@@JSON@@'+json.dumps({'fbx':report['fbx'],'materials':material_rows,
      'qa':{k:{'pass':v['pass'],'errors':v['errors'],'warnings':v['warnings']} for k,v in qa.items()}},ensure_ascii=False,default=str),flush=True)
