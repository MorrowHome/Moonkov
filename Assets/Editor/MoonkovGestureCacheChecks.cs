using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MoonkovGestureCacheChecks
{
    [Serializable] private sealed class BoneNames { public string[] bones; }
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;

    [MenuItem("Tools/Moonkov/Performance/Check exact-rig animation caches")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the isolated cache checks in Edit Mode.");
        Check(typeof(DollSingerBakedGestures), new[] {
            "Assets/DollSinger/Animation/Blender/WeaponGestures.json",
            "Assets/HatsuneMiku/Animation/WeaponGestures.json",
            "Assets/Shian/Animation/WeaponGestures.json" });
        Check(typeof(DollSingerMeleeBake), new[] {
            "Assets/DollSinger/Animation/Melee/Resources/MoonkovMeleeGestures.json",
            "Assets/HatsuneMiku/Animation/MeleeGestures.json",
            "Assets/Shian/Animation/MeleeGestures.json" });
        Debug.Log("Gesture caches passed: three alternating exact rigs, shared immutable samples, independent actor bones, and session reset.");
    }

    private static void Check(Type type, string[] paths)
    {
        var root = new GameObject("Temporary animation cache fixture") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var reset = type.GetMethod("ResetCache", Static);
        var cache = (IDictionary)type.GetField("Libraries", Static).GetValue(null);
        var library = type.GetField("library", Instance);
        var bones = type.GetField("bones", Instance);
        var assets = new TextAsset[paths.Length];
        var actors = new Animator[paths.Length];
        var first = new object[paths.Length];
        try
        {
            reset.Invoke(null, null);
            for (int i = 0; i < paths.Length; i++)
            {
                assets[i] = AssetDatabase.LoadAssetAtPath<TextAsset>(paths[i]);
                Require(assets[i], "Missing exact-rig fixture: " + paths[i]);
                var actor = new GameObject("Rig " + i); actor.transform.SetParent(root.transform, false);
                actors[i] = actor.AddComponent<Animator>();
                // Bone names precede the large sample arrays. Read only this header;
                // the production constructor parses and validates the actual full bake.
                var header = new char[8192];
                using var reader = new StreamReader(paths[i]);
                string text = new string(header, 0, reader.Read(header, 0, header.Length));
                var match = Regex.Match(text, "\"bones\"\\s*:\\s*(\\[[^\\]]*\\])");
                Require(match.Success, "The fixture's bone header was not found.");
                var names = new HashSet<string>(JsonUtility.FromJson<BoneNames>("{" + match.Value + "}").bones);
                names.UnionWith(new[] { "上半身2", "肩P.L", "肩P.R", "人指１.R", "人指先.R", "手首.R" });
                foreach (string name in names)
                    new GameObject(name).transform.SetParent(actor.transform, false);
                first[i] = Activator.CreateInstance(type, actors[i], assets[i]);
            }
            for (int pass = 0; pass < 2; pass++) for (int i = 0; i < paths.Length; i++)
            {
                var next = Activator.CreateInstance(type, actors[i], assets[i]);
                Require(ReferenceEquals(library.GetValue(first[i]), library.GetValue(next)),
                    "Alternating rigs reparsed an already cached library.");
                Require(!ReferenceEquals(bones.GetValue(first[i]), bones.GetValue(next)),
                    "Actors shared a mutable bone binding array.");
                for (int j = 0; j < i; j++)
                    Require(!ReferenceEquals(library.GetValue(first[i]), library.GetValue(first[j])),
                        "Different exact-rig assets shared the same parsed library.");
            }
            Require(cache.Count == paths.Length, "The cache did not retain all exact rigs.");
            reset.Invoke(null, null);
            Require(cache.Count == 0, "Starting a new play session retained stale libraries.");
        }
        finally
        {
            reset.Invoke(null, null);
            Object.DestroyImmediate(root);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
