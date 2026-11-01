using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class EnergyShieldFittingChecks
{
    private static readonly Type Data = typeof(EnergyShieldPresentation).Assembly.GetType("Unity.MP_FPS.EnergyShieldFittingData");
    private static readonly MethodInfo Read = Data.GetMethod("For", BindingFlags.Public | BindingFlags.Static);
    private static int assertions, meshes, neckSamples;

    [MenuItem("Tools/Moonkov/Performance/Check equipment fitting data")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            throw new InvalidOperationException("Pause Play before checking the runtime cache.");
        assertions = meshes = neckSamples = 0;
        foreach (string path in new[] {
            "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab",
            "Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab",
            "Assets/Shian/Prefabs/ShianNetworkPlayer.prefab" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var source in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (source.sharedMesh && !source.name.Contains("Runtime")) Check(source);
        }
        string result = $"Equipment fitting: PASS ({assertions} checks, {meshes} meshes, {neckSamples} neck samples). " +
            (EditorApplication.isPlaying ? "Runtime reuse and material isolation checked." : "Edit preview refresh checked; runtime reuse requires paused Play.");
        Directory.CreateDirectory("LocalData/Performance60");
        File.WriteAllText("LocalData/Performance60/equipment-fitting-checks.txt", result);
        return result;
    }

    private static void Check(SkinnedMeshRenderer source)
    {
        var host = new GameObject("Equipment fitting data check") { hideFlags = HideFlags.HideAndDontSave };
        Material replacement = null;
        try
        {
            var renderer = host.AddComponent<SkinnedMeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMesh = source.sharedMesh;
            renderer.sharedMaterials = source.sharedMaterials;
            var mesh = source.sharedMesh;
            var data = Read.Invoke(null, new object[] { renderer });
            Require(Field<Vector3[]>(data, "Vertices").SequenceEqual(mesh.vertices), "Source vertex positions changed.");
            Require(Field<BoneWeight[]>(data, "Weights").SequenceEqual(mesh.boneWeights), "Skin weights changed.");
            Require(Field<Matrix4x4[]>(data, "Bindposes").SequenceEqual(mesh.bindposes), "Skin bindposes changed.");
            var expectedSkin = Selected(mesh, renderer.sharedMaterials, false);
            var expectedTorso = Selected(mesh, renderer.sharedMaterials, true);
            Require(Field<int[]>(data, "Skin").SequenceEqual(expectedSkin), "Skin candidate order differs from the original fit.");
            Require(Field<int[]>(data, "Torso").SequenceEqual(expectedTorso), "Torso candidate order differs from the original fit.");
            var weights = mesh.boneWeights;
            int neck = Array.FindIndex(source.bones, b => b && (b.name == "首" || b.name == "Neck"));
            int head = Array.FindIndex(source.bones, b => b && (b.name == "頭" || b.name == "Head"));
            foreach (var pair in new[] { (neck, head), (0, 1), (Math.Max(0, mesh.bindposes.Length - 2), Math.Max(0, mesh.bindposes.Length - 1)) })
            {
                int[] expected = weights.Length != mesh.vertexCount ? Array.Empty<int>() : expectedSkin.Where(i => {
                    var w = weights[i];
                    float Weight(int bone, float value) => bone == pair.Item1 || bone == pair.Item2 ? value : 0;
                    return !(Weight(w.boneIndex0, w.weight0) + Weight(w.boneIndex1, w.weight1) +
                        Weight(w.boneIndex2, w.weight2) + Weight(w.boneIndex3, w.weight3) < .7f);
                }).ToArray();
                var actual = (int[])Data.GetMethod("Neck").Invoke(data, new object[] { pair.Item1, pair.Item2 });
                Require(actual.SequenceEqual(expected), "Neck candidate order differs from the original fit.");
                neckSamples += actual.Length;
            }
            var repeat = Read.Invoke(null, new object[] { renderer });
            Require(ReferenceEquals(data, repeat) == EditorApplication.isPlaying, "Runtime reuse or edit preview invalidation failed.");
            if (renderer.sharedMaterials.Length > 0)
            {
                var original = renderer.sharedMaterials;
                var shader = original.FirstOrDefault(m => m)?.shader ?? Shader.Find("Universal Render Pipeline/Lit");
                replacement = new Material(shader) { name = "Unclassified fitting check", hideFlags = HideFlags.HideAndDontSave };
                renderer.sharedMaterials = Enumerable.Repeat(replacement, original.Length).ToArray();
                var alternate = Read.Invoke(null, new object[] { renderer });
                Require(Field<int[]>(alternate, "Torso").Length == 0, "A material change reused stale torso candidates.");
                Require(Field<int[]>(alternate, "Skin").Length == mesh.vertexCount, "The original no-skin fallback was lost.");
                renderer.sharedMaterials = original;
                var restored = Read.Invoke(null, new object[] { renderer });
                Require(!EditorApplication.isPlaying || ReferenceEquals(data, restored), "Restoring materials lost the original cache entry.");
            }
            meshes++;
        }
        finally
        {
            Object.DestroyImmediate(host);
            if (replacement) Object.DestroyImmediate(replacement);
        }
    }

    private static int[] Selected(Mesh mesh, Material[] materials, bool torso)
    {
        var accepted = new bool[mesh.vertexCount];
        bool any = false;
        for (int sub = 0; sub < Mathf.Min(mesh.subMeshCount, materials.Length); sub++)
        {
            string name = materials[sub] ? materials[sub].name : "";
            if (!name.Equals("Body", StringComparison.OrdinalIgnoreCase) &&
                name.IndexOf(torso ? "Dress" : "Skin", StringComparison.OrdinalIgnoreCase) < 0) continue;
            foreach (int index in mesh.GetTriangles(sub)) accepted[index] = true;
            any = true;
        }
        return Enumerable.Range(0, mesh.vertexCount).Where(i => accepted[i] || !torso && !any).ToArray();
    }

    private static T Field<T>(object value, string name) => (T)Data.GetField(name).GetValue(value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
