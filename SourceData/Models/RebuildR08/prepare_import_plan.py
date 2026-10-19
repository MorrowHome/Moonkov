from pathlib import Path
import json, hashlib, re, shutil
ROOT=Path(__file__).resolve().parent
PROJECT=ROOT.parents[2]
native=json.loads((ROOT/'NativePrepared_v2'/'Manifest.json').read_text(encoding='utf-8'))
OUTPUT='Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/RebuildR08'
base='Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/DenseRingConceptB/Materials/'
paths={'B_Dark_Deck_And_Sealed_Cover':base+'Mat_03.mat','B_DeepGray_Structural_Steel':base+'Mat_01.mat',
       'B_GrayWhite_Rough_Cladding':base+'Mat_00.mat','B_Gray_Mineral_Support':base+'Mat_04.mat',
       'B_Restricted_Engineering_Orange':base+'Mat_02.mat',
       'R23_Matte_Deck_Fine_Finish':'Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/R23/Materials/R23_Matte_Deck_Fine_Finish.mat',
       'R24_Slate_Gray_Industrial_Enamel':'Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/R24/Materials/R24_Slate_Gray_Industrial_Enamel.mat',
       'R24_Compacted_Regolith_Trace':'Assets/MoonEnvironment/Variants/TheophilusBase/VisualPass01/SilverSand/SilverSand_R27S_Compacted_WorldContinuous_PBR.mat'}
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
gamma=lambda v:12.92*v if v<=0.0031308 else 1.055*v**(1/2.4)-0.055
bindings=[]
for material in native['materials']:
    name=material['source_material']; create=name not in paths
    path=paths.get(name,OUTPUT+'/Materials/'+name+'.mat')
    if create:assert name.startswith('R05_') and len(material['nodes'])==2
    principal=next(n['inputs'] for n in material['nodes'] if n['type']=='ShaderNodeBsdfPrincipled')
    color=[gamma(v) for v in principal['Base Color'][:3]]+[principal['Base Color'][3]]
    if name.startswith('B_'):
        text=(PROJECT/path).read_text(encoding='utf-8-sig')
        values=list(map(float,re.search(r'_BaseColor: \{r: ([^,]+), g: ([^,]+), b: ([^,]+),',text).groups()))
        assert max(abs(a-b) for a,b in zip(color,values))<1e-5
        assert abs(float(re.search(r'_Metallic: ([^\r\n]+)',text)[1])-principal['Metallic'])<1e-5
        assert abs(float(re.search(r'_Smoothness: ([^\r\n]+)',text)[1])-(1-principal['Roughness']))<1e-5
    bindings.append({'source':name,'path':path,'create':create,'baseColorGamma':color,
                     'metallic':principal['Metallic'],'smoothness':1-principal['Roughness'],
                     'preserveLocalLunar':name=='R24_Compacted_Regolith_Trace',
                     'beforeSHA256':None if create else sha(PROJECT/path)})
assert sum(b['create'] for b in bindings)==9
formal='Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity'
plan={'output':OUTPUT,'formal':formal,'formalBeforeSHA256':sha(PROJECT/formal),
      'nativeManifestSHA256':sha(ROOT/'NativePrepared_v2'/'Manifest.json'),
      'sourceSHA256':native['sourceSHA256'],'materials':bindings,'objects':native['objects'],
      'newMeshes':1271,'newMaterials':9,'reusedMaterials':8,'protectedLunarSlots':26,
      'application':'display-only, no inherited base replacement, no colliders/lights/scripts/navigation',
      'oldRail':'R04_N03_R08_Aerial_Landing_Right_Rail',
      'expectedOldRailMatchesFromSerializedFormalScene':0,
      'rollbackRoot':'REBUILD_R08_DISPLAY_ROOT',
      'protection':{'snapshot':'repository-after.json','existingFileHashesPreserved':True,
                    'noCloudLunarMaterialCreated':True,'sourceMaterialsOverwritten':False}}
(ROOT/'ImportPlan.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')
shutil.copyfile(PROJECT/formal,ROOT/'BeforeRebuildR08_Formal.unity')
print(json.dumps({k:plan[k] for k in ('output','formalBeforeSHA256','newMeshes','newMaterials','reusedMaterials','protectedLunarSlots')},indent=2))
