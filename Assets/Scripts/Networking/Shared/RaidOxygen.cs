// 项圈供氧的规则。服务端根据环境、角色移动和补氧点更新剩余氧气。HUD 可读取消耗速度、补给速度与缺氧倒计时。
using UnityEngine;

namespace Unity.MP_FPS {
    /// <summary>Server-owned suit reserve and hypoxia. UI only reads replicated results.</summary>
    public static class RaidOxygen {
        public const float Capacity = 100, LowThreshold = 25, CriticalThreshold = 10;

        public static void Initialize(ref PredictedPlayerGhost state) {
            state.Oxygen = Capacity;
            state.BreathableAir = false;
            state.HypoxiaSeconds = state.OxygenDrainPerSecond = state.OxygenSupplyPerSecond = 0;
            state.AmbientPressureKPa = state.AmbientOxygenPartialPressureKPa = 0;
            state.OxygenGraceRemaining = 0;
        }

        public static bool CanRefill(in PredictedPlayerGhost state) {
            return state.BodyHealthInitialized && state.CurrentHealth > 0 &&
                   (!state.ProtectionInitialized || state.HeadDomeEquipped) && float.IsFinite(state.Oxygen) &&
                   state.Oxygen < Capacity;
        }

        // 便携补氧增加项圈资源并清重新窒息等待。返回实际加了多少，而不是直接采用物品标的数量。
        public static float Refill(ref PredictedPlayerGhost state, float amount) {
            if (!CanRefill(state) || !float.IsFinite(amount) || amount <= 0) return 0;
            float before = Mathf.Max(0, state.Oxygen);
            state.Oxygen = Mathf.Min(Capacity, before + amount);
            state.HypoxiaSeconds = 0;
            state.OxygenGraceRemaining = 0;
            return state.Oxygen - before;
        }

        public static bool NeedsSuitAir(in PredictedPlayerGhost state) {
            return !state.BreathableAir && (state.OxygenDrainPerSecond <= 0 ||
                                            state.OxygenSupplyPerSecond < state.OxygenDrainPerSecond);
        }

        // HUD 用净消耗速度估计剩余时间。处于有氧区或仍在净补充时，没有耗尽倒计时。
        public static float RemainingSeconds(in PredictedPlayerGhost state) {
            float netDrain = state.OxygenDrainPerSecond - state.OxygenSupplyPerSecond;
            return state.BreathableAir || !float.IsFinite(netDrain) || !float.IsFinite(state.Oxygen) || netDrain <= 0
                ? float.PositiveInfinity
                : Mathf.Max(0, state.Oxygen) / netDrain;
        }

        public static bool ReceivingExternalSupply(in PredictedPlayerGhost state) {
            return CanRefill(state) && float.IsFinite(state.OxygenSupplyPerSecond) && state.OxygenSupplyPerSecond > 0 &&
                   float.IsFinite(state.OxygenDrainPerSecond) && state.OxygenDrainPerSecond >= 0;
        }

        // 预计补满的时间也按净供给算，室外补氧站可能一边供一边耗。无净增长就不显示补满倒计时。
        public static float RefillSeconds(in PredictedPlayerGhost state) {
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0 || !float.IsFinite(state.Oxygen) ||
                (state.ProtectionInitialized && !state.HeadDomeEquipped))
                return float.PositiveInfinity;
            if (state.Oxygen >= Capacity) return 0;
            float netSupply = state.OxygenSupplyPerSecond - state.OxygenDrainPerSecond;
            return ReceivingExternalSupply(state) && float.IsFinite(netSupply) && netSupply > 0
                ? (Capacity - Mathf.Max(0, state.Oxygen)) / netSupply
                : float.PositiveInfinity;
        }

        // Empty and full must remain distinct from a tiny positive or almost-full reserve.
        public static int DisplayPercent(float oxygen) {
            return !float.IsFinite(oxygen) ? -1 :
                oxygen <= 0 ? 0 :
                oxygen >= Capacity ? 100 : Mathf.Clamp(Mathf.FloorToInt(oxygen), 1, 99);
        }

