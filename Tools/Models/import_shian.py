"""Convert the authorized Shian PMX to a plain Unity rig, preserving source art.

Blender --background --factory-startup --python-exit-code 1 --python
Tools/Models/import_shian.py -- --mmd-tools /path/to/blender_mmd_tools
The original PMX is never changed. The PMX@ header is normalized in a temporary
copy. D legs become the canonical Humanoid legs; their original bind stays intact.
"""
import argparse
import collections
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import tempfile
import zipfile

PROJECT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('miku_import', Path(__file__).with_name('import_hatsune_miku.py'))
miku = importlib.util.module_from_spec(spec)
spec.loader.exec_module(miku)


def main():
    import bpy
    import numpy as np
    from mathutils import Matrix, Vector
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path.home() / 'Downloads/诗岸/诗岸.pmx')
    parser.add_argument('--mmd-tools', type=Path, required=True)
    parser.add_argument('--height', type=float, default=1.65)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    sys.path.insert(0, str(Path.home() / '.codex/skills/blender-core/scripts'))
    import env_report
    print('BLENDER_ENV', json.dumps(env_report.run()))
    source = args.source.resolve()
    reader = miku.PmxReader(source)
    signature = reader.data[:4]
    assert signature in (b'PMX ', b'PMX@'), signature
    reader.data = b'PMX ' + reader.data[4:]
    parsed = reader.parse()
    by_name = {b['name']: b for b in parsed['bones']}
    lo = [min(v['position'][a] for v in parsed['vertices']) for a in range(3)]
    hi = [max(v['position'][a] for v in parsed['vertices']) for a in range(3)]
    scale = args.height / (hi[1] - lo[1])
    normalized_dir = Path(tempfile.mkdtemp(prefix='moonkov-shian-source-'))
    normalized = normalized_dir / source.name
    normalized.write_bytes(reader.data)
    (normalized_dir / 'textures').symlink_to(source.parent / 'textures', target_is_directory=True)
    addon = args.mmd_tools.resolve()
    sys.path.insert(0, str(addon))
    for wheel in sorted((addon / 'mmd_tools/wheels').glob('*.whl')):
        runtime = Path(tempfile.gettempdir()) / 'moonkov-miku-import' / wheel.stem
        runtime.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(wheel) as z: z.extractall(runtime)
        sys.path.insert(0, str(runtime))
    import mmd_tools
    mmd_tools.register()
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.ops.mmd_tools.import_model(filepath=str(normalized), scale=scale,
        types={'MESH', 'ARMATURE', 'PHYSICS', 'DISPLAY', 'MORPHS'},
        clean_model=False, remove_doubles=False, fix_bone_order=False,
        fix_ik_links=False, apply_bone_fixed_axis=False, rename_bones=False,
        import_adduv2_as_vertex_colors=True, save_log=False)
    original_mesh = next(o for o in bpy.data.objects if o.type == 'MESH' and len(o.data.vertices) == len(parsed['vertices']))
    original_rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    source_dir = PROJECT / 'ArtSource/Shian'
    source_dir.mkdir(parents=True, exist_ok=True)
    source_blend = source_dir / 'Shian.blend'
    # Record the exact user source and permission text alongside packed editable art.
    shutil.copyfile(source.parent / 'readme.txt', source_dir / 'readme.txt')
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(source_blend), compress=True, check_existing=False)
    model_dir = PROJECT / 'Assets/Shian/Art/Model'
    texture_dir = PROJECT / 'Assets/Shian/Art/Textures'
    model_dir.mkdir(parents=True, exist_ok=True); texture_dir.mkdir(parents=True, exist_ok=True)
    texture_info = []
    for index, texture in enumerate(parsed['textures']):
        origin = source.parent / texture.replace('\\', '/')
        if origin.exists():
            dest = texture_dir / origin.name
            shutil.copyfile(origin, dest)
            image = bpy.data.images.load(str(dest), check_existing=True)
            pixels = np.empty(len(image.pixels), dtype=np.float32)
            image.pixels.foreach_get(pixels)
            alpha_min = float(pixels[3::4].min()) if image.channels == 4 else 1.0
            alpha_max = float(pixels[3::4].max()) if image.channels == 4 else 1.0
            del pixels
            texture_info.append(dict(source=texture, asset=str(dest.relative_to(PROJECT)),
                sha256=hashlib.sha256(origin.read_bytes()).hexdigest(), width=image.size[0], height=image.size[1],
                hasAlpha=alpha_min < .999, alphaMin=alpha_min, alphaMax=alpha_max))
        else:
            assert not any(m['textureIndex'] == index or m['sphereIndex'] == index for m in parsed['materials'])
            texture_info.append(dict(source=texture, asset='', unusedMissing=True))
    # Promote actual weighted D legs. Only ordinary thigh has weights (21 each),
    # and its head is exactly coincident with D thigh. Other D bindings stay exact.
    weight_remap = {}
    for side in ('左', '右'):
        a, b = by_name[side + '足'], by_name[side + '足D']
        assert a['position'] == b['position']
        weight_remap[a['index']] = b['index']
    weighted = {weight_remap.get(i, i) for v in parsed['vertices'] for i,w in zip(v['indices'],v['weights']) if w > 0}
    keep = set(weighted)
    keep.update(b['index'] for b in parsed['bones'] if any(b['name'] == side + digit + '先' for side in ('左','右') for digit in ('人指','中指','薬指','小指','親指')))
    keep.update(b['index'] for b in parsed['bones'] if b['name'].startswith(('スカート_', 'hair.')))
    for index in list(keep):
        parent = parsed['bones'][index]['parent']
        while parent >= 0:
            keep.add(parent); parent = parsed['bones'][parent]['parent']
    names = {}
    for b in parsed['bones']:
        if b['index'] not in keep: continue
        name = b['name']
        if name in tuple(s + p + 'D' for s in ('左','右') for p in ('足','ひざ','足首')): name = name[:-1]
        names[b['name']] = miku.canonical_name(name)
    assert len(set(names.values())) == len(names)
    rig = bpy.data.objects.new('Shian_Rig', bpy.data.armatures.new('Shian_Rig'))
    bpy.context.collection.objects.link(rig); rig.matrix_world = original_rig.matrix_world.copy()
    bpy.context.view_layer.objects.active = rig; rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for b in parsed['bones']:
        if b['index'] not in keep: continue
        original = original_rig.data.bones[b['name']]
        edit = rig.data.edit_bones.new(names[b['name']])
        edit.matrix = original.matrix_local.copy(); edit.length = max(original.length, .001); edit.use_deform = True
    for b in parsed['bones']:
        if b['index'] not in keep or b['parent'] < 0: continue
        edit = rig.data.edit_bones[names[b['name']]]
        edit.parent = rig.data.edit_bones[names[parsed['bones'][b['parent']]['name']]]
        edit.use_connect = False
    # Existing fingertip markers are kept. Hair needs terminal markers so the
    # existing spring component can simulate its final weighted segment as well.
    tips = []
    children = collections.Counter(b['parent'] for b in parsed['bones'] if b['index'] in keep)
    for b in parsed['bones']:
        if not b['name'].startswith('hair.') or children[b['index']]: continue
        tail = b['tail']; assert not isinstance(tail, int)
        offset = Vector(tail).xzy * scale
        edit = rig.data.edit_bones.new(names[b['name']] + '_tip')
        edit.head = original_rig.data.bones[b['name']].head_local + offset
        edit.tail = edit.head + (offset.normalized() if offset.length > 1e-8 else Vector((0,0,-1))) * .008
        edit.parent = rig.data.edit_bones[names[b['name']]]; edit.use_deform = True
        tips.append(dict(name=edit.name, parent=names[b['name']], positionBlender=list(edit.head)))
    bpy.ops.object.mode_set(mode='OBJECT')
    mesh = bpy.data.objects.new('Shian_Body', original_mesh.data.copy()); bpy.context.collection.objects.link(mesh)
    mesh.matrix_world = original_mesh.matrix_world.copy(); mesh.parent = rig; mesh.matrix_parent_inverse = rig.matrix_world.inverted()
    mesh.data.name = 'Shian_Body'; mesh.vertex_groups.clear()
    groups = {b['index']:mesh.vertex_groups.new(name=names[b['name']]) for b in parsed['bones'] if b['index'] in keep}
    expected_weights = []
    for index,v in enumerate(parsed['vertices']):
        weights = collections.defaultdict(float)
        for i,w in zip(v['indices'],v['weights']):
            if w > 0: weights[weight_remap.get(i,i)] += w
        total = sum(weights.values()); weights = {i:w/total for i,w in weights.items()}
        expected_weights.append({groups[i].name:w for i,w in weights.items()})
        for i,w in weights.items(): groups[i].add([index],w,'REPLACE')
    wanted_morphs = {m['name'] for m in parsed['morphs'] if m['type']==1}
    if mesh.data.shape_keys:
        mesh.data.shape_keys.animation_data_clear()
        for key in list(mesh.data.shape_keys.key_blocks)[1:]:
            if key.name not in wanted_morphs: mesh.shape_key_remove(key)
            else: key.value = 0
    mod = mesh.modifiers.new('Shian_Skin','ARMATURE'); mod.object = rig
    materials = []
    for index,entry in enumerate(parsed['materials']):
        material = bpy.data.materials.new('Shian_' + str(index).zfill(2) + '_' + entry['name'])
        material.use_nodes = True
        tree=material.node_tree; tree.nodes.clear()
        out=tree.nodes.new('ShaderNodeOutputMaterial'); shader=tree.nodes.new('ShaderNodeBsdfPrincipled')
        shader.inputs['Roughness'].default_value=.85; tree.links.new(shader.outputs['BSDF'],out.inputs['Surface'])
        assert entry['textureIndex'] >= 0, 'Source material requires explicit untextured handling'
        tex=texture_info[entry['textureIndex']]
        image_node=tree.nodes.new('ShaderNodeTexImage'); image_node.image=bpy.data.images.load(str(PROJECT/tex['asset']),check_existing=True)
        tree.links.new(image_node.outputs['Color'],shader.inputs['Base Color'])
        if tex['hasAlpha']: tree.links.new(image_node.outputs['Alpha'],shader.inputs['Alpha'])
        material.diffuse_color=tuple(entry['diffuse']); material.use_backface_culling=not bool(entry['flags']&1)
        mesh.data.materials[index]=material
        role=('Hair' if entry['textureIndex']==8 else 'Face' if entry['textureIndex'] in (4,5,7,10) else 'Skin' if entry['textureIndex'] in (0,6,11) else 'Dress')
        material_entry = dict(entry)
        material_entry['sourceName'] = material_entry['name']
        material_entry['name'] = material.name
        semantic = ('Face' if entry['name'] == 'face_Baked' else 'Body' if entry['name'] == 'body.002_Baked' else 'Hair' if index == 9 or 30 <= index <= 38 else role + str(index).zfill(2))
        materials.append(dict(**material_entry, slot=index, semanticName=semantic, assetName=material.name, role=role, hasAlpha=tex['hasAlpha'],
            alphaMin=tex['alphaMin'], alphaMax=tex['alphaMax'], doubleSided=bool(entry['flags']&1)))
    for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
    rig.data.pose_position='REST'
    bpy.context.view_layer.update()
    fbx=model_dir/'Shian.fbx'
    bpy.ops.object.select_all(action='DESELECT'); mesh.select_set(True); rig.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(fbx),use_selection=True,object_types={'ARMATURE','MESH'},global_scale=1,
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_NONE',axis_forward='-Z',axis_up='Y',
        use_mesh_modifiers=False,mesh_smooth_type='OFF',add_leaf_bones=False,use_armature_deform_only=False,
        bake_anim=False,path_mode='RELATIVE',embed_textures=False)
    # A clean runtime source also supports repeatable independent QA/rendering.
    for obj in list(bpy.data.objects):
        if obj not in (mesh,rig): bpy.data.objects.remove(obj,do_unlink=True)
    runtime_blend=Path(tempfile.gettempdir())/'moonkov-shian-runtime.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(runtime_blend),check_existing=False)
    prior=set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=str(fbx),use_anim=False)
    delivered=[o for o in bpy.data.objects if o not in prior]
    dm=next(o for o in delivered if o.type=='MESH'); dr=next(o for o in delivered if o.type=='ARMATURE')
    assert len(dm.data.vertices)==len(parsed['vertices'])
    assert len(dm.data.polygons)==parsed['triangleCount']
    assert len(dr.data.bones)==len(rig.data.bones)
    assert {k.name for k in dm.data.shape_keys.key_blocks[1:]}==wanted_morphs
    max_w=max_p=max_s=max_b=0.0
    for i,(v,p) in enumerate(zip(dm.data.vertices,parsed['vertices'])):
        actual={dm.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>0}; expect=expected_weights[i]
        max_w=max(max_w,max(abs(actual.get(n,0)-expect.get(n,0)) for n in actual.keys()|expect.keys()))
        max_p=max(max_p,(dm.matrix_world@v.co-Vector(p['position']).xzy*scale).length)
    for name in wanted_morphs:
        for a,b in zip(mesh.data.shape_keys.key_blocks[name].data,dm.data.shape_keys.key_blocks[name].data):
            max_s=max(max_s,(mesh.matrix_world@a.co-dm.matrix_world@b.co).length)
    for b in rig.data.bones:
        max_b=max(max_b,(rig.matrix_world@b.head_local-dr.matrix_world@dr.data.bones[b.name].head_local).length)
    counts=collections.Counter(p.material_index for p in dm.data.polygons)
    assert [counts[i] for i in range(len(materials))]==[m['triangleCount'] for m in materials]
    assert max_w<1e-6 and max_p<1e-5 and max_s<1e-5 and max_b<1e-5,(max_w,max_p,max_s,max_b)
    def unity(p): return [-p[0]*scale,p[1]*scale,-p[2]*scale]
    manifest=dict(version=1, source=str(source),sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
        originalSignature=signature.decode(), normalizedHeaderOnlyInTemporaryCopy=True, targetHeight=args.height,scale=scale,
        sourceBlend=str(source_blend.relative_to(PROJECT)),fbx=str(fbx.relative_to(PROJECT)),
        addon=dict(repository=miku.ADDON_URL,revision=miku.ADDON_REVISION,version=mmd_tools.MMD_TOOLS_VERSION),blenderVersion=bpy.app.version_string,
        boundsUnity=dict(min=[-hi[0]*scale,lo[1]*scale,-hi[2]*scale],max=[-lo[0]*scale,hi[1]*scale,-lo[2]*scale]),
        coordinateConversion='Blender=(PMX.x,PMX.z,PMX.y)*scale; Unity=(-PMX.x,PMX.y,-PMX.z)*scale',
        grantPolicy='Source retains complete MMD setup. Runtime contains bind transforms, skin and vertex morphs only.',
        legPolicy='Weighted D legs promoted to canonical legs with their original bind unchanged; coincident ordinary thigh weights merged into D thigh and normalized.',
        weightRemap=[dict(source=parsed['bones'][i]['name'],target=names[parsed['bones'][j]['name']]) for i,j in weight_remap.items()],
        vertexCount=len(parsed['vertices']),triangleCount=parsed['triangleCount'],boneCount=len(rig.data.bones),
        vertexMorphs=[m for m in parsed['morphs'] if m['type']==1],materials=materials,textures=texture_info,
        bones=[dict(source=b['name'],canonical=names[b['name']],parent=names[parsed['bones'][b['parent']]['name']] if b['parent']>=0 else '',
            positionUnity=unity(b['position']),weightedVertexCount=b['weightedVertexCount'],originalGrant=b.get('grant')) for b in parsed['bones'] if b['index'] in keep],
        removedBones=[b['name'] for b in parsed['bones'] if b['index'] not in keep],tipBones=tips,
        hairRoots=[b['name'] for b in parsed['bones'] if b['name'].startswith('hair.') and parsed['bones'][b['parent']]['name']=='頭'],
        skirtRoots=['スカート_0_'+str(i) for i in range(16)],accessoryRoots=[],tieRoots=[],chestRoots=['胸上2.L','胸上2.R'],
        passiveChainBones=[],firstPersonHiddenBonePrefixes=['頭','目.','hair.'],
        originalRigidBodyCount=len(parsed['rigidBodies']),originalJointCount=len(parsed['joints']),
        fbxRoundTrip=dict(passed=True,vertexCount=len(dm.data.vertices),triangleCount=len(dm.data.polygons),boneCount=len(dr.data.bones),
            materialCount=len(materials),vertexMorphCount=len(wanted_morphs),maxWeightError=max_w,maxVertexErrorMetres=max_p,maxShapeErrorMetres=max_s,maxBoneErrorMetres=max_b))
    (model_dir/'ShianImport.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
    print('SHIAN_IMPORT_SUCCESS',json.dumps(manifest['fbxRoundTrip']))
    shutil.rmtree(normalized_dir)

if __name__=='__main__': main()
