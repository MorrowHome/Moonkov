using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Server-owned suit reserve and hypoxia. UI only reads replicated results.</summary>
    public static class RaidOxygen
    {
        public const float Capacity = 100, LowThreshold = 25, CriticalThreshold = 10;
        public static void Initialize(ref PredictedPlayerGhost state)
        {
            state.Oxygen = Capacity;
            state.BreathableAir = false;
            state.HypoxiaSeconds = state.OxygenDrainPerSecond = state.OxygenSupplyPerSecond = 0;
            state.AmbientPressureKPa = state.AmbientOxygenPartialPressureKPa = 0;
            state.OxygenGraceRemaining = 0;
        }

        public static bool CanRefill(in PredictedPlayerGhost state) => state.BodyHealthInitialized &&
            state.CurrentHealth > 0 && float.IsFinite(state.Oxygen) && state.Oxygen < Capacity;

        public static float Refill(ref PredictedPlayerGhost state, float amount)
        {
            if (!CanRefill(state) || !float.IsFinite(amount) || amount <= 0) return 0;
            float before = Mathf.Max(0, state.Oxygen);
            state.Oxygen = Mathf.Min(Capacity, before + amount);
            state.HypoxiaSeconds = 0;
            state.OxygenGraceRemaining = 0;
            return state.Oxygen - before;
        }

        public static bool NeedsSuitAir(in PredictedPlayerGhost state) => !state.BreathableAir &&
            (state.OxygenDrainPerSecond <= 0 || state.OxygenSupplyPerSecond < state.OxygenDrainPerSecond);

        public static float RemainingSeconds(in PredictedPlayerGhost state)
        {
            float netDrain = state.OxygenDrainPerSecond - state.OxygenSupplyPerSecond;
            return state.BreathableAir || netDrain <= 0 ? float.PositiveInfinity : Mathf.Max(0, state.Oxygen) / netDrain;
        }

        public static void Tick(ref PredictedPlayerGhost state, in AtmosphereSample atmosphere,
            in OxygenParameters settings, float dt, uint tick, float stationSupplyPerSecond = 0)
        {
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0 || !float.IsFinite(dt) || dt <= 0) return;
            // C# default(struct) bypasses constructors; make that public API case safe too.
            var parameters = settings.OutdoorSeconds > 0 ? settings : OxygenParameters.Default;
            state.Oxygen = float.IsFinite(state.Oxygen) ? Mathf.Clamp(state.Oxygen, 0, Capacity) : 0;
            state.HypoxiaSeconds = float.IsFinite(state.HypoxiaSeconds) ? Mathf.Max(0, state.HypoxiaSeconds) : 0;
            state.BreathableAir = atmosphere.Breathable;
            state.AmbientPressureKPa = FiniteNonnegative(atmosphere.PressureKPa);
            state.AmbientOxygenPartialPressureKPa = FiniteNonnegative(atmosphere.PressureKPa * atmosphere.OxygenFraction);
            bool moving = state.ControllerState.MovementSpeed > .1f;
            float exertion = moving ? (state.ControllerState.Sprinting ? parameters.SprintingMultiplier : parameters.WalkingMultiplier) : 1;
            state.OxygenDrainPerSecond = atmosphere.Breathable ? 0 : Capacity / parameters.OutdoorSeconds * exertion;
            if(state.AlienFoodEffectSeconds>0 && state.AlienFoodEffect==AlienFoodEffect.OxygenReserve)state.OxygenDrainPerSecond*=.75f;
            float ambientRefill = atmosphere.Breathable && atmosphere.AllowsSuitRefill ? parameters.AirRefillPerSecond : 0;
            // Overlapping sources do not multiply supply. A station can also supply a sealed suit in vacuum.
            state.OxygenSupplyPerSecond = Mathf.Max(ambientRefill, FiniteNonnegative(stationSupplyPerSecond));
            float netDrain = state.OxygenDrainPerSecond - state.OxygenSupplyPerSecond;
            float exhaustedTime = netDrain > 0 ? Mathf.Max(0, dt - state.Oxygen / netDrain) : 0;
            state.Oxygen = Mathf.Clamp(state.Oxygen - netDrain * dt, 0, Capacity);
            if (atmosphere.Breathable || exhaustedTime <= 0)
            {
                state.HypoxiaSeconds = 0;
                state.OxygenGraceRemaining = !atmosphere.Breathable && netDrain > 0 && state.Oxygen <= 0
                    ? parameters.GraceSeconds : 0;
                return;
            }
            float before = Mathf.Max(0, state.HypoxiaSeconds - parameters.GraceSeconds);
            state.HypoxiaSeconds += exhaustedTime;
            state.OxygenGraceRemaining = Mathf.Max(0, parameters.GraceSeconds - state.HypoxiaSeconds);
            float exposure = Mathf.Max(0, state.HypoxiaSeconds - parameters.GraceSeconds) - before;
            if (exposure > 0) RaidHealth.Damage(ref state, BodyPart.Chest, exposure * parameters.DamagePerSecond, tick);
        }
        private static float FiniteNonnegative(float value) => float.IsFinite(value) ? Mathf.Max(0, value) : 0;
    }
}