        public static void Tick(
            ref PredictedPlayerGhost state,
            in AtmosphereSample atmosphere,
            in OxygenParameters settings,
            float dt,
            uint tick,
            float stationSupplyPerSecond = 0,
            bool sealedReserve = true,
            bool? movingOverride = null,
            float equipmentDrainMultiplier = 1) {
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0 || !float.IsFinite(dt) || dt <= 0) return;
            // C# default(struct) bypasses constructors; make that public API case safe too.
            var parameters = settings.OutdoorSeconds > 0 ? settings : OxygenParameters.Default;
            state.Oxygen = float.IsFinite(state.Oxygen) ? Mathf.Clamp(state.Oxygen, 0, Capacity) : 0;
            state.HypoxiaSeconds = float.IsFinite(state.HypoxiaSeconds) ? Mathf.Max(0, state.HypoxiaSeconds) : 0;
            state.BreathableAir = atmosphere.Breathable;
            state.AmbientPressureKPa = FiniteNonnegative(atmosphere.PressureKPa);
            state.AmbientOxygenPartialPressureKPa =
                FiniteNonnegative(atmosphere.PressureKPa * atmosphere.OxygenFraction);
            bool moving = movingOverride ?? state.ControllerState.MovementSpeed > .1f;
            float exertion = moving
                ? state.ControllerState.Sprinting ? parameters.SprintingMultiplier : parameters.WalkingMultiplier
                : 1;
            // 走跑影响真空耗氧，进入可呼吸环境就不用项圈储备。氧气循环器和特殊食物效果按下方规则改变消耗。
            state.OxygenDrainPerSecond = atmosphere.Breathable ? 0 : Capacity / parameters.OutdoorSeconds * exertion;
            if (sealedReserve && float.IsFinite(equipmentDrainMultiplier))
                state.OxygenDrainPerSecond *= Mathf.Clamp(equipmentDrainMultiplier, .75f, 1);
            if (state.AlienFoodEffectSeconds > 0 && state.AlienFoodEffect == AlienFoodEffect.OxygenReserve)
                state.OxygenDrainPerSecond *= .75f;
            float ambientRefill = sealedReserve && atmosphere.Breathable && atmosphere.AllowsSuitRefill
                ? parameters.AirRefillPerSecond
                : 0;
            // Overlapping sources do not multiply supply. A station can also supply a sealed suit in vacuum.
            // 环境和站点重叠时取更强供给，避免同一人站在两个点旁异常快速补满。没佩戴储氧装备时没有项圈可以加。
            state.OxygenSupplyPerSecond =
                sealedReserve ? Mathf.Max(ambientRefill, FiniteNonnegative(stationSupplyPerSecond)) : 0;
            if (!sealedReserve) state.Oxygen = 0;
            float netDrain = state.OxygenDrainPerSecond - state.OxygenSupplyPerSecond;
            // 这一帧里可能先用完存氧、后进入缺氧。只把氧气用完后的那段时间算进缺氧宽限与伤害。
            float exhaustedTime = netDrain > 0 ? Mathf.Max(0, dt - state.Oxygen / netDrain) : 0;
            state.Oxygen = Mathf.Clamp(state.Oxygen - netDrain * dt, 0, Capacity);
            // 能呼吸或还有氧气时清累计缺氧。持续真空且资源耗尽后才累计倒计时，过宽限只对新增的缺氧时间扣胸部生命。
            if (atmosphere.Breathable || exhaustedTime <= 0) {
                state.HypoxiaSeconds = 0;
                state.OxygenGraceRemaining = !atmosphere.Breathable && netDrain > 0 && state.Oxygen <= 0
                    ? parameters.GraceSeconds
                    : 0;
                return;
            }
            float before = Mathf.Max(0, state.HypoxiaSeconds - parameters.GraceSeconds);
            state.HypoxiaSeconds += exhaustedTime;
            state.OxygenGraceRemaining = Mathf.Max(0, parameters.GraceSeconds - state.HypoxiaSeconds);
            float exposure = Mathf.Max(0, state.HypoxiaSeconds - parameters.GraceSeconds) - before;
            if (exposure > 0)
                RaidHealth.Damage(ref state, BodyPart.Chest, exposure * parameters.DamagePerSecond, tick, true);
        }

        private static float FiniteNonnegative(float value) { return float.IsFinite(value) ? Mathf.Max(0, value) : 0; }
    }
}