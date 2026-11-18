// 把天幕、环境光、太阳和雾的设置存成资源。LightingProfileApplier 在加载配置时把这些值应用给场景。
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS {
    [CreateAssetMenu(fileName = "LightingProfile", menuName = "Lighting/Lighting Profile")]
    public class LightingProfile : ScriptableObject {
        [Header("Skybox")] public Material skybox;
        [Header("Sun Light")] public Light sun;
        // 环境光和直射太阳分别配置。管方向的光源引用与背景光、雾的参数都由同一份场景配置给出。
        [Header("Ambient")] public AmbientMode ambientMode = AmbientMode.Skybox;
        public Color ambientLight = Color.gray;
        public float ambientIntensity = 1f;
        // 雾参数支持颜色和距离或密度，由 Unity 使用选定的雾模式解释。同一地图换配置时能一起应用。
        [Header("Fog")] public bool fog;
        public FogMode fogMode = FogMode.Exponential;
        public Color fogColor = Color.gray;
        public float fogDensity = 0.01f;
        public float fogStartDistance = 0f;
        public float fogEndDistance = 300f;
    }
}