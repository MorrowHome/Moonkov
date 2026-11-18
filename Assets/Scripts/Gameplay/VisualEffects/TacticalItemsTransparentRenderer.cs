// 给战术烟雾和透明粒子安排专用 URP 渲染步骤。场上有这些效果且相机真的画世界时才加入，叠加 UI 和第一人称的相机无需重画。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS {
    // Owned by TacticalItemsFeedback. Only dedicated tactical particle and smoke volume shaders opt in.
    // No renderer asset, SilverPrint material, or third-party particle asset is modified.
    internal sealed class TacticalItemsTransparentRenderer : IDisposable {
        private readonly TacticalPass pass = new();
        private readonly Func<bool> hasParticles;
        private readonly TacticalItemsFeedback owner;
        private bool disposed;

        internal TacticalItemsTransparentRenderer(TacticalItemsFeedback owner, Func<bool> hasParticles) {
            this.owner = owner;
            this.hasParticles = hasParticles ?? throw new ArgumentNullException(nameof(hasParticles));
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera) {
            if (disposed) return;
            // EditMode preview fixtures can invoke Awake manually without a native OnDestroy.
            // Retire a stale subscription before invoking any delegate on a destroyed owner.
            // 所属的表现物销毁后，应先取消这份相机订阅。临时预览退出也能避免在旧对象上继续查询粒子。
            if (!owner) {
                Dispose();
                return;
            }
            if (!camera || !hasParticles() || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return;
            camera.TryGetComponent<UniversalAdditionalCameraData>(out var data);
            // Base camera renders world effects once; HUD/first-person overlay cameras do not repeat them.
            if (data && data.renderType == CameraRenderType.Overlay) return;
            int uiLayers = LayerMask.GetMask("UI", "ServerPlayer", "ClientPlayer", "FirstPersonOverlay");
            // 看不到世界层的相机就没有烟要画。叠加相机和只画菜单的层不会重复提交一遍透明效果。
            if ((camera.cullingMask & ~uiLayers) == 0) return;
            var renderer = data ? data.scriptableRenderer : pipeline.scriptableRenderer;
            if (renderer != null) renderer.EnqueuePass(pass);
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
        }

        private sealed class TacticalPass : ScriptableRenderPass {
            private static readonly List<ShaderTagId> Tags = new() { new ShaderTagId("MoonkovTacticalTransparent") };
            private static readonly int DepthId = Shader.PropertyToID("_MoonkovTacticalSceneDepth");
            private static readonly int DepthScaleId = Shader.PropertyToID("_MoonkovTacticalDepthScaleBias");

            internal TacticalPass() {
                // SilverPrint is 599; URP post-process completes before this event.
                // Owner dark sight is 600; tactical alpha is 601; screen warnings are 602.
                renderPassEvent = (RenderPassEvent)601;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData) {
                var resources = frameData.Get<UniversalResourceData>();
                // 需要一份有效的相机深度让烟雾和场景正确遮挡。只有可用的渲染目标才进入这一步。
                if (resources.isActiveTargetBackBuffer || !resources.cameraDepthTexture.IsValid()) return;
                var camera = frameData.Get<UniversalCameraData>();
                var rendering = frameData.Get<UniversalRenderingData>();
                var drawing = RenderingUtils.CreateDrawingSettings(Tags, rendering, camera,
                    frameData.Get<UniversalLightData>(), SortingCriteria.CommonTransparent);
                var filtering = new FilteringSettings(RenderQueueRange.transparent);
                using var builder =
                    graph.AddRasterRenderPass<PassData>("Tactical transparent over silver print", out var data);
                data.color = resources.activeColorTexture;
                data.depth = resources.cameraDepthTexture;
                data.renderers =
                    graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
                builder.UseRendererList(data.renderers);
                builder.UseTexture(data.depth, AccessFlags.Read);
                // Alpha blending reads the existing graded target and writes the composed result.
                builder.SetRenderAttachment(data.color, 0, AccessFlags.ReadWrite);
                // Renderer-list materials cannot receive a draw-local MaterialPropertyBlock.
                // Bind a dedicated current-camera depth only inside this raster execution;
                // the graph declares its read above. No unrelated shader consumes this name.
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => {
                    RTHandle depth = data.depth;
                    var scale = depth.useScaling ? (Vector2)depth.rtHandleProperties.rtHandleScale : Vector2.one;
                    bool flip = context.GetTextureUVOrigin(in data.depth) != context.GetTextureUVOrigin(in data.color);
                    var bias = flip ? new Vector4(scale.x, -scale.y, 0, scale.y) : new Vector4(scale.x, scale.y, 0, 0);
                    context.cmd.SetGlobalTexture(DepthId, data.depth);
                    context.cmd.SetGlobalVector(DepthScaleId, bias);
                    context.cmd.DrawRendererList(data.renderers);
                });
            }

            private sealed class PassData {
                public TextureHandle color, depth;
                public RendererListHandle renderers;
            }
        }
    }
}