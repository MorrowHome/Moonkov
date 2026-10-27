"""Prepare existing LR06 PBR images for the independent Unity display scene."""
import hashlib
import json
import shutil
from pathlib import Path

base = Path(__file__).resolve().parent
project = base.parents[2]
source = json.loads((base / 'FullPreviewStaging/BlenderSourcePBR.json').read_text())
inventory = {row['source_image']: row for row in json.loads((base / 'PackedTextureManifest.json').read_text())['images']}
recipes = json.loads((base / 'FullPreviewStaging/UnityPlan.json').read_text())['materials']
indices = {row['name']: i for i, row in enumerate(recipes)}
textures = project / 'Assets/MoonEnvironment/Variants/LR06FullPreview/Textures'
textures.mkdir(parents=True, exist_ok=True)


def incoming(material, node, socket):
    matches = [link for link in material['links'] if link['to'] == node and link['input'] == socket]
    assert len(matches) == 1, (material['name'], node, socket, matches)
    return matches[0]


def copy_map(image_name, kind):
    info = inventory[image_name]
    assert info['colorspace'] == 'Non-Color', image_name
    src = base / info['path']
    assert hashlib.sha256(src.read_bytes()).hexdigest() == info['sha256'], image_name
    dst = textures / (info['sha256'] + '_' + kind + '.png')
    if dst.exists():
        assert hashlib.sha256(dst.read_bytes()).hexdigest() == info['sha256'], dst
    else:
        shutil.copyfile(src, dst)
    return {'source_image': image_name, 'source_sha256': info['sha256'],
            'asset_path': str(dst.relative_to(project)), 'source_size': info['size'],
            'source_colorspace': info['colorspace'], 'unity_max_size': 1024}


materials = []
for graph in source['materials']:
    nodes = {node['name']: node for node in graph['nodes']}
    bsdf = next(node for node in graph['nodes'] if node['type'] == 'BSDF_PRINCIPLED')
    base_node = nodes[incoming(graph, bsdf['name'], 'Base Color')['from']]
    normal_node = nodes[incoming(graph, bsdf['name'], 'Normal')['from']]
    assert normal_node['type'] == 'NORMAL_MAP' and normal_node['space'] == 'TANGENT' and normal_node['strength'] == 1
    normal_image = nodes[incoming(graph, normal_node['name'], 'Color')['from']]
    metallic_node = nodes[incoming(graph, bsdf['name'], 'Metallic')['from']]
    assert metallic_node['type'] == 'SEPARATE_COLOR'
    metallic_link = incoming(graph, bsdf['name'], 'Metallic')
    assert metallic_link['output'] == 'Red'
    mask_node = nodes[incoming(graph, metallic_node['name'], 'Color')['from']]
    rough_node = nodes[incoming(graph, bsdf['name'], 'Roughness')['from']]
    assert rough_node['type'] == 'MATH' and rough_node['operation'] == 'SUBTRACT'
    assert rough_node['inputs'][0]['value'] == 1 and not rough_node['inputs'][0]['linked']
    assert incoming(graph, rough_node['name'], 'Value')['from'] == mask_node['name']
    assert incoming(graph, rough_node['name'], 'Value')['output'] == 'Alpha'
    assert all(node['type'] == 'TEX_IMAGE' for node in (base_node, normal_image, mask_node))
    for node in (base_node, normal_image, mask_node):
        uv_node = nodes[incoming(graph, node['name'], 'Vector')['from']]
        assert uv_node['type'] == 'UVMAP' and uv_node['uv_map'] == 'UVMap'
    index = indices[graph['name']]
    assert recipes[index]['base_texture']['sha256'] == inventory[base_node['image']]['sha256']
    materials.append({'name': graph['name'], 'index': index,
                      'base_image': base_node['image'],
                      'base_asset_path': 'Assets/MoonEnvironment/Variants/LR06FullPreview/Textures/' + inventory[base_node['image']]['sha256'] + '.png',
                      'normal': copy_map(normal_image['image'], 'normal'),
                      'metallic_smoothness': copy_map(mask_node['image'], 'metallic_smoothness'),
                      'uv': 'UVMap / Unity UV0', 'normal_convention': 'tangent space, OpenGL +Y',
                      'channels': 'R=metallic, A=smoothness; Blender roughness=1-A'})

assert len(materials) == 7
result = {'scope': 'Source PBR images for existing independent LR06 display materials; no gameplay or light conversion.',
          'source_sha256': 'cdb003ad58e2c081076cc874eb9f1c4fdf3c216e056321257dabcf5d6f817054',
          'materials': materials}
(base / 'SharedPBRPlan.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
unique = {item[k]['asset_path'] for item in materials for k in ('normal', 'metallic_smoothness')}
print('Materials:', len(materials), 'unique copied PBR textures:', len(unique))
for material in materials: print(material['index'], material['name'])
