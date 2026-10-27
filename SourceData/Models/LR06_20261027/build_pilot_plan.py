"""Build a hash-bound import plan for the small NR12 pilot only."""
import hashlib
import json
from pathlib import Path

root=Path(__file__).resolve().parent
pilot=root/'NR12Pilot_Corrected'
manifest=json.loads((pilot/'PilotManifest.json').read_text())
qa=json.loads((pilot/'RoundTripQA.json').read_text())
masks=json.loads((pilot/'MaskManifest.json').read_text())
sources=json.loads((root/'LightConversionInventory.json').read_text())['lights']
def item(path,role,colorspace=None):
    p=Path(path)
    if not p.is_absolute():p=root/p
    return {'path':str(p.relative_to(root)),'role':role,'bytes':p.stat().st_size,
            'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'colorspace':colorspace}

assets=[item(manifest['fbx']['path'],'model')]
for obj in manifest['objects']:
    for key,value in obj['textures'].items():
        if key in {'normal_control','normal_strong_control'}:continue
        assets.append(item(value['path'],key,value['colorspace']))
for mask in masks:assets.append(item(mask['path'],'metallic_smoothness',mask['colorspace']))
lights=[]
for light in sources:
    if light['name'] in {'NR01_V5_SOUTH_TRANSFER_AREA','NR01_V5_NORTH_TRANSFER_AREA'}:
        lights.append(light)
assert len(lights)==2
for row in qa['objects']:
    assert row['max_coordinate_error_m']<0.003 and row['signed_volume_local_m3']>0
assert manifest['objects'][2]['source_material']=='LR06_NR01_V5_Transfer_Diffuser_Warm_LOCAL_ONLY'
result={'status':'awaiting_Unity_import_and_visual_validation',
        'source_sha256':manifest['source_sha256'],
        'scope':'one wall, one housing, one warm diffuser; source lights recorded for later conversion',
        'fbx_import':{'axis_forward':'-Z','axis_up':'Y','scale':1,'import_normals':True,
                      'calculate_tangents':True,'generate_collider':False,'import_lights':False},
        'coordinate_check':'For each model compare bounds with Blender (x,z,y); test identity and 180-degree Y correction. Do not save a scene or prefab on mismatch.',
        'materials':manifest['objects'],'assets':assets,'source_lights':lights,
        'notes':['URP Lit base color texture is sRGB. Metallic/smoothness mask and normal are linear.',
                 'Metallic/smoothness mask uses metallic in R and 1 minus Blender roughness in A.',
                 'The diffuser uses its source base color and HDR emission; it requires no UV.',
                 'Blender AREA-light power is not a directly transferable Unity intensity.',
                 'The normal bake responds to an exaggerated bump, but the real effect is sub-8-bit and remains visually unverified.',
                 'No baked-lighting or runtime switch integration is claimed.']}
(root/'PilotImportPlan.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print(json.dumps({'assets':len(assets),'lights':len(lights),'status':result['status']}))
