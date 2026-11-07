# FT14 + FT17 + outer DEM isolated candidate

ZIP SHA256: `074f6468f6887c4343f93014b55ec74f51951478cede3d4e27d78f01d8b697b0`.

All 35 FBX files match the canonical partition QA hashes. No older unsplit DEM is included. Unity imported 35 named terrain meshes / 1122065 triangles, matching the cloud independent reimport count. Cloud documents 12 omitted pre-existing zero-area repeated-index triangles; original DEM bytes are unchanged. Maximum per-object AABB corner error after the common single Y=180 basis is 0.935 mm.

The combined candidate scene has 3646 meshes and 2823 candidate non-convex colliders. Saved scene references have been checked. Each outer DEM tile has one collider; old terrain and old colliders were not copied into this candidate or modified in the formal scene. The three unresolved small FT17 terrain cuts remain visual-only as documented in the FT17 candidate README.

Bounding-box-centre downward rays hit 29 of 35 outer tiles. A centre may be outside an irregular tile footprint; see ActualSurfaceRayChecks.tsv for the follow-up using an actual nondegenerate terrain triangle. Neither check validates continuous seams, staircase clearance or a real-player route. The scene is NOT a playable map acceptance.

The 388 evaluated interaction poses contain 118 EMPTY light anchors, 135 EMPTY button roots and 135 MESH keycaps. Current source positions, NOT embedded historical MC00 reference coordinates, must be used. The maximum old/current light position difference is 2750.839 m. CurrentWorldPositions_UnityXYZ.tsv applies only the known coordinate position conversion (x,y,z -> x,z,y); rotation/pivot and gameplay binding are NOT installed. Preserve source matrices for binding review; do not reapply source transforms to world-baked FBX vertices.

These are light anchors, not engine lights. Historical powers are not calibrated engine intensity. Fixed baked fill and switchable illumination require separate ownership; static baked direct light cannot be switched off by disabling a runtime Light. Old 135-button autonomous lighting is not re-enabled by this import.

Pending: V73 core visual geometry and exact replacement/semantic-anchor contract; native bake material/lighting inputs or an approved parameter reconstruction; relocation and safe reuse of existing eight airlock buttons, task devices, oxygen and routes; Windows baking with controlled exposure; actual player collision/route tests. No old irradiance, reflection or SH cache was reused for these candidate meshes.

Formal map, original DEM, user UI/graphics/settings changes and rollback assets remain intact. No Host, deployment, accounts/save, SubScene investigation, upload or push. Raw ZIP, bridge inputs and detailed audit evidence are in E:/Code/Codex/2026-10-29/task/DEM_Staging/.
