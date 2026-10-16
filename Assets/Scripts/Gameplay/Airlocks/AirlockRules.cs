using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    public enum AirlockCommand : byte { OpenOuter, CloseOuter, OpenInner, CloseInner, EqualizeInside, EqualizeOutside, CancelEqualization }
    public enum AirlockFeedback : byte { Accepted, OtherDoorNotSealed, PressureMismatch, Equalizing, DoorsNotSealed, Obstructed, OutOfRange, Unavailable, StaleRequest, Pending }

    [Serializable]
    public sealed class AirlockSettings
    {
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

        public bool IsValid => Finite(InsidePressureKPa) && InsidePressureKPa > 0 && Finite(OutsidePressureKPa) &&
            OutsidePressureKPa >= 0 && OutsidePressureKPa <= InsidePressureKPa && Finite(SupplyOxygenFraction) &&
            SupplyOxygenFraction >= 0 && SupplyOxygenFraction <= 1 && Finite(PressurizeSeconds) && PressurizeSeconds > 0 &&
            Finite(DepressurizeSeconds) && DepressurizeSeconds > 0 && Finite(DoorTravelSeconds) && DoorTravelSeconds > 0 &&
            Finite(OpeningPressureToleranceKPa) && OpeningPressureToleranceKPa >= 0 && Finite(SealedFractionTolerance) &&
            SealedFractionTolerance >= 0 && SealedFractionTolerance < .1f && Finite(ChamberVolumeM3) && ChamberVolumeM3 > 0 &&
            Finite(BreathablePressureKPa) && BreathablePressureKPa >= 0 && Finite(BreathableOxygenPartialPressureKPa) &&
            BreathableOxygenPartialPressureKPa >= 0 && Finite(InteractionDistanceM) && InteractionDistanceM > 0;
        internal static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    [Serializable]
    public struct AirlockState
    {
        public float PressureKPa, OuterFraction, InnerFraction;
        public bool OuterOpening, InnerOpening, Equalizing, TargetInside;
        public uint Revision;
        public AirlockFeedback Feedback;
    }

    // Owns the pressure/interlock rules; transforms, sounds, transport and oxygen remain separate.
    public sealed class AirlockRules
    {
        public AirlockSettings Settings { get; }
        public AirlockState State { get; private set; }
        public bool OuterSealed => State.OuterFraction <= Settings.SealedFractionTolerance && !State.OuterOpening;
        public bool InnerSealed => State.InnerFraction <= Settings.SealedFractionTolerance && !State.InnerOpening;
        public bool Breathable => State.PressureKPa >= Settings.BreathablePressureKPa &&
            State.PressureKPa * Settings.SupplyOxygenFraction >= Settings.BreathableOxygenPartialPressureKPa;

        public AirlockRules(AirlockSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (!settings.IsValid) throw new ArgumentException("Invalid airlock settings", nameof(settings));
            State = new AirlockState { PressureKPa = settings.OutsidePressureKPa, Feedback = AirlockFeedback.Accepted };
        }

        public AirlockFeedback Check(AirlockCommand command, bool outerObstructed = false, bool innerObstructed = false)
        {
            if (!Settings.IsValid) return AirlockFeedback.Unavailable;
            switch (command)
            {
                case AirlockCommand.OpenOuter:
                    if (!InnerSealed) return AirlockFeedback.OtherDoorNotSealed;
                    if (State.Equalizing) return AirlockFeedback.Equalizing;
                    return Mathf.Abs(State.PressureKPa - Settings.OutsidePressureKPa) <= Settings.OpeningPressureToleranceKPa ?
                        AirlockFeedback.Accepted : AirlockFeedback.PressureMismatch;
                case AirlockCommand.OpenInner:
                    if (!OuterSealed) return AirlockFeedback.OtherDoorNotSealed;
                    if (State.Equalizing) return AirlockFeedback.Equalizing;
                    return Mathf.Abs(State.PressureKPa - Settings.InsidePressureKPa) <= Settings.OpeningPressureToleranceKPa ?
                        AirlockFeedback.Accepted : AirlockFeedback.PressureMismatch;
                case AirlockCommand.CloseOuter: return outerObstructed ? AirlockFeedback.Obstructed : AirlockFeedback.Accepted;
                case AirlockCommand.CloseInner: return innerObstructed ? AirlockFeedback.Obstructed : AirlockFeedback.Accepted;
                case AirlockCommand.EqualizeInside:
                case AirlockCommand.EqualizeOutside:
                    return OuterSealed && InnerSealed ? AirlockFeedback.Accepted : AirlockFeedback.DoorsNotSealed;
                case AirlockCommand.CancelEqualization: return AirlockFeedback.Accepted;
                default: return AirlockFeedback.Unavailable;
            }
        }

        public AirlockFeedback Request(AirlockCommand command, bool outerObstructed = false, bool innerObstructed = false)
        {
            var s = State; s.Feedback = Check(command, outerObstructed, innerObstructed);
            if (s.Feedback == AirlockFeedback.Accepted)
            {
                switch (command)
                {
                    case AirlockCommand.OpenOuter: s.OuterOpening = true; break;
                    case AirlockCommand.CloseOuter: s.OuterOpening = false; break;
                    case AirlockCommand.OpenInner: s.InnerOpening = true; break;
                    case AirlockCommand.CloseInner: s.InnerOpening = false; break;
                    case AirlockCommand.EqualizeInside: s.TargetInside = true; s.Equalizing = true; break;
                    case AirlockCommand.EqualizeOutside: s.TargetInside = false; s.Equalizing = true; break;
                    case AirlockCommand.CancelEqualization: s.Equalizing = false; break;
                }
            }
            s.Revision++; State = s; return s.Feedback;
        }

        public void Tick(float dt, bool outerObstructed = false, bool innerObstructed = false,
            bool outerOpeningBlocked = false, bool innerOpeningBlocked = false)
        {
            if (!AirlockSettings.Finite(dt) || dt <= 0 || !Settings.IsValid) return;
            var s = State;
            // Reverse a closing door if someone enters its fixed aperture sensor mid-motion.
            if (!s.OuterOpening && s.OuterFraction > 0 && outerObstructed) { s.OuterOpening = true; s.Feedback = AirlockFeedback.Obstructed; }
            if (!s.InnerOpening && s.InnerFraction > 0 && innerObstructed) { s.InnerOpening = true; s.Feedback = AirlockFeedback.Obstructed; }
            bool outerHeld = s.OuterOpening && outerOpeningBlocked, innerHeld = s.InnerOpening && innerOpeningBlocked;
            if (outerHeld || innerHeld) s.Feedback = AirlockFeedback.Obstructed;
            else if (s.Feedback == AirlockFeedback.Obstructed && !outerObstructed && !innerObstructed) s.Feedback = AirlockFeedback.Accepted;
            if (!outerHeld) s.OuterFraction = Mathf.MoveTowards(s.OuterFraction, s.OuterOpening ? 1 : 0, dt / Settings.DoorTravelSeconds);
            if (!innerHeld) s.InnerFraction = Mathf.MoveTowards(s.InnerFraction, s.InnerOpening ? 1 : 0, dt / Settings.DoorTravelSeconds);
            if (s.Equalizing)
            {
                if (s.OuterOpening || s.InnerOpening || s.OuterFraction > Settings.SealedFractionTolerance || s.InnerFraction > Settings.SealedFractionTolerance)
                { s.Equalizing = false; s.Feedback = AirlockFeedback.DoorsNotSealed; }
                else
                {
                    float target = s.TargetInside ? Settings.InsidePressureKPa : Settings.OutsidePressureKPa;
                    float seconds = s.TargetInside ? Settings.PressurizeSeconds : Settings.DepressurizeSeconds;
                    s.PressureKPa = Mathf.MoveTowards(s.PressureKPa, target,
                        (Settings.InsidePressureKPa - Settings.OutsidePressureKPa) * dt / seconds);
                    if (Mathf.Abs(s.PressureKPa - target) < .0001f) { s.PressureKPa = target; s.Equalizing = false; }
                }
            }
            if (s.OuterFraction > Settings.SealedFractionTolerance) s.PressureKPa = Settings.OutsidePressureKPa;
            else if (s.InnerFraction > Settings.SealedFractionTolerance) s.PressureKPa = Settings.InsidePressureKPa;
            s.PressureKPa = Mathf.Clamp(s.PressureKPa, Settings.OutsidePressureKPa, Settings.InsidePressureKPa);
            if (s.PressureKPa != State.PressureKPa || s.OuterFraction != State.OuterFraction || s.InnerFraction != State.InnerFraction ||
                s.OuterOpening != State.OuterOpening || s.InnerOpening != State.InnerOpening || s.Equalizing != State.Equalizing || s.Feedback != State.Feedback)
            { s.Revision++; State = s; }
        }

        public bool ApplySnapshot(AirlockState state, bool freshSession)
        {
            if (!AirlockSettings.Finite(state.PressureKPa) || !AirlockSettings.Finite(state.OuterFraction) || !AirlockSettings.Finite(state.InnerFraction) ||
                state.PressureKPa < Settings.OutsidePressureKPa || state.PressureKPa > Settings.InsidePressureKPa ||
                state.OuterFraction < 0 || state.OuterFraction > 1 || state.InnerFraction < 0 || state.InnerFraction > 1 ||
                (state.OuterFraction > Settings.SealedFractionTolerance || state.OuterOpening) &&
                (state.InnerFraction > Settings.SealedFractionTolerance || state.InnerOpening) ||
                state.Equalizing && (state.OuterFraction > Settings.SealedFractionTolerance || state.InnerFraction > Settings.SealedFractionTolerance || state.OuterOpening || state.InnerOpening) ||
                !freshSession && state.Revision <= State.Revision) return false;
            State = state; return true;
        }
    }
}
