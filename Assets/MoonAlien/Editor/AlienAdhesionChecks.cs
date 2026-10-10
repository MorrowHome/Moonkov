using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.MoonAlien.Editor
{
    public static class AlienAdhesionChecks
    {
        [MenuItem("Moonkov/Alien/Run Adhesion Frame and Clearance Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run outside Play.");
            Quaternion frame = Quaternion.identity;
            for (int i = 1; i <= 360; i++)
            {
                Vector3 up = Quaternion.AngleAxis(i, Vector3.right) * Vector3.up;
                var next = AlienSurfaceFrame.Transport(frame, up);
                Require(Vector3.Angle(next * Vector3.up, up) < .05f, "Frame lost surface normal");
                Require(Quaternion.Angle(frame, next) < 1.05f, "Transport discontinuity");
                Require(Mathf.Abs(Vector3.Dot(next * Vector3.up, next * Vector3.forward)) < .0001f, "Non-tangent heading");
                frame = next;
            }
            Require(Quaternion.Angle(frame, Quaternion.identity) < .05f, "Full-loop frame failed to return");
            var step = AlienSurfaceFrame.StepNormal(Vector3.up, Vector3.back, 90f, 1f / 60f);
            Require(Vector3.Angle(Vector3.up, step) <= 1.51f, "Normal speed bound");
            ClearanceChecks();
            Debug.Log("Adhesion math/probe checks passed. Authored traversal, foot contact continuity, visuals, moving surfaces and network behavior still require Play acceptance.");
        }

        private static void ClearanceChecks()
        {
            var previous = SceneManager.GetActiveScene();
            int count = SceneManager.sceneCount;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Temporary adhesion checks");
                Vector3 origin = new Vector3(22000, 22000, 22000);
                root.transform.position = origin;
                var terrain = new GameObject("Allowed terrain").transform; terrain.SetParent(root.transform, false);
                Cube(terrain, "Floor", new Vector3(0, -.25f, 0), new Vector3(8, .5f, 8));
                Cube(terrain, "Wall", new Vector3(0, 2, 3.25f), new Vector3(8, 4, .5f));
                Cube(terrain, "Ceiling", new Vector3(0, 4.25f, 0), new Vector3(8, .5f, 6));
                var probe = root.AddComponent<AlienGroundProbe>(); probe.Configure(terrain);
                Physics.SyncTransforms();
                Require(probe.Surface(origin + Vector3.up * .66f, Vector3.up, out _, .1f, 1.25f), "Floor contact");
                Require(probe.Surface(origin + new Vector3(0, 2, 2.34f), Vector3.back, out _, .1f, 1.25f), "Wall contact");
                Require(probe.Surface(origin + new Vector3(0, 3.34f, 0), Vector3.down, out _, .1f, 1.25f), "Inverted ceiling contact");
                Require(!probe.Overlaps(origin + new Vector3(0, .66f, 2.34f), AlienAdhesionRoute.ClearanceRadius), "Clear inner corner sphere");
                Require(probe.Overlaps(origin + new Vector3(0, .2f, 2.8f), AlienAdhesionRoute.ClearanceRadius), "Penetrating shell must reject");
                Cube(terrain, "Inserted small blocker", new Vector3(0, 2, 0), Vector3.one * .05f);
                Physics.SyncTransforms();
                Require(probe.Overlaps(origin + new Vector3(0, 2, 0), .53f), "Current-centre overlap must reject before attempted escape");
                Require(probe.Obstructed(origin + new Vector3(0, 2, 1), origin + new Vector3(0, 2, 5), .53f), "Swept wall blocks tunnelling");
                Require(!probe.Surface(origin + new Vector3(20, 2, 0), Vector3.down, out _, .1f, 1.25f), "Missing ceiling cannot adhere");
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                Physics.SyncTransforms();
                Require(SceneManager.sceneCount == count && SceneManager.GetActiveScene().handle == previous.handle, "Check scene cleanup");
            }
        }
        private static void Cube(Transform parent, string label, Vector3 position, Vector3 scale)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube); item.name = label;
            item.transform.SetParent(parent, false); item.transform.localPosition = position; item.transform.localScale = scale;
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
