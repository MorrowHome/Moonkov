// 把异形的动作和已经过的时间换成液体动画的参数。蓄力逐渐鼓起，出招按节拍喷涌，恢复和死亡时逐渐收敛。
using Unity.MP_FPS.SpiderLab;
using UnityEngine;

namespace Unity.MP_FPS {
    public static class AlienLiquidMotion {
        public static LiquidReaction Evaluate(
            AlienCreatureProfile profile,
            AlienAction action,
            float age,
            float windup,
            float active,
            float recovery,
            Vector3 centre,
            Vector3 direction) {
            var reaction = new LiquidReaction {
                Centre = centre, Direction = direction,
                Strength = profile.VoidLiquid ? profile.LiquidReactionStrength : 0
            };
            // 蓄力使用平滑增长。威吓则先涨后落，留下可以完整播放的小峰值。
            if (action == AlienAction.Windup) {
                reaction.Charge = Mathf.SmoothStep(0, 1, age / Mathf.Max(.05f, windup));
            } else if (action == AlienAction.Dominance) {
                reaction.Charge = .65f * Mathf.Sin(Mathf.Clamp01(age / profile.BossDisplaySeconds) * Mathf.PI);
            } else if (action == AlienAction.Strike || action == AlienAction.Attached) {
                // 不同体型在一次出招里能有不同的液体脉冲次数。这里计算画面强度，不产生伤害。
                int pulses = profile.Species == AlienSpecies.Slime || profile.Species == AlienSpecies.Caterpillar ||
                             profile.Species == AlienSpecies.Leech
                    ? 3
                    : 1;
                float t = Mathf.Clamp01(age / Mathf.Max(.05f, active));
                reaction.Burst = Mathf.Sin(Mathf.Repeat(t * pulses, 1) * Mathf.PI);
                reaction.Charge = .25f;
            // 收招时把流体逐步拉回原状。死亡的短促爆发则快速消退。
            } else if (action == AlienAction.Recover) {
                reaction.Recovery = 1 - Mathf.Clamp01(age / Mathf.Max(.05f, recovery));
            } else if (action == AlienAction.Dead) { reaction.Burst = .5f * Mathf.Exp(-age * 5); }
            return reaction;
        }
    }
}