// 远征氧气配置资产。用于设置室外氧气能撑多久、有氧处恢复速度、缺氧后多久受伤、以及不同移动速度的耗氧倍率。
// 运行时通过 OxygenParameters 读一整组参数，构造时把配置坏值换回可用值。
using UnityEngine;

namespace Unity.MP_FPS {
    [CreateAssetMenu(menuName = "Moonkov/Survival/Oxygen Settings", fileName = "OxygenSettings")]
    public sealed class OxygenSettings : ScriptableObject {
        [SerializeField] [Min(1)] private float m_OutdoorSeconds = 600;
        [SerializeField] [Min(0)] private float m_AirRefillPerSecond = 5;
        [SerializeField] [Min(0)] private float m_HypoxiaGraceSeconds = 8;
        [SerializeField] [Min(0)] private float m_HypoxiaDamagePerSecond = 3;
        [SerializeField] [Min(1)] private float m_WalkingMultiplier = 1.1f;
        [SerializeField] [Min(1)] private float m_SprintingMultiplier = 1.75f;

        public OxygenParameters Parameters =>
            // 把配置资产字段打包成运行时的值类型。一处读全组，服务端耗氧时不必逐个找场景字段。
            new(m_OutdoorSeconds, m_AirRefillPerSecond, m_HypoxiaGraceSeconds, m_HypoxiaDamagePerSecond,
                m_WalkingMultiplier, m_SprintingMultiplier);
    }

    // 这些不可变的数值也能直接用于本地预览或模拟测试。
    public readonly struct OxygenParameters {
        // 地图没有额外配置时可以拿整组默认值。
        public static OxygenParameters Default => new(600, 5, 8, 3, 1.1f, 1.75f);
        public readonly float OutdoorSeconds, AirRefillPerSecond, GraceSeconds, DamagePerSecond;
        public readonly float WalkingMultiplier, SprintingMultiplier;

        public OxygenParameters(
            float outdoorSeconds = 600,
            float airRefillPerSecond = 5,
            float graceSeconds = 8,
            float damagePerSecond = 3,
            float walkingMultiplier = 1.1f,
            float sprintingMultiplier = 1.75f) {
            OutdoorSeconds = Valid(outdoorSeconds, 600, 1);
            AirRefillPerSecond = Valid(airRefillPerSecond, 5, 0);
            GraceSeconds = Valid(graceSeconds, 8, 0);
            DamagePerSecond = Valid(damagePerSecond, 3, 0);
            WalkingMultiplier = Valid(walkingMultiplier, 1.1f, 1);
            SprintingMultiplier = Valid(sprintingMultiplier, 1.75f, 1);
        }

        // 防止过低或缺失的数据被用于计时和速率计算。先给无效值用备用值，再保证符合字段的最小约束。
        private static float Valid(float value, float fallback, float minimum) {
            return float.IsFinite(value) ? Mathf.Max(minimum, value) : fallback;
        }
    }
}