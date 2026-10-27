"""Run a narrow source QA probe. Never save or repair the delivered source."""
import bpy
import json
import sys
from pathlib import Path

OUT=Path(__file__).resolve().parent
sys.path.insert(0,'/Users/morrow/.codex/skills/blender-asset-qa/scripts')
import qa_check
import review_shots

targets=['WH01_Integrated_Seven_Rib_Wheel_Web_DISPLAY_ONLY',
         'HU01_Integrated_Stepped_Shaft_Two_Flat_Lockring_DISPLAY_ONLY']
report={'scope':'WH01/HU01 source probe only; not whole-map or Unity acceptance', 'assets':{}}
for name in targets:
    report['assets'][name]=qa_check.run(objects=[name],profile='game')
(OUT/'SourceQualityProbe.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
for mode in ['random','wire']:
    result=review_shots.run(objects=targets,out=str(OUT/('WH01_HU01_'+mode+'.png')),
                            views=['front','right','top','three_quarter'],mode=mode,res=384)
    report[mode]=result
(OUT/'SourceQualityProbe.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print('@@JSON@@'+json.dumps(report,ensure_ascii=False),flush=True)
