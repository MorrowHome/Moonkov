using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Apply the source OCIO display transform to one camera's HDR scene, before overlay UI.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class SourceAgXCamera : MonoBehaviour
    {
        [SerializeField] private Material displayTransform;
        [SerializeField] private bool allowOverlay;
        private Camera owner;
        private DisplayPass pass;

        public Material DisplayTransform => displayTransform;
        public bool AllowsOverlay => allowOverlay;

        public void Configure(Material material, bool applyToOverlay = false)
        {
            if (displayTransform == material && allowOverlay == applyToOverlay) return;
            displayTransform = material; allowOverlay = applyToOverlay; pass = null;
        }

        private void OnEnable()
        {
            owner = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += Enqueue;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Enqueue;
            pass = null;
        }

        internal void Enqueue(ScriptableRenderContext context, Camera camera)
        {
            if (camera != owner || !displayTransform || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return;
            var additional = camera.GetUniversalAdditionalCameraData();
            if (additional.renderType == CameraRenderType.Overlay && !allowOverlay) return;
            if (pass == null) pass = new DisplayPass(displayTransform);
            additional.scriptableRenderer.EnqueuePass(pass);
        }

        private sealed class DisplayPass : ScriptableRenderPass
        {
            private readonly Material material;
            private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            private static readonly int Source = Shader.PropertyToID("_BlitTexture");
            private static readonly int Scale = Shader.PropertyToID("_BlitScaleBias");

            public DisplayPass(Material material)
            {
                this.material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var source = resources.activeColorTexture;
                var descriptor = graph.GetTextureDesc(source);
                descriptor.name = "Source AgX display transform";
                descriptor.clearBuffer = false;
                var destination = graph.CreateTexture(descriptor);
                using var builder = graph.AddRasterRenderPass<Data>("Source OCIO AgX", out var data);
                data.source = source; data.destination = destination;
                data.material = material; data.properties = properties;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (Data d, RasterGraphContext context) =>
                {
                    RTHandle handle = d.source;
                    Vector2 scale = handle.useScaling ? (Vector2)handle.rtHandleProperties.rtHandleScale : Vector2.one;
                    bool flip = context.GetTextureUVOrigin(in d.source) != context.GetTextureUVOrigin(in d.destination);
                    d.properties.Clear();
                    d.properties.SetTexture(Source, d.source);
                    d.properties.SetVector(Scale, flip ? new Vector4(scale.x, -scale.y, 0, scale.y) : new Vector4(scale.x, scale.y, 0, 0));
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, d.properties);
                });
                resources.cameraColor = destination;
            }

            private sealed class Data
            {
                public TextureHandle source, destination;
                public Material material;
                public MaterialPropertyBlock properties;
            }
        }
    }
}
