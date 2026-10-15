R29 FINAL INDUSTRIAL MATERIAL / UV DELIVERY
Direct upgrade over the already-installed R21 + R23 + compact R24 display.
No R22 import. No sequential R25/R26/R27/R28 installation.

CONTENTS
7 final industrial materials, 21 unique real PNG files (one file per hash), one lightweight external-image Blender library, exact face/UV JSON and a guarded idempotent application script. This is not a full scene and includes no geometry objects, lights, cameras or colliders. Existing R23/R24 materials are referenced by their already-installed names and are not rebundled.

19 EXISTING OBJECTS
The exact final R29 material state changes 4639 faces across 19 existing source objects. Geometry/topology and existing UVMap hashes agree with R21+R23 sources and the compact R24 delta. R25/R26 new geometry and the R29 grillage replacement belong to the separate final-geometry package. The superseded original R21_04 grillage is deliberately excluded here.
The mapping contains per-face old/new MATERIAL NAMES, never full-scene old slot numbers. R24 compact slot orders therefore work. Only final-used new UV layers and loop rows are supplied. Superseded intermediate UV layers are omitted.

BLENDER SOURCE APPLICATION
Unzip while preserving the Textures subdirectory next to the .blend and JSON files.
Run before importing any new-geometry placeholder materials:
  import sys
  sys.path.insert(0, patch_dir)
  from apply_r29_industrial_final import apply_r29_industrial_final
  result = apply_r29_industrial_final(patch_dir)
The function loads all seven final industrial materials, validates every target before mutation, adds only necessary new UV layers and remaps exact faces. It does not save or render. The existing mesh datablock is retained and the object receives a copy with material/UV changes only. Running again skips all 19 already-applied objects.
A name collision with a non-final material is rejected, not silently overwritten. New geometry should reuse these loaded final material names and reuse existing R23/R24 basic materials according to the geometry manifest.
If only the seven material definitions are needed for the separate new-geometry package, use load_r29_industrial_materials(patch_dir) from the same script.

ACTUAL PNG / SHADER CONTRACT
The authoritative R29_Industrial_Final_Mapping.json identifies every image by SHA256, role, UV layer, colorspace and channel formula. The PNGs are actual textures, not shader-node substitutes.
- BaseColor: sRGB RGB.
- MetallicGloss: linear R=metallic, G=roughness, B=dust coverage, A=1-G.
- Normals: linear tangent-space OpenGL +Y; import as normal maps using the established target convention.
- R26-family micro detail: original metric UVMap * 2; normal strength0.24; roughness=clamp(atlas.G+(detail.R-0.5)*0.07,0,1), smoothness=1-roughness.
- R27-family: unique atlas UV; normal strength0.8; roughness=G and smoothness=A.
- R28-family: unique macro UV plus metric detail UV; encoded-normal composition and factors are explicit in each material record.

URP / MULTI-UV LIMITATIONS
This is not a ready-made Unity shader or an automatic Blender-node conversion. Recreate the documented texture channels and multi-UV expressions in the local compatible shader, or explicitly rebake for its supported channels. Allocate UV channels without overwriting existing lightmap UVs; the JSON layer names are logical source names, not promises that Unity UV1 is free.
Blender loop indices refer to mesh corners, not Unity vertex indices. Apply them through the verified source importer/exporter with exact topology correspondence. Do not paste these arrays directly into Unity mesh vertex arrays.
Normal tangents must be generated for the UV that actually samples that normal: R26 uses original metric UVMap; R27 uses its atlas UV; R28 macro and detail share tangent orientation. Tangents generated from another UV can give incorrect lighting. Preserve the established Blender-to-Unity axis conversion and mirrored handedness exactly once.
No local Unity shader compilation, render equivalence, gameplay, collider or performance validation is claimed.

VERIFICATION / ROLLBACK
One fresh Blender session recreated the 19 source objects from the untouched R24 rollback and compact R24 delta, applied all 4639 faces, then confirmed a second call skipped all 19 objects. All 21 external PNGs loaded, no final images packed in the light library. No scene save or render occurred.
The full R27/R29 source files and earlier material branches remain unchanged. Recoverable pre-patch mesh datablocks are retained; use the existing project backup for local rollback.
