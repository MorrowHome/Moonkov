using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Scene-owned silver print grade, before damage overlays and screen-space UI.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MoonSilverPrint : MonoBehaviour
    {
        [SerializeField] private Material material;
        [Tooltip("Depth-tested actor/UI protection; required so the environment grade cannot recolor subjects.")]
        [SerializeField] private Material subjectMaskMaterial;
        [Header("黑白银盐 / Silver print")]
        [SerializeField, Range(0, 1)] private float intensity = 1;
        [SerializeField, Range(-2, 2)] private float exposure = .3f;
        [SerializeField, Range(0, .45f)] private float blackPoint = .035f;
        [SerializeField, Range(.5f, 1)] private float whitePoint = .82f;
        [SerializeField, Range(.5f, 2)] private float contrast = 1.15f;
        [Tooltip("Retain continuous illumination gradients before the print contrast curve, so rounded forms and surface highlights keep their shading.")]
        [SerializeField, Range(0, 1)] private float shadingRetention;
        [SerializeField, Range(0, 1)] private float silverRelief = .32f;
        [SerializeField, Range(0, .1f)] private float grain = .022f;
        [Header("清晰线条与黑白分界 / Graphic edges")]
        [Tooltip("Separate midtones into stronger black and white masses while retaining a smooth transition.")]
        [SerializeField, Range(0, 1)] private float tonalSeparation = .5f;
        [Tooltip("Thin foreground-side ink contour from relative depth discontinuities.")]
        [SerializeField, Range(0, 1)] private float contourStrength = .65f;
        [SerializeField, Range(.5f, 2)] private float contourWidth = 1;
        [Header("纯钴蓝点色 / Cobalt accents")]
        [SerializeField, ColorUsage(false)] private Color accentColor = new Color(0, .04f, 1);
        [SerializeField, Range(0, 1)] private float accentStrength = .92f;
        [SerializeField, Range(0, 1)] private float accentThreshold = .22f;
        [Header("光影与银盐晕光 / Light and halation")]
        [Tooltip("Bounded local shadow lift. Real black stays black; no temporal exposure pumping.")]
        [SerializeField, Range(0, 1)] private float localAdaptation = .45f;
        [SerializeField, Range(0, .5f)] private float halation = .16f;
        [SerializeField, Range(.5f, .98f)] private float halationThreshold = .78f;
        [SerializeField, Range(.5f, 4)] private float halationRadius = 1.8f;
        [Tooltip("World-anchored silver mottling; does not slide over stationary architecture when the camera moves.")]
        [SerializeField, Range(0, .3f)] private float surfacePatina = .1f;
        [SerializeField] private bool previewInSceneView = true;

        private Material runtimeMaterial;
        private SilverPrintPass pass;
        private static readonly int ToneId = Shader.PropertyToID("_SilverTone");
        private static readonly int TextureId = Shader.PropertyToID("_SilverTexture");
        private static readonly int AccentId = Shader.PropertyToID("_SilverAccent");
        private static readonly int AccentColorId = Shader.PropertyToID("_SilverAccentColor");
        private static readonly int OpticsId = Shader.PropertyToID("_SilverOptics");
        private static readonly int SurfaceId = Shader.PropertyToID("_SilverSurface");
        private static readonly int GraphicId = Shader.PropertyToID("_SilverGraphic");

        public float Intensity { get => intensity; set => intensity = Mathf.Clamp01(value); }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (intensity <= 0 || !material || !subjectMaskMaterial || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
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
            pass.SubjectMaskMaterial = subjectMaskMaterial;
            pass.SubjectLayers = subjectLayers;
            runtimeMaterial.SetVector(ToneId, new Vector4(exposure, blackPoint, whitePoint, contrast));
            runtimeMaterial.SetVector(TextureId, new Vector4(silverRelief, grain, intensity, 0));
            runtimeMaterial.SetVector(AccentId, new Vector4(accentStrength, accentThreshold, 0, 0));
            runtimeMaterial.SetVector(OpticsId, new Vector4(halation, halationThreshold, halationRadius, localAdaptation));
            runtimeMaterial.SetVector(SurfaceId, new Vector4(surfacePatina, 0, 0, 0));
            runtimeMaterial.SetVector(GraphicId, new Vector4(tonalSeparation, contourStrength, contourWidth, shadingRetention));
            // Shader performs its art direction in display space; do not implicitly linearize this swatch.
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
            private readonly List<Renderer> subjects = new List<Renderer>();
            private readonly List<Material> subjectMaterials = new List<Material>();
            private readonly Plane[] frustum = new Plane[6];
            public Material SubjectMaskMaterial { private get; set; }
            public int SubjectLayers { private get; set; }
            private static readonly int SourceId = Shader.PropertyToID("_BlitTexture");
            private static readonly int ScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int SourceTexelId = Shader.PropertyToID("_SilverSourceTexel");
            private static readonly int MeterId = Shader.PropertyToID("_SilverMeter");
            private static readonly int MeterScaleId = Shader.PropertyToID("_SilverMeterScaleBias");
            private static readonly int DepthId = Shader.PropertyToID("_SilverDepth");
            private static readonly int DepthScaleId = Shader.PropertyToID("_SilverDepthScaleBias");
            private static readonly int HasDepthId = Shader.PropertyToID("_SilverHasDepth");
            private static readonly int ProtectionId = Shader.PropertyToID("_SilverProtection");
            private static readonly int ProtectionScaleId = Shader.PropertyToID("_SilverProtectionScaleBias");
            private static readonly int SubjectDepthId = Shader.PropertyToID("_MoonSubjectSceneDepth");
            private static readonly int SubjectDepthScaleId = Shader.PropertyToID("_MoonSubjectDepthScaleBias");
            private static readonly List<ShaderTagId> SubjectTags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit")
            };
            private static readonly List<ShaderTagId> SubjectCoverageTags = new List<ShaderTagId>
            {
                new ShaderTagId("MoonSilverPrintSubjectMask")
            };

            public SilverPrintPass(Material grade)
            {
                this.grade = grade;
                // Tonemapping is finished, but gameplay damage overlays at 600 retain their warning colors.
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.AfterRenderingPostProcessing - 1);
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                // Fail closed if the resolved scene depth is unavailable: never apply a
                // whole-screen grade without being able to protect visible actors.
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                var source = resources.activeColorTexture;
                var protection = ProtectSubjects(renderGraph, frameData, resources, source);
                var descriptor = renderGraph.GetTextureDesc(source);
                descriptor.name = "Moon silver print";
                descriptor.clearBuffer = false;
                var destination = renderGraph.CreateTexture(descriptor);
                // Graph-owned quarter-size buffers serve both the local tone meter and optical halo.
                // Keep alpha: it holds the extracted highlights independently from mean scene color.
                descriptor.width = Mathf.Max(1, descriptor.width / 4);
                descriptor.height = Mathf.Max(1, descriptor.height / 4);
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                descriptor.filterMode = FilterMode.Bilinear;
                descriptor.name = "Silver light field";
                var lightField = renderGraph.CreateTexture(descriptor);
                descriptor.name = "Silver horizontal blur";
                var horizontal = renderGraph.CreateTexture(descriptor);
                descriptor.name = "Silver local light";
                var localLight = renderGraph.CreateTexture(descriptor);
                AddPass(renderGraph, source, lightField, TextureHandle.nullHandle, 1, "Silver light extraction");
                AddPass(renderGraph, lightField, horizontal, TextureHandle.nullHandle, 2, "Silver horizontal blur");
                AddPass(renderGraph, horizontal, localLight, TextureHandle.nullHandle, 3, "Silver vertical blur");
                AddPass(renderGraph, source, destination, localLight, 0, "Moon silver print", resources.cameraDepthTexture, protection);
                resources.cameraColor = destination;
            }

            private void AddPass(RenderGraph graph, TextureHandle source, TextureHandle destination,
                TextureHandle meter, int shaderPass, string name, TextureHandle depth = default, TextureHandle protection = default)
            {
                using var builder = graph.AddRasterRenderPass<PassData>(name, out var data);
                data.source = source;
                data.destination = destination;
                data.meter = meter;
                data.depth = depth;
                data.protection = protection;
                data.material = grade;
                data.properties = properties;
                data.shaderPass = shaderPass;
                var descriptor = graph.GetTextureDesc(source);
                data.texel = new Vector4(1f / descriptor.width, 1f / descriptor.height, descriptor.width, descriptor.height);
                builder.UseTexture(source, AccessFlags.Read);
                if (meter.IsValid()) builder.UseTexture(meter, AccessFlags.Read);
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
                    if (passData.meter.IsValid())
                    {
                        block.SetTexture(MeterId, passData.meter);
                        block.SetVector(MeterScaleId, ScaleBias(context, passData.meter, passData.destination));
                    }
                    if (passData.protection.IsValid())
                    {
                        block.SetTexture(ProtectionId, passData.protection);
                        block.SetVector(ProtectionScaleId, ScaleBias(context, passData.protection, passData.destination));
                    }
                    context.cmd.DrawProcedural(Matrix4x4.identity, passData.material, passData.shaderPass,
                        MeshTopology.Triangles, 3, 1, block);
                });
            }

            private TextureHandle ProtectSubjects(RenderGraph graph, ContextContainer frameData,
                UniversalResourceData resources, TextureHandle source)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                GeometryUtility.CalculateFrustumPlanes(cameraData.camera, frustum);
                MoonSilverPrintSubject.Collect(cameraData.camera, frustum, subjects);
                var descriptor = graph.GetTextureDesc(source);
                descriptor.name = "Silver print protected subjects";
                descriptor.colorFormat = GraphicsFormat.R8_UNorm;
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.clearBuffer = true;
                descriptor.clearColor = Color.black;
                descriptor.filterMode = FilterMode.Point;
                var mask = graph.CreateTexture(descriptor);
                using var builder = graph.AddRasterRenderPass<SubjectPassData>("Silver print actor/UI protection", out var data);
                data.mask = mask;
                data.depth = resources.cameraDepthTexture;
                data.material = SubjectMaskMaterial;
                data.renderers = subjects;
                data.materials = subjectMaterials;
                // Existing presentation layers cover world UI/legacy client meshes;
                // registered ownership covers Default-layer skin, weapons and alien rigs.
                var drawing = RenderingUtils.CreateDrawingSettings(SubjectTags, frameData.Get<UniversalRenderingData>(),
                    cameraData, frameData.Get<UniversalLightData>(), SortingCriteria.CommonOpaque);
                drawing.overrideMaterial = SubjectMaskMaterial;
                drawing.overrideMaterialPassIndex = 0;
                var filtering = new FilteringSettings(RenderQueueRange.opaque, SubjectLayers);
                data.layerSubjects = graph.CreateRendererList(new RendererListParams(
                    frameData.Get<UniversalRenderingData>().cullResults, drawing, filtering));
                var coverageDrawing = RenderingUtils.CreateDrawingSettings(SubjectCoverageTags,
                    frameData.Get<UniversalRenderingData>(), cameraData, frameData.Get<UniversalLightData>(), SortingCriteria.CommonOpaque);
                // These actor shaders explicitly opt in to native coverage. Drawing
                // through the cull results also preserves SpriteRenderer bindings.
                var coverageFiltering = new FilteringSettings(RenderQueueRange.all);
                data.coverageSubjects = graph.CreateRendererList(new RendererListParams(
                    frameData.Get<UniversalRenderingData>().cullResults, coverageDrawing, coverageFiltering));
                builder.UseRendererList(data.layerSubjects);
                builder.UseRendererList(data.coverageSubjects);
                builder.UseTexture(data.depth, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (SubjectPassData passData, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(SubjectDepthId, passData.depth);
                    context.cmd.SetGlobalVector(SubjectDepthScaleId, ScaleBias(context, passData.depth, passData.mask));
                    context.cmd.DrawRendererList(passData.layerSubjects);
                    context.cmd.DrawRendererList(passData.coverageSubjects);
                    foreach (var renderer in passData.renderers)
                    {
                        if (!renderer) continue;
                        renderer.GetSharedMaterials(passData.materials);
                        for (int i = 0; i < passData.materials.Count; i++)
                        {
                            var sourceMaterial = passData.materials[i];
                            if (!sourceMaterial) continue;
                            int coveragePass = sourceMaterial.FindPass("SilverPrintSubjectMask");
                            if (coveragePass >= 0) continue; // Drawn with its native renderer list above.
                            if (sourceMaterial.renderQueue <= (int)RenderQueue.GeometryLast ||
                                (sourceMaterial.HasProperty("_ZWrite") && sourceMaterial.GetFloat("_ZWrite") > .5f))
                                context.cmd.DrawRenderer(renderer, passData.material, i, 0);
                            // Unknown non-depth-writing shaders cannot safely use a solid
                            // replacement mask. They must supply their actual coverage pass.
                        }
                    }
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
                public TextureHandle source, destination, meter, depth, protection;
                public Material material;
                public MaterialPropertyBlock properties;
                public Vector4 texel;
                public int shaderPass;
            }

            private sealed class SubjectPassData
            {
                public TextureHandle mask, depth;
                public RendererListHandle layerSubjects, coverageSubjects;
                public Material material;
                public List<Renderer> renderers;
                public List<Material> materials;
            }
        }
    }
}
