using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Runtime budget for the fixed, baked world. Authoring assets stay unchanged.</summary>
    public static class SourceWorldRuntimeQuality
    {
        public const float RenderScale = .5f;
        public const int TextureMipmapLimit = 1;
        // Keep the authored camera range. A short global clip cuts off buildings and targets.
        public const float ViewDistance = 0f;
        public const float SecondaryMotionDistance = 12f;
        public const float FullRateCreatureDistance = 12f;
        private static UniversalRenderPipelineAsset s_RuntimePipeline;

        public static bool Active => Application.isPlaying && s_RuntimePipeline &&
            QualitySettings.renderPipeline == s_RuntimePipeline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => s_RuntimePipeline = null;

        public static void Configure(UniversalRenderPipelineAsset runtimePipeline)
        {
            runtimePipeline.renderScale = RenderScale;
            runtimePipeline.msaaSampleCount = 1;
            runtimePipeline.shadowDistance = 0;
            // URP exposes these serialized switches with internal setters. Update only
            // the runtime clone, so shadow setup is disabled as well as shadow drawing.
            JsonUtility.FromJsonOverwrite("{\"m_MainLightShadowsSupported\":false," +
                "\"m_AdditionalLightShadowsSupported\":false,\"m_SoftShadowsSupported\":false}", runtimePipeline);
            s_RuntimePipeline = runtimePipeline;
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
