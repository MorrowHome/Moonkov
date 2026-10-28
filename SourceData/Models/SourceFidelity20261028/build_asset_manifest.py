"""Bind source graph texture variants to the actual full-world material set."""
import pathlib,json,hashlib,re
ROOT=pathlib.Path(__file__).resolve().parent;PROJECT=ROOT.parents[2];WORK=PROJECT/'LocalData/SourceFidelity20261028/World';DEST='Assets/MoonEnvironment/Variants/SourceFidelity/World/'
compiled=json.loads((ROOT/'MaterialIR/CompiledMaterials.json').read_text());geometry=json.loads((WORK/'WorldGeometry.json').read_text());prepared=json.loads((WORK/'PreparedWorld.json').read_text());textures={};materials=[]
for row in compiled['materials']:
 if row['id'] not in geometry['materialIds']:continue
 bindings=[]
 for index,texture in enumerate(row['textures']):
  image=compiled['images'][texture['image']];key=hashlib.sha256((image['sha256']+image['colorspace']+texture['extension']+texture['interpolation']).encode()).hexdigest()[:24];extension=pathlib.Path(image['path']).suffix.lower();asset=DEST+'Textures/'+key+extension
  textures[key]={'source':str((ROOT/image['path']).relative_to(PROJECT)),'asset':asset,'srgb':image['colorspace']=='sRGB','wrap':texture['extension'],'sha256':image['sha256'],'maxSize':max(image['size'])}
  bindings.append({'property':'_SourceTex'+str(index),'asset':asset})
 name=re.sub(r'[^A-Za-z0-9_.-]','_',row['name'])
 materials.append({'id':row['id'],'name':row['name'],'shader':'MoonEnvironment/Source/'+f'{row["id"]:03d}','asset':DEST+'Materials/'+f'{row["id"]:03d}_'+name+'.mat','textures':bindings,'transparent':row['transparent']})
atlases=[{'id':row['id'],'size':row['size'],'sourceIrradiance':str((WORK/'Bakes'/f'Irradiance_{row["id"]:03d}.exr').relative_to(PROJECT)),'assetIrradiance':DEST+'Lighting/'+f'Irradiance_{row["id"]:03d}.exr','sourceStatistics':str((WORK/'Bakes'/f'SurfaceStatistics_{row["id"]:03d}.png').relative_to(PROJECT)),'assetStatistics':DEST+'Lighting/'+f'SurfaceStatistics_{row["id"]:03d}.png'} for row in prepared['atlases']]
selection_path=WORK/'SelectedLightingQuality.json'
if selection_path.exists():
 selection=json.loads(selection_path.read_text())
 if selection['sourceSha256']!=compiled['source_sha256']:raise ValueError('Selected lighting source mismatch')
 directory=(WORK/selection['directory']).resolve()
 if WORK.resolve() not in directory.parents:raise ValueError('Selected lighting must stay inside the world workspace')
 jobs={a['id']:a for a in atlases}
 for chosen in selection['atlases']:
  index=chosen['id'];job=jobs[index];checkpoint=directory/f'Irradiance_{index:03d}.json'
  if hashlib.sha256(checkpoint.read_bytes()).hexdigest()!=chosen['checkpointSha256']:raise ValueError('Selected lighting checkpoint changed')
  c=json.loads(checkpoint.read_text())['configuration']
  if c['sourceSha256']!=compiled['source_sha256'] or c['atlas']!=index or c['size']!=job['size']:raise ValueError('Selected lighting configuration mismatch')
  for field,filename,hashkey in [('sourceIrradiance',f'Irradiance_{index:03d}.exr','irradianceSha256'),('sourceStatistics',f'SurfaceStatistics_{index:03d}.png','statisticsSha256')]:
   path=directory/filename
   if hashlib.sha256(path.read_bytes()).hexdigest()!=chosen[hashkey]:raise ValueError('Selected lighting payload changed: '+filename)
   job[field]=str(path.relative_to(PROJECT))
manifest={'sourceSha256':compiled['source_sha256'],'textures':list(textures.values()),'materials':materials,'atlases':atlases,'vertices':geometry['vertices'],'triangles':geometry['triangles'],'chunkCount':len(geometry['chunks'])}
(WORK/'WorldAssetManifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2));print('RUNTIME_WORLD_ASSET_MANIFEST',len(textures),len(materials),len(atlases),len(geometry['chunks']))
