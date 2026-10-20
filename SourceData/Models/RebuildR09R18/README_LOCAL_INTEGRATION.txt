R09-R18 cumulative display-only integration, 2026-10-20.
1629 meshes in 33 FBX partitions; 121395 triangles. The source archive is byte-verified.
Blender native preparation independently validates exact FBX local vertex hashes,
UV0/material identities and local pivots, then converts Blender XYZ to Unity XZY.
UV1 is reserved, normals retained, tangents regenerated in Unity. Source quality
gates are preserved; finite tangents do not certify normal-map quality.

15 source material identities bind to 9 EXISTING local materials. R14 image maps
are retained only inside the source archive for review and are not imported or
bound. R14 categories use documented existing local category substitutes.
No old materials, textures, shaders, DEM, colliders, lighting, culling, LOD or
batch meshes are replaced. No collider, light or runtime script is added.

R16 old-rod correction IS NOT APPLIED. A unique target instance was located,
but it contains no descendant renderers and no verified combined triangle /
submesh ownership map exists. Never globally hide the shared source names.
The additive R16 gallery is applied independently. R19 and R20 are excluded.

Current formal branch contains 0 R08 increment colliders. The prior collision
commit e0edffe3 is retained separately and was not merged during this display task.
Static checks do not certify play, traversal, builds, performance or final visuals.

Rollback: disable/remove only REBUILD_R09_R18_DISPLAY_ROOT, or revert only the
scene-binding task commit; optionally revert its preceding asset/source commit.
Do not reset unrelated work. Detailed evidence is in Docs/AI delivery JSON.

Scripts retain this task's exact workspace paths for reproducibility. Native binary
intermediates are generated in that workspace, not under Assets. They are not part
of the portable FBX contract; extract the archive and adjust script paths to rerun.
