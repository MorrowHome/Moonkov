R19 accepted minimum display delta, 2026-10-20.
9 existing R18 wall display MeshFilter references and renderer material slots are
changed only after exact GX/partition/old native mesh validation. 43 additive
objects are in their separate prefab. Replacement FBX is never instantiated.
All old source Mesh assets, materials, colliders and transformations are retained.

4 new private URP Lit materials, 1 reused R05_exposed_alloy, 8 private textures.
BaseColor sRGB=true. Roughness and packed MetallicSmoothness sRGB=false.
NormalMap importer uses OpenGL +Y, flipGreen=false; UV0 authored scale, no extra
tiling. Packed RGB metallic=0, alpha=255-roughness_R proved for every pixel.
Smoothness multiplier=1 and metallic alpha channel are set explicitly.
Actual lit visual appearance remains unaccepted; static data is not performance.

R20 is rejected and excluded. R16 rods remain untouched. R08 collision commit is
not merged. No Play/Host/account/deployment/SubScene runtime/alien work is started.

Rollback R19 binding commit first; it restores all 9 old display mesh/material
references and removes the additive scene root. Optionally revert assets/source
commit afterwards. Detailed old references are in UnityApplication.json.
Scripts preserve exact task workspace paths for reproducibility; portable source
is the verified FBX ZIP. Native intermediates are not an upstream meshbin contract.
