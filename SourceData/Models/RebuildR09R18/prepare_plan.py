from pathlib import Path
import json,hashlib,shutil
p=Path(__file__).resolve().parent;r=Path(r'E:\UnityProjects\FPS_Template');n=json.loads((p/'NativePrepared/Manifest.json').read_text())
old=json.loads((r/'SourceData/Models/RebuildR08/ImportPlan.json').read_text());mapping={x['source']:x['path'] for x in old['materials']}
mapping['R24_Service_Stencil_Offwhite']='Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/R24/Materials/R24_Service_Stencil_Offwhite.mat'
equivalence={'R14_Coated_Cladding':'B_GrayWhite_Rough_Cladding','R14_Coated_Steel':'B_DeepGray_Structural_Steel','R14_Dark_Assembly':'B_Dark_Deck_And_Sealed_Cover','R14_Exposed_Alloy':'R05_exposed_alloy','R14_Mineral_Sill':'B_Gray_Mineral_Support','R14_Orange_Coating':'B_Restricted_Engineering_Orange'}
for a,b in equivalence.items():mapping[a]=mapping[b]
def sha(f):return hashlib.sha256(f.read_bytes()).hexdigest()
materials=[]
for x in n['materials']:
 name=x['source_material'];path=mapping[name];assert (r/path).is_file(),path
 materials.append({'source':name,'path':path,'create':False,'beforeSHA256':sha(r/path),'binding':'existing local identity' if name not in equivalence else 'existing local visual-category substitution; R14 compact maps not bound; R19 separate','substituteIdentity':equivalence.get(name,name)})
formal='Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity';output='Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/RebuildR09R18'
assert not (r/output).exists()
plan={'output':output,'formal':formal,'formalBeforeSHA256':sha(r/formal),'nativeManifestSHA256':sha(p/'NativePrepared/Manifest.json'),'sourceSHA256':n['sourceSHA256'],'materials':materials,'objects':n['objects'],'newMeshes':1629,'partitions':33,'triangles':121395,'newMaterials':0,'newTextures':0,'application':'display-only cumulative R09-R18; no R19/R20; no old renderer/collider/optimization changes','rollbackRoot':'REBUILD_R09_R18_DISPLAY_ROOT','R16SeamRepair':'Only apply if per-instance optimized renderer/submesh ownership is proven; otherwise record not applied. Gallery additions independent.','qualityGates':'Preserve exact QUALITY_GATES_EXACT.json; 8 collapsed-text UV objects / 2394 triangles, 3 tangent-review objects / 4 zero vectors, 4 near-degenerate objects / 9 triangles; not normal-map-ready certification.'}
(p/'ImportPlan.json').write_text(json.dumps(plan,indent=2),encoding='utf8');shutil.copyfile(r/formal,p/'BeforeFormal.unity')
print(json.dumps({'meshes':1629,'partitions':33,'triangles':121395,'sourceMaterialIdentities':len(materials),'existingLocalMaterialAssets':len(set(x['path'] for x in materials)),'newMaterials':0},indent=2))
