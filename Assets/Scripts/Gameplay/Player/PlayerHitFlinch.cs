using UnityEngine;

namespace Unity.MP_FPS
{
    // Observes confirmed physical impacts. This offset is only used by the rendered camera.
    public struct PlayerHitFlinch
    {
        private const float Duration = .24f;
        private uint m_ImpactTick, m_ShieldHit, m_ShieldBreak;
        private bool m_Observed, m_Alive;
        private float m_Age;
        private Vector3 m_Angles;

        public Vector3 Step(in PredictedPlayerGhost state, float deltaTime, float magnification, bool visible)
        {
            bool alive = EnergyShieldRules.Alive(state);
            if (!m_Observed || alive && !m_Alive)
            {
                m_Observed = true;
                m_ImpactTick = state.LastImpactTick;
                m_ShieldHit = state.ShieldHitSequence;
                m_ShieldBreak = state.ShieldBreakSequence;
            }
            m_Alive = alive;
            bool hit = MoonkovPlayerAudio.NewTick(state.LastImpactTick, ref m_ImpactTick);
            bool shield = Advance(state.ShieldHitSequence, ref m_ShieldHit);
            bool broken = Advance(state.ShieldBreakSequence, ref m_ShieldBreak);
            if (!visible || !alive)
            {
                m_Angles = Vector3.zero;
                return Vector3.zero;
            }

            m_Age += deltaTime;
            if (hit || shield)
            {
                // A single snapshot may carry both shield absorption and tissue damage.
                float strength = hit
                    ? Mathf.Lerp(1.25f, 2.9f, Mathf.Clamp01(
                        (state.LastHitTick == state.LastImpactTick ? state.LastDamageAmount : 20f) / 65f))
                    : broken ? 1.55f : Mathf.Lerp(.65f, 1.25f, Mathf.Clamp01(state.ShieldHitStrength));
                uint eventId = hit ? state.LastImpactTick : state.ShieldHitSequence;
                float side = (unchecked(eventId * 2654435761u) & 0x80000000u) == 0 ? 1f : -1f;
                m_Angles = new Vector3(-1, side * .32f, -side * .46f) * strength;
                m_Age = 0;
            }

            if (m_Age >= Duration || m_Angles == Vector3.zero) return Vector3.zero;
            float remaining = 1f - m_Age / Duration;
            float weight = remaining * remaining * Mathf.Cos(m_Age * 19f);
            float zoom = Mathf.Max(1f, magnification);
            return new Vector3(m_Angles.x / zoom, m_Angles.y / zoom, m_Angles.z) * weight;
        }

        private static bool Advance(uint sequence, ref uint observed)
        {
            if (unchecked((int)(sequence - observed)) <= 0) return false;
            observed = sequence;
            return true;
        }
    }
}
