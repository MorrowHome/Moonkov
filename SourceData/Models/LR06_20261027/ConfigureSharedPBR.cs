// Restore source-backed normal and metallic/smoothness maps on LR06 display materials.
// Trial uses material clones; Apply only saves the seven display-only material assets.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class LR06ConfigureSharedPBR
{
    const string Folder = "Assets/MoonEnvironment/Variants/LR06FullPreview";
    const string ScenePath = Folder + "/Scenes/LR06_Full_Display.unity";
    const string PlanPath = "SourceData/Models/LR06_20261027/SharedPBRPlan.json";
    const string Output = "Library/BuildValidation/LR06PBR";
    [Serializable] public class MapInfo { public string asset_path; }
    [Serializable] public class MaterialRow { public string name, base_asset_path; public int index; public MapInfo normal, metallic_smoothness; }
    [Serializable] public class Plan { public MaterialRow[] materials; }
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static string MaterialPath(int index) => Folder + "/Materials/LR06_" + index.ToString("D3") + ".mat";
    public static object Trial() => Run(false);
    public static object Apply() => Run(true);

    static object Run(bool apply)
    {
        Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer, "Editor busy");
        Need(!SceneManager.GetSceneByPath(ScenePath).isLoaded, "Display scene is already open");
        var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        var plan = (Plan)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
            new object[] { System.IO.File.ReadAllText(PlanPath), typeof(Plan) });
        Need(plan != null && plan.materials != null && plan.materials.Length == 7, "Unexpected PBR plan");
        var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        foreach (var material in plan.materials)
        {
            foreach (var map in new[] { (material.normal, true), (material.metallic_smoothness, false) })
            {
                if (textures.ContainsKey(map.Item1.asset_path)) continue;
                Need(System.IO.File.Exists(map.Item1.asset_path), "Missing PBR image " + map.Item1.asset_path);
                AssetDatabase.ImportAsset(map.Item1.asset_path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(map.Item1.asset_path) as TextureImporter;
                Need(importer, "Missing texture importer " + map.Item1.asset_path);
                importer.textureType = map.Item2 ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.flipGreenChannel = false;
                importer.convertToNormalmap = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.maxTextureSize = 1024;
                importer.isReadable = false;
                importer.SaveAndReimport();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(map.Item1.asset_path);
                Need(texture && texture.width <= 1024 && texture.height <= 1024, "PBR texture did not import");
                textures.Add(map.Item1.asset_path, texture);
            }
        }
        Need(textures.Count == 13, "Unexpected number of shared PBR textures");
        var oldScene = SceneManager.GetActiveScene();
        string oldPath = oldScene.path; bool oldDirty = oldScene.isDirty;
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var previous = RenderTexture.active;
        var clones = new List<Material>();
        RenderTexture target = null; Texture2D pixels = null;
        try
        {
            Need(scene.IsValid() && scene.isLoaded, "Display scene did not open");
            if (SceneManager.GetActiveScene() != scene) Need(SceneManager.SetActiveScene(scene), "Could not activate display scene");
            var roots = scene.GetRootGameObjects();
            var renderers = roots.SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
            var camera = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).Single();
            var lights = roots.SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToArray();
            Need(renderers.Length == 65 && lights.Length == 32 && lights.Count(light => light.type == LightType.Point) == 31 &&
                lights.Where(light => light.type == LightType.Point).All(light => light.shadows == LightShadows.None), "Unexpected baseline scene");
            int baselineShadowLights = lights.Count(light => light.shadows != LightShadows.None);
            var originals = plan.materials.ToDictionary(row => row.name, row => AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(row.index)));
            Need(plan.materials.All(row => originals[row.name] && originals[row.name].GetTexture("_BaseMap") &&
                AssetDatabase.GetAssetPath(originals[row.name].GetTexture("_BaseMap")) == row.base_asset_path), "Source base texture mismatch");
            var materialSet = new HashSet<Material>(originals.Values);
            var relevant = renderers.Where(r => r.sharedMaterials.Any(m => m && materialSet.Contains(m))).ToArray();
            var missingUv = relevant.Where(r => !r.GetComponent<MeshFilter>().sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0)).Select(r => r.name).ToArray();
            var missingTangents = relevant.Where(r => !r.GetComponent<MeshFilter>().sharedMesh.HasVertexAttribute(VertexAttribute.Tangent)).Select(r => r.name).ToArray();
            Need(missingUv.Length == 0 && missingTangents.Length == 0, "A shared PBR renderer lacks UV0 or tangents");
            camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            target = new RenderTexture(960, 600, 24, RenderTextureFormat.ARGB32); Need(target.Create(), "Render target creation failed");
            pixels = new Texture2D(960, 600, TextureFormat.RGB24, false);
            System.IO.Directory.CreateDirectory(Output);
            void Capture(string name, Vector3 position, Vector3 look)
            {
                camera.transform.position = position; camera.transform.LookAt(look);
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); pixels.Apply();
                System.IO.File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
            }
            var view = new Vector3(1840, 255, 2255); var focus = new Vector3(1873, 240, 2298);
            if (!apply) Capture("UnityR6T06Before", view, focus);
            foreach (var row in plan.materials)
            {
                var original = originals[row.name];
                var material = apply ? original : new Material(original) { hideFlags = HideFlags.HideAndDontSave };
                if (!apply) clones.Add(material);
                material.SetTexture("_BumpMap", textures[row.normal.asset_path]);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
                material.SetTexture("_MetallicGlossMap", textures[row.metallic_smoothness.asset_path]);
                material.SetFloat("_Smoothness", 1f);
                material.SetFloat("_SmoothnessTextureChannel", 0f);
                material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (!apply)
                    foreach (var renderer in renderers)
                    {
                        var slots = renderer.sharedMaterials; bool changed = false;
                        for (int i = 0; i < slots.Length; i++) if (slots[i] == original) { slots[i] = material; changed = true; }
                        if (changed) renderer.sharedMaterials = slots;
                    }
            }
            Capture(apply ? "UnityR6T06Applied" : "UnityR6T06Trial", view, focus);
            Capture(apply ? "UnityOverviewApplied" : "UnityOverviewTrial", new Vector3(1250, 950, 1400), new Vector3(2000, 220, 2180));
            if (!apply)
            {
                // Compare the actual source sun direction separately from PBR changes.
                // Blender (-Z) direction (0.5367, 0.2043, -0.8187) maps to Unity (x,z,y).
                var sun = lights.Single(light => light.type == LightType.Directional);
                sun.transform.rotation = Quaternion.LookRotation(new Vector3(.53673595f, -.81865478f, .20425206f));
                // Unity stores the light color in sRGB; Blender's socket is linear.
                sun.color = new Color(.8f, .88f, 1f).gamma;
                Capture("UnityR6T06SourceDirectionTrial", view, focus);
                Capture("UnityOverviewSourceDirectionTrial", new Vector3(1250, 950, 1400), new Vector3(2000, 220, 2180));
                sun.shadows = LightShadows.Soft;
                Capture("UnityR6T06SourceDirectionShadowTrial", view, focus);
                Capture("UnityOverviewSourceDirectionShadowTrial", new Vector3(1250, 950, 1400), new Vector3(2000, 220, 2180));
                RenderSettings.ambientLight = new Color(.08f, .08f, .09f);
                Capture("UnityR6T06CalibratedTrial", view, focus);
                Capture("UnityOverviewCalibratedTrial", new Vector3(1250, 950, 1400), new Vector3(2000, 220, 2180));
            }
            if (apply) foreach (var material in originals.Values) AssetDatabase.SaveAssetIfDirty(material);
            var textureReports = textures.Select(pair => new { path = pair.Key, pair.Value.width, pair.Value.height,
                format = pair.Value.graphicsFormat.ToString(), runtimeBytes = Profiler.GetRuntimeMemorySizeLong(pair.Value),
                importerType = ((TextureImporter)AssetImporter.GetAtPath(pair.Key)).textureType.ToString(),
                sRGB = ((TextureImporter)AssetImporter.GetAtPath(pair.Key)).sRGBTexture,
                flipGreen = ((TextureImporter)AssetImporter.GetAtPath(pair.Key)).flipGreenChannel }).ToArray();
            var report = new { status = apply ? "pbr_materials_applied" : "pbr_material_trial_not_saved", materialCount = plan.materials.Length,
                textureCount = textures.Count, textureRuntimeBytes = textureReports.Sum(x => x.runtimeBytes), textureReports,
                matchingRenderers = relevant.Length, missingUv, missingTangents,
                lights = lights.Length, pointLights = lights.Count(l => l.type == LightType.Point), baselineShadowLights,
                finalTrialShadowLights = lights.Count(l => l.shadows != LightShadows.None),
                cameraPostProcessing = camera.GetUniversalAdditionalCameraData().renderPostProcessing, originalScene = oldPath, originalSceneDirty = oldDirty };
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText(Output + (apply ? "/UnityPBRApplied.json" : "/UnityPBRTrial.json"), serialized);
            return report;
        }
        finally
        {
            RenderTexture.active = previous;
            if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (oldScene.IsValid() && SceneManager.GetActiveScene() != oldScene) SceneManager.SetActiveScene(oldScene);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            foreach (var material in clones) if (material) UnityEngine.Object.DestroyImmediate(material);
            Need(oldScene.path == oldPath && oldScene.isDirty == oldDirty, "Original scene changed during PBR work");
        }
    }
}
