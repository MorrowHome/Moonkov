// Reopen the saved display and verify PBR bindings and texture import settings.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

public static class LR06VerifySharedPBR
{
    [Serializable] public class MapInfo { public string asset_path; }
    [Serializable] public class MaterialRow { public string name, base_asset_path; public int index; public MapInfo normal, metallic_smoothness; }
    [Serializable] public class Plan { public MaterialRow[] materials; }
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    public static object Run()
    {
        const string folder = "Assets/MoonEnvironment/Variants/LR06FullPreview";
        string path = folder + "/Scenes/LR06_Full_Display.unity";
        Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Editor busy");
        var original = SceneManager.GetActiveScene(); string originalPath = original.path; bool originalDirty = original.isDirty;
        var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        var plan = (Plan)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
            new object[] { System.IO.File.ReadAllText("SourceData/Models/LR06_20261027/SharedPBRPlan.json"), typeof(Plan) });
        var scene = EditorSceneManager.OpenPreviewScene(path);
        try
        {
            Need(scene.IsValid() && scene.isLoaded, "Display scene could not reopen");
            var roots = scene.GetRootGameObjects();
            var renderers = roots.SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
            var terrains = roots.SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).ToArray();
            var colliders = roots.SelectMany(r => r.GetComponentsInChildren<Collider>(true)).ToArray();
            var lights = roots.SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToArray();
            Need(renderers.Length == 65 && terrains.Length == 2 && colliders.Length == 0 && lights.Length == 32 &&
                 lights.Count(l => l.type == LightType.Point) == 31 && lights.Where(l => l.type == LightType.Point).All(l => l.shadows == LightShadows.None),
                 "Unexpected scene references");
            int references = 0;
            foreach (var row in plan.materials)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Materials/LR06_" + row.index.ToString("D3") + ".mat");
                Need(material && material.shader && material.shader.name == "Universal Render Pipeline/Lit", "Missing material " + row.name);
                Need(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")) == row.base_asset_path &&
                    AssetDatabase.GetAssetPath(material.GetTexture("_BumpMap")) == row.normal.asset_path &&
                    AssetDatabase.GetAssetPath(material.GetTexture("_MetallicGlossMap")) == row.metallic_smoothness.asset_path,
                    "Wrong texture binding " + row.name);
                Need(material.IsKeywordEnabled("_NORMALMAP") && material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") &&
                    !material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A") &&
                    Mathf.Approximately(material.GetFloat("_Smoothness"), 1) && Mathf.Approximately(material.GetFloat("_BumpScale"), 1),
                    "Wrong PBR shader state " + row.name);
                var color = AssetImporter.GetAtPath(row.base_asset_path) as TextureImporter;
                var normal = AssetImporter.GetAtPath(row.normal.asset_path) as TextureImporter;
                var mask = AssetImporter.GetAtPath(row.metallic_smoothness.asset_path) as TextureImporter;
                Need(color && color.sRGBTexture && normal && !normal.sRGBTexture && normal.textureType == TextureImporterType.NormalMap &&
                    !normal.flipGreenChannel && mask && !mask.sRGBTexture && mask.alphaSource == TextureImporterAlphaSource.FromInput &&
                    !mask.alphaIsTransparency, "Wrong PBR texture import settings " + row.name);
                var matches = renderers.Where(r => r.sharedMaterials.Contains(material)).ToArray();
                Need(matches.Length > 0 && matches.All(r => r.GetComponent<MeshFilter>().sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0) &&
                    r.GetComponent<MeshFilter>().sharedMesh.HasVertexAttribute(VertexAttribute.Tangent)), "Material renderer missing UV/tangents " + row.name);
                references += matches.Length;
            }
            var report = new { status = "saved_scene_and_pbr_reopened", materials = plan.materials.Length, rendererMaterialReferences = references,
                renderers = renderers.Length, terrains = terrains.Length, colliders = colliders.Length, lights = lights.Length,
                pointLights = lights.Count(l => l.type == LightType.Point), shadowLights = lights.Count(l => l.shadows != LightShadows.None),
                originalScene = originalPath, originalSceneDirty = originalDirty };
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText("Library/BuildValidation/LR06PBR/UnityPBRReopen.json", serialized);
            return report;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Need(original.path == originalPath && original.isDirty == originalDirty, "Original scene changed during reopen check");
        }
    }
}
