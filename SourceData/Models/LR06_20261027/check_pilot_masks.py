"""Check the actual 8-bit PNG bytes used by the URP metallic/smoothness masks."""
import json
import struct
import zlib
from pathlib import Path

root=Path(__file__).resolve().parent/'NR12Pilot_Corrected'
def rgba(path):
    data=Path(path).read_bytes()
    assert data[:8]==b'\x89PNG\r\n\x1a\n'
    offset=8;packed=[];width=height=None
    while offset<len(data):
        length=struct.unpack('>I',data[offset:offset+4])[0]
        kind=data[offset+4:offset+8];payload=data[offset+8:offset+8+length]
        if kind==b'IHDR':
            width,height,depth,kind_color,compression,fil,interlace=struct.unpack('>IIBBBBB',payload)
            assert (depth,kind_color,compression,fil,interlace)==(8,6,0,0,0)
        if kind==b'IDAT':packed.append(payload)
        if kind==b'IEND':break
        offset+=length+12
    raw=zlib.decompress(b''.join(packed));stride=width*4;out=bytearray();previous=bytearray(stride)
    def paeth(a,b,c):
        p=a+b-c;pa=abs(p-a);pb=abs(p-b);pc=abs(p-c)
        return a if pa<=pb and pa<=pc else b if pb<=pc else c
    for y in range(height):
        typ=raw[y*(stride+1)];current=bytearray(raw[y*(stride+1)+1:(y+1)*(stride+1)])
        for x in range(stride):
            a=current[x-4] if x>=4 else 0;b=previous[x];c=previous[x-4] if x>=4 else 0
            predict=(0,a,b,(a+b)//2,paeth(a,b,c))[typ]
            current[x]=(current[x]+predict)&255
        out.extend(current);previous=current
    return width,height,out

rows=[]
for i,metal in [(0,.015),(1,.43)]:
    w,h,rough=rgba(root/f'pilot_{i}_roughness.png')
    w2,h2,mask=rgba(root/f'pilot_{i}_metallic_smoothness.png')
    assert (w,h)==(w2,h2)
    pixels=[x for x in range(0,len(mask),4) if rough[x+3]>250]
    metallic_errors=[abs(mask[x]-round(metal*255)) for x in pixels]
    smooth_errors=[abs(mask[x+3]-(255-rough[x])) for x in pixels]
    row={'object_index':i,'compared_pixels':len(pixels),'metallic_max_error_8bit':max(metallic_errors,default=0),
         'smoothness_max_error_8bit':max(smooth_errors,default=0)}
    rows.append(row)
    assert pixels and row['metallic_max_error_8bit']<=1 and row['smoothness_max_error_8bit']<=1,row
(root/'MaskValidation.json').write_text(json.dumps(rows,indent=2))
print(json.dumps(rows))
