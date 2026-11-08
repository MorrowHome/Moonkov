using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS
{
    // Collect from the raid's existing targets. One visible-only mask serves every nearby object.
    internal sealed class RaidInteractionOutline : ScriptableRenderPass, IDisposable
    {
        internal const float DiscoveryRange = 8f;
        private const int MaxDraws = 96;
        private static readonly int MaskId = Shader.PropertyToID("_InteractionMask");
        private static readonly int ScaleId = Shader.PropertyToID("_InteractionMaskScaleBias");
        private static readonly int EdgeId = Shader.PropertyToID("_InteractionEdge");
        private readonly Plane[] m_Frustum = new Plane[6];
        private readonly List<Renderer> m_Scratch = new();
        private readonly HashSet<Renderer> m_Seen = new();
        private readonly Draw[] m_Draws = new Draw[MaxDraws];
        private MaterialPropertyBlock m_Properties;
        private Camera m_Camera;
        private Material m_Material;
        private Vector3 m_Actor;
        private int m_Count;
        private float m_NextCollection;
        private double m_CollectedAt;
        private bool m_Subscribed, m_Steady;

        internal RaidInteractionOutline() => renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

        internal bool BeginCollection(Camera camera, Vector3 actor, bool allowed, float now)
        {
            if (!allowed || !camera || !camera.isActiveAndEnabled || camera.cameraType != CameraType.Game)
            { Hide(); return false; }
            if (camera == m_Camera && now < m_NextCollection && (actor - m_Actor).sqrMagnitude < .5f) return false;
            m_Camera = camera; m_Actor = actor; m_NextCollection = now + .1f;
            Array.Clear(m_Draws, 0, m_Count);
            m_Count = 0; m_Seen.Clear();
            GeometryUtility.CalculateFrustumPlanes(camera, m_Frustum);
            return true;
        }

        internal void Add(Transform target, Vector3 point, float range = DiscoveryRange)
        {
            if (!Nearby(target, point, range)) return;
            target.GetComponentsInChildren(false, m_Scratch);
            foreach (var renderer in m_Scratch)
            {
                if (m_Count == MaxDraws) break;
                if (!renderer || !renderer.enabled || renderer.forceRenderingOff ||
                    (m_Camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                    !GeometryUtility.TestPlanesAABB(m_Frustum, renderer.bounds) || !m_Seen.Add(renderer)) continue;
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh :
                    renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                if (!mesh) continue;
                for (int submesh = 0; submesh < mesh.subMeshCount && m_Count < MaxDraws; submesh++)
                    m_Draws[m_Count++] = new Draw(renderer, submesh);
            }
            m_Scratch.Clear();
        }

        internal void Add(Mesh mesh, Transform target, Vector3 point)
        {
            if (!mesh) { Add(target, point); return; }
            if (!Nearby(target, point, DiscoveryRange)) return;
            var matrix = target.localToWorldMatrix;
            var bounds = mesh.bounds;
            var half = bounds.extents;
            var x = matrix.MultiplyVector(Vector3.right * half.x);
            var y = matrix.MultiplyVector(Vector3.up * half.y);
            var z = matrix.MultiplyVector(Vector3.forward * half.z);
            bounds.center = matrix.MultiplyPoint3x4(bounds.center);
            bounds.extents = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            if (GeometryUtility.TestPlanesAABB(m_Frustum, bounds)) m_Draws[m_Count++] = new Draw(mesh, target);
        }

        private bool Nearby(Transform target, Vector3 point, float range) => target && target.gameObject.activeInHierarchy &&
            m_Count < MaxDraws && (point - m_Actor).sqrMagnitude <= range * range;

        internal void FinishCollection(bool steady)
        {
            m_Steady = steady;
            m_CollectedAt = Time.realtimeSinceStartupAsDouble;
            if (m_Count == 0 || m_Subscribed) return;
            var shader = Resources.Load<Shader>("Moonkov/RaidInteractionOutline");
            if (!shader || !shader.isSupported) return;
            // RaidHUD constructs this pass in a field initializer; native objects must wait until runtime.
            m_Properties ??= new MaterialPropertyBlock();
            m_Material = new Material(shader) { name = "Raid interaction outline", hideFlags = HideFlags.HideAndDontSave };
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            m_Subscribed = true;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != m_Camera || m_Count == 0 || Time.realtimeSinceStartupAsDouble - m_CollectedAt > .35) return;
            camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional);
            if (additional && additional.renderType == CameraRenderType.Overlay ||
                GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset pipeline) return;
            (additional ? additional.scriptableRenderer : pipeline.scriptableRenderer)?.EnqueuePass(this);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            var depth = resources.activeDepthTexture;
            if (camera.camera != m_Camera || !m_Material || m_Count == 0 || !depth.IsValid()) return;

            var descriptor = graph.GetTextureDesc(depth);
            descriptor.name = "Visible interactable mask";
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.colorFormat = descriptor.msaaSamples == MSAASamples.None ? GraphicsFormat.R8_UNorm : GraphicsFormat.R8G8B8A8_UNorm;
            descriptor.bindTextureMS = false;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            descriptor.filterMode = FilterMode.Bilinear;
            var mask = graph.CreateTexture(descriptor);
            using (var builder = graph.AddRasterRenderPass<MaskData>("Visible raid interactables", out var data))
            {
                data.Material = m_Material; data.Count = m_Count; data.Draws = m_Draws;
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
                builder.SetRenderFunc(static (MaskData d, RasterGraphContext context) =>
                {
                    for (int i = 0; i < d.Count; i++)
                    {
                        var draw = d.Draws[i];
                        if (draw.Renderer && draw.Renderer.enabled && draw.Renderer.gameObject.activeInHierarchy)
                            context.cmd.DrawRenderer(draw.Renderer, d.Material, draw.Submesh, 0);
                        else if (draw.Mesh && draw.Target && draw.Target.gameObject.activeInHierarchy)
                            context.cmd.DrawMesh(draw.Mesh, draw.Target.localToWorldMatrix, d.Material, 0, 0);
                    }
                });
            }

            using (var builder = graph.AddRasterRenderPass<EdgeData>("Raid interaction contours", out var data))
            {
                data.Mask = mask; data.Color = resources.activeColorTexture;
                data.Material = m_Material; data.Properties = m_Properties;
                // At a reduced world scale, the contour still needs a full mask pixel.
                float width = Mathf.Max(1, camera.cameraTargetDescriptor.width), height = Mathf.Max(1, camera.cameraTargetDescriptor.height);
                float pulse = m_Steady ? .68f : .64f + .24f * Mathf.Sin(Time.unscaledTime * 3.6f);
                data.Edge = new Vector4(1.2f / width, 1.2f / height, pulse, 0);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(data.Color, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (EdgeData d, RasterGraphContext context) =>
                {
                    var block = d.Properties; block.Clear();
                    Texture texture = d.Mask;
                    block.SetTexture(MaskId, texture);
                    block.SetVector(ScaleId, ScaleBias(context, d.Mask, d.Color));
                    block.SetVector(EdgeId, d.Edge);
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 1, MeshTopology.Triangles, 3, 1, block);
                });
            }
        }

        private static Vector4 ScaleBias(RasterGraphContext context, TextureHandle source, TextureHandle destination)
        {
            RTHandle texture = source;
            var scale = texture.useScaling ? (Vector2)texture.rtHandleProperties.rtHandleScale : Vector2.one;
            return context.GetTextureUVOrigin(in source) != context.GetTextureUVOrigin(in destination)
                ? new Vector4(scale.x, -scale.y, 0, scale.y) : new Vector4(scale.x, scale.y, 0, 0);
        }

        internal void Hide()
        {
            m_Camera = null;
            Array.Clear(m_Draws, 0, m_Count);
            m_Seen.Clear(); m_Count = 0;
        }

        public void Dispose()
        {
            Hide();
            if (m_Subscribed) RenderPipelineManager.beginCameraRendering -= BeginCamera;
            m_Subscribed = false;
            if (m_Material)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(m_Material);
                else UnityEngine.Object.DestroyImmediate(m_Material);
            }
            m_Material = null;
        }

        private sealed class MaskData
        {
            internal Material Material;
            internal Draw[] Draws;
            internal int Count;
        }
        private readonly struct Draw
        {
            internal readonly Renderer Renderer;
            internal readonly Mesh Mesh;
            internal readonly Transform Target;
            internal readonly int Submesh;
            internal Draw(Renderer renderer, int submesh) : this() { Renderer = renderer; Submesh = submesh; }
            internal Draw(Mesh mesh, Transform target) : this() { Mesh = mesh; Target = target; }
        }
        private sealed class EdgeData
        {
            internal TextureHandle Mask, Color;
            internal Material Material;
            internal MaterialPropertyBlock Properties;
            internal Vector4 Edge;
        }
    }
}
