// Editor-only render configuration for the independent LR06 display scene.
using UnityEngine;
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class LR06PreviewShadowScope : MonoBehaviour
{
#if UNITY_EDITOR
    [SerializeField] UniversalRenderPipelineAsset sourcePipeline;
    [SerializeField] UniversalRenderPipelineAsset previewPipeline;
    [NonSerialized] RenderPipelineAsset previousQualityPipeline;
    [NonSerialized] bool ownsOverride;

    public void Configure(UniversalRenderPipelineAsset source, UniversalRenderPipelineAsset preview)
    {
        sourcePipeline = source;
        previewPipeline = preview;
        RefreshScope();
    }

    void OnEnable()
    {
        EditorSceneManager.activeSceneChangedInEditMode += SceneChanged;
        SceneManager.activeSceneChanged += SceneChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        RefreshScope();
    }

    void OnDisable()
    {
        EditorSceneManager.activeSceneChangedInEditMode -= SceneChanged;
        SceneManager.activeSceneChanged -= SceneChanged;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        ReleaseScope();
    }

    void SceneChanged(Scene previous, Scene next) => RefreshScope();
    void PlayModeChanged(PlayModeStateChange state) => RefreshScope();

    void RefreshScope()
    {
        var scene = gameObject.scene;
        bool active = !EditorApplication.isPlayingOrWillChangePlaymode && scene.IsValid() && scene.isLoaded &&
                      !EditorSceneManager.IsPreviewScene(scene) && SceneManager.GetActiveScene() == scene &&
                      sourcePipeline && previewPipeline;
        if (!active) { ReleaseScope(); return; }
        if (ownsOverride) return;
        // Respect a different pipeline chosen by the user or another context.
        if (GraphicsSettings.currentRenderPipeline != sourcePipeline) return;
        previousQualityPipeline = QualitySettings.renderPipeline;
        QualitySettings.renderPipeline = previewPipeline;
        ownsOverride = true;
    }

    void ReleaseScope()
    {
        if (!ownsOverride) return;
        if (QualitySettings.renderPipeline == previewPipeline)
            QualitySettings.renderPipeline = previousQualityPipeline;
        previousQualityPipeline = null;
        ownsOverride = false;
    }
#endif
}
