using UnityEngine;

namespace Unity.MP_FPS.Client
{
    // Presentation only. No heart-rate field, medical simulation, network or save state is added.
    public readonly struct RaidVitalSignal
    {
        public readonly bool Ready, Alive, Critical;
        public readonly float Frequency;
        public readonly string Warning;
        public RaidVitalSignal(bool ready, bool alive, bool critical, float frequency, string warning)
        { Ready = ready; Alive = alive; Critical = critical; Frequency = frequency; Warning = warning; }

        public static RaidVitalSignal Read(in PredictedPlayerGhost state)
        {
            if (!state.BodyHealthInitialized) return new RaidVitalSignal(false, false, false, 0, "");
            bool downed = state.ClashEnabled && state.ClashLife == ClashLife.Downed;
            bool alive = state.CurrentHealth > 0 || downed;
            if (!alive) return new RaidVitalSignal(true, false, true, 0, "VITAL SIGNAL LOST");
            bool needsAir = RaidOxygen.NeedsSuitAir(state);
            float danger = RaidHealth.DangerFraction(state);
            bool hypoxic = needsAir && state.Oxygen <= 0;
            bool critical = downed || danger < .35f || hypoxic || needsAir && state.Oxygen <= 10 || state.Energy <= 0 || state.Hydration <= 0;
            string warning = downed ? "VITALS CRITICAL" : hypoxic ? state.OxygenGraceRemaining > 0 ? "OXYGEN DEPLETED" : "HYPOXIA" : danger < .35f ? "VITALS CRITICAL" :
                state.Hydration <= 0 ? "DEHYDRATED" : state.Energy <= 0 ? "STARVING" :
                needsAir && state.Oxygen <= RaidOxygen.CriticalThreshold ? "CRITICAL OXYGEN" :
                needsAir && state.Oxygen <= 25 ? "LOW OXYGEN" : state.Hydration <= 25 ? "THIRSTY" : state.Energy <= 25 ? "HUNGRY" :
                state.TacticalPoisonSeconds > 0 ? "POISONED" : "";
            float stress = Mathf.Clamp01(1 - danger);
            float frequency = 1.05f + (state.ControllerState.Sprinting ? .55f : 0) + stress * .4f + (hypoxic ? .3f : 0);
            return new RaidVitalSignal(true, true, critical, Mathf.Clamp(frequency, 1.05f, 2.4f), warning);
        }

        public static float BodyFill(float health, float maximum, bool ready)
        {
            if (!ready || !float.IsFinite(health) || !float.IsFinite(maximum) || maximum <= 0) return 0;
            return Mathf.Clamp01(health/maximum);
        }

        // Tint the lit scan mesh; a separate dim layer always preserves the full anatomy.
        public static Color BodyTint(float health, float maximum, bool ready)
        {
            if (!ready || !float.IsFinite(health) || !float.IsFinite(maximum) || maximum <= 0)
                return new Color(.4f,.45f,.5f,.22f);
            if (health <= 0) return new Color(.55f,.6f,.65f,.45f);
            float fraction = Mathf.Clamp01(health/maximum);
            var red = new Color(1,.08f,.04f); var orange = new Color(1,.26f,.07f); var amber = new Color(1,.7f,.22f);
            if (fraction <= .25f) return Color.Lerp(red,orange,fraction/.25f);
            if (fraction <= .6f) return Color.Lerp(orange,amber,(fraction-.25f)/.35f);
            return Color.Lerp(amber,Color.white,(fraction-.6f)/.4f);
        }

        public static int ReserveCells(float value, int cells)
        {
            if (!float.IsFinite(value) || value <= 0 || cells <= 0) return 0;
            if (value >= 100) return cells;
            return Mathf.Clamp(Mathf.FloorToInt(value * .01f * cells), 1, cells - 1);
        }

        public static float Sample(float phase)
        {
            phase = Mathf.Repeat(phase, 1);
            float Peak(float at, float width, float height)
            { float t = (phase - at) / width; return height * Mathf.Exp(-t * t); }
            return Peak(.13f, .035f, .09f) - Peak(.245f, .012f, .1f) + Peak(.28f, .013f, .9f) -
                Peak(.315f, .018f, .22f) + Peak(.49f, .065f, .18f);
        }
    }
}
