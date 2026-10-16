import json,statistics
from pathlib import Path
root=Path('Docs/Images/LilToonNative')
p=json.loads((root/'Performance.json').read_text())
s={}
for stage,name in [(0,'before'),(1,'after')]:
 a=[r for r in p['samples'] if r['stage']==stage]
 s[name]={'samples':len(a),**{k:statistics.median(r[k] for r in a) for k in ['submitMs','mainMs','gpuMs','frameMs','draws','setPass','triangles']}}
s.update({'comparison':'Fixed pose, 960x1080, same native-compatible shader, grade/exposure and lighting; 4 interleaved A/B blocks; 60 warmup +120 measured frames/block','gpuAvailable':False,'limitations':'Editor CPU timings include Editor activity; no standalone FPS or GPU cost conclusion. Both sets use the corrected outline and HDR specular hooks; their incremental GPU cost is not isolated.','dataMapResidentEditorBytes':7045872})
(root/'PerformanceSummary.json').write_text(json.dumps(s,ensure_ascii=False,indent=2))
