# FT14 isolated candidate — not a playable delivery

Source ZIP SHA256: `9aa9be54bbd613562fe7c0f5bdb41699ac10684107707a005ca43bc0a44a649e`.
FBX SHA256: `2e716f3661b7fb2a456ebbce8a6e394693389da3a3675be08e43293f1b2e3895`.

Unity imported 2124 uniquely named meshes and 110156 triangles at metre scale. One parent Y=180 degree rotation converts the actual Unity FBX import to project coordinates (Blender x,y,z -> Unity x,z,y). Do not apply original object matrices again. Maximum per-object AABB corner error is 0.943 mm.

`FT14_Collision_Candidate.unity` is separate from the formal map. It contains 1318 static non-convex MeshColliders selected by structural names. 1146 floor/deck/landing centre rays hit their own candidate collider. This is a limited support check; it does not certify doorway clearance, stair continuity, arbitrary floor coverage, navigation or real-player traversal. Unclassified decoration remains without new collision. No existing collision or gameplay object was replaced.

Materials remain FBX import placeholders. Procedural source textures and fixed source lighting were not supplied. No lightmap, reflection, SH or source AgX cache has been reused for this new geometry. No baking or real-player route test has run.

V73's 118 lights are EMPTY anchors, not actual Blender/Unity lights. The 135-button mapping is historical source identity data, not a relocated FT14 gameplay mapping. Do not enable retired controllers or claim static baked light switches off. Preserve the formal map's eight working airlock buttons, mission bindings, oxygen and loot systems.

Original package and detailed evidence: `E:/Code/Codex/2026-10-29/task/FT14_Staging/`. Formal scene and active MainMenu were not saved or replaced. No Host, deployment, account/save or SubScene investigation ran. No upload or push.
