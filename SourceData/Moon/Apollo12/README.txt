Apollo 12 base-map candidate (2026-10-11)

Building version upgrades and reusable optimization menus:
Docs/BuildingOptimizationWorkflow.md (Chinese, relative to project root).

Source: LROC NAC_DTM_APOLLO12 v1.9, Apollo 12 mare landing-site DTM.
Official product: https://data.lroc.im-ldi.com/lroc/view_rdr/NAC_DTM_APOLLO12
Original TIF: 13,506,631 bytes; SHA-256:
d19ddf661a8675d1c3faed5e15574a220bb5976ce463034375cfefcde33f3103
The original PDS label is retained beside the TIF. Producer: LRO/LROC team,
Arizona State University. This is measured topography, not generated lunar art.

Reproduce data: python Tools/Terrain/prepare_apollo12_base.py
Dependencies: numpy, Pillow. The downloader verifies pinned source hashes.
Crop [736,112,1025,1025] uses native 2 m samples (2048 m square), flips TIFF
rows so Unity X is east and Z is north, and preserves 1:1 vertical scale.
An additive elevation offset keeps Unity heights positive; it is not scaling.
No missing samples are filled and no extra craters are generated.

Observed DEM relief: 35.244 m. Base footprint relief: 6.024 m.
Only the base footprint/apron is graded, with a 32 m smooth transition.
Maximum cut/fill: 1.755 / 5.251 m; changed region is about 3.75% of the map.
This construction grading is authored gameplay terrain, not observed data.
Both observed and graded float32 heights and TerrainData assets are retained.
The remaining terrain retains its natural craters and shallow undulations.

Unity assets: Assets/MoonEnvironment/Variants/Apollo12Base/
Open: Tools > Moon Environment > Apollo 12 > Open Base Preview
Scene: Scenes/MoonBaseApollo12.unity
Reusable environment: Prefabs/Apollo12Base.prefab
Terrain/Apollo12Observed.asset is ungraded; Apollo12BaseGraded.asset is used
by the scene. Source/dem.json records heights, offsets, hashes and grading.

Building asset: Moonkov_Expanded_Base_Latest (3).fbx, at original unit scale.
It remains a nested prefab with editable component objects. The presentation
plane and imported directional suns are disabled only on the new instance.
Imported model cameras remain disabled by the earlier importer fix.
The original FBX and original MoonGameScene/terrain/prefab are not rebuilt.

Indoor light compatibility: the FBX contains 239 Realtime Rectangle lights,
which URP cannot render as realtime area lighting. On this candidate's nested
model instance only, these become 130-degree realtime spots (80-degree inner
cone after calibration). The source intensity x 0.02 clamped to 2..24 is an
upper limit, also applied to the six original spots; manually dimmer values
are retained. This corrects six native spots previously left at 550/1100.
All fixtures use low-tier soft shadows to retain wall occlusion. Original positions,
directions, colors and source FBX remain unchanged. These values are a tunable
preview approximation, not a physical conversion of DCC watts. Check indoor
brightness, wall leakage and frame rate in actual rooms before acceptance.
Fifty-seven downward fixtures now use smaller fixed ranges fitted to floor
height, with a minimum of 8 m. This never follows the viewer's distance.
Sun-only automatic exposure is disabled: it previously treated artificially
lit interiors as darkness and could add +4 EV (16x). Exposure is fixed, with
a small authored ambient fill (0.025, 0.03, 0.04), not baked or realtime GI.

LocalLightBudget on "Base 3 / modular FBX" owns enabled state and intensity of
its 245 local fixtures during Play. Limit Lights By Distance is OFF by default:
powered lights retain authored intensity and shadows regardless of camera
distance. URP handles light influence culling. Sun and flashlight are unmanaged.
The previous nearest-20 strategy (plus 4 transition slots) is retained only as
an opt-in performance A/B. It visibly darkens rooms until the player approaches;
fading does not correct that visibility error. It is not the gameplay default.
Managed spots use the low 256px soft shadow tier and a 4096px additional-light
shadow atlas. Decorative caster simplification reduced building casters from
18,189 to 10,811 (another 3,502 nonstructural parts in the latest pass).
Geometry, colliders and detachability are retained. Structural
names are protected from the small-part rule. Prepare Selected Building reuses
this authoring pass for future exports. Full-lighting performance and
shadow-atlas behavior need runtime acceptance; no 4060 Laptop result is claimed.

Gameplay switches must call LocalLightBudget.SetPowered(light, on), rather than
writing Light.enabled/intensity. Power survives distance culling. During Play, use the
component context menu Runtime/All Lights Off or All Lights On to check this.
No wall interaction or network power replication is added. Disabling the
component restores current power states. For an A/B, toggle Limit Lights By
Distance at the same pose, let fades settle, and save separate capture reports.
Verify FPS at the previously slow position, dark/bright room switching, and that
a switched-off fixture remains off after walking away and back.

