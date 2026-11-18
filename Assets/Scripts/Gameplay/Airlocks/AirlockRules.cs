// 气闸的规则和状态机。用同一份气压、门开度和互锁条件来决定能否开关门、充气或泄压。
// AirlockController 调规则，再把计算好的开度和气压应用到场景。
using System;
using UnityEngine;

namespace Unity.MP_FPS {
    public enum AirlockCommand : byte {
        OpenOuter,
        CloseOuter,
        OpenInner,
        CloseInner,
        EqualizeInside,
        EqualizeOutside,
        CancelEqualization,
        CycleOuter,
        CycleInner
    }

    public enum AirlockFeedback : byte {
        Accepted,
        OtherDoorNotSealed,
        PressureMismatch,
        Equalizing,
        DoorsNotSealed,
        Obstructed,
        OutOfRange,
        Unavailable,
        StaleRequest,
        Pending
    }

    [Serializable]
    public sealed class AirlockSettings {
        // 外侧和内侧是两个固定目标气压；真正随时间变化的是舱里的 PressureKPa。
        [Min(0)] public float OutsidePressureKPa;
        [Min(1)] public float InsidePressureKPa = 101.325f;
        [Range(0, 1)] public float SupplyOxygenFraction = .21f;
        [Min(.1f)] public float ChamberVolumeM3 = 30;
        [Min(.1f)] public float PressurizeSeconds = 6;
        [Min(.1f)] public float DepressurizeSeconds = 6;
        [Min(.1f)] public float DoorTravelSeconds = 1.2f;
        [Min(.001f)] public float OpeningPressureToleranceKPa = 1;
        [Range(.0001f, .05f)] public float SealedFractionTolerance = .001f;
        [Min(0)] public float BreathablePressureKPa = 60;
        [Min(0)] public float BreathableOxygenPartialPressureKPa = 15;
        [Min(.1f)] public float InteractionDistanceM = 3;

        // 规则在开始前拒绝无效气压、时间和密封阈值。比如门的行程时间为零会让每帧步长算坏。
        public bool IsValid =>
            Finite(InsidePressureKPa) && InsidePressureKPa > 0 && Finite(OutsidePressureKPa) &&
            OutsidePressureKPa >= 0 && OutsidePressureKPa <= InsidePressureKPa && Finite(SupplyOxygenFraction) &&
            SupplyOxygenFraction >= 0 && SupplyOxygenFraction <= 1 && Finite(PressurizeSeconds) &&
            PressurizeSeconds > 0 && Finite(DepressurizeSeconds) && DepressurizeSeconds > 0 &&
            Finite(DoorTravelSeconds) && DoorTravelSeconds > 0 && Finite(OpeningPressureToleranceKPa) &&
            OpeningPressureToleranceKPa >= 0 && Finite(SealedFractionTolerance) && SealedFractionTolerance >= 0 &&
            SealedFractionTolerance < .1f && Finite(ChamberVolumeM3) && ChamberVolumeM3 > 0 &&
            Finite(BreathablePressureKPa) && BreathablePressureKPa >= 0 && Finite(BreathableOxygenPartialPressureKPa) &&
            BreathableOxygenPartialPressureKPa >= 0 && Finite(InteractionDistanceM) && InteractionDistanceM > 0;

        internal static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
    }

    [Serializable]
    public struct AirlockState {
        // 开度 0 表示门板关到位，1 表示开到头；Opening 表示门接下来往哪边走，开度还可能在路上。
        public float PressureKPa, OuterFraction, InnerFraction;
        public bool OuterOpening, InnerOpening, Equalizing, TargetInside;
        // 版本跟着可观察的状态变化更新。同一个气闸的快照可据此舍弃乱序到达的旧包。
        public uint Revision;
        public AirlockFeedback Feedback;
    }

