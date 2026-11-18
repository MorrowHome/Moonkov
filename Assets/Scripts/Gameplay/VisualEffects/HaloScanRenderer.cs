// HaloScanClientSystem 把已收到的扫描波和接触点交给这一层。只在对应游戏相机上叠画扫描特效，超时或切相机就清理。
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS {
    // 只为本地相机临时执行一遍渲染。无需改管线资源、另建相机或查遍整个场景。
    public static class HaloScanRenderer {
        private static Driver s_Driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Clear(); }

        public static void Clear() {
            s_Driver?.Dispose();
            s_Driver = null;
        }

        public static void Publish(Camera camera, HaloScanClientState state, double now) {
            if (!camera || !state.Active || camera.cameraType != CameraType.Game) {
                Clear();
                return;
            }
            if (s_Driver != null && s_Driver.Camera != camera) Clear();
            s_Driver ??= new Driver(camera);
            s_Driver.Cache = state;
            s_Driver.PublishedAt = now;
        }

        private sealed class Driver : IDisposable {
            internal readonly Camera Camera;
            internal HaloScanClientState Cache;
            internal double PublishedAt;
            private readonly Material m_Material;
            private readonly ScanPass m_Pass;
            private bool m_Disposed;

            internal Driver(Camera camera) {
                Camera = camera;
                var shader = Resources.Load<Shader>("Moonkov/HaloScan");
                if (shader && shader.isSupported) {
                    m_Material = new Material(shader)
                        { name = "Halo terrain scan", hideFlags = HideFlags.HideAndDontSave };
                    m_Pass = new ScanPass(m_Material) { Owner = camera };
                }
                RenderPipelineManager.beginCameraRendering += BeginCamera;
            }

            private void BeginCamera(ScriptableRenderContext context, Camera camera) {
                if (m_Disposed || camera != Camera) return;
                double now = Time.realtimeSinceStartupAsDouble;
                // 客户端要持续发布有用的扫描状态。超过短时间没有刷新或脉冲失效后，自动解绑相机。
                if (!Camera || Cache == null || !Cache.Active || now - PublishedAt > .25) {
                    Clear();
                    return;
                }
                camera.TryGetComponent<UniversalAdditionalCameraData>(out var additional);
                if ((additional && additional.renderType == CameraRenderType.Overlay) || m_Pass == null ||
                    !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))
                    return;
                m_Pass.Elapsed = (float)(now - Cache.StartedAt);
                m_Pass.Origin = Cache.Pulse.Origin;
                m_Pass.Color = HaloAppearanceCatalog.Current
                    ? HaloAppearanceCatalog.Current.ForWeapon(Cache.Pulse.Weapon).Color
                    : Color.cyan;
                m_Pass.Count = m_Pass.RendererCount = 0;
                for (int i = 0; i < Cache.Contacts.Length; i++) {
                    // 服务器发来的每个接触点有自己的有效期。只有仍可显示的目标才查询它的轮廓网格，并限制每帧数量。
                    if (!Cache.Visible(i, now)) continue;
                    var p = Cache.Contacts[i].Position;
                    float fade =
                        Mathf.Clamp01((HaloScanRules.RevealSeconds - (float)(now - Cache.ReceivedAt[i])) / .6f);
                    m_Pass.Contacts[m_Pass.Count++] = new Vector4(p.x, p.y, p.z, fade);
                    var outlines = Cache.Outlines[i];
                    if (outlines == null) continue;
                    foreach (var renderer in outlines) {
                        if (m_Pass.RendererCount >= m_Pass.Renderers.Length) break;
                        if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                            !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) ||
                            Vector3.Distance(renderer.bounds.center, p) > 4)
                            continue;
                        m_Pass.Renderers[m_Pass.RendererCount++] = renderer;
                    }
                }
                // 脉冲本身还能在没有目标时播放。整段动画过去且没有接触点才不再加入渲染。
                if (m_Pass.Count == 0 && m_Pass.Elapsed > HaloScanRules.Duration + .2f) return;
                (additional ? additional.scriptableRenderer : pipeline.scriptableRenderer).EnqueuePass(m_Pass);
            }

            public void Dispose() {
                if (m_Disposed) return;
                m_Disposed = true;
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                if (m_Material) {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(m_Material);
                    else
                        UnityEngine.Object.DestroyImmediate(m_Material);
                }
            }
        }

        private sealed class ScanPass : ScriptableRenderPass {
            private readonly Material m_Material;
            private readonly MaterialPropertyBlock m_Properties = new();
            internal Camera Owner;
            internal Vector3 Origin;
            internal Color Color;
            internal float Elapsed;
            internal int Count, RendererCount;
            internal readonly Vector4[] Contacts = new Vector4[HaloScanRules.MaxContacts];
            internal readonly Renderer[] Renderers = new Renderer[96];

            internal ScanPass(Material material) {
                m_Material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData) {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                if (camera.camera != Owner || resources.isActiveTargetBackBuffer || !m_Material) return;
                var source = resources.activeColorTexture;
                var descriptor = graph.GetTextureDesc(source);
                descriptor.name = "Halo scan composite";
                descriptor.clearBuffer = false;
                // 扫描材质需要先读相机当前画面，再写到另一个颜色目标。接触点和实际网格随后进入着色步骤，作为这次显示的数据。
                var destination = graph.CreateTexture(descriptor);
                using var builder =
                    graph.AddRasterRenderPass<PassData>("Halo scan / authorized enemy silhouettes", out var data);
                data.Source = source;
                data.Destination = destination;
                data.Depth = resources.cameraDepthTexture;
                data.Material = m_Material;
                data.Properties = m_Properties;
                data.Projection = camera.GetProjectionMatrix();
                data.View = camera.GetViewMatrix();
                data.Origin = Origin;
                data.Color = Color;
                data.Elapsed = Elapsed;
                data.Texel = new Vector4(1f / descriptor.width, 1f / descriptor.height, descriptor.width,
                    descriptor.height);
                data.Right = Owner.transform.right;
                data.Up = Owner.transform.up;
                data.Contacts = Contacts;
                data.Count = Count;
                data.Renderers = Renderers;
                data.RendererCount = RendererCount;
                builder.UseTexture(source, AccessFlags.Read);
                if (data.Depth.IsValid()) builder.UseTexture(data.Depth, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) => {
                    var block = d.Properties;
                    block.Clear();
                    Texture sourceTexture = d.Source;
                    block.SetTexture("_BlitTexture", sourceTexture);
                    block.SetVector("_BlitScaleBias", ScaleBias(context, d.Source, d.Destination));
                    bool flip = context.GetTextureUVOrigin(in d.Destination) == TextureUVOrigin.BottomLeft;
                    var vp = GL.GetGPUProjectionMatrix(d.Projection, flip) * d.View;
                    block.SetMatrix("_ScanInverseVP", vp.inverse);
                    block.SetMatrix("_ScanVP", vp);
                    block.SetVector("_ScanOrigin", d.Origin);
                    block.SetVector("_ScanState",
                        new Vector4(d.Elapsed * HaloScanRules.Speed, HaloScanRules.Range, d.Elapsed,
                            d.Depth.IsValid() ? 1 : 0));
                    block.SetColor("_ScanColor", d.Color);
                    block.SetVector("_ScanTexel", d.Texel);
                    if (d.Depth.IsValid()) {
                        Texture depth = d.Depth;
                        block.SetTexture("_ScanDepth", depth);
                        block.SetVector("_ScanDepthScaleBias", ScaleBias(context, d.Depth, d.Destination));
                    }
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 0, MeshTopology.Triangles, 3, 1, block);
                    if (d.Count > 0) {
                        block.SetVectorArray("_ScanContacts", d.Contacts);
                        block.SetVector("_ScanRight", d.Right);
                        block.SetVector("_ScanUp", d.Up);
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 1, MeshTopology.Triangles, 6,
                            d.Count, block);
                    }
                    for (int i = 0; i < d.RendererCount; i++)
                        if (d.Renderers[i])
                            context.cmd.DrawRenderer(d.Renderers[i], d.Material, 0, 2);
                });
                resources.cameraColor = destination;
            }

            private static Vector4 ScaleBias(
                RasterGraphContext context,
                TextureHandle source,
                TextureHandle destination) {
                RTHandle handle = source;
                var scale = handle.useScaling ? (Vector2)handle.rtHandleProperties.rtHandleScale : Vector2.one;
                return context.GetTextureUVOrigin(in source) != context.GetTextureUVOrigin(in destination)
                    ? new Vector4(scale.x, -scale.y, 0, scale.y)
                    : new Vector4(scale.x, scale.y, 0, 0);
            }

            private sealed class PassData {
                internal TextureHandle Source, Destination, Depth;
                internal Material Material;
                internal MaterialPropertyBlock Properties;
                internal Matrix4x4 Projection, View;
                internal Vector3 Origin, Right, Up;
                internal Color Color;
                internal Vector4 Texel;
                internal float Elapsed;
                internal Vector4[] Contacts;
                internal Renderer[] Renderers;
                internal int Count, RendererCount;
            }
        }
    }
}