using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.MoonAlien.Editor
{
    public static class AlienAmbushBuilder
    {
        [MenuItem("Moonkov/Alien/Create Observation Ambush Sandbox (Additive)")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before creating the sandbox.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Alien observation ambush - no damage or networking");
                root.transform.position = new Vector3(12000, 12000, 12000);
                var terrain = new GameObject("Allowed sandbox terrain").transform;
                terrain.SetParent(root.transform, false);
                Box(terrain, "Floor", new Vector3(0, -.25f, 5), new Vector3(14, .5f, 16));
                Box(terrain, "Cover and climbing wall", new Vector3(0, 2.25f, 6), new Vector3(4, 4.5f, 2));
                Box(terrain, "Ceiling perch underside", new Vector3(0, 4.75f, 2.5f), new Vector3(6, .5f, 5));
                // Asymmetric side cover makes one flank less exposed to a remembered target.
                Box(terrain, "Left concealment", new Vector3(-2.6f, 1, 6.8f), new Vector3(.5f, 2, 1.5f));
                var dummy = new GameObject("Dummy target - controls, not a player");
                dummy.transform.SetParent(root.transform, false);
                dummy.transform.localPosition = new Vector3(0, 0, 2.5f);
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Dummy torso"; body.transform.SetParent(dummy.transform, false);
                body.transform.localPosition = new Vector3(0, .8f, 0);
                body.transform.localScale = new Vector3(.45f, .8f, .45f);
                UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
                dummy.AddComponent<AlienDummyTarget>();
                const float r = AlienAdhesionRoute.BodyOffset;
                var nodes = new[]
                {
                    P(0, 4.5f-r, 1.5f, Vector3.down), P(0, 4.5f-r, 5-r, Vector3.down),
                    P(0, 4.5f-r, 5-r, Vector3.back), P(0, r, 5-r, Vector3.back),
                    P(0, r, 5-r, Vector3.up), P(0, r, 2.5f, Vector3.up),
                    P(0, r, .5f, Vector3.up), P(-3.5f, r, 3.5f, Vector3.up),
                    P(-3.5f, r, 8.5f, Vector3.up), P(0, r, 9, Vector3.up),
                    P(3.5f, r, 8.5f, Vector3.up), P(3.5f, r, 3.5f, Vector3.up)
                };
                var links = new[]
                {
                    new Vector2Int(0,1), new Vector2Int(1,2), new Vector2Int(2,3),
                    new Vector2Int(3,4), new Vector2Int(4,5), new Vector2Int(5,6),
                    new Vector2Int(5,7), new Vector2Int(7,8), new Vector2Int(8,9),
                    new Vector2Int(9,10), new Vector2Int(10,11), new Vector2Int(11,5)
                };
                var alien = new GameObject("Autonomous sandbox quadruped");
                alien.transform.SetParent(root.transform, false);
                alien.transform.localPosition = nodes[0].Centre - nodes[0].Up * r;
                alien.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.down);
                alien.AddComponent<AlienGroundProbe>().Configure(terrain);
                alien.AddComponent<ProceduralAlienLegs>();
                alien.AddComponent<AlienPrimitiveRig>();
                var motor = alien.AddComponent<AlienAdhesionRoute>();
                motor.Configure(root.transform, new[] { nodes[0], nodes[0] });
                alien.AddComponent<AlienAmbushSandbox>().Configure(root.transform, dummy.transform, nodes, links, 0);
                var camera = new GameObject("Ambush sandbox camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.transform.localPosition = new Vector3(-11, 8, -10);
                camera.transform.LookAt(root.transform.position + new Vector3(0, 2, 5));
                camera.farClipPlane = 60; camera.depth = 100;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f,.2f,.24f);
                var light = new GameObject("Sandbox point light").AddComponent<Light>();
                light.transform.SetParent(root.transform, false); light.transform.localPosition = new Vector3(0,3,1);
                light.type = LightType.Point; light.range = 25; light.intensity = 15;
                Undo.RegisterCreatedObjectUndo(root, "Create observation ambush sandbox");
                Selection.activeGameObject = alien;
                EditorSceneManager.MarkSceneDirty(scene);
            }
            catch { EditorSceneManager.CloseScene(scene, true); throw; }
            finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
        }
        private static AlienAdhesionRoute.Pose P(float x,float y,float z,Vector3 normal) =>
            new AlienAdhesionRoute.Pose(new Vector3(x,y,z),normal);
        private static void Box(Transform parent,string label,Vector3 position,Vector3 scale)
        {
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube); box.name=label;
            box.transform.SetParent(parent,false); box.transform.localPosition=position; box.transform.localScale=scale;
        }
    }
}
