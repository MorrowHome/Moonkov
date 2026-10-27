using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class LR06VerifyDisplayShadowScope
{
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    public static object Run()
    {
        const string path = "Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity";
        const string previewPath = "Assets/MoonEnvironment/Variants/LR06FullPreview/Settings/LR06_Display_URP.asset";
        const string sourcePath = "Assets/Settings/PC_RPAsset.asset";
        Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !SceneManager.GetSceneByPath(path).isLoaded, "Editor busy");
        var old = SceneManager.GetActiveScene(); string oldPath = old.path; bool oldDirty = old.isDirty;
        var oldQuality = QualitySettings.renderPipeline;
        var source = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(sourcePath);
        var preview = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(previewPath);
        Need(source && preview && GraphicsSettings.currentRenderPipeline == source && Mathf.Approximately(source.shadowDistance, 50) &&
             Mathf.Approximately(preview.shadowDistance, 200), "Unexpected pipelines");
        bool sourceDirty = EditorUtility.IsDirty(source);
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            var scope = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .Single(c => c && c.GetType().Name == "LR06PreviewShadowScope");
            Need(QualitySettings.renderPipeline == oldQuality, "Inactive display changed pipeline");
            Need(SceneManager.SetActiveScene(scene) && GraphicsSettings.currentRenderPipeline == preview, "Scope did not activate");
            scope.enabled = false;
            Need(QualitySettings.renderPipeline == oldQuality && GraphicsSettings.currentRenderPipeline == source, "Disabling scope did not restore pipeline");
            scope.enabled = true;
            Need(GraphicsSettings.currentRenderPipeline == preview, "Re-enabling scope did not reactivate pipeline");
            Need(SceneManager.SetActiveScene(old) && QualitySettings.renderPipeline == oldQuality && GraphicsSettings.currentRenderPipeline == source,
                "Leaving display did not restore pipeline");
            var report = new { status = "scene_scoped_pipeline_restoration_checked", sourceShadowDistance = source.shadowDistance,
                previewShadowDistance = preview.shadowDistance, inactiveUnchanged = true, deactivateRestored = true,
                reenableRestored = true, switchSceneRestored = true, sourcePipelineDirty = sourceDirty, originalScene = oldPath };
            var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText("Library/BuildValidation/LR06UVLighting/ShadowScopeCheck.json", serialized);
            return report;
        }
        finally
        {
            if (old.IsValid() && SceneManager.GetActiveScene() != old) SceneManager.SetActiveScene(old);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            Need(QualitySettings.renderPipeline == oldQuality && EditorUtility.IsDirty(source) == sourceDirty && old.path == oldPath && old.isDirty == oldDirty,
                "Original state changed during scope check");
        }
    }
}
