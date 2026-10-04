using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Optional scene-wide lunar lighting, restored when the scene is disabled.</summary>
    [ExecuteAlways]
    public sealed class MoonSceneLighting : MonoBehaviour
    {
        [SerializeField] private Light sun;
        [SerializeField] private RenderPipelineAsset pipeline;
        [SerializeField] private Material spaceSkybox;
        [SerializeField] private Color nightAmbient = new Color(.012f, .015f, .02f);
        private Material runtimeSkybox;
        private static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
        private static readonly int SunVisibleId = Shader.PropertyToID("_SunVisible");
        private static readonly int DaylightId = Shader.PropertyToID("_Daylight");
        private RenderPipelineAsset previousPipeline;
        private Material previousSkybox;
        private AmbientMode previousAmbientMode;
        private Color previousAmbient;
        private float previousAmbientIntensity, previousReflections;
        private Light previousSun;
        private bool previousFog;
        private bool applied;
        private Quaternion initialSunRotation;
        private float initialSunIntensity, sunAzimuth;
        private bool initialSunEnabled;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                UpdateSky();
                return;
            }
            // Dedicated/headless servers still need terrain collision, but no sky rendering.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (sun)
            {
                initialSunRotation = sun.transform.rotation;
                initialSunIntensity = sun.intensity;
                initialSunEnabled = sun.enabled;
                sunAzimuth = sun.transform.eulerAngles.y;
            }
            previousPipeline = QualitySettings.renderPipeline;
            previousSkybox = RenderSettings.skybox;
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbient = RenderSettings.ambientLight;
            previousAmbientIntensity = RenderSettings.ambientIntensity;
            previousReflections = RenderSettings.reflectionIntensity;
            previousSun = RenderSettings.sun;
            previousFog = RenderSettings.fog;
            if (pipeline) QualitySettings.renderPipeline = pipeline;
            if (spaceSkybox)
            {
                runtimeSkybox = Instantiate(spaceSkybox);
                runtimeSkybox.name = spaceSkybox.name + " (runtime)";
                runtimeSkybox.hideFlags = HideFlags.DontSave;
            }
            RenderSettings.skybox = runtimeSkybox;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0;
            RenderSettings.reflectionIntensity = 0;
            RenderSettings.sun = sun;
            RenderSettings.fog = false;
            applied = true;
            UpdateSky();
        }

        private void LateUpdate() => UpdateSky();

        public void ApplyTimeOfDay(double totalHours)
        {
            if (!applied || !sun) return;
            double hour = (totalHours % 24 + 24) % 24;
            sun.transform.rotation = Quaternion.Euler((float)((hour - 6) * 15), sunAzimuth, 0);
            float height = -sun.transform.forward.y;
            float daylight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .08f, height));
            sun.intensity = initialSunIntensity * daylight;
            sun.enabled = initialSunEnabled && height > 0;
            // Very faint night fill keeps navigation readable; no atmospheric sky or fog is added.
            RenderSettings.ambientLight = Color.Lerp(nightAmbient, Color.black, daylight);
            RenderSettings.ambientIntensity = 1;
            UpdateSky();
        }

        private void UpdateSky()
        {
            Material sky = runtimeSkybox;
            // Keep the authored sky reference serializable in Edit Mode. Only its derived
            // sun uniforms change; gameplay always writes to the runtime material clone.
            if (!Application.isPlaying && RenderSettings.skybox == spaceSkybox) sky = spaceSkybox;
            if (!sky) return;
            Vector3 direction = sun ? -sun.transform.forward : Vector3.up;
            if (sky.GetVector(SunDirectionId) != (Vector4)direction) sky.SetVector(SunDirectionId, direction);
            float visible = sun && sun.isActiveAndEnabled ? 1 : 0;
            if (sky.GetFloat(SunVisibleId) != visible) sky.SetFloat(SunVisibleId, visible);
            // Visibility/exposure approximation, not atmospheric scattering.
            float daylight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.08f, 0.08f, direction.y));
            if (sky.GetFloat(DaylightId) != daylight) sky.SetFloat(DaylightId, daylight);
        }

        private void OnDisable()
        {
            if (!applied) return;
            if (sun)
            {
                sun.transform.rotation = initialSunRotation;
                sun.intensity = initialSunIntensity;
                sun.enabled = initialSunEnabled;
            }
            if (QualitySettings.renderPipeline == pipeline) QualitySettings.renderPipeline = previousPipeline;
            RenderSettings.skybox = previousSkybox;
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbient;
            RenderSettings.ambientIntensity = previousAmbientIntensity;
            RenderSettings.reflectionIntensity = previousReflections;
            RenderSettings.sun = previousSun;
            RenderSettings.fog = previousFog;
            if (runtimeSkybox) Destroy(runtimeSkybox);
            runtimeSkybox = null;
            applied = false;
        }
    }
}
