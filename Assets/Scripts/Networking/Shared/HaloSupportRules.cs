// 外放光环作为照明、范围护盾和哨戒时的规则。这里决定能保护谁、何时开火，以及吸收伤害后留下什么受击表现数据。
using Unity.Mathematics;
using UnityEngine;

namespace Unity.MP_FPS {
    public enum HaloDeploymentMode : byte {
        Transfer,
        AllyGuard,
        Light,
        Shield,
        Sentry
    }

    public static class HaloSupportRules {
        public const float PlacementRange = 8, LeashRange = 18, ShieldRadius = 2.5f, SentryRange = 18;
        public const float SentryInterval = .65f;

        // 照明、范围罩和哨戒都以场上固定点为中心，和飞向队友的转送是不同分支。
        public static bool Anchored(in PredictedPlayerGhost state) {
            return state.HaloDeploymentMode >= HaloDeploymentMode.Light;
        }

        public static bool CanProtect(in PredictedPlayerGhost target) {
            return EnergyShieldRules.Alive(target) && (!target.ClashEnabled || target.ClashCanAct);
        }

        public static bool Within(float3 a, float3 b, float range) {
            return math.all(math.isfinite(a)) && math.all(math.isfinite(b)) && math.distancesq(a, b) <= range * range;
        }

        // 切换按照明、范围护盾、哨戒循环。外放模式编码同时给动作和表现判断。
        public static HaloDeploymentMode NextMode(HaloDeploymentMode mode) {
            return mode == HaloDeploymentMode.Light ? HaloDeploymentMode.Shield :
                mode == HaloDeploymentMode.Shield ? HaloDeploymentMode.Sentry : HaloDeploymentMode.Light;
        }

        // 用当前光环能量先重算可发射弹数，还没完成放置或武器冷却未结束就不能替玩家开火。
        public static bool ReadySentry(ref PredictedPlayerGhost state, WeaponData weapon) {
            HaloEnergyRules.RefreshAmmo(ref state, weapon);
            return state.HaloDeploymentMode == HaloDeploymentMode.Sentry &&
                   state.HaloTransferPhase == HaloTransferPhase.Sending && HaloTransferRules.CanSend(state) &&
                   weapon != null && state.CurrentAmmo > 0 && state.WeaponCooldown >= Mathf.Max(SentryInterval,
                       weapon.BurstCount > 1 ? weapon.BurstCooldown : weapon.CooldownInMs);
        }

        // Deliberately broad aim error. Weapon pellet spread is applied afterwards as usual.
        // 用当前网络 tick 导出这次哨戒的偏差方向，可重复得到同样的射击采样。武器自己的散布之后再加。
        public static Vector3 SentryDirection(Vector3 forward, uint tick) {
            uint hash = unchecked(tick * 747796405u + 2891336453u);
            hash = unchecked(((hash >> ((int)(hash >> 28) + 4)) ^ hash) * 277803737u);
            hash ^= hash >> 22;
            float radius = Mathf.Sqrt((hash & 65535u) / 65536f) * Mathf.Tan(9 * Mathf.Deg2Rad);
            float angle = (hash >> 16) / 65536f * Mathf.PI * 2;
            return Quaternion.LookRotation(forward) *
                   new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 1).normalized;
        }

        // 外放动画是一次带用时的过渡。到点才切到已放置阶段，之后才允许范围照明与自动动作。
        public static void AdvanceDeployment(ref PredictedPlayerGhost state, float dt) {
            if (state.HaloTransferPhase != HaloTransferPhase.Extending || !float.IsFinite(dt) || dt <= 0) return;
            state.HaloTransferRemaining = Mathf.Max(0, state.HaloTransferRemaining - dt);
            if (state.HaloTransferRemaining <= 0) state.HaloTransferPhase = HaloTransferPhase.Sending;
        }

        // 周围的外放能量罩从提供者的光环池扣能。扣够的伤害不进入目标身体，受击点记录在聚合保护者上。
        public static float Absorb(
            ref PredictedPlayerGhost provider,
            float incoming,
            uint tick,
            RaidDamageKind kind,
            float3 point) {
            if (!EnergyShieldRules.Blocks(kind) || !float.IsFinite(incoming) || incoming <= 0) return 0;
            float absorbed = HaloEnergyRules.Spend(ref provider, incoming);
            if (absorbed > 0) RecordHit(ref provider, absorbed, incoming, tick, point);
            return absorbed;
        }

        public static void RecordHit(
            ref PredictedPlayerGhost state,
            float absorbed,
            float incoming,
            uint tick,
            float3 point) {
            state.ShieldHitSequence++;
            state.ShieldHitStrength = Mathf.Clamp01(absorbed / HaloEnergyRules.Maximum(state) * 3);
            state.ShieldHitPoint = math.all(math.isfinite(point)) ? point : HaloTransferRules.Anchor(state);
            state.LastHitTick = tick;
            state.ControllerState.IsHit = true;
            if (absorbed >= incoming) {
                state.LastDamageAmount = 0;
                state.LastHitPart = BodyPart.Auto;
            }
        }
    }
}