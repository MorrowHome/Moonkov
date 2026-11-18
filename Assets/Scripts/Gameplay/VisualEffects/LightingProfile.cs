// 把天幕、环境光、太阳和雾的设置存成资源。LightingProfileApplier 在加载配置时把这些值应用给场景。
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS {
    [CreateAssetMenu(fileName = "LightingProfile", menuName = "Lighting/Lighting Profile")]
    public class LightingProfile : ScriptableObject {
        [Header("Skybox")] public Material skybox;
        [Header("Sun Light")] public Light sun;
        [Header("Ambient")] public AmbientMode ambientMode = AmbientMode.Skybox;
        public Color ambientLight = Color.gray;
        public float ambientIntensity = 1f;
        [Header("Fog")] public bool fog;
        public FogMode fogMode = FogMode.Exponential;
        public Color fogColor = Color.gray;
        public float fogDensity = 0.01f;
        public float fogStartDistance = 0f;
        public float fogEndDistance = 300f;
    }
}