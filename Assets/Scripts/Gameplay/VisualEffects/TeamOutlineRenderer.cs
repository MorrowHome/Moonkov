using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS
{
    // Owned by one client world. Bounded character-only mask; no scene scan or extra camera.
    public sealed class TeamOutlineRenderer : IDisposable
    {
        public const int MaxDraws = 96;
        private Camera m_Camera;
        private Material m_Material;
        private OutlinePass m_Pass;
        private double m_PublishedAt;
        private bool m_Subscribed;
        private readonly Plane[] m_Frustum = new Plane[6];

        public void Publish(Camera camera, List<SkinnedMeshRenderer> renderers, double now)
        {
            if (!camera || camera.cameraType != CameraType.Game || renderers.Count == 0) { Clear(); return; }
            if (!m_Material)
            {
                var shader = Resources.Load<Shader>("Moonkov/TeamOutline");
                if (!shader || !shader.isSupported) return;
                m_Material = new Material(shader) { name = "Squad tracker outline", hideFlags = HideFlags.HideAndDontSave };
                m_Pass = new OutlinePass(m_Material);
            }
            m_Camera = camera; m_Pass.Owner = camera; m_PublishedAt = now; m_Pass.Count = 0;
            GeometryUtility.CalculateFrustumPlanes(camera, m_Frustum);
            foreach (var renderer in renderers)
            {
                if (!renderer || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                    !renderer.sharedMesh || !GeometryUtility.TestPlanesAABB(m_Frustum, renderer.bounds)) continue;
                for (int submesh = 0; submesh < renderer.sharedMesh.subMeshCount && m_Pass.Count < MaxDraws; submesh++)
                {
                    m_Pass.Renderers[m_Pass.Count] = renderer;
                    m_Pass.Submeshes[m_Pass.Count++] = submesh;
                }
            }
            if (!m_Subscribed) { RenderPipelineManager.beginCameraRendering += BeginCamera; m_Subscribed = true; }
        }
        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!m_Camera || Time.realtimeSinceStartupAsDouble - m_PublishedAt > .5) { Clear(); return; }
            if (camera != m_Camera || m_Pass.Count == 0) return;
            camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional);
            if (additional && additional.renderType == CameraRenderType.Overlay ||
                !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return;
            (additional ? additional.scriptableRenderer : pipeline.scriptableRenderer).EnqueuePass(m_Pass);
        }
        public void Dispose() => Clear();
        public void Clear()
        {
            if (m_Subscribed) RenderPipelineManager.beginCameraRendering -= BeginCamera;
            m_Subscribed = false; m_Camera = null; m_Pass = null;
            if (m_Material)
            { if (Application.isPlaying) UnityEngine.Object.Destroy(m_Material); else UnityEngine.Object.DestroyImmediate(m_Material); }
            m_Material = null;
        }

        private sealed class OutlinePass : ScriptableRenderPass
        {
            internal Camera Owner;
            internal int Count;
            internal readonly Renderer[] Renderers = new Renderer[MaxDraws];
            internal readonly int[] Submeshes = new int[MaxDraws];
            private readonly Material m_Material;
            private readonly MaterialPropertyBlock m_Properties = new();
            private static readonly int SourceId = Shader.PropertyToID("_BlitTexture"), MaskId = Shader.PropertyToID("_TeamMask"),
                SourceScaleId = Shader.PropertyToID("_BlitScaleBias"), MaskScaleId = Shader.PropertyToID("_TeamMaskScaleBias"),
                TexelId = Shader.PropertyToID("_TeamTexel");
            internal OutlinePass(Material material)
            {
                m_Material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                if (camera.camera != Owner || resources.isActiveTargetBackBuffer || !m_Material || Count == 0) return;
                var source = resources.activeColorTexture;
                var descriptor = graph.GetTextureDesc(source);
                descriptor.name = "Squad tracker composite"; descriptor.clearBuffer = false;
                var destination = graph.CreateTexture(descriptor);
                descriptor.name = "Squad tracker character mask"; descriptor.colorFormat = GraphicsFormat.R8_UNorm;
                descriptor.depthBufferBits = DepthBits.None; descriptor.msaaSamples = MSAASamples.None;
                descriptor.clearBuffer = true; descriptor.clearColor = Color.clear; descriptor.filterMode = FilterMode.Bilinear;
                var mask = graph.CreateTexture(descriptor);
                using (var builder = graph.AddRasterRenderPass<MaskData>("Squad tracker silhouettes", out var data))
                {
                    data.Material = m_Material; data.Renderers = Renderers; data.Submeshes = Submeshes; data.Count = Count;
                    builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (MaskData d, RasterGraphContext context) =>
                    {
                        for (int i = 0; i < d.Count; i++)
                            if (d.Renderers[i]) context.cmd.DrawRenderer(d.Renderers[i], d.Material, d.Submeshes[i], 0);
                    });
                }
                using (var builder = graph.AddRasterRenderPass<CompositeData>("Squad tracker teal outline", out var data))
                {
                    data.Source = source; data.Mask = mask; data.Destination = destination;
                    data.Material = m_Material; data.Properties = m_Properties;
                    // Keep the line near two output pixels even with the project's low world render scale.
                    data.Texel = new Vector4(1.8f / Mathf.Max(1, Owner.pixelWidth), 1.8f / Mathf.Max(1, Owner.pixelHeight), 0, 0);
                    builder.UseTexture(source, AccessFlags.Read); builder.UseTexture(mask, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (CompositeData d, RasterGraphContext context) =>
                    {
                        var block = d.Properties; block.Clear();
                        Texture sourceTexture = d.Source, maskTexture = d.Mask;
                        block.SetTexture(SourceId, sourceTexture); block.SetTexture(MaskId, maskTexture);
                        block.SetVector(SourceScaleId, ScaleBias(context, d.Source, d.Destination));
                        block.SetVector(MaskScaleId, ScaleBias(context, d.Mask, d.Destination));
                        block.SetVector(TexelId, d.Texel);
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 1, MeshTopology.Triangles, 3, 1, block);
                    });
                }
                resources.cameraColor = destination;
            }
            private static Vector4 ScaleBias(RasterGraphContext context, TextureHandle source, TextureHandle destination)
            {
                RTHandle handle = source;
                var scale = handle.useScaling ? (Vector2)handle.rtHandleProperties.rtHandleScale : Vector2.one;
                return context.GetTextureUVOrigin(in source) != context.GetTextureUVOrigin(in destination)
                    ? new Vector4(scale.x, -scale.y, 0, scale.y) : new Vector4(scale.x, scale.y, 0, 0);
            }
            private sealed class MaskData
            {
                internal Material Material;
                internal Renderer[] Renderers;
                internal int[] Submeshes;
                internal int Count;
            }
            private sealed class CompositeData
            {
                internal TextureHandle Source, Mask, Destination;
                internal Material Material;
                internal MaterialPropertyBlock Properties;
                internal Vector4 Texel;
            }
        }
    }
}
