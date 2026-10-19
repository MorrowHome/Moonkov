using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;
using Unity.MP_FPS.Missions;

public static class RaidMissionAuthoring
{
    public const string ScenePath = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
    public const string Folder = "Assets/MoonEnvironment/Variants/TheophilusBase/Missions";
    internal static bool HasLiveWorlds()
    {
        foreach (var world in Unity.Entities.World.All)
            if ((world.Flags & (Unity.Entities.WorldFlags.Game | Unity.Entities.WorldFlags.GameClient | Unity.Entities.WorldFlags.GameServer)) != 0) return true;
        return false;
    }
    private const string PrefabPath = Folder + "/MC00MissionSite.prefab";
    private static readonly (string Id, string Source, string Label)[] Bindings = {
        ("entry", "PANEL_MC00_ENTRY_EVA_ROOT", "入口 / 异常设施信号"),
        ("power_a", "PANEL_MC00_TASK_L22_IO_ROOT", "L22 / 隔离柜与 18:42 校验标签"),
        ("power_b", "SW_MC00_L22_CEILING_02_ROOT", "L22 / 母线柜与 18:47 校验标签"),
        ("sampler", "PANEL_MC00_TASK_L22_REPLAY_ROOT", "L22 / 月核封存装置"),
        ("archive", "PANEL_MC00_TASK_L22_OPERATIONS_ROOT", "L22 / 事故档案终端") };

