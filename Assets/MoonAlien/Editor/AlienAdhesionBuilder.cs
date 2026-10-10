using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.MoonAlien.Editor
{
    public static class AlienAdhesionBuilder
    {
        [MenuItem("Moonkov/Alien/Create Adhesion Acceptance Course (Additive)")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before creating the course.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Alien adhesion acceptance - authored routes, UNVALIDATED");
                root.transform.position = new Vector3(11000, 11000, 11000);
                var terrain = new GameObject("Allowed static adhesion surfaces").transform;
                terrain.SetParent(root.transform, false);
                Box(terrain, "Inner floor", new Vector3(0, -.25f, 2.5f), new Vector3(6, .5f, 7));
                Box(terrain, "Inner wall", new Vector3(0, 2.5f, 6.25f), new Vector3(6, 5, .5f));
                Box(terrain, "Ceiling underside", new Vector3(0, 5.25f, 3), new Vector3(6, .5f, 6));
                Box(terrain, "Outer floor", new Vector3(10, -.25f, 5), new Vector3(6, .5f, 14));
                Box(terrain, "Convex pillar", new Vector3(10, 1.5f, 7), new Vector3(6, 3, 2));
                const float r = AlienAdhesionRoute.BodyOffset;
                var inner = new[]
                {
                    Pose(0, r, 0, Vector3.up), Pose(0, r, 6-r, Vector3.up),
                    Pose(0, r, 6-r, Vector3.back), Pose(0, 5-r, 6-r, Vector3.back),
                    Pose(0, 5-r, 6-r, Vector3.down), Pose(0, 5-r, .5f, Vector3.down)
                };
                var outer = new List<AlienAdhesionRoute.Pose>
                {
                    Pose(10, r, 0, Vector3.up), Pose(10, r, 6-r, Vector3.up),
                    Pose(10, r, 6-r, Vector3.back), Pose(10, 3, 6-r, Vector3.back)
                };
                for (int i = 1; i <= 9; i++)
                {
                    float angle = i * 10f * Mathf.Deg2Rad;
                    Vector3 up = new Vector3(0, Mathf.Sin(angle), -Mathf.Cos(angle));
                    outer.Add(new AlienAdhesionRoute.Pose(new Vector3(10, 3, 6) + up * r, up));
                }
                outer.Add(Pose(10, 3+r, 8, Vector3.up));
                for (int i = 1; i <= 9; i++)
                {
                    float angle = i * 10f * Mathf.Deg2Rad;
                    Vector3 up = new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle));
                    outer.Add(new AlienAdhesionRoute.Pose(new Vector3(10, 3, 8) + up * r, up));
                }
                outer.Add(Pose(10, r, 8+r, Vector3.forward));
                outer.Add(Pose(10, r, 8+r, Vector3.up));
                outer.Add(Pose(10, r, 11, Vector3.up));
                var a = Alien(root.transform, terrain, "Inner corners and ceiling", inner);
                var b = Alien(root.transform, terrain, "Convex wrap course", outer.ToArray());
                root.AddComponent<AlienAdhesionReadout>().Configure(new[] { a, b });
                var camera = new GameObject("Adhesion course camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.transform.localPosition = new Vector3(-12, 9, -13);
                camera.transform.LookAt(root.transform.position + new Vector3(6, 2, 5));
                camera.farClipPlane = 70f;
                camera.depth = 100f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.18f, .2f, .24f);
                var light = new GameObject("Course point light").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.transform.localPosition = new Vector3(4, 3, 0);
                light.type = LightType.Point; light.range = 30f; light.intensity = 15f;
                Undo.RegisterCreatedObjectUndo(root, "Create adhesion course");
                Selection.activeGameObject = root;
                EditorSceneManager.MarkSceneDirty(scene);
            }
            catch { EditorSceneManager.CloseScene(scene, true); throw; }
            finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }

        private static AlienAdhesionRoute.Pose Pose(float x, float y, float z, Vector3 up) =>
            new AlienAdhesionRoute.Pose(new Vector3(x, y, z), up);
        private static AlienAdhesionRoute Alien(Transform course, Transform terrain, string label, AlienAdhesionRoute.Pose[] route)
        {
            var alien = new GameObject(label);
            alien.transform.SetParent(course, false);
            alien.transform.localPosition = route[0].Centre - route[0].Up * AlienAdhesionRoute.BodyOffset;
            alien.AddComponent<AlienGroundProbe>().Configure(terrain);
            alien.AddComponent<ProceduralAlienLegs>();
            alien.AddComponent<AlienPrimitiveRig>();
            var driver = alien.AddComponent<AlienAdhesionRoute>();
            driver.Configure(course, route);
            return driver;
        }
        private static void Box(Transform parent, string label, Vector3 position, Vector3 scale)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = label; box.transform.SetParent(parent, false);
            box.transform.localPosition = position; box.transform.localScale = scale;
        }
    }
}