    // 这里计算气压与双门互锁。门板位姿、声音、同步和氧气由各自系统处理。
    public sealed class AirlockRules {
        private AirlockCommand? m_Cycle;
        public AirlockSettings Settings { get; }
        public AirlockState State { get; private set; }
        public bool OuterSealed => State.OuterFraction <= Settings.SealedFractionTolerance && !State.OuterOpening;
        public bool InnerSealed => State.InnerFraction <= Settings.SealedFractionTolerance && !State.InnerOpening;

        public bool Breathable =>
            State.PressureKPa >= Settings.BreathablePressureKPa && State.PressureKPa * Settings.SupplyOxygenFraction >=
            Settings.BreathableOxygenPartialPressureKPa;

        public AirlockRules(AirlockSettings settings) {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (!settings.IsValid) throw new ArgumentException("Invalid airlock settings", nameof(settings));
            State = new AirlockState { PressureKPa = settings.OutsidePressureKPa, Feedback = AirlockFeedback.Accepted };
        }

        public AirlockFeedback Check(
            AirlockCommand command,
            bool outerObstructed = false,
            bool innerObstructed = false) {
            if (!Settings.IsValid) return AirlockFeedback.Unavailable;
            // 自动循环会接管接下来的关门、调压和开门，期间只允许取消，避免新指令和它抢状态。
            if (m_Cycle.HasValue && command != AirlockCommand.CancelEqualization) return AirlockFeedback.Equalizing;
            switch (command) {
                case AirlockCommand.OpenOuter:
                    // 有人想直接开外门时，先确认内门密封，再确认舱压和舱外压差足够小。内门按相同道理处理。
                    if (!InnerSealed) return AirlockFeedback.OtherDoorNotSealed;
                    if (State.Equalizing) return AirlockFeedback.Equalizing;
                    return Mathf.Abs(State.PressureKPa - Settings.OutsidePressureKPa) <=
                           Settings.OpeningPressureToleranceKPa
                        ? AirlockFeedback.Accepted
                        : AirlockFeedback.PressureMismatch;
                case AirlockCommand.OpenInner:
                    if (!OuterSealed) return AirlockFeedback.OtherDoorNotSealed;
                    if (State.Equalizing) return AirlockFeedback.Equalizing;
                    return Mathf.Abs(State.PressureKPa - Settings.InsidePressureKPa) <=
                           Settings.OpeningPressureToleranceKPa
                        ? AirlockFeedback.Accepted
                        : AirlockFeedback.PressureMismatch;
                // 关门的条件在这里主要是防夹。直接调压的条件更严，两侧都密封才行。
                case AirlockCommand.CloseOuter:
                    return outerObstructed ? AirlockFeedback.Obstructed : AirlockFeedback.Accepted;
                case AirlockCommand.CloseInner:
                    return innerObstructed ? AirlockFeedback.Obstructed : AirlockFeedback.Accepted;
                case AirlockCommand.EqualizeInside:
                case AirlockCommand.EqualizeOutside:
                    return OuterSealed && InnerSealed ? AirlockFeedback.Accepted : AirlockFeedback.DoorsNotSealed;
                case AirlockCommand.CycleOuter:
                    if (State.Equalizing) return AirlockFeedback.Equalizing;
                    // 请求自动出舱时，先判断下一步能否安全关内门。舱压会在后续 Tick 慢慢调整。
                    return !InnerSealed && innerObstructed ? AirlockFeedback.Obstructed : AirlockFeedback.Accepted;
                case AirlockCommand.CycleInner:
                    if (State.Equalizing) return AirlockFeedback.Equalizing;
                    return !OuterSealed && outerObstructed ? AirlockFeedback.Obstructed : AirlockFeedback.Accepted;
                case AirlockCommand.CancelEqualization: return AirlockFeedback.Accepted;
                default: return AirlockFeedback.Unavailable;
            }
        }

