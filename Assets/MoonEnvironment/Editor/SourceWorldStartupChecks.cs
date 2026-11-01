using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.Moon.Editor
{
    public static class SourceWorldStartupChecks
    {
        private static int checks;
        private static void Require(bool value, string description)
        {
            if (!value) throw new InvalidOperationException(description);
            checks++;
        }

        [MenuItem("Tools/Moon Environment/Source World/Check startup resource policy")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run checks in Edit Mode.");
            checks = 0;
            CheckSerialization();
            CheckReflectionBudget();
            string report = $"Source world startup checks passed: {checks}. Serialization round trip, pixel/mesh preservation, probe selection, budget and cleanup.";
            Directory.CreateDirectory("LocalData/SourceWorldStartupAudit");
            File.WriteAllText("LocalData/SourceWorldStartupAudit/Checks.txt", report);
            Debug.Log(report);
        }

        private static void CheckSerialization()
        {
            var mesh = new Mesh { name = "ReadOnly mesh fixture" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            var cube = new Cubemap(8, TextureFormat.RGBAHalf, true) { name = "ReadOnly cube fixture" };
            for (int face = 0; face < 6; face++) cube.SetPixels(Enumerable.Repeat(new Color(face / 6f, .25f, .5f), 64).ToArray(), (CubemapFace)face);
            cube.Apply(true, false);
            EditorUtility.CompressCubemapTexture(cube, TextureFormat.BC6H, TextureCompressionQuality.Best);
            cube.Apply(false, false);
            var array = new Texture2DArray(8, 8, 2, TextureFormat.BC5, false, true) { name = "ReadOnly array fixture" };
            var pixels = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
            for (int layer = 0; layer < 2; layer++) array.SetPixelData(pixels, 0, layer);
            array.Apply(false, false);
            var objects = new Object[] { mesh, cube, array };
            Object[] loaded = Array.Empty<Object>();
            try
            {
                var cubePixels = new byte[6 * cube.mipmapCount][];
                for (int face = 0; face < 6; face++)
                    for (int mip = 0; mip < cube.mipmapCount; mip++) cubePixels[face * cube.mipmapCount + mip] = cube.GetPixelData<byte>(mip, (CubemapFace)face).ToArray();
                foreach (var asset in objects)
                {
                    Require(SourceWorldRuntimeAssets.SetReadable(asset, false), "First transition must change readability.");
                    Require(!SourceWorldRuntimeAssets.SetReadable(asset, false), "Readability change must be idempotent.");
                }
                string path = "LocalData/SourceWorldStartupAudit/ReadOnlyRoundTrip.asset";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                InternalEditorUtility.SaveToSerializedFileAndForget(objects, path, false);
                loaded = InternalEditorUtility.LoadSerializedFileAndForget(path);
                Require(loaded.Length == 3, "All fixture objects must survive serialization.");
                var savedMesh = loaded.OfType<Mesh>().Single();
                var savedCube = loaded.OfType<Cubemap>().Single();
                var savedArray = loaded.OfType<Texture2DArray>().Single();
                Require(!savedMesh.isReadable && !savedCube.isReadable && !savedArray.isReadable, "Read-only flags must persist after reload.");
                // Re-enable only the loaded fixtures to prove that serialization
                // retained the bytes needed by Unity's Player asset build.
                foreach (var asset in loaded) SourceWorldRuntimeAssets.SetReadable(asset, true);
                Require(savedMesh.vertices.SequenceEqual(new[] { Vector3.zero, Vector3.right, Vector3.up }), "Vertex payload changed.");
                Require(savedMesh.triangles.SequenceEqual(new[] { 0, 1, 2 }), "Index payload changed.");
                Require(savedMesh.uv.SequenceEqual(new[] { Vector2.zero, Vector2.right, Vector2.up }), "UV payload changed.");
                Require(savedCube.format == TextureFormat.BC6H && savedCube.mipmapCount == cube.mipmapCount, "Cubemap layout changed.");
                for (int face = 0; face < 6; face++)
                    for (int mip = 0; mip < cube.mipmapCount; mip++)
                        Require(savedCube.GetPixelData<byte>(mip, (CubemapFace)face).ToArray().SequenceEqual(cubePixels[face * cube.mipmapCount + mip]), "Cubemap pixels changed.");
                for (int layer = 0; layer < 2; layer++)
                    Require(savedArray.GetPixelData<byte>(0, layer).ToArray().SequenceEqual(pixels), "Statistics pixels changed.");
            }
            finally
            {
                foreach (var asset in loaded) if (asset) Object.DestroyImmediate(asset);
                foreach (var asset in objects) if (asset) Object.DestroyImmediate(asset);
            }
        }

        private static void CheckReflectionBudget()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var cube = new Cubemap(512, TextureFormat.BC6H, true);
            SourceWorldReflectionBudget budget = null;
            try
            {
                GameObject Create(string name)
                {
                    var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); return go;
                }
                var camera = Create("Reflection budget camera").AddComponent<Camera>();
                camera.enabled = false; camera.fieldOfView = 90; camera.aspect = 1; camera.farClipPlane = 200;
                var probes = new ReflectionProbe[34];
                for (int i = 0; i < probes.Length; i++)
                {
                    var go = Create("Probe " + i); go.transform.position = new Vector3(0, 0, 5 + i * 3);
                    var probe = go.AddComponent<ReflectionProbe>(); probe.mode = ReflectionProbeMode.Custom;
                    probe.customBakedTexture = cube; probe.size = Vector3.one * 4; probes[i] = probe;
                }
                probes[0].enabled = false;
                probes[1].transform.position = Vector3.back * 500;
                probes[2].gameObject.SetActive(false);
                // A room containing the camera must beat closer captures outside it.
                probes[30].center = -probes[30].transform.position;
                probes[30].size = Vector3.one * 6;
                budget = new SourceWorldReflectionBudget(probes, 16384);
                Require(budget.Limit == 12, "Desktop reflection budget must be twelve.");
                Require(probes.All(p => !p.enabled), "Disable the unbounded set before the first camera cull.");
                budget.Prepare(camera, 1, 0);
                Require(budget.ActiveCount == 12 && probes.Count(p => p.enabled) == 12, "Too many active reflection probes.");
                Require(probes[30].enabled, "Containing room lost its local reflection.");
                Require(!probes[0].enabled && !probes[1].enabled && !probes[2].enabled, "Disabled, inactive or out-of-view probes were selected.");
                var first = probes.Select(p => p.enabled).ToArray();
                camera.transform.position = Vector3.forward * .1f;
                budget.Prepare(camera, 2, .05);
                Require(probes.Select(p => p.enabled).SequenceEqual(first), "Small movement caused reflection churn before refresh.");
                camera.transform.position = Vector3.forward * 90;
                budget.Prepare(camera, 2, .06);
                Require(probes.Select(p => p.enabled).SequenceEqual(first), "A second view expanded the per-frame reflection set.");
                budget.Prepare(camera, 3, .07);
                Require(probes[33].enabled && !probes[3].enabled, "Camera cut failed to select the new nearby region.");
                Require(probes.Count(p => p.enabled) <= 12, "Camera cut exceeded the budget.");
                budget.Restore(); budget.Restore();
                Require(!probes[0].enabled && probes.Skip(1).All(p => p.enabled), "Original enabled states were not restored.");
                budget = new SourceWorldReflectionBudget(probes, 8192);
                Require(budget.Limit == 6, "Smaller GPU atlas must lower the budget.");
                budget.Prepare(camera, 4, 1);
                Require(probes.Count(p => p.enabled) <= 6, "Small GPU atlas budget exceeded.");
                budget.Restore();
                budget = new SourceWorldReflectionBudget(probes, 2048);
                Require(budget.Limit == 0, "Too-small atlas must use the existing global reflection fallback.");
                budget.Prepare(camera, 5, 2);
                Require(probes.All(p => !p.enabled), "Zero-budget fallback left local probes enabled.");
            }
            finally
            {
                budget?.Restore();
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(cube);
            }
        }
    }
}
