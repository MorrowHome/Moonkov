R24 -> R29 minimal LOCAL GROUND handoff

Scope: exactly six R27S local display meshes. NO R21 main/display Terrain material is supplied or bound. Retain the user's existing lunar Terrain textures/shader. This package neither changes TerrainData nor source DEM, collision or navigation.

DEFAULT DELIVERY: R29_Local_Ground_External_Materials_And_Textures.zip (<100MiB).
Extract its contents together; retain the Textures/ relative folder. It includes one lightweight Blender material library with external references and one physical copy of each PNG. The same PNGs serve Blender and Unity; do not duplicate them into another textures folder.
R29_Local_Ground_Three_Materials_External.blend is92,525 bytes and references17 of the22 provided images; the other5 are Unity smoothness/normal channel-ready variants.
Optional_R28/ contains the standalone small mask and formulas; disabled by default. No optional cloud full-Terrain application script is included.
The earlier packed library and split-purpose ZIPs are cloud rollback/staging artifacts only; NOT default transfer dependencies.

Exact default assignments (slot index zero-based):
R27S_01_Compacted_Work_Fronts_And_Broad_Spoil_Gradients: 0 Compacted
R27S_02_Stepped_Cut_Shoulder_And_Compressed_Faces: 0 Compacted,1 DustyClasts
R27S_03_Local_Grading_And_Plate_Seat_Traces: 0 FineDust
R27S_04B_Eighteen_Density_Graded_Regolith_Clods: 0 Compacted,1 DustyClasts,2 FineDust
R27S_04_Nine_Local_Cut_Rock_Slabs: 0 DustyClasts
R27S_05_Four_Anchored_Construction_Cover_Plates: existing R24_Dark_Forged_Joint_Steel, R24_Slate_Gray_Industrial_Enamel, R24_Exposed_Machined_Contact_Steel in slots0,1,2.
Full names/counts and per-object UVs are in R29_Local_Ground_Manifest.json.
Blender binding: apply_local_ground_blender.py defines apply_local_ground_materials(); it only binds the exact six R27S names and requires the three existing R24 steel materials. It never saves, renders, generates colliders or touches Terrain.

UV and world mapping for Unity display materials:
UV0 / Blender UVMap = dominant-axis physical metres. Do not pack, normalize or rescale these UVs. Main local map sampling=UV0/4m. Micro normal sampling=UV0/.25m.
UV1 / Blender R8_Regolith_3p7m = source worldXY/3.7m.
UV2 / Blender R8_Terrain_Global_4096m = source worldXY/4096m.
These additional UV channels are authored material data; do not overwrite them with automatic lightmap UV generation. Preserve/explicitly allocate another channel if lightmaps are required.
Blender uses worldXY/Z-up. Established Unity XYZ=Blender XZY once, so ground world texture sampling is Unity worldXZ.
World64mBase/rough/height-or-normal = worldXZ/64m; Macro256m=worldXZ/256m. Local Macro32m=worldXZ/32m.
Edge blend mask UV=(worldXZ-(1928,2038))/(186,140), CLIP outside bounds to zero. It is a local-mesh material blend control, not a mainTerrain override.
An explicit origin-offset adjustment is required if the imported scene is rebased; do not guess or alter mesh scale.

Texture channels:
BaseColor only is sRGB. Roughness, height, masks, macro maps, normal maps and MetallicGloss are linear.
Normal_OpenGL is tangent +Y, import Texture Type=Normal Map, Flip Green OFF; preserve/calculuate MikkTSpace tangents using UV0.
Roughness r -> smoothness 1-r. MetallicGloss RGB=0, A=smoothness. Do not connect roughness directly as smoothness.
Height16 is optional visual bump only; use the recorded min/max scale, never modify geometry/colliders.
Repeat tiled PBR/macro maps. Clamp and explicit outside-zero gate for the R26S edge mask. Mipmaps on, trilinear, anisotropy4–8.

The Blender materials combine world64m meso texture,256m macro gray variation, existing local4m grains and the world edge mask. Simply importing FBX/Blender into Unity does NOT reproduce these nodes. The per-layer contract and texture settings are supplied so a display-only Unity shader can be rebuilt. No target Unity shader compilation, scene binding or runtime validation has occurred. Do not reuse the old R26 simple importer as proof of R27 shader equivalence.

Existing local Terrain may look different from the cloud preview's backing terrain. These three materials are scoped only to the six display meshes; no authority is given to replace the underlying localTerrain to match them. If a seam requires tuning, blend local display layers into the user's existing surface with explicit approval rather than replacing its main shader.

R28 optional: current distant preview shows a very subtle effect. Keep its layer disabled until requested; its HLSL is only an optional local-input modulation function, not a shader replacement.

Exact per-layer color/normal/roughness formulas, ratio means and height scales are in R29_Local_Ground_Shader_Reconstruction.json. These are an explicit reconstruction contract, not a claim that a Unity shader already exists or runs.
