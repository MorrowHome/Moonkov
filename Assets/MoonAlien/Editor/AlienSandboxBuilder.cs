using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.MoonAlien.Editor
{
    public static class AlienSandboxBuilder
    {
        [MenuItem("Moonkov/Alien/Create Isolated Sandbox (Additive)")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before creating the sandbox.");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Alien Sandbox - unsaved and isolated");
                root.transform.position = new Vector3(10000f, 10000f, 10000f);
                var ground = new GameObject("Sandbox terrain only").transform;
                ground.SetParent(root.transform, false);
                Box(ground, "Flat start", new Vector3(0, -.25f, 1.5f), new Vector3(6, .5f, 9));
                var ramp = Box(ground, "Slope 18 degrees", new Vector3(0, .62f, 9), new Vector3(6, .5f, 6.5f));
                ramp.transform.localRotation = Quaternion.Euler(-18, 0, 0);
                Box(ground, "Raised landing", new Vector3(0, 1.62f, 14), new Vector3(6, .5f, 4));
                for (int i = 0; i < 4; i++) Box(ground, "Low stair " + i,
                    new Vector3(0, 1.62f - i * .18f, 16.5f + i), new Vector3(6, .5f, 1));
                Box(ground, "Ledge approach", new Vector3(0, 1.08f, 21), new Vector3(6, .5f, 4));
                // Spatially separate future adhesion fixtures; this first motor deliberately cannot traverse them.
                var climbing = new GameObject("PENDING - adhesion wall ceiling corner fixtures").transform;
                climbing.SetParent(ground, false);
                Box(climbing, "Wall", new Vector3(10, 2.5f, 8), new Vector3(.5f, 5, 8));
                Box(climbing, "Ceiling underside", new Vector3(13, 5, 8), new Vector3(6, .5f, 8));
                Box(climbing, "Inner corner", new Vector3(13, 2.5f, 12), new Vector3(6, 5, .5f));
                Box(climbing, "Outer corner pillar", new Vector3(17, 1.5f, 4), new Vector3(2, 3, 2));
                var alien = new GameObject("Quadruped - presentation sandbox");
                alien.transform.SetParent(root.transform, false);
                alien.AddComponent<AlienGroundProbe>().Configure(ground);
                var legs = alien.AddComponent<ProceduralAlienLegs>();
                alien.AddComponent<AlienPrimitiveRig>();
                var driver = alien.AddComponent<AlienSandboxDriver>();
                alien.AddComponent<AlienSandboxReadout>().Configure(driver, legs);
                var camera = new GameObject("Sandbox camera (remove/unload to restore view)").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.transform.localPosition = new Vector3(-9, 6, -6);
                camera.transform.LookAt(root.transform.position + new Vector3(0, .5f, 8));
                camera.farClipPlane = 75f;
                camera.depth = 100;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.18f, .20f, .24f);
                var light = new GameObject("Sandbox light").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.type = LightType.Point;
                light.transform.localPosition = new Vector3(-2, 6, 8);
                light.range = 35f;
                light.intensity = 10f;
                Undo.RegisterCreatedObjectUndo(root, "Create alien sandbox");
                Selection.activeGameObject = alien;
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log("Created an UNSAVED additive alien sandbox. No build settings/assets changed. For isolated Play, close gameplay scenes without saving their changes; save this new scene only if desired.");
            }
            catch { EditorSceneManager.CloseScene(scene, true); throw; }
            finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }

        private static GameObject Box(Transform parent, string label, Vector3 position, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = label;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            return box;
        }
    }
}
