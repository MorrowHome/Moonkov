using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS
{
    // The live world is softened before UI Toolkit draws its sharp text and item icons.
    public static class RaidInventoryBlur
    {
        private static Driver s_Driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Clear();

        public static void Clear() { s_Driver?.Dispose(); s_Driver = null; }

        public static void Publish(Camera camera)
        {
            if (!camera || !camera.isActiveAndEnabled || camera.cameraType != CameraType.Game ||
                camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.renderType == CameraRenderType.Overlay)
            { Clear(); return; }
            if (s_Driver != null && s_Driver.Camera != camera) Clear();
            s_Driver ??= new Driver(camera);
            s_Driver.PublishedAt = Time.realtimeSinceStartupAsDouble;
        }

        private sealed class Driver : IDisposable
        {
            internal readonly Camera Camera;
            internal double PublishedAt;
            private readonly Material m_Material;
            private readonly BlurPass m_Pass;
            private bool m_Disposed;

            internal Driver(Camera camera)
            {
                Camera = camera;
                var shader = Resources.Load<Shader>("Moonkov/RaidInventoryBlur");
                if (shader && shader.isSupported)
                {
                    m_Material = new Material(shader) { name = "Live inventory backdrop", hideFlags = HideFlags.HideAndDontSave };
                    m_Pass = new BlurPass(m_Material, camera);
                }
                RenderPipelineManager.beginCameraRendering += BeginCamera;
            }

            private void BeginCamera(ScriptableRenderContext context, Camera camera)
            {
                if (m_Disposed) return;
                if (!Camera || !Camera.isActiveAndEnabled || Time.realtimeSinceStartupAsDouble - PublishedAt > .25)
                { Clear(); return; }
                if (camera != Camera || m_Pass == null ||
                    !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return;
                camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional);
                if (additional && additional.renderType == CameraRenderType.Overlay) return;
                (additional ? additional.scriptableRenderer : pipeline.scriptableRenderer)?.EnqueuePass(m_Pass);
            }

            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                CoreUtils.Destroy(m_Material);
            }
        }

        private sealed class BlurPass : ScriptableRenderPass
        {
            private readonly Material m_Material;
            private readonly Camera m_Owner;
            private readonly MaterialPropertyBlock m_Horizontal = new(), m_Vertical = new();
            private static readonly int SourceId = Shader.PropertyToID("_BlitTexture");
            private static readonly int ScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int StepId = Shader.PropertyToID("_BlurStep");

            internal BlurPass(Material material, Camera owner)
            {
                m_Material = material; m_Owner = owner;
                // After other world effects, before URP composites the overlay UI.
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing + 1;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (frameData.Get<UniversalCameraData>().camera != m_Owner || resources.isActiveTargetBackBuffer || !m_Material) return;
                var source = resources.activeColorTexture;
                if (!source.IsValid()) return;
                var descriptor = graph.GetTextureDesc(source);
                int width = descriptor.width, height = descriptor.height;
                descriptor.clearBuffer = false;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.bindTextureMS = false;
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.filterMode = FilterMode.Bilinear;
                descriptor.name = "Inventory blur / horizontal half resolution";
                descriptor.width = Mathf.Max(1, width / 2); descriptor.height = Mathf.Max(1, height / 2);
                var intermediate = graph.CreateTexture(descriptor);
                descriptor.name = "Inventory blur / live world";
                descriptor.width = width; descriptor.height = height;
                var destination = graph.CreateTexture(descriptor);
                // About four pixels of softening at 1080p. Silhouettes and motion remain readable.
                float radius = 2.4f * height / 1080f;
                Record(graph, source, intermediate, m_Horizontal, new Vector2(radius / width, 0), "Inventory blur / horizontal");
                Record(graph, intermediate, destination, m_Vertical, new Vector2(0, radius / height), "Inventory blur / vertical");
                resources.cameraColor = destination;
            }

            private void Record(RenderGraph graph, TextureHandle source, TextureHandle destination,
                MaterialPropertyBlock properties, Vector2 step, string name)
            {
                using var builder = graph.AddRasterRenderPass<PassData>(name, out var data);
                data.Source = source; data.Destination = destination; data.Material = m_Material;
                data.Properties = properties; data.Step = step;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    // The material blit helper does not account for differing texture UV origins.
                    RTHandle handle = d.Source;
                    var scale = handle.useScaling ? (Vector2)handle.rtHandleProperties.rtHandleScale : Vector2.one;
                    bool flip = context.GetTextureUVOrigin(in d.Source) != context.GetTextureUVOrigin(in d.Destination);
                    var block = d.Properties; block.Clear();
                    Texture texture = d.Source;
                    block.SetTexture(SourceId, texture);
                    block.SetVector(ScaleBiasId, flip ? new Vector4(scale.x, -scale.y, 0, scale.y) : new Vector4(scale.x, scale.y, 0, 0));
                    block.SetVector(StepId, new Vector4(d.Step.x * scale.x, d.Step.y * scale.y, 0, 0));
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 0, MeshTopology.Triangles, 3, 1, block);
                });
            }

            private sealed class PassData
            {
                internal TextureHandle Source, Destination;
                internal Material Material;
                internal MaterialPropertyBlock Properties;
                internal Vector2 Step;
            }
        }
    }
}
