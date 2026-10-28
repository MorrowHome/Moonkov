using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon
{
    /// <summary>One fixed-lighting owner for the formal source world. Restores borrowed state on unload.</summary>
    [DefaultExecutionOrder(100), DisallowMultipleComponent]
    public sealed class SourceWorldLighting : MonoBehaviour
    {
        [Serializable] public struct RendererBinding
        {
            public MeshRenderer renderer;
            public int atlas;
        }
        [SerializeField] private SourceWorldLightingData data;
        [SerializeField] private RendererBinding[] bindings = Array.Empty<RendererBinding>();
        private LightmapData[] previousMaps;
        private LightProbes previousProbes;
        private LightmapsMode previousMode;
        private Texture previousStatistics;
        private float previousStatisticsEnabled, previousSpecularSamples, previousDiffuseSamples;
        private bool applied;
        private static SourceWorldLighting owner;
        private readonly Dictionary<Camera, CameraState> cameras = new Dictionary<Camera, CameraState>();
        private static readonly int Statistics = Shader.PropertyToID("_SourceAOAtlases");
        private static readonly int StatisticsEnabled = Shader.PropertyToID("_SourceAOEnabled");
        private static readonly int SpecularSamples = Shader.PropertyToID("_SourceSpecularSamples");
        private static readonly int DiffuseSamples = Shader.PropertyToID("_SourceDiffuseSamples");
        public SourceWorldLightingData Data => data;
        public bool IsApplied => applied;
        public RendererBinding[] Bindings => bindings;

        private sealed class CameraState
        {
            public bool postProcessing, allowHDR;
            public CameraOverrideOption colorOption, depthOption;
            public CameraClearFlags clearFlags;
            public Color background;
            public SourceAgXCamera display;
            public bool createdDisplay, displayEnabled, previousAllowsOverlay;
            public Material previousDisplay;
        }

        public void Configure(SourceWorldLightingData lighting, RendererBinding[] renderers)
        {
            Restore(); data = lighting; bindings = renderers ?? Array.Empty<RendererBinding>();
            if (isActiveAndEnabled && Application.IsPlaying(gameObject)) Apply();
        }

        private void OnEnable()
        {
            if (Application.IsPlaying(gameObject) && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) Apply();
        }

        public void Apply()
        {
            if (applied) return;
            if (owner && owner != this) throw new InvalidOperationException("Another source world owns the fixed lighting.");
            if (!data || data.irradiance == null || data.irradiance.Length == 0 || !data.displayTransform || !data.surfaceStatistics)
                throw new InvalidOperationException("Source world has incomplete baked lighting data.");
            foreach (var texture in data.irradiance)
                if (!texture) throw new InvalidOperationException("Source world is missing an irradiance atlas.");
            foreach (var binding in bindings)
                if (!binding.renderer || binding.atlas < 0 || binding.atlas >= data.irradiance.Length)
                    throw new InvalidOperationException("Source world has an invalid renderer/atlas binding.");
            previousMaps = LightmapSettings.lightmaps; previousMode = LightmapSettings.lightmapsMode;
            previousProbes = LightmapSettings.lightProbes;
            previousStatistics = Shader.GetGlobalTexture(Statistics);
            previousStatisticsEnabled = Shader.GetGlobalFloat(StatisticsEnabled);
            previousSpecularSamples = Shader.GetGlobalFloat(SpecularSamples);
            previousDiffuseSamples = Shader.GetGlobalFloat(DiffuseSamples);
            var maps = new LightmapData[data.irradiance.Length];
            for (int i = 0; i < maps.Length; i++) maps[i] = new LightmapData { lightmapColor = data.irradiance[i] };
            LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional; LightmapSettings.lightmaps = maps;
            if (data.characterProbes) LightmapSettings.lightProbes = data.characterProbes;
            foreach (var binding in bindings)
            {
                binding.renderer.lightmapIndex = binding.atlas;
                binding.renderer.lightmapScaleOffset = new Vector4(1, 1, 0, 0);
            }
            Shader.SetGlobalTexture(Statistics, data.surfaceStatistics); Shader.SetGlobalFloat(StatisticsEnabled, 1);
            Shader.SetGlobalFloat(SpecularSamples, data.specularSamples); Shader.SetGlobalFloat(DiffuseSamples, data.diffuseSamples);
            applied = true; owner = this;
            RenderPipelineManager.beginCameraRendering += PrepareCamera;
        }

        private void PrepareCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!applied || !camera || camera.cameraType != CameraType.Game) return;
            var additional = camera.GetUniversalAdditionalCameraData();
            if (additional.renderType == CameraRenderType.Overlay)
            {
                // The base camera configured its 3D overlays before this callback.
                if (cameras.ContainsKey(camera)) { additional.renderPostProcessing = false; camera.allowHDR = true; }
                return;
            }
            if (Application.isPlaying && !Application.IsPlaying(camera.gameObject)) return;
            if (!Application.IsPlaying(gameObject) && camera.gameObject.scene != gameObject.scene) return;
            int subjects = LayerMask.GetMask("UI", "ServerPlayer", "ClientPlayer", "FirstPersonOverlay");
            if ((camera.cullingMask & ~subjects) == 0) return;
            Camera displayCamera = camera;
            int ui = LayerMask.GetMask("UI");
            var stack = additional.cameraStack;
            if (stack != null)
                foreach (var overlay in stack)
                    if (overlay && overlay.isActiveAndEnabled && (overlay.cullingMask & ~ui) != 0) displayCamera = overlay;
            bool alreadySubscribed = camera.TryGetComponent<SourceAgXCamera>(out var previousDisplay) && previousDisplay.isActiveAndEnabled;
            var state = ConfigureCamera(camera, displayCamera == camera, false);
            if (stack != null)
                foreach (var overlay in stack)
                    if (overlay && overlay.isActiveAndEnabled && (overlay.cullingMask & ~ui) != 0)
                        ConfigureCamera(overlay, displayCamera == overlay, true);
            // New event subscribers do not participate in the current invocation.
            if (!alreadySubscribed && displayCamera == camera) state.display.Enqueue(context, camera);
        }

        private CameraState ConfigureCamera(Camera camera, bool displayHere, bool overlay)
        {
            var additional = camera.GetUniversalAdditionalCameraData();
            if (!cameras.TryGetValue(camera, out var state))
            {
                var display = camera.GetComponent<SourceAgXCamera>();
                state = new CameraState
                {
                    postProcessing = additional.renderPostProcessing, allowHDR = camera.allowHDR,
                    colorOption = additional.requiresColorOption, depthOption = additional.requiresDepthOption,
                    clearFlags = camera.clearFlags, background = camera.backgroundColor,
                    createdDisplay = !display, displayEnabled = display && display.enabled,
                    previousAllowsOverlay = display && display.AllowsOverlay,
                    previousDisplay = display ? display.DisplayTransform : null
                };
                if (!display) display = camera.gameObject.AddComponent<SourceAgXCamera>();
                state.display = display; cameras.Add(camera, state);
            }
            // Gameplay camera controllers may write their old post setting each LateUpdate.
            additional.renderPostProcessing = false; camera.allowHDR = true;
            additional.requiresColorOption = CameraOverrideOption.On; additional.requiresDepthOption = CameraOverrideOption.On;
            if (!overlay) { camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = data.linearBackground.gamma; }
            state.display.Configure(displayHere ? data.displayTransform : null, overlay); state.display.enabled = true;
            return state;
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
        public void Restore()
        {
            if (!applied) return;
            RenderPipelineManager.beginCameraRendering -= PrepareCamera;
            foreach (var pair in cameras)
            {
                if (!pair.Key) continue;
                var additional = pair.Key.GetUniversalAdditionalCameraData(); additional.renderPostProcessing = pair.Value.postProcessing;
                additional.requiresColorOption = pair.Value.colorOption; additional.requiresDepthOption = pair.Value.depthOption;
                pair.Key.allowHDR = pair.Value.allowHDR; pair.Key.clearFlags = pair.Value.clearFlags; pair.Key.backgroundColor = pair.Value.background;
                var display = pair.Value.display;
                if (!display) continue;
                if (pair.Value.createdDisplay)
                {
                    display.enabled = false;
                    if (Application.IsPlaying(display.gameObject)) Destroy(display); else DestroyImmediate(display);
                }
                else { display.Configure(pair.Value.previousDisplay, pair.Value.previousAllowsOverlay); display.enabled = pair.Value.displayEnabled; }
            }
            cameras.Clear();
            LightmapSettings.lightmapsMode = previousMode; LightmapSettings.lightmaps = previousMaps;
            LightmapSettings.lightProbes = previousProbes;
            Shader.SetGlobalTexture(Statistics, previousStatistics); Shader.SetGlobalFloat(StatisticsEnabled, previousStatisticsEnabled);
            Shader.SetGlobalFloat(SpecularSamples, previousSpecularSamples); Shader.SetGlobalFloat(DiffuseSamples, previousDiffuseSamples);
            previousMaps = null; previousProbes = null; previousStatistics = null;
            applied = false; if (owner == this) owner = null;
        }
    }
}
