// 计算光环护盾能挡的游戏伤害。一次外部命中先扣共享光环能量，再把剩余伤害交给身体部位。全部挡下时也会留下护盾受击事件。
using Unity.Mathematics;
using UnityEngine;

namespace Unity.MP_FPS {
    // 服务端掌管战斗资源。身体内部损伤不经过 Absorb。
    public static class EnergyShieldRules {
        public const float Capacity = 100,
            EnergyCapacity = 100,
            RecoveryDelay = 6,
            RecoveryPerSecond = 8,
            EnergyPerShield = .5f;

        // 只挡被划为外部战斗的种类。缺氧、营养不良和其他身体损耗不需要先过光环。
        public static bool Blocks(RaidDamageKind kind) {
            return kind == RaidDamageKind.Bullet || kind == RaidDamageKind.Explosion || kind == RaidDamageKind.Melee ||
                   kind == RaidDamageKind.Fire;
        }

        public static float Resource(float value) {
            return float.IsFinite(value) ? Mathf.Clamp(value, 0, Capacity) : 0;
        }

        public static bool Alive(in PredictedPlayerGhost state) {
            return state.CurrentHealth > 0 && (!state.ClashEnabled || state.ClashLife == ClashLife.Alive);
        }

        // 返回需要由身体承受的剩余伤害。一次外部命中先调用一次，再分到各部位。
        // 伤害发生时按共享光环能量结算。能量只够挡一部分就把余量交回调用者，一次命中不能对多个身体部位重复扣光环能量。
        public static float Absorb(
            ref PredictedPlayerGhost state,
            float incoming,
            uint tick,
            RaidDamageKind kind,
            float3 impact = default) {
            if (!float.IsFinite(incoming) || incoming <= 0) return 0;
            // 玩家正在举盾仍要核对活着、能用光环和允许的伤害种类。条件不满足时把原伤害原样给调用者。
            if (!state.HaloDefending || !HaloEnergyRules.CanDefend(state) || !Alive(state) || !Blocks(kind))
                return incoming;
            float absorbed = HaloEnergyRules.Spend(ref state, incoming);
            if (absorbed <= 0) return incoming;
            // 真的扣了护盾能量就发受击序号、强度与点位。哪怕完全挡住，也要播护盾命中并中断相应动作。
            state.ShieldHitSequence++;
            state.ShieldHitStrength = Mathf.Clamp01(absorbed / Capacity * 3);
            state.ShieldHitPoint = math.all(math.isfinite(impact)) && math.lengthsq(impact) > .001f
                ? impact
                : state.ControllerState.CurrentPosition + new float3(0, 1, 0) +
                  math.mul(state.ControllerState.CurrentRotation, new float3(0, 0, .4f));
            // 全挡也会打断动作并发出护盾受击事件，身体血量保持原值。
            state.LastHitTick = tick;
            state.ControllerState.IsHit = true;
            if (absorbed >= incoming) {
                state.LastDamageAmount = 0;
                state.LastHitPart = BodyPart.Auto;
            }
            return incoming - absorbed;
        }

        public static void Tick(ref PredictedPlayerGhost state, float dt, bool canRecover = true) {
            HaloEnergyRules.Tick(ref state, dt, canRecover);
        }
    }
}