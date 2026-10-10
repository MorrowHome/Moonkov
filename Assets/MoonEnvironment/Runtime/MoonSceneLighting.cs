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
        [SerializeField] private Color nightAmbient = Color.black;
        public enum ClockMode { GameplayDay, MeanLunarOrbit }
        [Header("Sky time (existing server clock is the only time source)")]
        [SerializeField] private ClockMode clockMode = ClockMode.MeanLunarOrbit;
        [Tooltip("UTC date at game hour zero. Mean orbit only: no libration or eclipses.")]
        [SerializeField] private string epochUtc = "2026-10-18T00:00:00Z";
        [SerializeField, Range(-90, 90)] private float latitude = -13.7855f;
        [SerializeField, Range(-180, 180)] private float longitude = 25.164f;
        [Tooltip("Earth hours per synchronized game hour. 1 preserves the 29.53-day lunar solar cycle.")]
        [SerializeField, Min(.001f)] private float astronomicalTimeScale = 1;
        [Header("Camera adaptation")]
        [SerializeField] private bool adaptExposure = true;
        [SerializeField, Range(0, 6)] private float maximumExposureEV = 4;
        [SerializeField, Min(.05f)] private float darkAdaptationSeconds = 8;
        [SerializeField, Min(.05f)] private float brightAdaptationSeconds = .6f;
        [SerializeField] private LayerMask exposureOccluders = ~0;
        private System.DateTime parsedEpoch;
        private MoonCameraExposure exposure;
        private Material runtimeSkybox;
        private static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
        private static readonly int SunVisibleId = Shader.PropertyToID("_SunVisible");
        private static readonly int DaylightId = Shader.PropertyToID("_Daylight");
        private static readonly int AstronomicalSkyId = Shader.PropertyToID("_AstronomicalSky");
        private static readonly int EarthDirectionId = Shader.PropertyToID("_EarthDirection");
        private static readonly int SkyEastId = Shader.PropertyToID("_SkyEast");
        private static readonly int SkyUpId = Shader.PropertyToID("_SkyUp");
        private static readonly int SkyNorthId = Shader.PropertyToID("_SkyNorth");
        private static readonly int EarthRotationId = Shader.PropertyToID("_EarthRotation");
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
            if (!System.DateTime.TryParse(epochUtc, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out parsedEpoch))
                parsedEpoch = new System.DateTime(2026, 10, 18, 0, 0, 0, System.DateTimeKind.Utc);
            if (adaptExposure) exposure = new MoonCameraExposure(transform);
            UpdateSky();
        }

        private void LateUpdate()
        {
            if (applied)
            {
                if (adaptExposure && exposure == null) exposure = new MoonCameraExposure(transform);
                if (!adaptExposure && exposure != null) { exposure.Dispose(); exposure = null; }
                exposure?.Tick(sun, maximumExposureEV, darkAdaptationSeconds, brightAdaptationSeconds, exposureOccluders);
            }
            UpdateSky();
        }

        public void ApplyTimeOfDay(double totalHours)
        {
            if (!applied || !sun) return;
            if (clockMode == ClockMode.MeanLunarOrbit)
            {
                MoonAstronomy.Evaluate(parsedEpoch, totalHours * astronomicalTimeScale, latitude, longitude,
                    out Vector3 direction, out Vector3 earth, out Vector3 east, out Vector3 up,
                    out Vector3 north, out float earthRotation);
                sun.transform.rotation = Quaternion.LookRotation(-direction,
                    Mathf.Abs(direction.y) > .999f ? Vector3.forward : Vector3.up);
                if (runtimeSkybox)
                {
                    runtimeSkybox.SetFloat(AstronomicalSkyId, 1);
                    runtimeSkybox.SetVector(EarthDirectionId, earth);
                    runtimeSkybox.SetVector(SkyEastId, east);
                    runtimeSkybox.SetVector(SkyUpId, up);
                    runtimeSkybox.SetVector(SkyNorthId, north);
                    runtimeSkybox.SetFloat(EarthRotationId, earthRotation);
                }
            }
            else
            {
                double hour = (totalHours % 24 + 24) % 24;
                sun.transform.rotation = Quaternion.Euler((float)((hour - 6) * 15), sunAzimuth, 0);
                if (runtimeSkybox) runtimeSkybox.SetFloat(AstronomicalSkyId, 0);
            }
            float height = -sun.transform.forward.y;
            // Only the finite solar disk crossing the horizon dims the Sun in
            // vacuum. No Earth-like atmospheric extinction at low elevation.
            float radius = Mathf.Sin(.2665f * Mathf.Deg2Rad);
            float daylight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-radius, radius, height));
            sun.intensity = initialSunIntensity * daylight;
            sun.enabled = initialSunEnabled && height > -radius;
            // Optional artistic fill stays explicit; the realism preset uses black.
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
            // Stars respond to the same camera adaptation as the terrain. The
            // remaining visibility curve approximates eye sensitivity, not air.
            float daylight = exposure != null
                ? 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.7f, 1, exposure.DarkAdaptation(maximumExposureEV)))
                : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.005f, .005f, direction.y));
            if (sky.GetFloat(DaylightId) != daylight) sky.SetFloat(DaylightId, daylight);
        }

        private void OnDisable()
        {
            if (!applied) return;
            exposure?.Dispose();
            exposure = null;
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
