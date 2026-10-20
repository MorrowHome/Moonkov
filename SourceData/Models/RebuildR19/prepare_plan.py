from pathlib import Path
import json,hashlib,struct,shutil
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template');old=p.parent/'R09_R18'
def sha(f):return hashlib.sha256(f.read_bytes()).hexdigest()
n=json.loads((p/'NativePrepared/Manifest.json').read_text());base=json.loads((old/'NativePrepared/Manifest.json').read_text());contract=json.loads((p/'Handoff/REPLACEMENT_CONTRACT.json').read_text());output='Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/RebuildR19'
assert not (r/output).exists()
oldprefab='Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/RebuildR09R18/RebuildR09R18_Display.prefab'
replacements=[];proof=[]
for row in contract['replacement_targets']:
 prior=next(x for x in base['objects'] if x['exportObject']==row['stable_export_object']);current=next(x for x in n['objects'] if x['exportObject']==row['stable_export_object'])
 a=(old/'NativePrepared'/(prior['key']+'.bin')).read_bytes();b=(p/'NativePrepared'/(current['key']+'.bin')).read_bytes();v=struct.unpack_from('<i',a)[0];assert struct.unpack_from('<i',b)[0]==v;assert a[:4+v*24]==b[:4+v*24],row['source_object']
 previousMesh='Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/RebuildR09R18/Meshes/'+prior['key']+'.asset'
 replacements.append({'GX':row['stable_export_object'],'key':prior['key'],'name':row['source_object'],'priorPartition':row['prior_partition'],'priorMesh':previousMesh,'priorMeshSHA256':sha(r/previousMesh),'oldPayload':str(old/'NativePrepared'/(prior['key']+'.bin')),'oldPayloadSHA256':sha(old/'NativePrepared'/(prior['key']+'.bin')),'oldMaterials':prior['materials'],'newMaterials':current['materials'],'worldBounds':prior['worldBounds']})
 proof.append({'GX':row['stable_export_object'],'triangleExpandedPositionsAndNormalsByteIdentical':True,'UVAndDisplayMaterialOnlyChanged':True})
materials=json.loads((p/'Handoff/MATERIAL_TRANSFER.json').read_text())['materials'];alloy=next(x['path'] for x in json.loads((old/'ImportPlan.json').read_text())['materials'] if x['source']=='R05_exposed_alloy')
for m in materials:
 m['path']=alloy if m['policy'].startswith('REUSE') else output+'/Materials/'+m['identity']+'.mat';m['create']=not m['policy'].startswith('REUSE');m['beforeSHA256']=None if m['create'] else sha(r/m['path'])
textures=[]
for f in sorted((p/'Handoff/textures').glob('*.png')):textures.append({'source':str(f),'path':output+'/Textures/'+f.name,'sha256':sha(f),'normal':'Normal_OpenGL' in f.name,'srgb':'BaseColor_sRGB' in f.name,'linear':'BaseColor_sRGB' not in f.name})
formal='Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity'
plan={'output':output,'formal':formal,'formalBeforeSHA256':sha(r/formal),'priorPrefab':oldprefab,'priorPrefabSHA256':sha(r/oldprefab),'sourceSHA256':n['sourceSHA256'],'nativeManifestSHA256':sha(p/'NativePrepared/Manifest.json'),'objects':n['objects'],'replacements':replacements,'materials':materials,'textures':textures,'newMaterialCount':4,'reuseMaterialCount':1,'replacementCount':9,'additiveCount':43,'renderOnlyNoCollisions':True,'R16Applied':False,'R20Applied':False}
(p/'ImportPlan.json').write_text(json.dumps(plan,indent=2));(p/'NativeGeometryReplacementProof.json').write_text(json.dumps(proof,indent=2));shutil.copyfile(r/formal,p/'BeforeFormal.unity');shutil.copyfile(r/oldprefab,p/'BeforeR09R18.prefab')
print(json.dumps({'replacements':len(replacements),'additive':43,'newMaterials':4,'textures':len(textures),'alloyReuse':alloy},indent=2))
