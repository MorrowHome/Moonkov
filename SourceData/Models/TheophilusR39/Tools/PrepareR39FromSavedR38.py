import bpy,sys,json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parent
PACKAGE=ROOT/'CompatWork511Recovered/R39'
OUT=ROOT/'PreparedR39Recovered'
OUTPUT=ROOT/'WorkBlends/Moonkov_R39_Display_Working.blend'
assert not OUT.exists() and not OUTPUT.exists()
previous=json.loads((ROOT/'PreparedR36R38Recovered/Manifest.json').read_text())
assert Path(bpy.data.filepath).resolve()==Path(previous['workingBlend'])
with Path(bpy.data.filepath).open('rb') as f:assert hashlib.file_digest(f,'sha256').hexdigest()==previous['workingBlendSHA256']
sys.path.insert(0,str(PACKAGE));sys.path.insert(0,str(ROOT))
import apply_r39_material_uv_patch as Apply
import r39_common as C
from NativeDisplayExport import export
spec=json.loads((PACKAGE/'R39_Deck0176_Material_UV_Patch.json').read_text())
ob=bpy.data.objects[spec['target_object']]
assert C.object_sig(ob)==spec['before_object_signature']
OUT.mkdir()
channels={u.name:0 if i==0 else i+1 for i,u in enumerate(ob.data.uv_layers)}
before=export(ob,OUT,'R39Baseline',channels)
Apply.apply()
assert C.object_sig(ob)==spec['after_object_signature']
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT),check_existing=True,relative_remap=True)
assert C.object_sig(ob)==spec['after_object_signature']
after=export(ob,OUT,'R39Patch',channels)
with OUTPUT.open('rb') as f:digest=hashlib.file_digest(f,'sha256').hexdigest()
result={'objects':[dict(after,kind='Patch')],'baseline':before,'patch':spec,'sourceWorkingBlend':previous['workingBlend'],'workingBlend':str(OUTPUT),'workingBlendSHA256':digest,'workingBlendBytes':OUTPUT.stat().st_size,'targetObject':ob.name,'sourceChangedTopFaces':1,'sourceChangedUVCoordinates':4,'newMaterials':1,'newTextures':4,'UnityChanged':False,'sceneBoundPatches':0,'geometryUnchanged':True,'nativeMeshCoordinateConvention':'Baked source world XZY minus (2015,2130,0); for future binding requires explicit Unity baseline mapping, not direct assignment to an arbitrary existing transform.'}
(OUT/'Manifest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('R39_PREPARED '+json.dumps({k:v for k,v in result.items() if k not in ['objects','baseline','patch']}))
