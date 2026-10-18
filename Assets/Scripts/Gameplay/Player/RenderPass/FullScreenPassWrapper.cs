using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS
{
    public class FullScreenPassWrapper
    {
        private FullScreenPassRendererFeature.FullScreenRenderPass _fullScreenRenderPass;

        public FullScreenPassWrapper(string passName, Material material, int passIndex, bool fetchActiveColor,
            bool bindDepthStencilAttachment, RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing)
        {
            _fullScreenRenderPass = new FullScreenPassRendererFeature.FullScreenRenderPass(passName);
            // The URP pass defaults to BeforeRendering; the subsequent camera clear
            // would erase a camera overlay injected at that point.
            _fullScreenRenderPass.renderPassEvent = injectionPoint;
            _fullScreenRenderPass.SetupMembers(material, passIndex, fetchActiveColor, bindDepthStencilAttachment);
        }

        public void EnqueuePass(Camera camera)
        {
            camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_fullScreenRenderPass);
        }
    }
}
