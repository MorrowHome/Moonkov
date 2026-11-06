# Verified-terrain candidate update

The three FT17 terrain cuts have passed exact geometric triangle-multiset comparison against the SHA-matched original binary FBX, including actual exported Unity world matrices. Missing triangles are 10 / 260 / 116, all exact zero-area triangles with repeated coordinate vertices. There are no missing positive-area triangle occurrences and no extra triangle occurrences. This check ignores index identity and winding; it is not a normals or complete physics acceptance.

`FT14_FT17_DEM_VerifiedTerrain_Candidate.unity` is a NEW copy; the original collision candidate and formal gameplay scene are unchanged. Three static, non-convex, non-trigger MeshColliders were added to this copy only. Total candidate collider count: 2826.

39 / 39 representative upward-facing support samples within the production character's 45-degree slope limit passed. An earlier diagnostic incorrectly used downward ground rays on near-vertical wall faces; it was retained locally and did not save a scene. Steep walls are not certified by the ground-support test. Full terrain seams and real-player routes remain unaccepted.

FT14 route evidence uses production capsule dimensions (height 1.7 m, radius .28 m, step .25 m, slope 45 degrees) with CharacterController.Move in EditMode. Start object: FT11_T1_Tread_000. End object: FT11_T1_Landing_020. 21 waypoints, 267 fixed-step movement calls, 10.117116 m travelled, no stall. The actual initial settled pose was not logged; the first recorded arrival is Tread001 at Unity XYZ (2317.741,212.3486,2052.477). Last recorded position is (2322.112,215.5697,2059.767). This is a capsule collision route check, not full player input, animation, a running raid or frame-time validation. No Host or profile store was started.

WindowsBakePreparation.json inventories 122 currently delivered FBX inputs with hashes and a controlled-exposure local pilot. It is preparation only: incomplete V73 core, source material/image associations and fixed/switchable lighting reconstruction remain pending. No bake or reuse of old caches is certified. Raw Unity dumps stay in the local workspace; no upload or push.
