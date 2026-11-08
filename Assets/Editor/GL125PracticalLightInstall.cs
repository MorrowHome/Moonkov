using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS.Moon.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GL125PracticalLightInstall
{
    private const string Formal = SourceWorldSceneInstall.ScenePath;

    [Serializable] private sealed class Source
    {
        public string name;
        public float[] position, forward, color;
        public float range, intensity;
        public bool probe;
    }

    [Serializable] private sealed class Lens
    {
        public int material;
        public float[] emission;
    }

    [Serializable] private sealed class Manifest
    {
        public int version;
        public string worldHash;
        public Source[] fixtures;
        public Lens[] lenses;
    }

    public static string Install(string manifestPath, string bakeRoot)
    {
        var scene = SceneManager.GetSceneByPath(Formal);
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            BuildPipeline.isBuildingPlayer || scene.isDirty)
            throw new InvalidOperationException("Finish the current Unity edit or run before installing GL125 fixture lighting.");

        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
        string revision = File.ReadAllText(GL125WorldAssets.Root + "/ImportSource.txt").Trim();
        if (manifest.version != 1 || manifest.worldHash != revision || manifest.fixtures.Length == 0 || manifest.lenses.Length == 0)
            throw new InvalidDataException("GL125 fixture stage does not match the installed map.");
        var lensMaterials = manifest.lenses.Select(lens =>
        {
            string path = $"{GL125WorldAssets.Root}/Materials/Material_{lens.material:0000}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material || !material.HasProperty("_EmissionColor"))
                throw new InvalidDataException("Missing GL125 lens material " + path);
            return (Material: material, Color: GL125WorldAssets.ColorFromLinear(lens.emission));
        }).ToArray();
        var assets = new GL125WorldAssets(Path.Combine(bakeRoot, "PreparedQuality"), Path.Combine(bakeRoot, "Evaluated"));

        string backup = Path.Combine(Path.GetDirectoryName(manifestPath), "TheophilusGameScene.before-PracticalLights.unity");
        if (!File.Exists(backup)) File.Copy(Formal, backup);
        var previous = SceneManager.GetActiveScene();
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(Formal, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var roots = scene.GetRootGameObjects();
            var world = roots.Single(root => root.name == "GL125 World");
            if (manifest.fixtures.Any(source => source.forward.Length != 3 || source.position.Length != 3 ||
                source.color.Length != 3 || source.range <= 0 || source.intensity <= 0 ||
                GL125WorldAssets.ToUnity(source.forward) == Vector3.zero))
                throw new InvalidDataException("Invalid GL125 light pose.");
            const string rootName = "GL125 Fixed Lights (Bake Sources)";
            var previousLights = world.transform.Find(rootName);
            if (previousLights) UnityEngine.Object.DestroyImmediate(previousLights.gameObject);
            var group = new GameObject(rootName);
            group.tag = "EditorOnly";
            group.transform.SetParent(world.transform, false);
            // This scene consumes the Blender bake. Keep the authoring sources
            // in the hierarchy without previewing their energy a second time.
            group.SetActive(false);
            foreach (var source in manifest.fixtures)
            {
                var fixture = new GameObject(source.name);
                fixture.transform.SetParent(group.transform, false);
                var direction = GL125WorldAssets.ToUnity(source.forward).normalized;
                var up = Mathf.Abs(direction.y) > .98f ? Vector3.forward : Vector3.up;
                fixture.transform.SetPositionAndRotation(GL125WorldAssets.ToUnity(source.position), Quaternion.LookRotation(direction, up));
                var light = fixture.AddComponent<Light>();
                light.type = LightType.Spot;
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.shadows = LightShadows.None;
                light.color = GL125WorldAssets.ColorFromLinear(source.color);
                light.intensity = source.intensity;
                light.range = source.range;
                light.spotAngle = 108;
            }
            var additional = manifest.fixtures.Where(source => source.probe).Select(source => new GL125WorldAssets.LightSpec
            {
                name = source.name, type = "AREA", position = source.position, forward = source.forward, color = source.color,
                energy = source.intensity * 32, probeRange = source.range, shadow = true
            }).ToArray();
            Physics.SyncTransforms();
            var probes = GL125SceneInstall.BuildProbes(assets, additional);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the GL125 fixture lighting.");
            foreach (var (material, color) in lensMaterials)
            {
                material.SetColor("_EmissionColor", color);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                material.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            return $"{manifest.fixtures.Length} baked light sources, {lensMaterials.Length} lens materials, " +
                $"{probes.count} character probes with {additional.Length} added fixture inputs.";
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
        }
    }
}