        public AirlockFeedback Request(
            AirlockCommand command,
            bool outerObstructed = false,
            bool innerObstructed = false) {
            // Check 只回答能不能做。Request 才更新目标状态，并把接受或拒绝原因写进状态。
            var s = State;
            s.Feedback = Check(command, outerObstructed, innerObstructed);
            if (s.Feedback == AirlockFeedback.Accepted)
                switch (command) {
                    case AirlockCommand.OpenOuter: s.OuterOpening = true; break;
                    case AirlockCommand.CloseOuter: s.OuterOpening = false; break;
                    case AirlockCommand.OpenInner: s.InnerOpening = true; break;
                    case AirlockCommand.CloseInner: s.InnerOpening = false; break;
                    case AirlockCommand.EqualizeInside:
                        s.TargetInside = true;
                        s.Equalizing = true;
                        break;
                    case AirlockCommand.EqualizeOutside:
                        s.TargetInside = false;
                        s.Equalizing = true;
                        break;
                    case AirlockCommand.CycleOuter:
                        m_Cycle = command;
                        s.InnerOpening = false;
                        AdvanceCycle(ref s);
                        break;
                    case AirlockCommand.CycleInner:
                        m_Cycle = command;
                        s.OuterOpening = false;
                        AdvanceCycle(ref s);
                        break;
                    case AirlockCommand.CancelEqualization:
                        s.Equalizing = false;
                        m_Cycle = null;
                        break;
                }
            s.Revision++;
            State = s;
            return s.Feedback;
        }

        // 自动过门的顺序是先等另一侧密封，再把舱压调成目标侧的气压，最后开目标侧的门。
        private void AdvanceCycle(ref AirlockState state) {
            if (!m_Cycle.HasValue || state.Equalizing) return;
            bool inside = m_Cycle == AirlockCommand.CycleInner;
            bool outerSealed = state.OuterFraction <= Settings.SealedFractionTolerance && !state.OuterOpening;
            bool innerSealed = state.InnerFraction <= Settings.SealedFractionTolerance && !state.InnerOpening;
            // 对侧门刚收到关闭命令也可能还在移动。看开度和方向一起确认密封后才进入下一步。
            if (inside ? !outerSealed : !innerSealed) return;
            float target = inside ? Settings.InsidePressureKPa : Settings.OutsidePressureKPa;
            if (Mathf.Abs(state.PressureKPa - target) <= Settings.OpeningPressureToleranceKPa) {
                if (inside)
                    state.InnerOpening = true;
                else
                    state.OuterOpening = true;
                m_Cycle = null;
                state.Feedback = AirlockFeedback.Accepted;
            // 如果目标侧的气压已匹配，会直接开始开门；还差一截才进入调压，且此时两边都得密封。
            } else if (outerSealed && innerSealed) {
                state.TargetInside = inside;
                state.Equalizing = true;
            }
        }

