using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Runtime budget for the fixed, baked world. Authoring assets stay unchanged.</summary>
    public static class SourceWorldRuntimeQuality
    {
        public const float RenderScale = 2f / 3f;
        public const float ViewDistance = 100f;

        public static void Configure(UniversalRenderPipelineAsset runtimePipeline)
        {
            runtimePipeline.renderScale = RenderScale;
            runtimePipeline.msaaSampleCount = 1;
            runtimePipeline.shadowDistance = 0;
        }

        public static float FarClip(float authoredDistance, float budget, float fieldOfView, bool orthographic)
        {
            if (budget <= 0 || orthographic) return authoredDistance;
            // Preserve apparent reach when aiming through a magnified sight. Its narrower
            // frustum offsets the increased range; retain a camera's shorter authored limit.
            float zoom = Mathf.Max(1, Mathf.Tan(37.5f * Mathf.Deg2Rad) /
                Mathf.Tan(Mathf.Clamp(fieldOfView, 1, 179) * .5f * Mathf.Deg2Rad));
            return Mathf.Min(authoredDistance, budget * zoom);
        }
    }
}
