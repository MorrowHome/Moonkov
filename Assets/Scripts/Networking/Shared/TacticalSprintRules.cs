// 按持续奔跑时间和负重计算体力。停跑一会后才恢复，耗尽之后要攒到恢复线才清掉疲劳，避免一节 tick 就在耗尽与正常间来回切换。
using System;

namespace Unity.MP_FPS {
    public struct TacticalSprintState {
        public bool Initialized, Exhausted;
        public float Value, RestDelay;
    }

    // Moonkov 第一版的冲刺耐力独立于饥饿、口渴、氧气，
    // 也和未实现的 Hunt 近战或战斗耐力各自分开。
    public static class TacticalSprintRules {
        public const float Capacity = 100,
            DrainPerSecond = 2,
            RecoveryPerSecond = 20,
            RecoveryDelay = 1.5f,
            ResumeThreshold = 25;

        public static void Reset(ref TacticalSprintState state) {
            state.Initialized = true;
            state.Value = Capacity;
            state.RestDelay = 0;
            state.Exhausted = false;
        }

        public static void Tick(
            ref TacticalSprintState state,
            float dt,
            bool sprinting,
            bool stimulated,
            bool canAct = true,
            float carriedWeight = 0) {
            // 首次用或读取无效数值时初始化为满。阈值与耗时之后按合法正时间推进。
            if (!state.Initialized || !float.IsFinite(state.Value)) Reset(ref state);
            if (!float.IsFinite(dt) || dt <= 0) return;
            // 无法行动或有体力药效时重置疲劳。减速和恢复仍由服务端回传的这份状态决定。
            if (!canAct || stimulated) {
                Reset(ref state);
                return;
            }
            // 实际连续冲刺使耐力下降并重置休息等待。负重越多这一步扣得越快。
            if (sprinting) {
                state.Value = Math.Max(0,
                    state.Value - DrainPerSecond * RaidEncumbrance.SprintDrain(carriedWeight) * dt);
                state.RestDelay = RecoveryDelay;
                if (state.Value <= .001f) {
                    state.Value = 0;
                    state.Exhausted = true;
                }
                return;
            }
            // 等待可能在一帧中结束，只将余下时间用于恢复。耗尽后到恢复门槛才重新进入非疲劳。
            float usable = Math.Max(0, dt - state.RestDelay);
            state.RestDelay = Math.Max(0, state.RestDelay - dt);
            state.Value = Math.Min(Capacity,
                state.Value + RecoveryPerSecond * RaidEncumbrance.SprintRecovery(carriedWeight) * usable);
            if (state.Exhausted && state.Value + .001f >= ResumeThreshold) state.Exhausted = false;
        }
    }
}