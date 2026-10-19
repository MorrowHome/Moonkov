Moonkov Rebuild R08 display integration, 2026-10-19

Locked full editable source SHA256:
b56a88516f3bb1ddc34ab9c109ccbe0ff9cf46cfc78df8685bcfeb7368742034
Full editable source is retained in the task R08/downloads folder and its private Drive backup.
The source zip here contains only 31 display-increment FBXs and their original audit/mapping files.
It does not replace the optimized base or contain R09.

Delivered: 1271 independent Mesh assets, 31 prefab groups, 279994 triangles.
Native mesh payloads preserve source local pivots and world placement using Blender XYZ -> Unity XZY,
reversed winding, original split normals/UV0, and Unity-generated tangents. UV1 remains reserved.
FBX local vertex bytes were checked exactly. Export-mesh bounds drift <=0.397mm;
Unity placement drift <=0.016mm relative to converted payloads.

Material policy: reuse 5 existing simple baseline PBR materials by verified color/metallic/smoothness
signatures, reuse existing R23 deck and R24 industrial enamel, and create 9 separate simple R05 Lit
materials. All 26 protected lunar slots use the current formal-scene compacted-ground material,
VisualPass01/SilverSand/SilverSand_R27S_Compacted_WorldContinuous_PBR.mat.
No cloud R24_Compacted_Regolith_Trace material/settings/texture is installed or overwritten.
The two existing terrain material references remain TheophilusOriginalRegolith.mat, unchanged.

Only new display geometry is installed. Existing colliders, terrain data/DEM, shaders/textures,
lighting/baked circuit data, static clusters, culling, rock LOD, UI, 118 light/135 button IDs remain.
No prior R04 rail exists in this Unity baseline; none is disabled. The archived old rail and R03
replacement are excluded from the exported increments. No inherited base is overlaid.

Formal scene: Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity
Root: REBUILD_R08_DISPLAY_ROOT
North entry toe: Unity world (1880.31,240.57,2431.21)
West continuous approach: Unity world (1825.25,167.70,2082.00)

Rollback: disable/remove only that added root, or revert the dedicated local scene-binding commit.
The separate asset commit can then be reverted if desired. Never reset the user's font edit.
Unity normalized an already-existing maximumBosses default to 1 while saving; this unrelated line
is excluded from the task scene commit and remains an unstaged worktree serialization difference.

Reproduction in an isolated checkout with the output folder absent:
1. Extract the zip into DisplayHandoff beneath this directory.
2. Run Blender 5.1.1 --background --factory-startup --disable-autoexec --python prepare_native_r08.py.
3. Run python prepare_import_plan.py against the intended clean formal-scene baseline.
4. Execute StageR08.cs through the existing Unity Pipeline eval_file as a detached job; query its
   job result and UnityIndependentImport.json. FinishStageR08.cs reloads already-created assets if
   prefab creation needs a confirmed resume. Do not rerun uncertain writes or overwrite output.
5. BindR08.cs checks current scene, existing component hashes and material bytes before saving.
   VerifyFormalR08.cs is read-only verification plus Scene view selection/framing.

Validation is static Editor/import/serialization validation. No Play, Host, accounts, persistence,
deployment, SubScene runtime troubleshooting, alien feature work, collision/nav additions,
target-device benchmark, or final visual-parity acceptance was performed.