    [MenuItem("Tools/Moonkov/Missions/Install formal MC00 hardware")]
    public static void Install() => Build(false);
    [MenuItem("Tools/Moonkov/Missions/Rebuild owned formal MC00 hardware")]
    public static void RebuildOwned() => Build(true);
    private static void Build(bool replace)
    {
        if (Application.dataPath.Replace('\\', '/') != "E:/UnityProjects/FPS_Template/Assets" || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating || HasLiveWorlds())
            throw new InvalidOperationException("Require the correct project in idle Edit Mode with no game Worlds.");
        for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty)
            throw new InvalidOperationException("Preserve unsaved scenes; finish current editor ownership before installing.");
        var original = SceneManager.GetActiveScene(); var target = SceneManager.GetSceneByPath(ScenePath);
        if (target.IsValid() && target.isLoaded) throw new InvalidOperationException("Install from an idle non-game scene; do not change an already open formal map owned by another task.");
        bool opened = !target.IsValid() || !target.isLoaded;
        if (opened) target = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        GameObject root = null;
        try
        {
            var old = target.GetRootGameObjects().Where(g => g.GetComponentInChildren<RaidMissionSite>(true)).ToArray();
            if (old.Length != 0 && (!replace || old.Length != 1 || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old[0]) != PrefabPath))
                throw new InvalidOperationException("Do not duplicate or replace hardware outside this tool's owned prefab.");
            if (replace && old.Length != 1) throw new InvalidOperationException("Require the existing owned mission prefab.");
            Physics.SyncTransforms();
            var transforms = target.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var anchors = new List<RaidMissionAnchor>();
            foreach (var binding in Bindings)
            {
                var matches = transforms.Where(t => t.name == binding.Source).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Require one real formal-map source: " + binding.Source);
                anchors.Add(Approach(binding.Id, binding.Label, matches[0]));
            }
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var dark = Material("HardwareGraphite", new Color(.075f, .09f, .115f), .7f);
            var steel = Material("HardwareSteel", new Color(.42f, .47f, .51f), .85f);
            var screen = Material("HardwareStatus", new Color(.13f, .25f, .45f), .3f, true);
            var crystal = Material("MooncoreCrystal", new Color(.68f, .82f, .9f), .65f);
            var paper = Material("ArchiveLabel", new Color(.72f, .73f, .67f), .05f);
            SceneManager.SetActiveScene(target);
            root = new GameObject("MC00 Mission Site"); SceneManager.MoveGameObjectToScene(root, target);
            var devices = new List<RaidMissionDevice>();
            foreach (var anchor in anchors) devices.Add(Device(root.transform, anchor, dark, steel, screen, crystal, paper));
            var site = root.AddComponent<RaidMissionSite>();
            site.Author(anchors.ToArray(), devices.ToArray(), Sound("MissionCutter", 80, .16f), Sound("MissionArchive", 140, .045f));
            if (site.Validate() != null) throw new InvalidOperationException(site.Validate());
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root); root = null;
            foreach (var previous in old) UnityEngine.Object.DestroyImmediate(previous);
            root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target);
            EditorSceneManager.MarkSceneDirty(target);
            if (!EditorSceneManager.SaveScene(target)) throw new IOException("Unable to save formal mission installation.");
            var report = string.Join("\n", anchors.Select(a => a.Id + " | " + a.Label + " | device=" + a.Position.ToString("F3") + " | approach=" + a.Approach.ToString("F3")));
            File.WriteAllText(Folder + "/Authoring.txt", "Formal MC00 sources; ground/capsule/line-of-sight authoring only. Runtime route/Play not yet accepted.\n" + report);
            AssetDatabase.ImportAsset(Folder + "/Authoring.txt"); AssetDatabase.SaveAssets();
            Debug.Log("[Missions] Installed " + anchors.Count + " authored devices in formal MC00. Existing hardware and lights preserved. " + report);
        }
        finally
        {
            if (opened && target.IsValid() && target.isLoaded) EditorSceneManager.CloseScene(target, true);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        }
    }

    private static RaidMissionAnchor Approach(string id, string label, Transform source)
    {
        var directions = new[] { source.forward, -source.forward, source.right, -source.right, Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        foreach (var raw in directions)
        {
            var direction = Vector3.ProjectOnPlane(raw, Vector3.up).normalized; if (direction.sqrMagnitude < .5f) continue;
            var p = source.position + direction * 1.05f;
            var hits = Physics.RaycastAll(p + Vector3.up * .2f, Vector3.down, 3.4f, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)
                .Where(h => h.normal.y > .75f && source.position.y - h.point.y > .55f).OrderBy(h => h.point.y).ToArray();
            foreach (var floor in hits)
            {
                var stand = floor.point + Vector3.up * .06f;
                if (Physics.CheckCapsule(stand + Vector3.up * .3f, stand + Vector3.up * 1.5f, .25f,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) continue;
                var point = source.position + direction * .4f;
                if (Physics.Linecast(stand + Vector3.up * 1.4f, point, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) continue;
                return new RaidMissionAnchor { Id = id, Label = label, Region = id == "entry" ? "MC00 · 入口" : "MC00 · L22",
                    Position = point, Approach = stand, Yaw = Quaternion.LookRotation(direction).eulerAngles.y };
            }
        }
        throw new InvalidOperationException("No physically clear station beside actual MC00 source: " + source.name);
    }

    private static Material Material(string name, Color color, float metallic, bool emission = false)
    {
        string path = Folder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing) return existing;
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        material.SetColor("_BaseColor", color); material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", .48f);
        if (emission) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color); }
        AssetDatabase.CreateAsset(material, path); return material;
    }
    private static GameObject Part(Transform parent, string name, PrimitiveType primitive, Vector3 position, Vector3 scale, Material material, bool solid = false)
    {
        var go = GameObject.CreatePrimitive(primitive); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = solid ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return go;
    }
    private static RaidMissionDevice Device(Transform parent, RaidMissionAnchor a, Material dark, Material steel, Material signal, Material crystal, Material paper)
    {
        var root = new GameObject(a.Label); root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(a.Position - Quaternion.Euler(0, a.Yaw, 0) * Vector3.forward * .15f, Quaternion.Euler(0, a.Yaw, 0));
        var t = root.transform; var indicators = new List<Renderer>(); var products = new List<GameObject>(); Transform rotor = null, lever = null;
        bool sample = a.Id == "sampler", archive = a.Id == "archive", tag = a.Id.StartsWith("tag_");
        float width = sample ? .58f : archive ? .48f : tag ? .2f : .38f;
        Part(t, "Physical equipment casing", PrimitiveType.Cube, Vector3.zero, new Vector3(width, sample ? .45f : .38f, .13f), dark, true);
        Part(t, "Brushed front plate", PrimitiveType.Cube, new Vector3(0, 0, .075f), new Vector3(width - .035f, .33f, .012f), steel);
        indicators.Add(Part(t, "State strip", PrimitiveType.Cube, new Vector3(-width * .35f, .1f, .09f), new Vector3(.025f, .13f, .018f), signal).GetComponent<Renderer>());
        if (sample)
        {
            float floor = a.Position.y - (a.Approach.y - .06f);
            if (floor >= 1.2f)
            {
                Part(t, "Sampling pedestal", PrimitiveType.Cube, new Vector3(0, -floor * .5f, -.015f), new Vector3(.27f, floor - .18f, .25f), dark, true);
                Part(t, "Grounded plinth", PrimitiveType.Cube, new Vector3(0, -floor + .055f, .04f), new Vector3(.58f, .11f, .5f), steel, true);
            }
            else Part(t, "Counter-mounted support bracket", PrimitiveType.Cube, new Vector3(0, -.22f, -.12f), new Vector3(.55f, .04f, .42f), steel, true);
            rotor = new GameObject("Cutting head").transform; rotor.SetParent(t, false); rotor.localPosition = new Vector3(0, .16f, .1f);
            var cutter = Part(rotor, "Cutting spindle", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.26f, .025f, .26f), dark);
            indicators.Add(cutter.GetComponent<Renderer>());
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -.12f : .12f;
                Part(t, "Sample protective clamp", PrimitiveType.Cube, new Vector3(x, -.08f, .16f), new Vector3(.12f, .035f, .12f), steel);
                var shard = new GameObject("Mooncore fragment " + (i + 1)); shard.transform.SetParent(t, false); shard.transform.localPosition = new Vector3(x, -.015f, .16f);
                shard.AddComponent<MeshFilter>().sharedMesh = CrystalMesh(); shard.AddComponent<MeshRenderer>().sharedMaterial = crystal; products.Add(shard);
            }
        }
        else if (archive)
        {
            Part(t, "Recorder access slot", PrimitiveType.Cube, new Vector3(0, -.09f, .1f), new Vector3(.28f, .04f, .045f), dark);
            var drive = Part(t, "Recovered archive cassette", PrimitiveType.Cube, new Vector3(0, -.08f, .15f), new Vector3(.18f, .09f, .055f), paper); products.Add(drive);
        }
        else if (a.Id == "power_a" || a.Id == "power_b")
        {
            lever = new GameObject("Isolation switch lever").transform; lever.SetParent(t, false); lever.localPosition = new Vector3(.05f, -.04f, .12f);
            Part(lever, "Lever metal grip", PrimitiveType.Cube, new Vector3(0, .04f, .025f), new Vector3(.04f, .13f, .045f), steel);
            for (int i = 0; i < 3; i++)
                Part(t, "Visible cable conductor", PrimitiveType.Cylinder, new Vector3(-.08f + i * .08f, -.27f, -.015f), new Vector3(.022f, .09f, .022f), i == 0 && a.Id == "power_a" ? dark : steel);
        }
        string title = a.Id == "power_a" ? "A / 18:42\nTHERMAL FAULT" : a.Id == "power_b" ? "B / 18:47\nBACKUP LIVE" : sample ? "MC00 / SAMPLE\nSEALED ARRAY" : archive ? "MC00 / ARCHIVE\nUPLINK LOST" : tag ?
            (a.Id == "tag_a" ? "CHECKSUM A\n18:42" : "CHECKSUM B\n18:47") : "MC00 / SIGNAL\nFACILITY LINK";
        var text = new GameObject("Physical device legend"); text.transform.SetParent(t, false); text.transform.localPosition = new Vector3(0, .03f, .09f);
        text.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var mesh = text.AddComponent<TextMesh>(); mesh.text = title; mesh.characterSize = tag ? .004f : .0055f; mesh.fontSize = 48;
        mesh.alignment = TextAlignment.Center; mesh.anchor = TextAnchor.MiddleCenter; mesh.color = new Color(.9f, .92f, .88f);
        text.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var device = root.AddComponent<RaidMissionDevice>(); device.Author(a.Id, indicators.ToArray(), rotor, lever, products.ToArray());
        device.Present(false, false, false, false, 0); return device;
    }
    private static Mesh CrystalMesh()
    {
        string path = Folder + "/MooncoreFragment.asset"; var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (existing) return existing;
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3, b = (i + 1) * Mathf.PI / 3;
            var p = new Vector3(Mathf.Cos(a) * .06f, 0, Mathf.Sin(a) * .06f); var q = new Vector3(Mathf.Cos(b) * .06f, 0, Mathf.Sin(b) * .06f);
            foreach (var face in new[] { new[] { new Vector3(.014f, .15f, 0), q, p }, new[] { new Vector3(-.015f, -.07f, 0), p, q } })
                foreach (var v in face) { triangles.Add(vertices.Count); vertices.Add(v); }
        }
        var mesh = new Mesh { name = "Mooncore fragment / six faceted" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path); return mesh;
    }
    private static SoundDef Sound(string name, float frequency, float amplitude)
    {
        string path = Folder + "/" + name + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<SoundDef>(path); if (existing) return existing;
        string wav = Folder + "/" + name + ".wav"; const int rate = 22050, count = rate * 2;
        using (var writer = new BinaryWriter(File.Create(wav)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
            for (int i = 0; i < count; i++)
            {
                double t = i / (double)rate, value = 0;
                for (int h = 1; h <= 5; h++) value += System.Math.Sin(2 * System.Math.PI * frequency * h * t) / h;
                value *= amplitude * (.85 + .15 * System.Math.Cos(System.Math.PI * t)); writer.Write((short)(System.Math.Max(-.9, System.Math.Min(.9, value)) * short.MaxValue));
            }
        }
        AssetDatabase.ImportAsset(wav, ImportAssetOptions.ForceSynchronousImport);
        var importer = (AudioImporter)AssetImporter.GetAtPath(wav); importer.forceToMono = true;
        var sample = importer.defaultSampleSettings; sample.preloadAudioData = true; sample.loadType = AudioClipLoadType.DecompressOnLoad;
        sample.compressionFormat = AudioCompressionFormat.ADPCM; importer.defaultSampleSettings = sample; importer.SaveAndReimport();
        var template = AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Moonkov/SoundDefs/RaidHullDrone.asset");
        var sound = UnityEngine.Object.Instantiate(template); sound.name = name; sound.Clips = new List<AudioClip> { AssetDatabase.LoadAssetAtPath<AudioClip>(wav) };
        sound.Role = SoundDef.MixRole.Interaction; sound.MixerGroup = SoundMixer.SoundMixerGroup.SFX; sound.Priority = 120; sound.Occlusion = true;
        sound.DistanceInfo.VolumeDistMin = 1; sound.DistanceInfo.VolumeDistMax = 28; sound.DistanceInfo.SpatialBlend = 1;
        AssetDatabase.CreateAsset(sound, path); return sound;
    }
}
