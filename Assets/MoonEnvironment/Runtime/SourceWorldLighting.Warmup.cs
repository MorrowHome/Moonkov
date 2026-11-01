using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon
{
    public sealed partial class SourceWorldLighting
    {
        private bool resourcesPrepared;

        // The map is already resident. Resolve its lazy material/texture data while
        // loading, before a turn or magnified sight exposes a new group of surfaces.
        public async Task PrepareResourcesAsync(CancellationToken cancellationToken)
        {
            if (resourcesPrepared || !applied || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            var materials = new HashSet<Material>();
            var bounds = new Bounds();
            bool hasBounds = false;
            int layers = 0;
            foreach (var binding in bindings)
            {
                if (!binding.renderer) continue;
                if (hasBounds) bounds.Encapsulate(binding.renderer.bounds);
                else { bounds = binding.renderer.bounds; hasBounds = true; }
                layers |= 1 << binding.renderer.gameObject.layer;
                foreach (var material in binding.renderer.sharedMaterials)
                    if (material) materials.Add(material);
            }
            if (!hasBounds) return;
            var textures = new HashSet<Texture>();
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var format = pipeline && pipeline.hdrColorBufferPrecision != HDRColorBufferPrecision._64Bits &&
                SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormatUsage.Blend)
                ? GraphicsFormat.B10G11R11_UFloatPack32 : SystemInfo.GetGraphicsFormat(DefaultFormat.HDR);
            var target = RenderTexture.GetTemporary(new RenderTextureDescriptor(32, 32)
                { graphicsFormat = format, depthBufferBits = 24, msaaSamples = 1 });
            GameObject cameraObject = null;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                foreach (var material in materials)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!this || !applied) return;
                    foreach (int property in material.GetTexturePropertyNameIDs())
                    {
                        var texture = material.GetTexture(property);
                        if (texture && textures.Add(texture)) texture.GetNativeTexturePtr();
                    }
                    // A single upload can exceed this budget, but batches never keep
                    // the loading screen from updating for the whole material set.
                    if (timer.ElapsedMilliseconds < 4) continue;
                    await Awaitable.NextFrameAsync(cancellationToken);
                    timer.Restart();
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!this || !applied) return;
                // An actual URP draw also prepares meshes, lightmaps and native material
                // state. A shader-only warmup leaves those first-visibility costs behind.
                // One orthographic view includes every map renderer; occlusion is off.
                cameraObject = new GameObject("Source world loading preparation") { hideFlags = HideFlags.HideAndDontSave };
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.z) + 1;
                camera.aspect = 1;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = bounds.size.y + 20;
                camera.transform.SetPositionAndRotation(bounds.center + Vector3.up * (bounds.extents.y + 10), Quaternion.Euler(90, 0, 0));
                camera.cullingMask = layers;
                camera.useOcclusionCulling = false;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) return;
                RenderPipeline.SubmitRenderRequest(camera, request);
                await Awaitable.NextFrameAsync(cancellationToken);
                resourcesPrepared = true;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(target);
                if (cameraObject) Destroy(cameraObject);
            }
        }
    }
}
