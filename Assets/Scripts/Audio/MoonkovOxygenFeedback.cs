using UnityEngine;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS
{
    public enum OxygenUrgency : byte { None, Low, Critical, Depleted }

    // Client presentation only. Never grants air or infers a successful item request.
    public sealed class MoonkovOxygenFeedback
    {
        private OxygenUrgency m_Urgency;
        private float m_NextAlert;

        public static OxygenUrgency Urgency(in PredictedPlayerGhost state)
        {
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0 || !RaidOxygen.NeedsSuitAir(state))
                return OxygenUrgency.None;
            return state.Oxygen <= 0 ? OxygenUrgency.Depleted :
                state.Oxygen <= RaidOxygen.CriticalThreshold ? OxygenUrgency.Critical :
                state.Oxygen <= RaidOxygen.LowThreshold ? OxygenUrgency.Low : OxygenUrgency.None;
        }

        public bool Observe(in PredictedPlayerGhost state, float now)
        {
            var next = Urgency(state);
            // Small refill/consumption fluctuations must not replay a threshold alert.
            if (next < m_Urgency && state.BodyHealthInitialized && state.CurrentHealth > 0 &&
                RaidOxygen.NeedsSuitAir(state) && state.Oxygen > 0)
            {
                if (m_Urgency == OxygenUrgency.Critical && state.Oxygen <= RaidOxygen.CriticalThreshold + 2)
                    next = OxygenUrgency.Critical;
                else if (m_Urgency == OxygenUrgency.Low && state.Oxygen <= RaidOxygen.LowThreshold + 2)
                    next = OxygenUrgency.Low;
            }
            if (next == OxygenUrgency.None) { Reset(); return false; }
            bool alert = next > m_Urgency || now >= m_NextAlert;
            m_Urgency = next;
            if (alert) m_NextAlert = now + (next == OxygenUrgency.Depleted ? 5 : next == OxygenUrgency.Critical ? 10 : 20);
            return alert;
        }

        public void Reset() { m_Urgency = OxygenUrgency.None; m_NextAlert = 0; }

        public static OxygenBreath Breath(in PredictedPlayerGhost state)
        {
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0) return default;
            float stamina = float.IsFinite(state.TacticalSprintStamina) ? state.TacticalSprintStamina : TacticalSprintRules.Capacity;
            float fatigue = 1 - Mathf.Clamp01(stamina / TacticalSprintRules.Capacity);
            if (state.MeleeEpoch != 0 && float.IsFinite(state.MeleeStamina))
                fatigue = Mathf.Max(fatigue, 1 - Mathf.Clamp01(state.MeleeStamina / MeleeRules.StaminaCapacity));
            // Recovery follows the actual reserves, including after the player releases sprint.
            float effort = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f, 1, fatigue));
            if (state.ControllerState.Sprinting && state.ControllerState.MovementSpeed > .1f)
                effort = Mathf.Max(effort, .35f);
            if (state.TacticalSprintExhausted) effort = 1;
            if (state.CurrentHealth < state.MaxHealth * .35f) effort = Mathf.Max(effort, .55f);
            var urgency = Urgency(state);
            if (urgency == OxygenUrgency.Depleted) effort = 1;
            else if (urgency == OxygenUrgency.Critical) effort = Mathf.Max(effort, .75f);
            else if (urgency == OxygenUrgency.Low) effort = Mathf.Max(effort, .45f);
            if (state.BreathableAir && effort <= .01f) return default;
            bool exerted = effort >= .45f;
            float interval = exerted ? Mathf.Lerp(2.6f, 1.7f, Mathf.InverseLerp(.45f, 1, effort)) :
                Mathf.Lerp(4.1f, 3.5f, effort / .45f);
            return new OxygenBreath(true, interval, Mathf.Lerp(.8f, 1.2f, effort), 0, exerted);
        }
    }

    public readonly struct OxygenBreath
    {
        public readonly bool Audible, Exerted;
        public readonly float Interval, Volume, Pitch;
        public OxygenBreath(bool audible, float interval, float volume, float pitch, bool exerted = false)
        { Audible = audible; Interval = interval; Volume = volume; Pitch = pitch; Exerted = exerted; }
    }
}
