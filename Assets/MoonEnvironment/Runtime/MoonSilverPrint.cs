using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Scene-owned, opt-in opaque environment treatment before transparent effects and tonemapping.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MoonSilverPrint : MonoBehaviour
    {
        [SerializeField] private Material material;
        [Tooltip("Legacy serialized reference. Environment scope now comes from native material passes, not subject exclusions.")]
        [SerializeField] private Material subjectMaskMaterial;
        [Header("环境外观 / Environment look")]
        [Tooltip("True keeps original PBR lighting. In development gameplay F1 temporarily previews the style, including a disabled component or zero intensity, without changing these authored settings.")]
        [SerializeField] private bool faithfulLighting = true;
        [Header("历史风格实验 / Legacy graphic finish")]
        [SerializeField, Range(0, 1)] private float intensity = 1;
        [SerializeField, Range(-2, 2)] private float exposure = 1f;
        [SerializeField, Range(0, .45f)] private float blackPoint = .025f;
        [Tooltip("Linear white reference after the effect exposure. Start near 0.26 for the formal map's dim white walls; this is not a display-space white threshold.")]
        [SerializeField, Range(.05f, 2)] private float whitePoint = .26f;
        [SerializeField, Range(.5f, 2)] private float contrast = 1.15f;
        [Tooltip("Retain continuous illumination gradients before the print contrast curve, so rounded forms and surface highlights keep their shading.")]
        [SerializeField, Range(0, 1)] private float shadingRetention = .35f;
        [SerializeField, Range(0, 1)] private float silverRelief = .4f;
        [Tooltip("Preserve the incoming HDR bright-light residual instead of clipping it to paper white. This does not invent a new reflection or internal marble texture.")]
        [SerializeField, Range(0, 2)] private float highlightRetention = 1;
        [Header("清晰线条与黑白分界 / Graphic edges")]
        [Tooltip("Separate midtones into stronger black and white masses while retaining a smooth transition.")]
        [SerializeField, Range(0, 1)] private float tonalSeparation = .88f;
        [Tooltip("Thin foreground-side ink contour from relative depth discontinuities.")]
        [SerializeField, Range(0, 1)] private float contourStrength = .48f;
        [SerializeField, Range(.5f, 2)] private float contourWidth = .85f;
        [Header("纯钴蓝点色 / Cobalt accents")]
        [SerializeField, ColorUsage(false)] private Color accentColor = new Color(0, .04f, 1);
        [SerializeField, Range(0, 1)] private float accentStrength = .92f;
        [SerializeField, Range(0, 1)] private float accentThreshold = .48f;
        // Keep old serialized values readable, but they no longer generate cloud
        // mottling, screen grain, local exposure lift or a second optical halo.
        [SerializeField, HideInInspector] private float grain, localAdaptation, halation, halationThreshold, halationRadius, surfacePatina;
        [SerializeField] private bool previewInSceneView;

        private Material runtimeMaterial;
        private SilverPrintPass pass;
        private static readonly int ToneId = Shader.PropertyToID("_SilverTone");
        private static readonly int TextureId = Shader.PropertyToID("_SilverTexture");
        private static readonly int AccentId = Shader.PropertyToID("_SilverAccent");
        private static readonly int AccentColorId = Shader.PropertyToID("_SilverAccentColor");
        private static readonly int GraphicId = Shader.PropertyToID("_SilverGraphic");

        public float Intensity { get => intensity; set => intensity = Mathf.Clamp01(value); }
        private float EffectiveIntensity
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (Application.isPlaying && DevelopmentComparisonEnabled && intensity <= 0) return 1;
#endif
                return intensity;
            }
        }
        public bool UsesFaithfulLighting
        {
            get
            {
                bool faithful = faithfulLighting;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (Application.isPlaying && DevelopmentComparisonEnabled) faithful = false;
#endif
                return faithful;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static bool DevelopmentComparisonEnabled { get; private set; }
        private static readonly List<MoonSilverPrint> DevelopmentEnabledEffects = new List<MoonSilverPrint>();
        /// <summary>Legacy alias for temporary comparison, not an instance's effective authored style.</summary>
        [System.Obsolete("Use DevelopmentComparisonEnabled; UsesFaithfulLighting reports the effective instance mode.")]
        public static bool DevelopmentStyleEnabled => DevelopmentComparisonEnabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDevelopmentStyle()
        {
            DevelopmentComparisonEnabled = false;
            foreach (var effect in DevelopmentEnabledEffects)
                if (effect) effect.enabled = false;
            DevelopmentEnabledEffects.Clear();
        }

        public static void ToggleDevelopmentStyle()
        {
            if (!Application.isPlaying) return;
            if (DevelopmentComparisonEnabled)
            {
                ResetDevelopmentStyle();
                Debug.Log("[环境外观 / F1] 恢复场景外观设置");
                return;
            }

            if (BeginDevelopmentPreview(FindObjectsByType<MoonSilverPrint>(FindObjectsSortMode.None)))
                Debug.Log("[环境外观 / F1] 临时开启风格化预览，再按 F1 恢复");
        }

        private static bool BeginDevelopmentPreview(MoonSilverPrint[] effects)
        {
            // The formal map deliberately saves this experiment disabled at intensity 0.
            // Temporarily subscribe it for this session; never change its authored intensity.
            var available = new List<MoonSilverPrint>();
            foreach (var effect in effects)
                if (effect && effect.material) available.Add(effect);
            if (available.Count == 0)
            {
                Debug.LogWarning("[环境外观 / F1] 当前地图没有配置风格化材质的 MoonSilverPrint 组件。");
                return false;
            }
            DevelopmentComparisonEnabled = true;
            foreach (var effect in available)
                if (!effect.enabled)
                {
                    DevelopmentEnabledEffects.Add(effect);
                    effect.enabled = true;
                }
            return true;
        }
#endif

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            float effectiveIntensity = EffectiveIntensity;
            if (UsesFaithfulLighting || effectiveIntensity <= 0 || !material || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return;
            if (camera.cameraType != CameraType.Game &&
                !(previewInSceneView && camera.cameraType == CameraType.SceneView)) return;
            // Isolated menu portraits and UI render textures are Game cameras too.
            int subjectLayers = LayerMask.GetMask("UI", "ServerPlayer", "ClientPlayer", "FirstPersonOverlay");
            if ((camera.cullingMask & ~subjectLayers) == 0) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return;
            // Scene View cameras do not carry UniversalAdditionalCameraData; use URP's default renderer.
            camera.TryGetComponent<UniversalAdditionalCameraData>(out var data);
            if (data && data.renderType == CameraRenderType.Overlay) return;
            var renderer = data ? data.scriptableRenderer : pipeline.scriptableRenderer;
            if (renderer == null) return;

            if (!runtimeMaterial || runtimeMaterial.shader != material.shader)
            {
                CoreUtils.Destroy(runtimeMaterial);
                runtimeMaterial = new Material(material) { name = "Silver print (runtime)", hideFlags = HideFlags.HideAndDontSave };
                pass = new SilverPrintPass(runtimeMaterial);
            }
            runtimeMaterial.SetFloat("_SilverFaithful", 0);
            runtimeMaterial.SetVector(ToneId, new Vector4(exposure, blackPoint, whitePoint, contrast));
            runtimeMaterial.SetVector(TextureId, new Vector4(silverRelief, 0, effectiveIntensity, highlightRetention));
            runtimeMaterial.SetVector(AccentId, new Vector4(accentStrength, accentThreshold, 0, 0));
            runtimeMaterial.SetVector(GraphicId, new Vector4(tonalSeparation, contourStrength, contourWidth, shadingRetention));
            // This authored swatch is sRGB; the HDR shader explicitly linearizes it.
            runtimeMaterial.SetVector(AccentColorId, new Vector4(accentColor.r, accentColor.g, accentColor.b, 1));
            renderer.EnqueuePass(pass);
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            CoreUtils.Destroy(runtimeMaterial);
            runtimeMaterial = null;
            pass = null;
        }

        private sealed class SilverPrintPass : ScriptableRenderPass
        {
            private readonly Material grade;
            private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            private static readonly int SourceId = Shader.PropertyToID("_BlitTexture");
            private static readonly int ScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int SourceTexelId = Shader.PropertyToID("_SilverSourceTexel");
            private static readonly int DepthId = Shader.PropertyToID("_SilverDepth");
            private static readonly int DepthScaleId = Shader.PropertyToID("_SilverDepthScaleBias");
            private static readonly int HasDepthId = Shader.PropertyToID("_SilverHasDepth");
            private static readonly int ProtectionId = Shader.PropertyToID("_SilverProtection");
            private static readonly int ProtectionScaleId = Shader.PropertyToID("_SilverProtectionScaleBias");
            private static readonly int EnvironmentDepthId = Shader.PropertyToID("_MoonEnvironmentSceneDepth");
            private static readonly int EnvironmentDepthScaleId = Shader.PropertyToID("_MoonEnvironmentDepthScaleBias");
            private static readonly List<ShaderTagId> EnvironmentTags = new List<ShaderTagId>
            {
                new ShaderTagId("MoonEnvironmentScope")
            };

            public SilverPrintPass(Material grade)
            {
                this.grade = grade;
                // Treat opaque environment HDR before transparent effects, bloom and tonemapping.
                // Final-depth masks at599 cannot separate already-composited transparent colors.
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 1);
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                // Fail closed: no native environment coverage/depth means no treatment.
                if (grade.GetFloat("_SilverFaithful") > .5f || resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                var source = resources.activeColorTexture;
                var protection = EnvironmentScope(renderGraph, frameData, resources, source);
                var descriptor = renderGraph.GetTextureDesc(source);
                descriptor.name = "Moon silver print";
                descriptor.clearBuffer = false;
                var destination = renderGraph.CreateTexture(descriptor);
                // One HDR composite after the positive scope mask; no local meter,
                // blur chain, artificial cloud texture or secondary bloom.
                AddPass(renderGraph, source, destination, resources.cameraDepthTexture, protection);
                resources.cameraColor = destination;
            }

            private void AddPass(RenderGraph graph, TextureHandle source, TextureHandle destination,
                TextureHandle depth, TextureHandle protection)
            {
                using var builder = graph.AddRasterRenderPass<PassData>("Black white cobalt HDR finish", out var data);
                data.source = source;
                data.destination = destination;
                data.depth = depth;
                data.protection = protection;
                data.material = grade;
                data.properties = properties;
                var descriptor = graph.GetTextureDesc(source);
                data.texel = new Vector4(1f / descriptor.width, 1f / descriptor.height, descriptor.width, descriptor.height);
                builder.UseTexture(source, AccessFlags.Read);
                if (depth.IsValid()) builder.UseTexture(depth, AccessFlags.Read);
                if (protection.IsValid()) builder.UseTexture(protection, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    var block = passData.properties;
                    block.Clear();
                    block.SetTexture(SourceId, passData.source);
                    block.SetVector(SourceTexelId, passData.texel);
                    block.SetVector(ScaleBiasId, ScaleBias(context, passData.source, passData.destination));
                    block.SetFloat(HasDepthId, passData.depth.IsValid() ? 1 : 0);
                    if (passData.depth.IsValid())
                    {
                        block.SetTexture(DepthId, passData.depth);
                        block.SetVector(DepthScaleId, ScaleBias(context, passData.depth, passData.destination));
                    }
                    if (passData.protection.IsValid())
                    {
                        block.SetTexture(ProtectionId, passData.protection);
                        block.SetVector(ProtectionScaleId, ScaleBias(context, passData.protection, passData.destination));
                    }
                    context.cmd.DrawProcedural(Matrix4x4.identity, passData.material, 0,
                        MeshTopology.Triangles, 3, 1, block);
                });
            }

            private TextureHandle EnvironmentScope(RenderGraph graph, ContextContainer frameData,
                UniversalResourceData resources, TextureHandle source)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var descriptor = graph.GetTextureDesc(source);
                descriptor.name = "Explicit opaque environment material coverage";
                descriptor.colorFormat = GraphicsFormat.R8_UNorm;
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.clearBuffer = true;
                descriptor.clearColor = Color.black;
                descriptor.filterMode = FilterMode.Point;
                var mask = graph.CreateTexture(descriptor);
                using var builder = graph.AddRasterRenderPass<EnvironmentPassData>("Native environment material scope", out var data);
                data.mask = mask;
                data.depth = resources.cameraDepthTexture;
                // Native shader passes preserve Terrain heightmap instancing, LOD and holes.
                // Stock Lit, actors, weapons, pickups, particles and UI do not opt in.
                // No override material, manual DrawRenderer or rendering/light-layer changes.
                var drawing = RenderingUtils.CreateDrawingSettings(EnvironmentTags, frameData.Get<UniversalRenderingData>(),
                    cameraData, frameData.Get<UniversalLightData>(), SortingCriteria.CommonOpaque);
                data.environment = graph.CreateRendererList(new RendererListParams(
                    frameData.Get<UniversalRenderingData>().cullResults, drawing, new FilteringSettings(RenderQueueRange.opaque)));
                builder.UseRendererList(data.environment);
                builder.UseTexture(data.depth, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                // These names are consumed solely by native coverage shaders in this draw.
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (EnvironmentPassData d, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(EnvironmentDepthId, d.depth);
                    context.cmd.SetGlobalVector(EnvironmentDepthScaleId, ScaleBias(context, d.depth, d.mask));
                    context.cmd.DrawRendererList(d.environment);
                });
                return mask;
            }

            private static Vector4 ScaleBias(RasterGraphContext context, TextureHandle source, TextureHandle destination)
            {
                RTHandle handle = source;
                Vector2 scale = handle.useScaling ? (Vector2)handle.rtHandleProperties.rtHandleScale : Vector2.one;
                bool flip = context.GetTextureUVOrigin(in source) != context.GetTextureUVOrigin(in destination);
                return flip ? new Vector4(scale.x, -scale.y, 0, scale.y) : new Vector4(scale.x, scale.y, 0, 0);
            }

            private sealed class PassData
            {
                public TextureHandle source, destination, depth, protection;
                public Material material;
                public MaterialPropertyBlock properties;
                public Vector4 texel;
            }

            private sealed class EnvironmentPassData
            {
                public TextureHandle mask, depth;
                public RendererListHandle environment;
            }
        }
    }
}
