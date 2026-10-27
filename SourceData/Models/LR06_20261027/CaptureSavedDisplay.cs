// Capture the saved display scene after reopening it. Do not save any scene.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class LR06CaptureSavedDisplay
{
    public static object Run()
    {
        const string path = "Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity";
        const string output = "Library/BuildValidation/LR06PBR";
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetSceneByPath(path).isLoaded)
            throw new Exception("Editor busy or display scene already open");
        var old = SceneManager.GetActiveScene(); var oldPath = old.path; bool oldDirty = old.isDirty;
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        var prior = RenderTexture.active;
        try
        {
            if (SceneManager.GetActiveScene() != scene) SceneManager.SetActiveScene(scene);
            var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single();
            camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            void Capture(string name, int width, int height, Vector3 position, Vector3 look)
            {
                var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                Texture2D pixels = null;
                try
                {
                    if (!target.Create()) throw new Exception("Could not create render target");
                    pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                    camera.transform.position = position; camera.transform.LookAt(look);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
                    System.IO.File.WriteAllBytes(output + "/" + name + ".png", pixels.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = prior;
                    if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
                    target.Release(); UnityEngine.Object.DestroyImmediate(target);
                }
            }
            Capture("UnityR6T06Reopened",960,600,new Vector3(1840,255,2255),new Vector3(1873,240,2298));
            Capture("UnityOverviewReopened",1600,1000,new Vector3(1250,950,1400),new Vector3(2000,220,2180));
            Capture("UnityWarmBuildingReopened",1600,1000,new Vector3(1920,265,2430),new Vector3(1946,255,2470));
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var shadowTextures = Resources.FindObjectsOfTypeAll<RenderTexture>().Where(t => t && t.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(t => new { t.name, t.width, t.height, bytes = Profiler.GetRuntimeMemorySizeLong(t) }).ToArray();
            var report = new { status = "saved_display_reopened_and_captured", cameraPostProcessing = camera.GetUniversalAdditionalCameraData().renderPostProcessing,
                pipeline = pipeline ? pipeline.name : null, mainLightShadowResolution = pipeline ? pipeline.mainLightShadowmapResolution : 0,
                shadowDistance = pipeline ? pipeline.shadowDistance : 0, shadowCascadeCount = pipeline ? pipeline.shadowCascadeCount : 0,
                visibleShadowTextures = shadowTextures, originalScene = oldPath, originalSceneDirty = oldDirty };
            var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText(output + "/UnityCaptureReopen.json", serialized);
            return report;
        }
        finally
        {
            RenderTexture.active = prior;
            if (old.IsValid() && SceneManager.GetActiveScene() != old) SceneManager.SetActiveScene(old);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            if (old.path != oldPath || old.isDirty != oldDirty) throw new Exception("Original scene changed");
        }
    }
}
