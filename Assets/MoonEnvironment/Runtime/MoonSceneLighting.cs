using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Optional scene-wide lunar lighting, restored when the scene is disabled.</summary>
    public sealed class MoonSceneLighting : MonoBehaviour
    {
        [SerializeField] private Light sun;
        [SerializeField] private RenderPipelineAsset pipeline;
        private RenderPipelineAsset previousPipeline;
        private Material previousSkybox;
        private AmbientMode previousAmbientMode;
        private Color previousAmbient;
        private float previousAmbientIntensity, previousReflections;
        private Light previousSun;
        private bool previousFog;
        private bool applied;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            previousPipeline = QualitySettings.renderPipeline;
            previousSkybox = RenderSettings.skybox;
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbient = RenderSettings.ambientLight;
            previousAmbientIntensity = RenderSettings.ambientIntensity;
            previousReflections = RenderSettings.reflectionIntensity;
            previousSun = RenderSettings.sun;
            previousFog = RenderSettings.fog;
            if (pipeline) QualitySettings.renderPipeline = pipeline;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0;
            RenderSettings.reflectionIntensity = 0;
            RenderSettings.sun = sun;
            RenderSettings.fog = false;
            applied = true;
        }

        private void OnDisable()
        {
            if (!applied) return;
            if (QualitySettings.renderPipeline == pipeline) QualitySettings.renderPipeline = previousPipeline;
            RenderSettings.skybox = previousSkybox;
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbient;
            RenderSettings.ambientIntensity = previousAmbientIntensity;
            RenderSettings.reflectionIntensity = previousReflections;
            RenderSettings.sun = previousSun;
            RenderSettings.fog = previousFog;
            applied = false;
        }
    }
}
