using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.MoonAlien.Editor
{
    public static class AlienSandboxChecks
    {
        [MenuItem("Moonkov/Alien/Run Math and Probe Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run checks outside Play.");
            var random = new System.Random(1907);
            for (int i = 0; i < 1000; i++)
            {
                var hip = new Vector3(Next(random), Next(random), Next(random));
                var target = hip + new Vector3(Next(random), Next(random), Next(random)) * 3f;
                var hint = new Vector3(Next(random), Next(random), Next(random));
                AlienLegMath.Solve(hip, target, hint, .95f, 1.05f, out var knee, out var foot);
                Near(Vector3.Distance(hip, knee), .95f, .001f, "Upper bone length");
                Near(Vector3.Distance(knee, foot), 1.05f, .001f, "Lower bone length");
                Require(IsFinite(knee) && IsFinite(foot), "Finite solution");
            }
            foreach (var target in new[] { Vector3.zero, Vector3.down * 2f, Vector3.up * 100f })
            {
                AlienLegMath.Solve(Vector3.zero, target, Vector3.down, .95f, 1.05f, out var knee, out var foot);
                Require(IsFinite(knee) && IsFinite(foot), "Degenerate IK remains finite");
                Near(Vector3.Distance(knee, foot), 1.05f, .001f, "Degenerate lower length");
            }
            var from = new Vector3(-1f, 0f, 0f);
            var to = new Vector3(1f, .3f, 1f);
            Near(Vector3.Distance(AlienLegMath.Swing(from, to, Vector3.up, 0f, .3f), from), 0f, .0001f, "Lift-off endpoint");
            Near(Vector3.Distance(AlienLegMath.Swing(from, to, Vector3.up, 1f, .3f), to), 0f, .0001f, "Landing endpoint");
            foreach (var rotation in new[] { Quaternion.Euler(0, 80, 0), Quaternion.Euler(0, 0, 90), Quaternion.Euler(180, 0, 0) })
            {
                Vector3 expected = rotation * AlienLegMath.Swing(from, to, Vector3.up, .4f, .3f);
                Vector3 actual = AlienLegMath.Swing(rotation * from, rotation * to, rotation * Vector3.up, .4f, .3f);
                Near(Vector3.Distance(expected, actual), 0f, .0001f, "Swing wall/ceiling frame invariance (math only)");
                AlienLegMath.Solve(Vector3.zero, to, Vector3.right, .95f, 1.05f, out var knee, out var foot);
                AlienLegMath.Solve(Vector3.zero, rotation * to, rotation * Vector3.right, .95f, 1.05f, out var rk, out var rf);
                Near(Vector3.Distance(rotation * knee, rk), 0f, .0001f, "IK contact-frame invariance");
                Near(Vector3.Distance(rotation * foot, rf), 0f, .0001f, "IK foot frame invariance");
            }
            float full = AlienLegMath.Damping(12f, .1f);
            float half = AlienLegMath.Damping(12f, .05f);
            Near(full, 1f - (1f - half) * (1f - half), .0001f, "Damping partition invariance");
            ProbeChecks();
            Debug.Log("Alien checks passed: 1000 seeded IK cases, degenerate reach, swing endpoints, rotated/inverted math frames, damping and isolated ground probe filtering. This does NOT validate wall/ceiling traversal, gait visuals, combat or LAN.");
        }

        private static void ProbeChecks()
        {
            var previous = SceneManager.GetActiveScene();
            int priorCount = SceneManager.sceneCount;
            var priorScenes = new Scene[priorCount];
            var priorRoots = new GameObject[priorCount][];
            var priorDirty = new bool[priorCount];
            for (int i = 0; i < priorCount; i++)
            {
                priorScenes[i] = SceneManager.GetSceneAt(i);
                priorRoots[i] = priorScenes[i].isLoaded ? priorScenes[i].GetRootGameObjects() : Array.Empty<GameObject>();
                priorDirty[i] = priorScenes[i].isDirty;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Alien isolated probe check");
                Vector3 origin = new Vector3(20000f, 20000f, 20000f);
                root.transform.position = origin;
                var terrain = new GameObject("Allowed terrain").transform;
                terrain.SetParent(root.transform, false);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.SetParent(terrain, false);
                floor.transform.localPosition = new Vector3(0, -.5f, 0);
                floor.transform.localScale = new Vector3(4, 1, 4);
                var distractor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                distractor.transform.SetParent(root.transform, false);
                distractor.transform.localPosition = Vector3.up * .5f;
                distractor.transform.localScale = new Vector3(1, .1f, 1);
                var probe = root.AddComponent<AlienGroundProbe>();
                probe.Configure(terrain);
                Physics.SyncTransforms();
                Require(probe.Ground(origin, out var hit) && hit.collider.gameObject == floor, "Ignore non-terrain collider");
                Require(!probe.Ground(origin + Vector3.right * 10f, out _), "Missing ledge rejected");
                floor.transform.rotation = Quaternion.Euler(0, 0, 65);
                Physics.SyncTransforms();
                Require(!probe.Ground(origin, out _), "Steep surface rejected by ground-only probe");
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                Physics.SyncTransforms();
                Require(SceneManager.sceneCount == priorCount, "Probe checks leaked a scene");
                Require(SceneManager.GetActiveScene() == previous, "Active scene was not restored");
                for (int i = 0; i < priorCount; i++)
                {
                    var preserved = SceneManager.GetSceneAt(i);
                    Require(preserved == priorScenes[i] && preserved.isDirty == priorDirty[i], "Existing scene identity/dirty state changed");
                    var roots = preserved.isLoaded ? preserved.GetRootGameObjects() : Array.Empty<GameObject>();
                    Require(roots.Length == priorRoots[i].Length, "Existing root count changed");
                    foreach (var oldRoot in priorRoots[i]) Require(Array.IndexOf(roots, oldRoot) >= 0, "Existing root changed");
                }
            }
        }

        private static float Next(System.Random random) => (float)random.NextDouble() * 2f - 1f;
        private static bool IsFinite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        private static void Near(float value, float expected, float tolerance, string message) => Require(Mathf.Abs(value - expected) <= tolerance, message);
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
