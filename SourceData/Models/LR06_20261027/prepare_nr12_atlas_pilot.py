"""Test two-object atlas baking on isolated, corrected NR12 pilot geometry."""
import bpy
import hashlib
import json
import sys
import numpy as np
from pathlib import Path

root=Path(__file__).resolve().parent
sys.path.insert(0, str(root))
from nr12_atlas_png import pack_metal_smoothness
out=root/'NR12AtlasPilot'
out.mkdir(exist_ok=True)
previous=json.loads((root/'NR12Pilot_Corrected/PilotManifest.json').read_text())
source=root/'Moonkov_WH01_HU01_LR06_Corridor_Warm_Fixture_Glow_REVIEW.blend'
assert hashlib.sha256(source.read_bytes()).hexdigest()==previous['source_sha256']
assert bpy.ops.import_scene.fbx(filepath=str(root/'NR12Pilot_Corrected/NR12_corridor_pilot.fbx'),
                                use_custom_normals=True,use_image_search=False)=={'FINISHED'}
objects=[bpy.data.objects[o['object']] for o in previous['objects']]
assert len(objects)==3
names=tuple(o['source_material'] for o in previous['objects'])
with bpy.data.libraries.load(str(source),link=False) as (available,loaded):
    assert all(n in available.materials for n in names)
    loaded.materials=list(names)
materials=list(loaded.materials)
assert all(materials)
for obj,mat in zip(objects,materials):
    obj.data.materials.clear();obj.data.materials.append(mat)

scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=8
scene.render.threads_mode='FIXED';scene.render.threads=4
scene.render.bake.margin=12;scene.render.bake.use_selected_to_active=False
scene.render.bake.use_clear=True

# The original wall and housing had no UV. Replace only the prior generated
# sample UV on these disposable copies; the emissive diffuser remains UV-less.
for obj in objects[:2]:
    for uv in list(obj.data.uv_layers):obj.data.uv_layers.remove(uv)
    obj.data.uv_layers.new(name='LR06_AtlasUV')
bpy.ops.object.select_all(action='DESELECT')
for obj in objects[:2]:obj.select_set(True)
bpy.context.view_layer.objects.active=objects[0]
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
assert bpy.ops.uv.smart_project(island_margin=.03)=={'FINISHED'}
bpy.ops.object.mode_set(mode='OBJECT')

def connected_output(mat,socket_name,image):
    tree=mat.node_tree;bsdf=next(n for n in tree.nodes if n.type=='BSDF_PRINCIPLED')
    output=next(n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output)
    surface=output.inputs['Surface'];prior=[(l.from_socket,l.to_socket) for l in list(surface.links)]
    for l in list(surface.links):tree.links.remove(l)
    emission=tree.nodes.new('ShaderNodeEmission');sock=bsdf.inputs[socket_name]
    if sock.is_linked:tree.links.new(sock.links[0].from_socket,emission.inputs['Color'])
    else:
        value=sock.default_value
        emission.inputs['Color'].default_value=tuple(value) if hasattr(value,'__len__') else (value,value,value,1)
    tree.links.new(emission.outputs['Emission'],surface)
    tex=tree.nodes.new('ShaderNodeTexImage');tex.image=image;tree.nodes.active=tex
    return (tree,emission,tex,prior)

def bake_scalar(socket_name,tag,color=False):
    image=bpy.data.images.new('LR06_Atlas_'+tag,1024,1024,alpha=True,float_buffer=True)
    image.colorspace_settings.name='sRGB' if color else 'Non-Color'
    changed=[connected_output(mat,socket_name,image) for mat in materials[:2]]
    try:
        assert bpy.ops.object.bake(type='EMIT')=={'FINISHED'}
        vals=np.empty(1024*1024*4,dtype=np.float32);image.pixels.foreach_get(vals)
        path=out/('atlas_'+tag+'.png');image.filepath_raw=str(path);image.file_format='PNG';image.save()
    finally:
        for tree,emit,node,prior in changed:
            tree.nodes.remove(node);tree.nodes.remove(emit)
            for a,b in prior:tree.links.new(a,b)
    print('ATLAS_BAKED',tag,flush=True)
    return image,vals.reshape((-1,4))

