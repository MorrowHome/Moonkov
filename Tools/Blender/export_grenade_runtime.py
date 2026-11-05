"""Read saved Blender grenade actions and retarget them without regenerating poses."""
import bpy
import hashlib
import json
import sys
from pathlib import Path
from mathutils import Matrix, Quaternion

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/Blender'))
from retarget_hatsune_miku import Rig,Retargeter,qmul,qinverse

SOURCE=ROOT/'ArtSource/DollSinger/GrenadeGestures.blend'
OUT=ROOT/'SourceData/Animations/Grenades'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene;rig=next(o for o in scene.objects if o.type=='ARMATURE')
bind=Rig(json.loads((ROOT/'Tools/Blender/DollSingerBind.json').read_text()))
tracks=Retargeter(bind,bind).arm_tracks
C=Matrix(((-1,0,0),(0,0,1),(0,-1,0)))
BR={b.name:(rig.matrix_world@b.matrix_local).to_quaternion() for b in rig.data.bones}
def xyzw(q):return (q.x,q.y,q.z,q.w)
motions={}
for kind,(start,end) in json.loads(scene['GrenadeMotionFrames']).items():
    values=[]
    for f in range(start,end+1):
        scene.frame_set(f);bpy.context.view_layer.update();world={}
        for n in bind.order:
            if n not in rig.pose.bones:continue
            current=(rig.matrix_world@rig.pose.bones[n].matrix).to_quaternion()
            delta=current@BR[n].inverted()
            q=(C@delta.to_matrix()@C.transposed()).to_quaternion()
            r=bind.r[n];world[n]=xyzw(q@Quaternion((r[3],r[0],r[1],r[2])))
        for n in tracks:
            values.extend(round(v,7) for v in qmul(qinverse(world[bind.parent[n]]),world[n]))
    motions[kind]=dict(frames=end-start+1,rotations=values)
source=dict(rig='DollSinger',source='ArtSource/DollSinger/GrenadeGestures.blend / compact grip and aim',
            sourceSha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),release=.55,bones=tracks,**motions)
(OUT/'DollSinger.json').write_text(json.dumps(source,separators=(',',':')))
reports=[]
for name in ('HatsuneMiku','Shian'):
    target=Rig(json.loads((ROOT/('Tools/Blender/'+name+'Bind.json')).read_text()))
    solver=Retargeter(bind,target);result=dict(source,rig=name,bones=solver.arm_tracks)
    for kind,motion in motions.items():
        result[kind]=dict(frames=motion['frames'],rotations=solver.samples(tracks,motion['rotations'],motion['frames']))
    (OUT/(name+'.json')).write_text(json.dumps(result,separators=(',',':')))
    reports.append(dict(rig=name,**solver.stats))
(OUT/'Export.json').write_text(json.dumps(dict(sourceSha256=source['sourceSha256'],rigs=reports),indent=2)+'\n')
print('@@JSON@@'+json.dumps(dict(source=source['source'],motions=len(motions),frames=sum(m['frames'] for m in motions.values()),rigs=reports)))
