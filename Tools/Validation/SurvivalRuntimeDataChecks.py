"""Verify source identity and lossless DollSinger bone/prop playback conversion."""
import hashlib,json,math,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/Blender'))
from retarget_hatsune_miku import Rig,add,sub,qrotate,qmul,dot,length
source=json.loads((ROOT/'SourceData/Animations/Survival/BlenderExport.json').read_text())
assert source['sourceSha256']==hashlib.sha256((ROOT/'ArtSource/DollSinger/SurvivalActions.blend').read_bytes()).hexdigest()
rig=Rig(json.loads((ROOT/'Tools/Blender/DollSingerBind.json').read_text()))
library=json.loads((ROOT/'Assets/Resources/Moonkov/Actions/DollSinger.json').read_text())
maximum_position=maximum_angle=0.0
samples=0
for src in source['motions']:
    code=src['items'][0] if src['items'] else ''
    motion=next(m for m in library['motions'] if m['action']==src['action'] and (not code or code in m.get('items',[])))
    for f in range(src['frames']):
        local={name:motion['rotations'][(f*len(library['bones'])+i)*4:(f*len(library['bones'])+i+1)*4] for i,name in enumerate(library['bones'])}
        positions,rotations=rig.evaluate(local=local)
        for i,name in enumerate(source['bones']):
            original=src['rotations'][(f*len(source['bones'])+i)*4:(f*len(source['bones'])+i+1)*4]
            # Dot after normalisation avoids mistaking rounded components for rotation loss.
            cosine=abs(dot(original,local[name]))/(length(original)*length(local[name]))
            maximum_angle=max(maximum_angle,math.degrees(2*math.acos(min(1,cosine))))
        if not code:continue
        for original,track in [(src['root'],motion['root'])]+[(p,next(t for t in motion['parts'] if t['name']==p['name'])) for p in src['parts']]:
            bone='手首.'+track['anchor'];point=add(positions[bone],qrotate(rotations[bone],track['positions'][f*3:f*3+3]));rotation=qmul(rotations[bone],track['rotations'][f*4:f*4+4]);expected=original['rotations'][f*4:f*4+4]
            maximum_position=max(maximum_position,length(sub(point,original['positions'][f*3:f*3+3])))
            maximum_angle=max(maximum_angle,math.degrees(2*math.acos(min(1,abs(dot(rotation,expected))/(length(rotation)*length(expected))))))
            samples+=1
assert maximum_position<.0005,maximum_position
assert maximum_angle<.001,maximum_angle
result={'sourceSha256':source['sourceSha256'],'result':'PASS','boneFrames':724,'propSamples':samples,'maximumPositionMetres':maximum_position,'maximumRotationDegrees':maximum_angle}
(ROOT/'SourceData/Animations/Survival/RuntimeSourceCheck.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result))
