using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.SpiderLab.Editor
{
    /// <summary>Bounded engine regression checks for cosmetic tissue contacts; no GPU fluid or raid session.</summary>
    public static class LiquidSpiderContactChecks
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/Moonkov/Aliens/Check tissue collider contacts")]
        public static void RunMenu() => Debug.Log(Run());

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run tissue contact checks in Edit Mode.");
            var original = SceneManager.GetActiveScene();
            var fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var assets = new List<Object>();
            var unexpectedLogs = new List<string>();
            var passed = new List<string>();
            LiquidSpiderPresentation skin = null;
            var logs = new Application.LogCallback((message, stack, type) =>
            {
                if (message.Contains("ClosestPoint") || type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    unexpectedLogs.Add(message);
            });
            Application.logMessageReceived += logs;
            try
            {
                SceneManager.SetActiveScene(fixture);
                Vector3 origin = new Vector3(12000, 100, 12000);
                var owner = new GameObject("Temporary tissue contact checks");
                owner.transform.position = origin;
                skin = new LiquidSpiderPresentation(owner.transform, 1);
                var type = typeof(LiquidSpiderPresentation);
                var bones = (Transform[])type.GetField("m_Bones", Private).GetValue(skin);
                var radii = (float[])type.GetField("m_Radii", Private).GetValue(skin);
                var compress = type.GetMethod("CompressTissue", Private);
                float radius = radii[0], gap = radius * .5f;
                float expected = Mathf.Clamp((gap - .012f) / radius, .16f, 1);
                var probe = skin.Renderer.GetComponent<SphereCollider>();
                Require(probe && probe.enabled && probe.isTrigger && probe.excludeLayers.value == -1 &&
                    probe.layerOverridePriority == 64 && probe.gameObject.layer == 2,
                    "Contact probe flags: exists=" + (bool)probe + ", enabled=" + probe.enabled + ", trigger=" + probe.isTrigger + ", exclude=" + probe.excludeLayers.value + ", priority=" + probe.layerOverridePriority + ", layer=" + probe.gameObject.layer);

                var surface = new GameObject("Fixture surface");
                surface.transform.position = origin;
                var box = surface.AddComponent<BoxCollider>();
                box.center = Vector3.down * .5f;
                Check("box", box, origin + Vector3.up * gap, expected);
                Check("inside box", box, origin - Vector3.up * .1f, .16f);
                Object.DestroyImmediate(box);

                var sphere = surface.AddComponent<SphereCollider>();
                sphere.center = Vector3.down * .5f;
                Check("sphere", sphere, origin + Vector3.up * gap, expected);
                Object.DestroyImmediate(sphere);

                var capsule = surface.AddComponent<CapsuleCollider>();
                capsule.height = 2; capsule.radius = .5f; capsule.center = Vector3.down;
                Check("capsule", capsule, origin + Vector3.up * gap, expected);
                Object.DestroyImmediate(capsule);

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(cube);
                var convex = surface.AddComponent<MeshCollider>();
                convex.sharedMesh = cubeMesh; convex.convex = true;
                surface.transform.position = origin - Vector3.up * .5f;
                Check("convex mesh", convex, origin + Vector3.up * gap, expected);
                Object.DestroyImmediate(convex);
                surface.transform.position = origin;

                var mesh = new Mesh { name = "Temporary concave surface" }; assets.Add(mesh);
                mesh.vertices = new[] { new Vector3(-4, 0, -4), new Vector3(4, 0, -4), new Vector3(4, 0, 4), new Vector3(-4, 0, 4) };
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; mesh.RecalculateBounds();
                var concave = surface.AddComponent<MeshCollider>(); concave.sharedMesh = mesh;
                foreach (float scale in new[] { .45f, 1f, 1.4f })
                {
                    owner.transform.localScale = Vector3.one * scale;
                    Check("concave mesh at scale " + scale, concave, origin + Vector3.up * gap, expected, 30);
                }
                owner.transform.localScale = Vector3.one;
                Check("deep mesh contact", concave, origin + Vector3.up * .01f, .16f);
                Check("clear of mesh", concave, origin + Vector3.up * (radius + .2f), 1);
                surface.transform.rotation = Quaternion.Euler(0, 0, 35);
                Check("rotated mesh", concave, origin + surface.transform.up * gap, expected);
                surface.transform.rotation = Quaternion.identity;
                Object.DestroyImmediate(concave);

                // A large AABB over empty space must not compress the tissue.
                mesh.vertices = new[] { new Vector3(-4, 0, -4), new Vector3(-3, 0, -4), new Vector3(-4, 0, -3),
                    new Vector3(3, 0, 3), new Vector3(4, 0, 3), new Vector3(4, 0, 4) };
                mesh.triangles = new[] { 0, 2, 1, 3, 5, 4 }; mesh.RecalculateBounds();
                concave = surface.AddComponent<MeshCollider>(); concave.sharedMesh = mesh;
                Check("empty concave bounds", concave, origin + Vector3.up * gap, 1);
                Object.DestroyImmediate(concave);

                var data = new TerrainData { heightmapResolution = 33, size = new Vector3(8, 1, 8) }; assets.Add(data);
                data.SetHeights(0, 0, new float[33, 33]);
                var terrain = surface.AddComponent<TerrainCollider>(); terrain.terrainData = data;
                surface.transform.position = origin - new Vector3(4, 0, 4);
                Check("terrain", terrain, origin + Vector3.up * gap, expected, 30);
                Object.DestroyImmediate(terrain);
                Check("no contacts", null, origin, 1);

                skin.Release(); skin = null;
                Require(!probe, "Releasing the presentation must destroy its contact probe.");
                Require(unexpectedLogs.Count == 0, "Unexpected physics logs: " + string.Join("; ", unexpectedLogs));
                return "Tissue collider contacts: PASS (" + passed.Count + " cases; repeated mesh/terrain queries; no ClosestPoint warnings). " + string.Join(", ", passed);

                void Check(string label, Collider collider, Vector3 point, float wanted, int repetitions = 1)
                {
                    UnityEngine.Physics.SyncTransforms();
                    bones[0].position = point;
                    for (int i = 0; i < repetitions; i++)
                        compress.Invoke(skin, new object[] { 0 });
                    Require(Mathf.Abs(bones[0].localScale.x - wanted) < .005f,
                        label + " compression: expected " + wanted + ", got " + bones[0].localScale.x);
                    Require(bones[0].position == point, label + " must not move a tissue endpoint.");
                    var nearby = UnityEngine.Physics.OverlapSphere(point, radius + .012f, 1, QueryTriggerInteraction.Ignore);
                    Require(Array.IndexOf(nearby, probe) < 0, label + " must not query the contact probe.");
                    if (wanted < 1 && collider)
                        Require(Array.IndexOf(nearby, collider) >= 0, label + " must exercise a real overlapping collider.");
                    passed.Add(label);
                }
            }
            finally
            {
                skin?.Release();
                Application.logMessageReceived -= logs;
                SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(fixture, true);
                foreach (var asset in assets) Object.DestroyImmediate(asset);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
