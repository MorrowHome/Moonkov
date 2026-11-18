// 把材料指定的一步全屏效果排入某台摄像机的 URP 渲染流程。受伤、黏液和眩光等镜头反馈会通过这个入口叠到画面上。
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS {
    public class FullScreenPassWrapper {
        private FullScreenPassRendererFeature.FullScreenRenderPass _fullScreenRenderPass;

        public FullScreenPassWrapper(
            string passName,
            Material material,
            int passIndex,
            bool fetchActiveColor,
            bool bindDepthStencilAttachment,
            RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing) {
            _fullScreenRenderPass = new FullScreenPassRendererFeature.FullScreenRenderPass(passName);
            // URP 的默认插入点是 BeforeRendering，随后还会清空相机颜色。
            // 若在该插入点叠加全屏画面，会被本帧清屏擦掉。
            // 全屏效果要排在正确的渲染阶段。放在清屏前刚画出来就会被清掉。
            _fullScreenRenderPass.renderPassEvent = injectionPoint;
            _fullScreenRenderPass.SetupMembers(material, passIndex, fetchActiveColor, bindDepthStencilAttachment);
        }

        // 只将这个效果挂到传入的相机渲染器上，多个视图可以有不同效果。
        public void EnqueuePass(Camera camera) {
            camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_fullScreenRenderPass);
        }
    }
}