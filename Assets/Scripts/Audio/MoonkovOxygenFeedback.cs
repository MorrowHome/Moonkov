// 从玩家同步过来的氧气、体力和身体状态算出告警等级和呼吸节奏，由 HUD 和角色声音使用。供氧和扣氧的规则在 RaidOxygen。
using UnityEngine;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS {
    public enum OxygenUrgency : byte {
        None,
        Low,
        Critical,
        Depleted
    }

    // 只负责客户端提示。氧气值和补给是否使用成功由玩法流程确认。
    public sealed class MoonkovOxygenFeedback {
        private OxygenUrgency m_Urgency;
        private float m_NextAlert;

        public static OxygenUrgency Urgency(in PredictedPlayerGhost state) {
            // 告警要等有效的身体状态，并且玩家真的需要从服里用氧。进有氧区或死亡后都回到无告警状态。
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0 || !RaidOxygen.NeedsSuitAir(state))
                return OxygenUrgency.None;
            return state.Oxygen <= 0 ? OxygenUrgency.Depleted :
                state.Oxygen <= RaidOxygen.CriticalThreshold ? OxygenUrgency.Critical :
                state.Oxygen <= RaidOxygen.LowThreshold ? OxygenUrgency.Low : OxygenUrgency.None;
        }

        public bool Observe(in PredictedPlayerGhost state, float now) {
            var next = Urgency(state);
            // 供氧和消耗交替时，数值可能在阈值附近来回波动。回升要多出一点余量才降告警等级，防止提示不断重播。
            // 补氧和耗氧的小幅抖动不应让阈值告警反复响。
            if (next < m_Urgency && state.BodyHealthInitialized && state.CurrentHealth > 0 &&
                RaidOxygen.NeedsSuitAir(state) && state.Oxygen > 0) {
                if (m_Urgency == OxygenUrgency.Critical && state.Oxygen <= RaidOxygen.CriticalThreshold + 2)
                    next = OxygenUrgency.Critical;
                else if (m_Urgency == OxygenUrgency.Low && state.Oxygen <= RaidOxygen.LowThreshold + 2)
                    next = OxygenUrgency.Low;
            }
            if (next == OxygenUrgency.None) {
                Reset();
                return false;
            }
            // 恶化到更危险的档次立即提醒。一直处在一个档次时按该档次的间隔 重复提醒。
            bool alert = next > m_Urgency || now >= m_NextAlert;
            m_Urgency = next;
            if (alert)
                m_NextAlert = now + (next == OxygenUrgency.Depleted ? 5 : next == OxygenUrgency.Critical ? 10 : 20);
            return alert;
        }

        public void Reset() {
            m_Urgency = OxygenUrgency.None;
            m_NextAlert = 0;
        }

        public static OxygenBreath Breath(in PredictedPlayerGhost state) {
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0) return default;
            float stamina = float.IsFinite(state.TacticalSprintStamina)
                ? state.TacticalSprintStamina
                : TacticalSprintRules.Capacity;
            // 体力值先换算成疲劳程度，再和近战耐力、冲刺、受伤与缺氧合并。角色停下后也需要实际恢复，呼吸才变缓。
            float fatigue = 1 - Mathf.Clamp01(stamina / TacticalSprintRules.Capacity);
            if (state.MeleeEpoch != 0 && float.IsFinite(state.MeleeStamina))
                fatigue = Mathf.Max(fatigue, 1 - Mathf.Clamp01(state.MeleeStamina / MeleeRules.StaminaCapacity));
            // 玩家松开冲刺后仍能按实际剩余氧气判断何时恢复。
            float effort = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.12f, 1, fatigue));
            if (state.ControllerState.Sprinting && state.ControllerState.MovementSpeed > .1f)
                effort = Mathf.Max(effort, .35f);
            if (state.TacticalSprintExhausted) effort = 1;
            if (state.CurrentHealth < state.MaxHealth * .35f) effort = Mathf.Max(effort, .55f);
            var urgency = Urgency(state);
            if (urgency == OxygenUrgency.Depleted)
                effort = 1;
            else if (urgency == OxygenUrgency.Critical)
                effort = Mathf.Max(effort, .75f);
            else if (urgency == OxygenUrgency.Low) effort = Mathf.Max(effort, .45f);
            // 有自然空气且不累时无需播面罩呼吸。其余情况随疲劳提高呼吸音量并缩短间隔。
            if (state.BreathableAir && effort <= .01f) return default;
            bool exerted = effort >= .45f;
            float interval = exerted
                ? Mathf.Lerp(3f, 2.3f, Mathf.InverseLerp(.45f, 1, effort))
                : Mathf.Lerp(4.1f, 3.5f, effort / .45f);
            return new OxygenBreath(true, interval, Mathf.Lerp(.8f, 1f, effort), 0, exerted);
        }
    }

    public readonly struct OxygenBreath {
        public readonly bool Audible, Exerted;
        public readonly float Interval, Volume, Pitch;

        public OxygenBreath(bool audible, float interval, float volume, float pitch, bool exerted = false) {
            Audible = audible;
            Interval = interval;
            Volume = volume;
            Pitch = pitch;
            Exerted = exerted;
        }
    }
}