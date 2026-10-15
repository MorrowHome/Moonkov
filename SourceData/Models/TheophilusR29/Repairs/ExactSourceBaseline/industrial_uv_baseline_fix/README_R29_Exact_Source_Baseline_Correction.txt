R29 EXACT SOURCE BASELINE CORRECTION
Read-only preflight first. Application remains a separate explicit action.
No new art, textures, models or replacement source UV coordinates.

TWO VERIFIED CAUSES
1. The original industrial package took 16 source UVMap hashes from the cloud-native R24 rollback instead of reloading the officially delivered six-decimal R23 JSON into float32. All 16 correct hashes were independently reconstructed from original R21 plus exact R23_R21_Metric_UV_Overrides.json. The relayed local read-only hashes match all 16. Unlisted loops were checked unchanged. Three compact-R24 guards remain unchanged.
2. Nine local objects use the exact material alias R13_Machined_Steel_Contact.001 on otherwise unchanged machined-contact faces. The affected old/new material-change faces already match. Complete restored-name hashes differ because they include this alias on unaffected faces. DATA and effective OBJECT names were reported equal. No generic suffix normalization is permitted.

IMPORTANT CORRECTION TO THE OLD REPORT
The original industrial package reopen report used cloud-native UV values. It is not proof that the officially delivered serialized R23 source baseline passed. This package provides the corrected test and leaves the original archive unchanged.
The old top-level APPLY_IN_BLENDER_SOURCE_ONLY.py still calls the OLD industrial entry. DO NOT resume that top-level script unchanged. Use the corrected preflight and explicit industrial application below, then continue the separate original geometry steps in the established order.

STRICT EQUIVALENCE RULE
Only R13_Machined_Steel_Contact.001 is allowed, only on the nine explicitly listed objects, and only where the authoritative full per-face list expects R13_Machined_Steel_Contact.
Before accepting it, runtime verification compares its complete shader-node types, socket values, relevant node properties, connections, material scalar values and real image-content SHA256 with the authoritative R13 definition exported from the accepted original R21 source. Names, UI layout and texture paths do not determine equivalence; shader values and image content do. Any missing or different authority/content fails closed. Equality with a possibly changed local canonical material is NOT sufficient.
All 47,912 source face names are checked individually against the authoritative source list. Only then is an equivalent restored-name hash calculated in memory for the original strict guard. No actual material name, slot, shared shader or unaffected face is changed.

ORIGINAL PACKAGE BINDING
The correction verifies the original industrial mapping, original entry script and original final face/new-UV JSON SHA256. The R23 handoff/UV JSON hashes and authority-list/fingerprint-helper hashes are bound in the correction JSON. The original 90 MiB industrial ZIP remains byte-identical.
Only the 16 expected source UV hashes and the explicitly proven equivalent restored-name hashes change in memory. Original topology, vertex coordinates, transforms, complete source UVMap, exact affected-face old/new material names and final new-UV checks remain enforced. No tolerance widening, source UV overwrite or fallback to the wrong cloud baseline exists.

READ-ONLY PREFLIGHT
Keep the original industrial_patch folder intact. Extract this correction into its own folder.
In the source Blender session:
  import sys
  sys.path.insert(0, correction_folder)
  from apply_r29_with_r23_serialized_uv_guard import preflight_r29_source_baseline
  report = preflight_r29_source_baseline(industrial_folder, correction_folder)
This only reads scene data and verifies the real external texture/library files. It does not append materials, add UVs, alter renderer states, apply objects or save.
The script's command-line default is also read-only:
  blender source.blend -P apply_r29_with_r23_serialized_uv_guard.py -- /path/to/industrial_patch

EXPLICIT INDUSTRIAL APPLICATION AFTER PREFLIGHT IS ACCEPTED
  from apply_r29_with_r23_serialized_uv_guard import apply_r29_with_r23_serialized_uv_guard
  result = apply_r29_with_r23_serialized_uv_guard(industrial_folder, correction_folder)
Or add --apply to the command-line invocation.
This delegates to the unchanged original industrial operation after corrected strict preflight. It applies 19 objects / 4,639 changed faces. Repeating skips already-applied objects. Continue the separate geometry steps afterward; do not invoke the old top-level industrial call again.

TEST STATUS
Cloud reconstruction: original R21 + official R23 serialized UV/material mapping + compact R24; the exact .001 alias was reproduced on the nine reported objects.
Read-only preflight produced zero material/slot/UV changes. A deliberately changed alias scalar was rejected. Correct application succeeded for all 19 objects; the second call skipped all 19. Original UVMap hashes, all unaffected face material names and the shared R13 alias name/fingerprint remained unchanged. No rendering or scene save occurred.
The cloud-authoritative fingerprint and reconstructed test do NOT establish the real local .001 fingerprint. Local read-only preflight must still confirm that exact definition before application. Any mismatch remains blocked for diagnosis.
