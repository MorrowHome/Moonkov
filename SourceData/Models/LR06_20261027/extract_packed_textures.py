"""Extract packed source-image bytes by SHA256 outside Unity's Assets folder."""
import bpy
import hashlib
import json
from pathlib import Path
from collections import Counter

root=Path(__file__).resolve().parent
source=Path(bpy.data.filepath)
with source.open('rb') as f:
    h=hashlib.sha256()
    while chunk:=f.read(4*1024*1024):h.update(chunk)
assert h.hexdigest()=='cdb003ad58e2c081076cc874eb9f1c4fdf3c216e056321257dabcf5d6f817054'
out=root/'PackedTextures'
out.mkdir(exist_ok=True)
def ext(data):
    if data.startswith(b'\x89PNG\r\n\x1a\n'):return '.png'
    if data.startswith(b'\xff\xd8\xff'):return '.jpg'
    if data.startswith(b'\x76\x2f\x31\x01'):return '.exr'
    if data.startswith((b'II*\x00',b'MM\x00*')):return '.tif'
    if data.startswith(b'#?RADIANCE'):return '.hdr'
    return '.bin'
rows=[]
for image in bpy.data.images:
    assert image.packed_file, 'Missing packed image: '+image.name
    data=bytes(image.packed_file.data)
    digest=hashlib.sha256(data).hexdigest()
    target=out/(digest+ext(data))
    if target.exists():
        assert target.stat().st_size==len(data) and hashlib.sha256(target.read_bytes()).hexdigest()==digest
    else:
        with target.open('xb') as f:f.write(data)
    rows.append({'source_image':image.name,'path':str(target.relative_to(root)),'bytes':len(data),
                 'sha256':digest,'format':target.suffix,'size':list(image.size),
                 'colorspace':image.colorspace_settings.name,'alpha_mode':image.alpha_mode})
result={'source_sha256':h.hexdigest(),'source_unmodified':True,'source_images':len(rows),
        'unique_files':len({r['sha256'] for r in rows}),'formats':dict(Counter(r['format'] for r in rows)),
        'images':rows}
(root/'PackedTextureManifest.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print('@@JSON@@'+json.dumps({k:v for k,v in result.items() if k!='images'}),flush=True)
