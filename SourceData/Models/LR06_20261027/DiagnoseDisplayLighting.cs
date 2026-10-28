// One-variable render comparisons in the existing display scene; never saves it.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class LR06DiagnoseDisplayLighting
{
    const string Path = "Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity";
    const string Output = "Library/BuildValidation/LR06UVLighting";
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    public static object Run()
    {
        Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer, "Editor busy");
        Need(!SceneManager.GetSceneByPath(Path).isLoaded, "Display scene already open");
        var oldScene = SceneManager.GetActiveScene(); string oldPath = oldScene.path; bool oldDirty = oldScene.isDirty;
        var qualityPipeline = QualitySettings.renderPipeline;
        var activePipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        Need(activePipeline, "Active pipeline is not URP");
        bool pipelineDirty = EditorUtility.IsDirty(activePipeline);
        var scene = EditorSceneManager.OpenScene(Path, OpenSceneMode.Additive);
        var priorTarget = RenderTexture.active;
        UniversalRenderPipelineAsset testPipeline = null;
        Material flat = null;
        try
        {
            if (SceneManager.GetActiveScene() != scene) Need(SceneManager.SetActiveScene(scene), "Cannot activate display scene");
            var roots = scene.GetRootGameObjects();
            var lights = roots.SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToArray();
            var sun = lights.Single(l => l.type == LightType.Directional);
            var points = lights.Where(l => l.type == LightType.Point).ToArray();
            var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single();
            var renderers = roots.SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
            // Reproduce the saved pre-fix 50 m baseline even after installing
            // the scene-local preview scope; the change remains temporary.
            var scope = camera.GetComponents<MonoBehaviour>().FirstOrDefault(c => c && c.GetType().Name == "LR06PreviewShadowScope");
            if (scope) scope.enabled = false;
            Need(GraphicsSettings.currentRenderPipeline == activePipeline, "Could not restore diagnostic baseline pipeline");
            Need(points.Length == 31 && sun.shadows == LightShadows.Soft && renderers.Length == 65, "Unexpected display state");
            var originalAmbient = RenderSettings.ambientLight;
            var originalMode = RenderSettings.ambientMode;
            var originalFog = RenderSettings.fog;
            var originalSky = RenderSettings.skybox;
            var originalLightStates = points.Select(p => p.enabled).ToArray();
            var originalSunShadows = sun.shadows;
            var additional = camera.GetUniversalAdditionalCameraData();
            camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(1840,255,2255); camera.transform.LookAt(new Vector3(1873,240,2298));
            camera.fieldOfView = 55;
            System.IO.Directory.CreateDirectory(Output);
            var captures = new List<string>();
            void Capture(string name, int samples = 1)
            {
                var rt = new RenderTexture(960, 600, 24, RenderTextureFormat.ARGB32) { antiAliasing = samples };
                Texture2D pixels = null;
                try
                {
                    Need(rt.Create(), "Cannot create render target");
                    pixels = new Texture2D(960,600,TextureFormat.RGB24,false);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    RenderTexture.active = rt; pixels.ReadPixels(new Rect(0,0,960,600),0,0); pixels.Apply();
                    var path = Output + "/" + name + ".png";
                    System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG()); captures.Add(path);
                }
                finally
                {
                    RenderTexture.active = priorTarget;
                    if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
                    rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                }
            }
            Capture("01_saved_all_lights");
            foreach (var light in points) light.enabled = false;
            Capture("02_sun_only_original_ambient");
            RenderSettings.ambientLight = new Color(.08f,.08f,.09f);
            Capture("03_sun_only_lower_ambient");
            sun.shadows = LightShadows.None;
            Capture("04_same_without_sun_shadows");
            sun.shadows = originalSunShadows;
            testPipeline = UnityEngine.Object.Instantiate(activePipeline);
            testPipeline.name = "Temporary LR06 shadow-distance diagnostic";
            testPipeline.hideFlags = HideFlags.HideAndDontSave;
            testPipeline.shadowDistance = 200;
            QualitySettings.renderPipeline = testPipeline;
            Need(GraphicsSettings.currentRenderPipeline == testPipeline, "Temporary pipeline was not selected");
            Capture("05_same_shadow_distance_200");
            RenderSettings.ambientLight = originalAmbient;
            Capture("05b_shadow_200_original_ambient");
            RenderSettings.ambientLight = new Color(.08f,.08f,.09f);
            for (int i=0; i<points.Length; i++) points[i].enabled = originalLightStates[i];
            Capture("06_shadow_200_warm_restored");
            var baseMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/MoonEnvironment/Variants/LR06FullPreview/Materials/LR06_315.mat");
            Need(baseMaterial, "Building material missing");
            flat = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave };
            flat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            flat.SetColor("_BaseColor", new Color(.65f,.65f,.65f,1));
            flat.SetTexture("_BumpMap", null); flat.DisableKeyword("_NORMALMAP");
            flat.SetTexture("_MetallicGlossMap", null); flat.DisableKeyword("_METALLICSPECGLOSSMAP");
            flat.SetFloat("_Metallic", 0); flat.SetFloat("_Smoothness", .2f);
            foreach (var renderer in renderers)
            {
                var slots = renderer.sharedMaterials; bool changed = false;
                for (int i=0; i<slots.Length; i++) if (slots[i] == baseMaterial) { slots[i] = flat; changed = true; }
                if (changed) renderer.sharedMaterials = slots;
            }
            Capture("07_flat_building_material");
            foreach (var renderer in renderers)
            {
                var slots = renderer.sharedMaterials; bool changed = false;
                for (int i=0; i<slots.Length; i++) if (slots[i] == flat) { slots[i] = baseMaterial; changed = true; }
                if (changed) renderer.sharedMaterials = slots;
            }
            Capture("08_same_with_msaa2", 2);
            Capture("09_same_with_msaa4", 4);
            var report = new { status = "temporary_diagnostics_not_saved", cameraPosition = new[]{1840,255,2255}, cameraTarget = new[]{1873,240,2298},
                width = 960, height = 600, fov = camera.fieldOfView, cameraPostProcessing = additional.renderPostProcessing,
                originalAmbient = new[]{originalAmbient.r, originalAmbient.g, originalAmbient.b}, ambientMode = originalMode.ToString(),
                originalFog, hasSkybox = originalSky != null, originalShadowDistance = activePipeline.shadowDistance,
                originalShadowResolution = activePipeline.mainLightShadowmapResolution, originalCascadeCount = activePipeline.shadowCascadeCount,
                pointLights = points.Length, sunShadows = originalSunShadows.ToString(), temporaryShadowDistance = testPipeline.shadowDistance,
                captures, originalScene = oldPath, originalSceneDirty = oldDirty };
            var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText(Output + "/LightingDiagnosis.json", serialized);
            return report;
        }
        finally
        {
            QualitySettings.renderPipeline = qualityPipeline;
            RenderTexture.active = priorTarget;
            if (oldScene.IsValid() && SceneManager.GetActiveScene() != oldScene) SceneManager.SetActiveScene(oldScene);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            if (flat) UnityEngine.Object.DestroyImmediate(flat);
            if (testPipeline) UnityEngine.Object.DestroyImmediate(testPipeline);
            Need(oldScene.path == oldPath && oldScene.isDirty == oldDirty, "Original scene changed during diagnostics");
            Need(EditorUtility.IsDirty(activePipeline) == pipelineDirty, "Shared pipeline asset changed during diagnostics");
        }
    }
}