        public void Tick(
            float dt,
            bool outerObstructed = false,
            bool innerObstructed = false,
            bool outerOpeningBlocked = false,
            bool innerOpeningBlocked = false) {
            if (!AirlockSettings.Finite(dt) || dt <= 0 || !Settings.IsValid) return;
            var s = State;
            // 门正在关时，有人进入门框感应范围就改为反开。
            if (!s.OuterOpening && s.OuterFraction > 0 && outerObstructed) {
                s.OuterOpening = true;
                s.Feedback = AirlockFeedback.Obstructed;
                m_Cycle = null;
            }
            if (!s.InnerOpening && s.InnerFraction > 0 && innerObstructed) {
                s.InnerOpening = true;
                s.Feedback = AirlockFeedback.Obstructed;
                m_Cycle = null;
            }
            // 关闭时遇人会反开。开启时路径受阻先停，等路径通畅再接着开。
            bool outerHeld = s.OuterOpening && outerOpeningBlocked, innerHeld = s.InnerOpening && innerOpeningBlocked;
            if (outerHeld || innerHeld)
                s.Feedback = AirlockFeedback.Obstructed;
            else if (s.Feedback == AirlockFeedback.Obstructed && !outerObstructed && !innerObstructed)
                s.Feedback = AirlockFeedback.Accepted;
            if (!outerHeld)
                s.OuterFraction = Mathf.MoveTowards(s.OuterFraction, s.OuterOpening ? 1 : 0,
                    dt / Settings.DoorTravelSeconds);
            if (!innerHeld)
                s.InnerFraction = Mathf.MoveTowards(s.InnerFraction, s.InnerOpening ? 1 : 0,
                    dt / Settings.DoorTravelSeconds);
            // 用内外最大压差除以配置秒数当作调压速度。起始气压离目标越近，剩下的调压时间越短。
            if (s.Equalizing) {
                if (s.OuterOpening || s.InnerOpening || s.OuterFraction > Settings.SealedFractionTolerance ||
                    s.InnerFraction > Settings.SealedFractionTolerance) {
                    s.Equalizing = false;
                    s.Feedback = AirlockFeedback.DoorsNotSealed;
                    m_Cycle = null;
                } else {
                    float target = s.TargetInside ? Settings.InsidePressureKPa : Settings.OutsidePressureKPa;
                    float seconds = s.TargetInside ? Settings.PressurizeSeconds : Settings.DepressurizeSeconds;
                    s.PressureKPa = Mathf.MoveTowards(s.PressureKPa, target,
                        (Settings.InsidePressureKPa - Settings.OutsidePressureKPa) * dt / seconds);
                    if (Mathf.Abs(s.PressureKPa - target) < .0001f) {
                        s.PressureKPa = target;
                        s.Equalizing = false;
                    }
                }
            }
            // 门已经打开时，舱内气压直接跟对应一侧一致。此时再保留密封舱内的气压会给角色判错氧气。
            if (s.OuterFraction > Settings.SealedFractionTolerance)
                s.PressureKPa = Settings.OutsidePressureKPa;
            else if (s.InnerFraction > Settings.SealedFractionTolerance) s.PressureKPa = Settings.InsidePressureKPa;
            // 到达目标或门关到位的这一拍，给自动循环一次机会进入下一阶段。
            s.PressureKPa = Mathf.Clamp(s.PressureKPa, Settings.OutsidePressureKPa, Settings.InsidePressureKPa);
            AdvanceCycle(ref s);
            if (s.PressureKPa != State.PressureKPa || s.OuterFraction != State.OuterFraction ||
                s.InnerFraction != State.InnerFraction || s.OuterOpening != State.OuterOpening ||
                s.InnerOpening != State.InnerOpening || s.Equalizing != State.Equalizing ||
                s.Feedback != State.Feedback) {
                s.Revision++;
                State = s;
            }
        }

        // 客户端接收快照时检查开度、气压和互锁条件。同一局只接受比当前更新的版本。
        public bool ApplySnapshot(AirlockState state, bool freshSession) {
            if (!AirlockSettings.Finite(state.PressureKPa) || !AirlockSettings.Finite(state.OuterFraction) ||
                !AirlockSettings.Finite(state.InnerFraction) || state.PressureKPa < Settings.OutsidePressureKPa ||
                state.PressureKPa > Settings.InsidePressureKPa || state.OuterFraction < 0 || state.OuterFraction > 1 ||
                state.InnerFraction < 0 || state.InnerFraction > 1 ||
                ((state.OuterFraction > Settings.SealedFractionTolerance || state.OuterOpening) &&
                 (state.InnerFraction > Settings.SealedFractionTolerance || state.InnerOpening)) || (state.Equalizing &&
                    (state.OuterFraction > Settings.SealedFractionTolerance ||
                     state.InnerFraction > Settings.SealedFractionTolerance || state.OuterOpening ||
                     state.InnerOpening)) || (!freshSession && state.Revision <= State.Revision))
                return false;
            m_Cycle = null;
            State = state;
            return true;
        }
    }
}