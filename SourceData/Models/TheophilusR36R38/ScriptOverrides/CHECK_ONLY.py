"""Read-only payload/baseline check. This is not a Windows import or runtime test."""
import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import supplement_common as C
D=C.mapping();C.runtime_guard();C.verify_payload(D);C.verify_baseline(D)
if C.MARKER in bpy.context.scene:C.verify_applied(D)
else:
    for k,names in D['expected_created'].items():
        for n in names:assert n not in getattr(bpy.data,k),'Existing/conflicting '+k+': '+n
print('R35_BASELINE_AND_PAYLOAD_MATCH. Read-only check; not Windows/Unity import verification.')
