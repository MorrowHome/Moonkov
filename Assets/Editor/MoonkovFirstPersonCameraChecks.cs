using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MP_FPS.DollSinger;

/// <summary>Isolated owner-mesh, actual near-plane and animated-motion checks. Never opens Play.</summary>
public static class MoonkovFirstPersonCameraChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    [MenuItem("Tools/Moonkov/Check First Person Camera")]
    public static void Run() => Debug.Log(RunReport() + "\n" + CheckMotion());

    public static string RunReport()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run camera checks in Edit Mode.");
        int cases = 0, rays = 0, chestHits = 0;
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            foreach (var path in new[] { "Assets/HatsuneMiku/Prefabs/HatsuneMikuPlayer.prefab",
                "Assets/DollSinger/Prefabs/DollSingerPlayer.prefab", "Assets/Shian/Prefabs/ShianPlayer.prefab" })
            {
                var scene = EditorSceneManager.NewPreviewScene();
                var baked = new Mesh(); GameObject root = null; DollSingerView view = null;
                try
                {
                    root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                    foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
                    foreach (var c in root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                    var animator = root.GetComponentInChildren<Animator>();
                    var clips = animator.runtimeAnimatorController.animationClips.Distinct().Where(c => new[] {
                        "Unarmed_Idle", "Walk_N", "DollSinger_Run_N_RelaxedFingers", "JumpLand", "Crouch_Idle", "Prone_Crawl" }.Contains(c.name)).ToArray();
                    animator.runtimeAnimatorController = null; animator.Rebind();
                    view = root.GetComponentInChildren<DollSingerView>(true);
                    var type = typeof(DollSingerView);
                    var renderer = (SkinnedMeshRenderer)type.GetMethod("FindBodyRenderer", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { root });
                    var source = renderer.sharedMesh; var original = source.vertices;
                    view.BindPlayer(root, true); view.SetFirstPerson(true);
                    var owner = (Mesh)type.GetField("firstPersonMesh", Private).GetValue(view);
                    Require(owner && owner != source && owner.vertexCount == source.vertexCount, "Missing owner mesh: " + path);
                    Require(owner.blendShapeCount == source.blendShapeCount && source.vertices.SequenceEqual(original), "World mesh or blend shape indexing changed: " + path);
                    renderer.sharedMesh = owner;
                    var transforms = root.GetComponentsInChildren<Transform>(true);
                    var positions = transforms.Select(t => t.localPosition).ToArray();
                    var rotations = transforms.Select(t => t.localRotation).ToArray();
                    var scales = transforms.Select(t => t.localScale).ToArray();
                    var head = animator.GetBoneTransform(HumanBodyBones.Head);
                    Quaternion headBind = Quaternion.Inverse(root.transform.rotation) * head.rotation;
                    var solve = type.GetMethod("GetFirstPersonEyePosition", Private);
                    var collider = renderer.gameObject.AddComponent<MeshCollider>();
                    void Pose(AnimationClip clip, float phase, float pitch, float yaw)
                    {
                        for (int i = 0; i < transforms.Length; i++)
                        {
                            transforms[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                            transforms[i].localScale = scales[i];
                        }
                        clip.SampleAnimation(root, clip.length * phase);
                        view.transform.rotation = root.transform.rotation * Quaternion.Euler(pitch, yaw, 0);
                        head.rotation = view.transform.rotation * headBind;
                        // Imported MMD renderers carry 100x scale. Unity's scaled bake
                        // matches their actual rendered mesh in the renderer's space.
                        collider.sharedMesh = null; renderer.BakeMesh(baked, true); collider.sharedMesh = baked;
                        Physics.SyncTransforms();
                    }
                    void CheckPlane(Quaternion rotation, string label)
                    {
                        view.transform.rotation = rotation;
                        Vector3 eye = (Vector3)solve.Invoke(view, null);
                        float h = view.firstPersonNearClip * Mathf.Tan(view.firstPersonFieldOfView * .5f * Mathf.Deg2Rad);
                        for (int x = -1; x <= 1; x++)
                        for (int y = -1; y <= 1; y++)
                        {
                            Vector3 offset = rotation * new Vector3(x * h * view.camera.aspect, y * h, view.firstPersonNearClip);
                            var ray = new Ray(eye, offset.normalized);
                            Require(!collider.Raycast(ray, out var hit, offset.magnitude + .002f),
                                "Near plane intersects owner body: " + label + " ray=" + x + "," + y + " distance=" + hit.distance);
                            rays++;
                        }
                        Require(float.IsFinite(eye.x) && float.IsFinite(eye.y) && float.IsFinite(eye.z), "Nonfinite eye: " + label);
                        cases++;
                    }
                    view.camera.aspect = 16f / 9;
                    foreach (var clip in clips)
                    foreach (float time in new[] { 0f, .33f, .66f })
                    foreach (float pitch in new[] { -60f, 0f, 45f, 80f })
                    foreach (float yaw in new[] { -70f, 0f, 70f })
                    {
                        Pose(clip, time, pitch, yaw);
                        Quaternion look = view.transform.rotation;
                        foreach (float roll in new[] { -15f, 0f, 15f })
                            CheckPlane(look * Quaternion.Euler(0, 0, roll), path + "/" + clip.name + "/" + pitch + "/" + yaw + "/" + roll);
                    }
                    var idle = clips.First(c => c.name == "Unarmed_Idle");
                    foreach (float fov in new[] { 40f, 75f, 100f })
                    foreach (float aspect in new[] { 16f / 9, 21f / 9 })
                    {
                        Pose(idle, .25f, 80, 0); view.firstPersonFieldOfView = fov; view.camera.aspect = aspect;
                        CheckPlane(view.transform.rotation, path + "/projection/" + fov + "/" + aspect);
                    }
                    view.firstPersonFieldOfView = 75; view.camera.aspect = 16f / 9;
                    Pose(idle, .25f, 80, 0);
                    var position = (Vector3)solve.Invoke(view, null);
                    int visibleChest = 0;
                    var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
                    var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                    for (int x = -1; x <= 1; x++)
                    for (int y = 0; y < 5; y++)
                    {
                        // A ray fan through the lower/central image must see actual chest skin/clothing.
                        Vector3 direction = view.transform.rotation * new Vector3(x * .25f, -.6f + y * .2f, 1);
                        if (!collider.Raycast(new Ray(position, direction.normalized), out var hit, 1.2f)) continue;
                        float height = Vector3.Dot(hit.point - chest.position, root.transform.up);
                        if (height >= -.05f && hit.point.y <= neck.position.y + .025f) visibleChest++;
                    }
                    Require(visibleChest >= 3, "Chest absent from downward image: " + path + " hits=" + visibleChest);
                    chestHits += visibleChest;
                    view.SetFirstPerson(false);
                    type.GetMethod("SetCameraMesh", Private).Invoke(view, new object[] { false });
                }
                finally
                {
                    if (view) view.BindPlayer(null, true);
                    if (root) UnityEngine.Object.DestroyImmediate(root);
                    UnityEngine.Object.DestroyImmediate(baked); EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }
        finally { Physics.queriesHitBackfaces = backfaces; }
        return "PASS owner camera: " + cases + " animated/projection poses, " + rays + " actual near-plane rays, " + chestHits
            + " downward chest hits across three characters; world mesh and shape indices preserved. No Play/network session.";
    }

    public static string CheckMotion()
    {
        var type = typeof(DollSingerView).Assembly.GetType("Unity.MP_FPS.DollSinger.DollSingerCameraMotion", true);
        var step = type.GetMethod("Step"); var reset = type.GetMethod("Reset");
        var spans = new System.Collections.Generic.List<float>();
        foreach (int hz in new[] { 30, 60, 144 })
        foreach (float aim in new[] { 0f, 1f })
        {
            var motion = Activator.CreateInstance(type);
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int i = 0; i < hz * 6; i++)
            {
                float t = i / (float)hz;
                var input = new Vector3(Mathf.Sin(t * Mathf.PI * 3) * .05f, 1.4f + Mathf.Sin(t * Mathf.PI * 6) * .05f, .03f);
                var offset = (Vector3)step.Invoke(motion, new object[] { input, true, aim, new Vector3(.2f, .35f, .35f), .22f, 1f / hz });
                Require(Mathf.Abs(offset.x) <= .0451f && Mathf.Abs(offset.y) <= .0551f, "Unbounded gait correction");
                if (i > hz * 2) { min = Mathf.Min(min, input.y + offset.y); max = Mathf.Max(max, input.y + offset.y); }
            }
            float span = max - min;
            Require(span > .015f && span < .06f, "Gait erased or not reduced: " + hz + "Hz span=" + span);
            spans.Add(span);
            var stationary = new Vector3(0, 1.4f, 0);
            Vector3 remaining = Vector3.zero;
            for (int i = 0; i < hz; i++) remaining = (Vector3)step.Invoke(motion,
                new object[] { stationary, false, aim, new Vector3(.2f, .35f, .35f), .22f, 1f / hz });
            Require(remaining.sqrMagnitude < 1e-9f, "Stopping retained a camera offset");
            reset.Invoke(motion, null);
            var first = (Vector3)step.Invoke(motion, new object[] { stationary, true, aim, Vector3.one, .22f, 1f / hz });
            Require(first == Vector3.zero, "Rebinding/teleport retained motion state");
        }
        for (int aim = 0; aim < 2; aim++)
        {
            var values = new[] { spans[aim], spans[2 + aim], spans[4 + aim] };
            Require(values.Max() / values.Min() < 1.15f, "Gait reduction depends excessively on frame rate");
        }
        Require(spans[1] < spans[0], "ADS did not reduce gait");
        return "PASS motion: 30/60/144 Hz, retained rhythm with reduced translation, lower ADS motion, stop/rebind reset and finite bounds.";
    }
}
