// Install a scene-scoped, editor-only URP copy for LR06 display shadows.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class LR06SetupDisplayShadowScope
{
    const string Root = "Assets/MoonEnvironment/Variants/LR06FullPreview";
    const string ScenePath = Root + "/Scenes/LR06_Full_Display.unity";
    const string PreviewPath = Root + "/Settings/LR06_Display_URP.asset";
    const string ScopePath = Root + "/Runtime/LR06PreviewShadowScope.cs";
    const string SourcePath = "Assets/Settings/PC_RPAsset.asset";
    static void Need(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static object ImportScope()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        return new { status = "requested_preview_scope_import", compiling = EditorApplication.isCompiling };
    }

    public static object Run()
    {
        Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer, "Editor busy");
        Need(!SceneManager.GetSceneByPath(ScenePath).isLoaded, "Display scene already open");
        var originalScene = SceneManager.GetActiveScene(); string originalPath = originalScene.path; bool originalDirty = originalScene.isDirty;
        var previousQualityPipeline = QualitySettings.renderPipeline;
        var source = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SourcePath);
        Need(source && GraphicsSettings.currentRenderPipeline == source, "Expected source pipeline is not active");
        bool sourceDirty = EditorUtility.IsDirty(source);
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ScopePath);
        Need(script && script.GetClass() != null, "Preview scope has not compiled");
        var preview = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PreviewPath);
        if (!preview)
        {
            preview = UnityEngine.Object.Instantiate(source);
            preview.name = "LR06 Display URP - 200m shadows";
            preview.shadowDistance = 200;
            AssetDatabase.CreateAsset(preview, PreviewPath);
            AssetDatabase.SaveAssetIfDirty(preview);
        }
        Need(Mathf.Approximately(preview.shadowDistance, 200) && Mathf.Approximately(source.shadowDistance, 50), "Unexpected shadow distances");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single();
            Need(!camera.GetComponent(script.GetClass()), "Preview scope already installed");
            var component = camera.gameObject.AddComponent(script.GetClass());
            Need(component, "Could not attach preview scope");
            script.GetClass().GetMethod("Configure").Invoke(component, new object[] { source, preview });
            Need(QualitySettings.renderPipeline == previousQualityPipeline, "Preview scope changed inactive scene");
            Need(SceneManager.SetActiveScene(scene), "Could not activate preview scene");
            Need(GraphicsSettings.currentRenderPipeline == preview && QualitySettings.renderPipeline == preview, "Preview pipeline did not activate");
            Need(SceneManager.SetActiveScene(originalScene), "Could not restore original scene");
            Need(QualitySettings.renderPipeline == previousQualityPipeline && GraphicsSettings.currentRenderPipeline == source, "Original pipeline did not restore");
            EditorUtility.SetDirty(component);
            Need(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save preview scene");
            Need(EditorUtility.IsDirty(source) == sourceDirty, "Source pipeline was dirtied");
            return new { status = "display_shadow_scope_saved", scene = ScenePath, previewPipeline = PreviewPath,
                sourceShadowDistance = source.shadowDistance, previewShadowDistance = preview.shadowDistance,
                activeSceneRestored = originalPath, sourcePipelineDirty = sourceDirty };
        }
        finally
        {
            if (originalScene.IsValid() && SceneManager.GetActiveScene() != originalScene) SceneManager.SetActiveScene(originalScene);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            Need(originalScene.path == originalPath && originalScene.isDirty == originalDirty, "Original scene changed");
            Need(QualitySettings.renderPipeline == previousQualityPipeline && EditorUtility.IsDirty(source) == sourceDirty, "Shared pipeline state changed");
        }
    }
}
