// 把角色快照转换成生命监测仪的提示、颜色和波形速度。波形由状态推算，用于界面反馈，不作为健康判定。
using UnityEngine;

namespace Unity.MP_FPS.Client {
    // 只负责界面表现。波形由已有角色状态推算，无需新增心率、医疗、网络或存档字段。
    public readonly struct RaidVitalSignal {
        public readonly bool Ready, Alive, Critical;
        public readonly float Frequency;
        public readonly string Warning;

        public RaidVitalSignal(bool ready, bool alive, bool critical, float frequency, string warning) {
            Ready = ready;
            Alive = alive;
            Critical = critical;
            Frequency = frequency;
            Warning = warning;
        }

        public static RaidVitalSignal Read(in PredictedPlayerGhost state) {
            // 角色部位尚未初始化时不要把默认数值当成死亡。UI 可以明确画等待时的信号。
            if (!state.BodyHealthInitialized) return new RaidVitalSignal(false, false, false, 0, "");
            bool downed = state.ClashEnabled && state.ClashLife == ClashLife.Downed;
            // 赏金倒地仍可能被队友救。保持有生命信号，但把风险显示为危急。
            bool alive = state.CurrentHealth > 0 || downed;
            if (!alive) return new RaidVitalSignal(true, false, true, 0, "VITAL SIGNAL LOST");
            bool needsAir = RaidOxygen.NeedsSuitAir(state);
            float danger = RaidHealth.DangerFraction(state);
            bool hypoxic = needsAir && state.Oxygen <= 0;
            bool critical = downed || danger < .35f || hypoxic || (needsAir && state.Oxygen <= 10) ||
                            state.Energy <= 0 || state.Hydration <= 0;
            string warning = downed ? "VITALS CRITICAL" :
                hypoxic ? state.OxygenGraceRemaining > 0 ? "OXYGEN DEPLETED" : "HYPOXIA" :
                danger < .35f ? "VITALS CRITICAL" :
                state.Hydration <= 0 ? "DEHYDRATED" :
                state.Energy <= 0 ? "STARVING" :
                needsAir && state.Oxygen <= RaidOxygen.CriticalThreshold ? "CRITICAL OXYGEN" :
                needsAir && state.Oxygen <= 25 ? "LOW OXYGEN" :
                state.Hydration <= 25 ? "THIRSTY" :
                state.Energy <= 25 ? "HUNGRY" :
                state.TacticalPoisonSeconds > 0 ? "POISONED" : "";
            float stress = Mathf.Clamp01(1 - danger);
            // 绘制频率由冲刺、伤势和缺氧程度推算，用来让危险可见。战斗、呼吸等规则并不使用这个频率。
            float frequency = 1.05f + (state.ControllerState.Sprinting ? .55f : 0) + stress * .4f + (hypoxic ? .3f : 0);
            return new RaidVitalSignal(true, true, critical, Mathf.Clamp(frequency, 1.05f, 2.4f), warning);
        }

        public static float BodyFill(float health, float maximum, bool ready) {
            if (!ready || !float.IsFinite(health) || !float.IsFinite(maximum) || maximum <= 0) return 0;
            return Mathf.Clamp01(health / maximum);
        }

        // 给亮色扫描区域着色。独立的暗色层始终保留完整人体轮廓。
        public static Color BodyTint(float health, float maximum, bool ready) {
            if (!ready || !float.IsFinite(health) || !float.IsFinite(maximum) || maximum <= 0)
                return new Color(.4f, .45f, .5f, .22f);
            if (health <= 0) return new Color(.55f, .6f, .65f, .45f);
            float fraction = Mathf.Clamp01(health / maximum);
            var red = new Color(1, .08f, .04f);
            var orange = new Color(1, .26f, .07f);
            var amber = new Color(1, .7f, .22f);
            if (fraction <= .25f) return Color.Lerp(red, orange, fraction / .25f);
            if (fraction <= .6f) return Color.Lerp(orange, amber, (fraction - .25f) / .35f);
            return Color.Lerp(amber, Color.white, (fraction - .6f) / .4f);
        }

        public static int ReserveCells(float value, int cells) {
            if (!float.IsFinite(value) || value <= 0 || cells <= 0) return 0;
            // 满值才点全格。仍有一点资源时至少留一格，可让空值和低值明显分开。
            if (value >= 100) return cells;
            return Mathf.Clamp(Mathf.FloorToInt(value * .01f * cells), 1, cells - 1);
        }

        public static float Sample(float phase) {
            // 波形按周期循环。按早到晚几个高低峰叠加出一段清楚的扫描信号。
            phase = Mathf.Repeat(phase, 1);

            float Peak(float at, float width, float height) {
                float t = (phase - at) / width;
                return height * Mathf.Exp(-t * t);
            }

            return Peak(.13f, .035f, .09f) - Peak(.245f, .012f, .1f) + Peak(.28f, .013f, .9f) -
                Peak(.315f, .018f, .22f) + Peak(.49f, .065f, .18f);
        }
    }
}