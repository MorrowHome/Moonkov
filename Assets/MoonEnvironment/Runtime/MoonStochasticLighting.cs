using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Opt-in independent direct-light sampling and low-sample shadows for environment shaders.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class MoonStochasticLighting : MonoBehaviour
    {
        public const string Keyword = "_MOON_STOCHASTIC_SHADOWS";
        private static readonly int Parameters = Shader.PropertyToID("_MoonStochasticShadowParams");
        private static readonly int NoiseParameters = Shader.PropertyToID("_MoonLightingNoiseParams");
        [SerializeField, Range(.5f, 3)] private float radiusTexels = 2.5f;
        [SerializeField, Range(1, 4)] private float grainPixels = 1;
        [SerializeField, Range(1, 2)] private int samples = 1;
        [UnityEngine.Serialization.FormerlySerializedAs("shadingProbability")]
        [SerializeField, Range(.25f, 1)] private float lightSamplingProbability = .85f;
        [Header("Low-sample preview look")]
        [Tooltip("Continuous, mean-preserving variation of direct light. Set light sampling probability to 1 to avoid dark roulette holes. This is an artistic approximation, not path tracing.")]
        [SerializeField, Range(0, 1)] private float directNoise;
        [SerializeField, Range(0, 1)] private float indirectNoise;
        [Tooltip("0 freezes the screen-space pattern. A positive rate animates the pattern without accumulating or denoising it.")]
        [SerializeField, Range(0, 30)] private float noiseUpdatesPerSecond;
        private Camera owner;
        private bool rendering, previousKeyword;
        private Vector4 previousParameters;
        private Vector4 previousNoiseParameters;

        private void OnEnable()
        {
            owner = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != owner || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            previousKeyword = Shader.IsKeywordEnabled(Keyword);
            previousParameters = Shader.GetGlobalVector(Parameters);
            previousNoiseParameters = Shader.GetGlobalVector(NoiseParameters);
            rendering = true;
            Shader.SetGlobalVector(Parameters, new Vector4(Mathf.Clamp(radiusTexels, .5f, 3),
                Mathf.Clamp(grainPixels, 1, 4), Mathf.Clamp(samples, 1, 2), Mathf.Clamp(lightSamplingProbability, .25f, 1)));
            float seed = noiseUpdatesPerSecond > 0 ? Mathf.Floor((float)Time.realtimeSinceStartupAsDouble * noiseUpdatesPerSecond) % 65536 : 0;
            Shader.SetGlobalVector(NoiseParameters, new Vector4(Mathf.Clamp01(directNoise), Mathf.Clamp01(indirectNoise), seed, 0));
            Shader.EnableKeyword(Keyword);
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == owner) Restore();
        }

        private void Restore()
        {
            if (!rendering) return;
            if (previousKeyword) Shader.EnableKeyword(Keyword);
            else Shader.DisableKeyword(Keyword);
            Shader.SetGlobalVector(Parameters, previousParameters);
            Shader.SetGlobalVector(NoiseParameters, previousNoiseParameters);
            rendering = false;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            Restore();
        }
    }
}
