// 场景启用时把 LightingProfile 写到 Unity 的 RenderSettings，并更新环境光。模板战场 GameScene 上有这个组件。
using UnityEngine;

namespace Unity.MP_FPS {
    public class LightingProfileApplier : MonoBehaviour {
        [SerializeField] private LightingProfile profile;

        private void OnEnable() {
            if (profile == null) {
                Debug.LogWarning("LightingProfileApplier has no assigned profile.");
                return;
            }
            ApplyLighting(profile);
        }

        private void ApplyLighting(LightingProfile p) {
            RenderSettings.skybox = p.skybox;
            RenderSettings.sun = p.sun;
            RenderSettings.ambientMode = p.ambientMode;
            RenderSettings.ambientLight = p.ambientLight;
            RenderSettings.ambientIntensity = p.ambientIntensity;
            RenderSettings.fog = p.fog;
            RenderSettings.fogMode = p.fogMode;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;
            RenderSettings.fogStartDistance = p.fogStartDistance;
            RenderSettings.fogEndDistance = p.fogEndDistance;
            DynamicGI.UpdateEnvironment();
        }
    }
}