import bpy,sys,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent
PACKAGE=ROOT/'CompatWork511Recovered/R36_R38'
OUT=ROOT/'PreparedR36R38Recovered'
OUTPUT=ROOT/'WorkBlends/Moonkov_R38_Display_Working.blend'
assert not OUT.exists() and not OUTPUT.exists()
restore=json.loads((ROOT/'R35RecoveryReopen.json').read_text())
assert Path(bpy.data.filepath).resolve()==Path(restore['path'])
sys.path.insert(0,str(PACKAGE));sys.path.insert(0,str(ROOT))
import supplement_common as C
import apply_display_supplement as Apply
from NativeDisplayExport import export
assert C.ROOT.resolve()==PACKAGE
assert C.MARKER not in bpy.context.scene
receipt=Apply.apply()
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT),check_existing=True,relative_remap=True)
C.verify_applied(C.mapping())
OUT.mkdir()
rows=[]
channels={'UVMap':0,'R8_Regolith_3p7m':6,'R8_Terrain_Global_4096m':7,'R38_Metric_UV':0}
for rec in C.mapping()['added_objects']:
    ob=bpy.data.objects[rec['name']]
    if ob.type=='MESH':
        row=export(ob,OUT,'R36R38New_'+str(len(rows)).zfill(2),channels)
        row['kind']='New';rows.append(row)
assert len(rows)==10
C.verify_applied(C.mapping())
with OUTPUT.open('rb') as f:digest=hashlib.file_digest(f,'sha256').hexdigest()
result={'objects':rows,'receipt':receipt,'newMeshes':10,'newMaterials':0,'newTextures':0,'scenePatches':0,'UVChannels':channels,'lightmapUVChannelReserved':1,'workingBlend':str(OUTPUT),'workingBlendSHA256':digest,'workingBlendBytes':OUTPUT.stat().st_size,'originalR29Changed':False,'UnityChanged':False}
(OUT/'Manifest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('SUPPLEMENT_EXPORTED '+json.dumps({k:v for k,v in result.items() if k not in ['objects','receipt']}))
