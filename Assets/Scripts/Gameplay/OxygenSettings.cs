using UnityEngine;

namespace Unity.MP_FPS
{
    [CreateAssetMenu(menuName = "Moonkov/Survival/Oxygen Settings", fileName = "OxygenSettings")]
    public sealed class OxygenSettings : ScriptableObject
    {
        [SerializeField, Min(1)] private float m_OutdoorSeconds = 600;
        [SerializeField, Min(0)] private float m_AirRefillPerSecond = 5;
        [SerializeField, Min(0)] private float m_HypoxiaGraceSeconds = 8;
        [SerializeField, Min(0)] private float m_HypoxiaDamagePerSecond = 3;
        [SerializeField, Min(1)] private float m_WalkingMultiplier = 1.1f;
        [SerializeField, Min(1)] private float m_SprintingMultiplier = 1.75f;

        public OxygenParameters Parameters => new OxygenParameters(m_OutdoorSeconds, m_AirRefillPerSecond,
            m_HypoxiaGraceSeconds, m_HypoxiaDamagePerSecond, m_WalkingMultiplier, m_SprintingMultiplier);
    }

    // Immutable values can also be supplied by a local preview or a simulation test.
    public readonly struct OxygenParameters
    {
        public static OxygenParameters Default => new OxygenParameters(600, 5, 8, 3, 1.1f, 1.75f);
        public readonly float OutdoorSeconds, AirRefillPerSecond, GraceSeconds, DamagePerSecond;
        public readonly float WalkingMultiplier, SprintingMultiplier;
        public OxygenParameters(float outdoorSeconds = 600, float airRefillPerSecond = 5,
            float graceSeconds = 8, float damagePerSecond = 3, float walkingMultiplier = 1.1f,
            float sprintingMultiplier = 1.75f)
        {
            OutdoorSeconds = Valid(outdoorSeconds, 600, 1);
            AirRefillPerSecond = Valid(airRefillPerSecond, 5, 0);
            GraceSeconds = Valid(graceSeconds, 8, 0);
            DamagePerSecond = Valid(damagePerSecond, 3, 0);
            WalkingMultiplier = Valid(walkingMultiplier, 1.1f, 1);
            SprintingMultiplier = Valid(sprintingMultiplier, 1.75f, 1);
        }
        private static float Valid(float value, float fallback, float minimum) =>
            float.IsFinite(value) ? Mathf.Max(minimum, value) : fallback;
    }
}
