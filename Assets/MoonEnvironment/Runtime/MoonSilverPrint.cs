using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Scene-owned silver print grade, before damage overlays and screen-space UI.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MoonSilverPrint : MonoBehaviour
    {
        [SerializeField] private Material material;
        [Header("黑白银盐 / Silver print")]
        [SerializeField, Range(0, 1)] private float intensity = 1;
        [SerializeField, Range(-2, 2)] private float exposure = .3f;
        [SerializeField, Range(0, .45f)] private float blackPoint = .035f;
        [SerializeField, Range(.5f, 1)] private float whitePoint = .82f;
        [SerializeField, Range(.5f, 2)] private float contrast = 1.15f;
        [SerializeField, Range(0, 1)] private float silverRelief = .32f;
        [SerializeField, Range(0, .1f)] private float grain = .022f;
        [Header("纯钴蓝点色 / Cobalt accents")]
        [SerializeField, ColorUsage(false)] private Color accentColor = new Color(0, .04f, 1);
        [SerializeField, Range(0, 1)] private float accentStrength = .92f;
        [SerializeField, Range(0, 1)] private float accentThreshold = .22f;
        [SerializeField] private bool previewInSceneView = true;

        private Material runtimeMaterial;
        private SilverPrintPass pass;
        private static readonly int ToneId = Shader.PropertyToID("_SilverTone");
        private static readonly int TextureId = Shader.PropertyToID("_SilverTexture");
        private static readonly int AccentId = Shader.PropertyToID("_SilverAccent");
        private static readonly int AccentColorId = Shader.PropertyToID("_SilverAccentColor");

        public float Intensity { get => intensity; set => intensity = Mathf.Clamp01(value); }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (intensity <= 0 || !material || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return;
            if (camera.cameraType != CameraType.Game &&
                !(previewInSceneView && camera.cameraType == CameraType.SceneView)) return;
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
            runtimeMaterial.SetVector(ToneId, new Vector4(exposure, blackPoint, whitePoint, contrast));
            runtimeMaterial.SetVector(TextureId, new Vector4(silverRelief, grain, intensity, 0));
            runtimeMaterial.SetVector(AccentId, new Vector4(accentStrength, accentThreshold, 0, 0));
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

            public SilverPrintPass(Material grade)
            {
                this.grade = grade;
                // Tonemapping is finished, but gameplay damage overlays at 600 retain their warning colors.
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.AfterRenderingPostProcessing - 1);
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var source = resources.activeColorTexture;
                var descriptor = renderGraph.GetTextureDesc(source);
                descriptor.name = "Moon silver print";
                descriptor.clearBuffer = false;
                var destination = renderGraph.CreateTexture(descriptor);
                var parameters = new RenderGraphUtils.BlitMaterialParameters(source, destination, grade, 0);
                renderGraph.AddBlitPass(parameters, passName: "Moon silver print");
                resources.cameraColor = destination;
            }
        }
    }
}
