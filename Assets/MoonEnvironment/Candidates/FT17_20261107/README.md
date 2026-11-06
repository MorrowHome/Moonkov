# FT17 + FT14 isolated candidate — not a playable delivery

Source ZIP SHA256: `e65696770c1d7b4d23aaa81bd1a00a781ff5d6ebd989f514e65d4913a558aeb3`.
FT17 source SHA256: `151c6fc9bd19f42801f1c8db1c8dbe38b561dcfc4adb09c1ccaea98aaf374c03`.

All 15 FBX hashes match their export manifests. Unity imported 1487 uniquely named FT17 meshes. Each object was checked against its source world AABB after the same single Y=180 parent basis used for FT14; maximum corner error is 1.104 mm. Original matrices must not be reapplied; evaluated mesh vertices already contain world transforms.

`FT14_FT17_Collision_Candidate.unity` includes the saved FT14 candidate plus FT17: 3611 meshes and 2788 candidate MeshColliders in total. FT17 contributes 1470 static non-convex colliders; 594 selected floor/deck/landing centre rays hit their own colliders. Collision classification is provisional; the scene has no working gameplay installation and has not been walked with the actual player. Moving leaves, hatches, glass and three unresolved terrain cuts have no new FT17 collision. See `ExcludedFromCollision.txt`.

Source triangle total is 127968; Unity retains 127438, a reduction of 530 across nine meshes. Independent Blender reimport found zero-area triangles. For six door/window meshes, the nonzero triangle counts match exactly. For the three terrain cuts, surface-area relative differences are about 1.06e-6, 7.57e-6 and 2.34e-6, but nonzero triangle counts differ; per-face equivalence remains unresolved. Therefore these cuts are visual-only candidates, not accepted ground collision:

- FT09_Precise_Natural_North_Cut
- FT09_Preserved_Precise_Central_Low_Cut
- FT09_Preserved_Preserved_North_Stair_Cut

Do not overlay old FT09 with FT17 in a formal scene. No formal scene, old collider or gameplay binding was changed. No full-map installer ran. Original assets remain available for rollback.

Pending: large outer DEM, exact old-to-new/core replacement and route-anchor contract, light/keycap/button evaluated transforms, fixed-versus-switchable light ownership and actual Blender bake inputs. Procedural materials are not baked and FBX materials are placeholders. No old lightmap/reflection/SH cache is valid for these meshes merely by proximity. Windows light calibration and real-player route acceptance have not run.

Detailed readbacks and audit scripts: `E:/Code/Codex/2026-10-29/task/FT17_Staging/`. No Host, deployment, save/account, SubScene investigation, upload or push.