Laptop target (not yet hardware-validated): RTX 4060 Laptop, 2560x1440 output,
stable 60 FPS / approximately 16.67 ms frame budget. The candidate uses its own
Settings/Apollo12LaptopPipeline.asset. It renders at 75% scale (1920x1080 for
1440p output) with Unity STP temporal upscaling, MSAA off as required by STP,
4096px main shadows, 350 m shadow distance, four near-biased cascades and medium
soft shadow filtering. The original MoonPipeline is unchanged. STP requires
post processing, no camera stack, no dynamic resolution and a motion-vector
capable renderer; the current preview camera meets these configuration checks.
This is STP, not DLSS; no third-party upscaler or package has been installed.
At the previous 3840x2160 output, 75% scale instead renders 2880x1620 internally:
select 2560x1440 in Game view for the agreed target, and record 2560x1600
separately if that is the actual laptop panel resolution.

Manual verification: use the previously slow bright/shadow-heavy position and
walk the same room/doorway route. Check lighting transitions, distant sunlight
shadow fade, and STP trails around weapons, character silhouettes and fences.
Tools > Moon Environment > Apollo 12 > Capture Frame Times (10 seconds in Play)
starts a bounded optional capture after a 1-second refocus delay. It never enters
Play or changes quality; click Game again and follow the route for 10 seconds.
Output: Library/BuildValidation/Apollo12Performance.json (last capture only).
Reports resolution, GPU, pipeline, median/P95/P99 frame times and fraction over
16.67 ms. GPU timing is explicitly marked unavailable when no valid samples
exist; Main Thread includes waits and is not pure CPU workload. Editor sampling
may miss frames; confirm final acceptance in a standalone build on the actual
4060 laptop, plugged in, at the agreed resolution. Current connected GPU is a
4070 SUPER, so its results cannot certify the laptop's 60 FPS target. No Play
performance capture or target-device test was run during this change.

MC00 follow-up, 2026-10-11:
The user's 752-frame capture at 1440p / 75% STP on the 4070 SUPER reported
12.938 ms median, 21.821 ms P95, 24.295 ms P99, and 20.479% above 16.67 ms.
Main-thread median was 12.660 ms (includes waits), GPU median 4.506 ms.
This points toward CPU-side rendering/culling/submission for investigation,
but the original report cannot exclude GPU spikes on the slowest frames.
Baseline preserved in Library/BuildValidation/Apollo12Performance-before-MC00.json.

MC00_ROOT has 9,443 mesh renderers, 9,443 mesh colliders, 34 materials and
863,210 triangles; all renderers cast shadows and none were statically batched.
Exact vertex/index/layout/bounds matching found 6,083 duplicate meshes. The
new prefab now reuses 3,360 unique meshes while retaining all independent
objects, transforms, materials and collider references. No geometry is removed,
no objects are welded together, and no new meshes or FBX changes are needed.
MC00MeshReuseTools performs this editor-only pass for new candidate builds.

The candidate pipeline enables GPU Resident Drawer and GPU camera occlusion.
Its Forward+ renderer and Direct3D12 setup satisfy the checked prerequisites.
ProjectSettings/GraphicsSettings.asset keeps all BRG shader variants, a required
global prerequisite for this feature (affects build shader compilation/size;
it does not enable GPU drawing on the original map's pipeline). Size-based
small-mesh culling remains zero to avoid adding new detail pop-in. Runtime
activation and performance still need the user's Play capture and later a
standalone run on the actual laptop. No new automated Play run was performed.

The capture now adds GPU P95/P99, main-thread P95, GPU occlusion activation,
and the 12 slowest sampled frames with camera pose, draw/shadow/SetPass counts
and BRG draw counts. Recorder values may lag wall frame time; Editor drawing
counters can include other views. Keep Game focused and repeat the same route:
face MC00 outside, enter it, then turn inside. Check missing/flickering pieces
as well as frame pacing. First-use shader compilation is warm-up, so traverse
once before recording the comparable 10-second sample.

Preview: existing DollSinger local controller; WASD, mouse, Shift, Space.
Starts first person; MMB switches view. The small Preview assembly only selects
the starting view after camera initialization; it is not a network adapter.
Sun is intentionally fixed at 12 degrees to expose subtle relief in this local
preview. This is an authored lighting setup, not astronomical reconstruction.

This separate preview is for terrain/building placement and local walking.
Formal gameplay now uses Apollo12GameScene from MainMenu, sharing this same
environment prefab. Gameplay spawn, loot and extraction points are configured
separately; see Docs/MoonNetworkMap.md. The original map is retained.
Neither preview authoring checks nor compilation establish multiplayer or
whole-map performance acceptance.

The create menu refuses to overwrite an existing candidate scene/terrain.
Edit the candidate normally; retain originals when making another variation.
