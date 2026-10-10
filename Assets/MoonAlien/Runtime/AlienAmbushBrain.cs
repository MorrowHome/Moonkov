using System;
using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    public enum AlienAmbushState { Perch, Stalk, Approach, Telegraph, Strike, Recover, Search, Dead }

    public readonly struct AlienAttackIntent
    {
        public readonly ulong Id;
        public readonly Vector3 Position;
        internal AlienAttackIntent(ulong id, Vector3 position) { Id = id; Position = position; }
    }

    /// <summary>
    /// Plain, observation-only sandbox rules. The caller owns LOS, physical movement,
    /// and time. No target Transform, damage, networking or physics is retained here.
    /// Supply the same nondecreasing time source to all timed methods.
    /// By default attack phases use the diagnostic float clock. Opt-in external combat
    /// emits a windup request only; AlienCombatContract owns all attack phase timing.
    /// </summary>
    public sealed class AlienAmbushBrain
    {
        public const float DecisionInterval = .2f;
        public const float ObservationLifetime = 4f;
        public const float AttackEligibilityLifetime = .15f;
        public const float TelegraphDuration = .7f;
        public const float StrikeDuration = .18f;
        public const float RecoveryDuration = 1.2f;
        public const float SearchDuration = 2f;
        public const float AttackRange = 1.6f;
        public const float ApproachRange = 7f;

        private float lastTime = float.NegativeInfinity;
        private double nextDecision = double.NegativeInfinity;
        private double stateSince;
        private Vector3 committedPosition;
        private AlienAttackIntent pendingIntent;
        private bool attackPending;
        private bool attackEligible;
        private float attackEligibilityTime = float.NegativeInfinity;
        private readonly bool externallyTimedCombat;
        private bool windupPending;
        private bool externalCombatCycle;
        private AlienCombatPhase externalCombatPhase;

        public AlienAmbushState State { get; private set; } = AlienAmbushState.Perch;
        public bool HasObservation { get; private set; }
        public Vector3 LastObservedPosition { get; private set; }
        public float LastObservedTime { get; private set; } = float.NegativeInfinity;
        public ulong LastAttackId { get; private set; }
        public float AttackDistance { get; }
        public bool WantsMovement => HasObservation &&
            (State == AlienAmbushState.Stalk || State == AlienAmbushState.Approach);
        public Vector3 MoveTarget => LastObservedPosition;
        public Vector3 CommittedAttackPosition => committedPosition;

        public AlienAmbushBrain(float attackDistance = AttackRange, bool externallyTimedCombat = false)
        {
            if (!Finite(attackDistance) || attackDistance <= 0f)
                throw new ArgumentOutOfRangeException(nameof(attackDistance));
            AttackDistance = attackDistance;
            this.externallyTimedCombat = externallyTimedCombat;
        }

        // Call only after current LOS, sensor range and FOV all confirm visibility.
        // Do not call for a hidden target merely because its Transform is still available.
        // Positions are copied values; subsequent changes to the source are irrelevant.
        public bool Observe(Vector3 visiblePosition, float now)
        {
            if (State == AlienAmbushState.Dead || !ValidTime(now) || !Finite(visiblePosition)) return false;
            // Sensing never advances timers: the adapter must first publish this frame's
            // attack eligibility, then call Advance, so stale eligibility cannot issue a hit.
            lastTime = now;
            HasObservation = true;
            LastObservedPosition = visiblePosition;
            LastObservedTime = now;
            return true;
        }

        // The adapter supplies same-frame confirmed LOS + FOV + attack-range eligibility.
        // No live target state is retained here. False cancels an in-progress wind-up now.
        public void SetAttackEligibility(bool allowed, float now)
        {
            if (State == AlienAmbushState.Dead || !ValidTime(now)) return;
            lastTime = now;
            attackEligible = allowed;
            attackEligibilityTime = now;
            if (!allowed && State == AlienAmbushState.Telegraph) CancelTelegraph(now);
        }

        // Safe to call each frame; actual tactical decisions are capped at 5 Hz.
        public void Decide(float now, Vector3 bodyPosition)
        {
            if (!ValidTime(now) || !Finite(bodyPosition) || State == AlienAmbushState.Dead) return;
            Advance(now);
            if (now < nextDecision) return;
            nextDecision = (double)now + DecisionInterval;
            if (attackPending || externalCombatCycle || !HasObservation || State == AlienAmbushState.Telegraph ||
                State == AlienAmbushState.Strike || State == AlienAmbushState.Recover) return;

            double x = (double)LastObservedPosition.x - bodyPosition.x;
            double y = (double)LastObservedPosition.y - bodyPosition.y;
            double z = (double)LastObservedPosition.z - bodyPosition.z;
            double distanceSquared = x * x + y * y + z * z;
            if (distanceSquared <= (double)AttackDistance * AttackDistance && CanAttack(now))
            {
                // Once telegraphed, even a later visible observation cannot steer this attack.
                committedPosition = LastObservedPosition;
                Enter(AlienAmbushState.Telegraph, now);
                if (externallyTimedCombat)
                {
                    windupPending = true;
                    externalCombatCycle = true;
                    externalCombatPhase = AlienCombatPhase.Windup;
                }
            }
            else Enter(distanceSquared <= (double)ApproachRange * ApproachRange ?
                AlienAmbushState.Approach : AlienAmbushState.Stalk, now);
        }

        /// <summary>
        /// Advances timers and forgetting independently of the decision cadence.
        /// At most one timed transition per call: a hitch cannot catch up a series of attacks.
        /// Durations start when the transition is observed, so hitches extend, never shorten,
        /// the next telegraph/recovery. Expiry cancels a not-yet-issued strike first.
        /// In external mode only observation, eligibility and search use this clock;
        /// no amount of float time advances Telegraph, Strike or Recover.
        /// </summary>
        public void Advance(float now)
        {
            if (State == AlienAmbushState.Dead || !ValidTime(now)) return;
            lastTime = now;
            if (HasObservation && (double)now - LastObservedTime >= ObservationLifetime)
            {
                Forget();
                if (externallyTimedCombat && State == AlienAmbushState.Telegraph) CancelTelegraph(now);
                else if (State == AlienAmbushState.Perch || State == AlienAmbushState.Stalk ||
                    State == AlienAmbushState.Approach || State == AlienAmbushState.Telegraph)
                    Enter(AlienAmbushState.Search, now);
            }

            if (State == AlienAmbushState.Telegraph && !CanAttack(now)) CancelTelegraph(now);
            double elapsed = (double)now - stateSince;
            if (externallyTimedCombat)
            {
                if (State == AlienAmbushState.Search && elapsed >= SearchDuration)
                    Enter(AlienAmbushState.Perch, now);
                return;
            }
            switch (State)
            {
                case AlienAmbushState.Telegraph:
                    if (elapsed >= TelegraphDuration && CanAttack(now) && !attackPending)
                    {
                        // Never wrap and reuse an identity, even after an unrealistic lifetime.
                        if (LastAttackId == ulong.MaxValue) { Kill(); return; }
                        LastAttackId++;
                        pendingIntent = new AlienAttackIntent(LastAttackId, committedPosition);
                        attackPending = true;
                        Enter(AlienAmbushState.Strike, now);
                    }
                    break;
                case AlienAmbushState.Strike:
                    if (elapsed >= StrikeDuration) Enter(AlienAmbushState.Recover, now);
                    break;
                case AlienAmbushState.Recover:
                    if (elapsed >= RecoveryDuration)
                        Enter(HasObservation ? AlienAmbushState.Stalk : AlienAmbushState.Search, now);
                    break;
                case AlienAmbushState.Search:
                    if (elapsed >= SearchDuration) Enter(AlienAmbushState.Perch, now);
                    break;
            }
        }

        // One-slot intent mailbox. A consumer must take it before another attack can begin.
        public bool TryConsumeAttack(out AlienAttackIntent intent)
        {
            intent = default;
            if (externallyTimedCombat || State == AlienAmbushState.Dead || !attackPending) return false;
            intent = pendingIntent;
            attackPending = false;
            pendingIntent = default;
            return true;
        }

        /// <summary>
        /// One-slot request mailbox for an external combat adapter. The copied aim is locked
        /// at tactical entry, and consumption never issues a diagnostic strike or attack ID.
        /// Begin the contract once, then apply its current phase even if beginning fails.
        /// </summary>
        public bool TryConsumeWindup(out Vector3 aim)
        {
            aim = Vector3.zero;
            if (!externallyTimedCombat || State != AlienAmbushState.Telegraph || !windupPending) return false;
            aim = committedPosition;
            windupPending = false;
            return true;
        }

        /// <summary>
        /// Mirrors the current authoritative contract; it never advances that contract.
        /// Forward phase skips are allowed for hitches; backwards phases cannot restart an
        /// attack. Idle acknowledges a consumed/canceled request and frees the tactical latch,
        /// but cannot erase an unread request. Cancellation leaves Telegraph immediately so
        /// the adapter can cancel its Windup before resolving hits. A stale Windup/Strike
        /// cannot resurrect that canceled state. Call with the same frame time as sensing.
        /// </summary>
        public void ApplyCombatPhase(AlienCombatPhase phase, float now)
        {
            if (!externallyTimedCombat || State == AlienAmbushState.Dead || !ValidTime(now) ||
                (int)phase < (int)AlienCombatPhase.Idle || (int)phase > (int)AlienCombatPhase.Dead) return;
            lastTime = now;
            if (phase == AlienCombatPhase.Dead) { Kill(); return; }
            if (!externalCombatCycle || windupPending) return;

            if (phase == AlienCombatPhase.Idle)
            {
                externalCombatCycle = false;
                externalCombatPhase = AlienCombatPhase.Idle;
                committedPosition = Vector3.zero;
                if (State == AlienAmbushState.Telegraph || State == AlienAmbushState.Strike ||
                    State == AlienAmbushState.Recover)
                    Enter(HasObservation ? AlienAmbushState.Stalk : AlienAmbushState.Search, now);
                return;
            }

            if ((int)phase < (int)externalCombatPhase) return;
            switch (phase)
            {
                case AlienCombatPhase.Windup:
                    // The tactical Telegraph already exists; never recreate a canceled one.
                    return;
                case AlienCombatPhase.Strike:
                    if (State != AlienAmbushState.Telegraph && State != AlienAmbushState.Strike) return;
                    Enter(AlienAmbushState.Strike, now);
                    break;
                case AlienCombatPhase.Recovery:
                    // A cancellation at/after strike opening still owes contract recovery.
                    Enter(AlienAmbushState.Recover, now);
                    break;
            }
            externalCombatPhase = phase;
        }

        public void NotifyRouteUnavailable(float now)
        {
            if (State == AlienAmbushState.Dead || !ValidTime(now)) return;
            // Failure is cancellation, not an opportunity to advance a due attack first.
            lastTime = now;
            Forget();
            attackPending = false;
            pendingIntent = default;
            windupPending = false;
            committedPosition = Vector3.zero;
            if (State != AlienAmbushState.Search) Enter(AlienAmbushState.Search, now);
        }

        public void Kill()
        {
            Forget();
            attackPending = false;
            pendingIntent = default;
            windupPending = false;
            externalCombatCycle = false;
            externalCombatPhase = AlienCombatPhase.Dead;
            committedPosition = Vector3.zero;
            State = AlienAmbushState.Dead;
        }

        private void Enter(AlienAmbushState state, float now)
        {
            if (State == state) return;
            State = state;
            stateSince = now;
        }

        private bool CanAttack(float now) => HasObservation && attackEligible &&
            (double)now - attackEligibilityTime <= AttackEligibilityLifetime;

        private void CancelTelegraph(float now)
        {
            windupPending = false;
            committedPosition = Vector3.zero;
            Enter(HasObservation ? AlienAmbushState.Approach : AlienAmbushState.Search, now);
        }

        private void Forget()
        {
            attackEligible = false;
            attackEligibilityTime = float.NegativeInfinity;
            HasObservation = false;
            LastObservedPosition = Vector3.zero;
            LastObservedTime = float.NegativeInfinity;
        }

        private bool ValidTime(float now) => Finite(now) && now >= 0f && now >= lastTime;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
