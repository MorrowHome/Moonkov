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
            var urgency = Urgency(state);
            if (urgency == OxygenUrgency.Depleted) return new OxygenBreath(true, 2.4f, 1.15f, 120);
            if (urgency == OxygenUrgency.Critical) return new OxygenBreath(true, 2.8f, 1f, 100);
            if (urgency == OxygenUrgency.Low) return new OxygenBreath(true, 3.3f, .85f, 45);
            bool effort = state.ControllerState.Sprinting && state.ControllerState.MovementSpeed > .1f ||
                state.CurrentHealth < state.MaxHealth * .35f;
            return effort ? new OxygenBreath(true, 3.2f, 1f, 100) :
                !state.BreathableAir ? new OxygenBreath(true, 4.1f, .65f, -15) : default;
        }
    }

    public readonly struct OxygenBreath
    {
        public readonly bool Audible;
        public readonly float Interval, Volume, Pitch;
        public OxygenBreath(bool audible, float interval, float volume, float pitch)
        { Audible = audible; Interval = interval; Volume = volume; Pitch = pitch; }
    }
}