base,base_pixels=bake_scalar('Base Color','basecolor',True)
rough,rough_pixels=bake_scalar('Roughness','roughness')
metal,metal_pixels=bake_scalar('Metallic','metallic')

normal=bpy.data.images.new('LR06_Atlas_Normal',1024,1024,alpha=True,float_buffer=True)
normal.colorspace_settings.name='Non-Color'
targets=[]
for mat in materials[:2]:
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=normal;mat.node_tree.nodes.active=node
    targets.append((mat.node_tree,node))
try:
    assert bpy.ops.object.bake(type='NORMAL',normal_space='TANGENT')=={'FINISHED'}
    normal.filepath_raw=str(out/'atlas_normal.exr');normal.file_format='OPEN_EXR';normal.save()
finally:
    for tree,node in targets:tree.nodes.remove(node)

# Blender's float image PNG export may premultiply RGB by alpha. URP requires
# independent metallic and smoothness channels, so pack the actual PNG samples.
pack_metal_smoothness(out)

# Probe a face centroid for each object. This catches a bake that cleared the
# first mesh while processing the second mesh into the same image.
probes=[]
for i,obj in enumerate(objects[:2]):
    face=max(obj.data.polygons,key=lambda p:p.area)
    uv=obj.data.uv_layers.active.data
    point=sum((uv[j].uv for j in face.loop_indices),start=uv[face.loop_indices[0]].uv*0)/len(face.loop_indices)
    x=min(1023,max(0,int(point.x*1024)));y=min(1023,max(0,int(point.y*1024)))
    actual=float(metal_pixels[y*1024+x,0]);expected=previous['objects'][i]['metallic']
    probes.append({'object':obj.name,'uv':list(point),'metallic':actual,'expected_metallic':expected})
    assert abs(actual-expected)<.015,(obj.name,actual,expected)

bpy.ops.object.select_all(action='DESELECT')
for obj in objects:obj.select_set(True)
bpy.context.view_layer.objects.active=objects[0]
fbx=out/'NR12_corridor_atlas_pilot.fbx'
assert bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'MESH'},
    use_mesh_modifiers=False,bake_anim=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y',
    global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_NONE',
    use_space_transform=True,bake_space_transform=False,use_triangles=True,path_mode='STRIP')=={'FINISHED'}

def record(path,color):
    return {'path':str(path.relative_to(root)),'colorspace':color,'bytes':path.stat().st_size,
            'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
shared={'basecolor':record(out/'atlas_basecolor.png','sRGB'),
        'roughness':record(out/'atlas_roughness.png','Non-Color'),
        'metallic':record(out/'atlas_metallic.png','Non-Color'),
        'mask':record(out/'atlas_metallic_smoothness.png','Non-Color'),
        'normal':record(out/'atlas_normal.exr','Non-Color')}
rows=[]
for i,entry in enumerate(previous['objects']):
    row=dict(entry)
    row['textures']=shared if i<2 else {}
    row['normal_status']='atlas_bake_needs_visual_comparison' if i<2 else 'constant'
    rows.append(row)
report={'status':'two_object_atlas_trial_not_unity_imported','source_sha256':previous['source_sha256'],
        'objects':rows,'fbx':record(fbx,None),'material_probe':probes,
        'notes':['Only generated sample UVs were replaced; no source UV or original blend changed.',
                 'No lighting was baked; two-object atlas is a scalability experiment.',
                 'Requires source/baked visual comparison and Unity validation before wider use.']}
(out/'PilotManifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print('@@JSON@@'+json.dumps({'status':report['status'],'probes':probes,'fbx':report['fbx']},ensure_ascii=False),flush=True)
