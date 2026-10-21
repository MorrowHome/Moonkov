R21 ring private face-coating display delta, 2026-10-21.
5 existing standalone renderers; 150 source faces = 300 exact local triangles.
Unique saved source component IDs and effective mesh SHA/indices were verified,
including face UV, normal/winding, original submesh0 and live non-batched state.
The five candidate FBX objects are NOT imported or instantiated.

Only a new URP Lit material and five display-mesh clones are created. Geometry,
vertex buffers, UV channels, normal/tangent/color attributes and global bounds
are unchanged. Non-target triangles retain order and old materials. The selected
triangles move into one appended private slot on each mesh. Submesh bounds are
explicitly calculated only on private clones. Metallic0, smoothness0.40; no maps.

Original mesh/material assets and colliders remain unchanged. Rollback must
restore both old MeshFilter and old renderer material references from
UnityApplication.json, or revert the scene-binding commit, before asset removal.

Saved data and read-only current target checks pass. Editor scene has a dirty
flag which is preserved. An auxiliary full scene-copy request was denied by
automatic approval review and was not executed or retried. No copy/clear/forced
save was used afterwards. Full Play/visual/performance acceptance was not run.

R08 collisions are not merged, R16 rods unchanged, R20/unpromoted candidates and
characters excluded. No Host/account/deployment/SubScene runtime/alien work.
Scripts retain exact task workspace paths; no new native meshbin source contract.
