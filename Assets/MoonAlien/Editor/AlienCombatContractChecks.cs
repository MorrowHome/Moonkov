using System;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

namespace Unity.MP_FPS.MoonAlien.Editor
{
    /// <summary>The same actual C# checks run from the Unity menu or the standalone console runner.</summary>
    public static class AlienCombatContractChecks
    {
#if UNITY_EDITOR
        [MenuItem("Moonkov/Alien/Run Combat Contract Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run outside Play.");
            int groups = RunAll();
            Debug.Log(groups + " combat contract C# check groups passed. Pure fake-health checks only; " +
                "this does not validate Unity physics, real player damage, server authority or networking.");
        }
#endif

        public static int RunAll()
        {
            ValidStrikeAndDuplicate();
            WindupBoundary();
            LastStrikeTick();
            StrikeEndExcluded();
            RecoveryEndExcluded();
            SkippedStrikeNeverCatchesUp();
            DefaultEvidenceRejected();
            EveryEvidenceGateRequired();
            EvidenceMustMatchExactTick();
            OneTargetAndOneActiveAttack();
            TargetIdentityAndGeneration();
            InvalidAndForeignTokens();
            CancelAtStrikeTick();
            CancelAfterConsumptionKeepsCooldown();
            RecoveryCancelKeepsCooldown();
            OldCancelCannotCancelNewAttack();
            AttackerDeathAtStrikeTick();
            TargetDeathAtStrikeTick();
            AlreadyCommittedHitIsNotRolledBack();
            AttackerRespawnRejectsOldReceipts();
            TargetRespawnRejectsOldReceipts();
            InvalidLifeResetPreservesState();
            SequenceNeverWraps();
            InvalidDamageRejected();
            InvalidHealthRejected();
            InvalidTimingRejected();
            TickOverflowAndMaximumBoundary();
            RegressingAndNegativeTicksRejected();
            ReorderedCallbacksCannotReopenStrike();
            FatalDamageClampsAndDeathIsTerminal();
            CommittedDamageBelongsToContract();
            SubUlpDamageConsumedOnce();
            MultipleAttackersStayIndependent();
            RepeatedAttacksUseBoundedState();
            InvalidConstructionAndNullSource();
            return 35;
        }

        private static AlienActorLife Attacker => new AlienActorLife(10, 1);
        private static AlienActorLife Target => new AlienActorLife(20, 1);
        private static AlienAttackTiming Timing => new AlienAttackTiming(3, 2, 4);
        private static AlienHitEvidence Good(long tick) => new AlienHitEvidence(tick, true, true, true, true, true, true);
        private static AlienAttackReceipt Begin(AlienCombatContract source, AlienActorLife target, long tick = 0,
            double damage = 25d)
        {
            Require(source.TryBeginAttack(target, tick, Timing, damage, Good(tick), out AlienAttackReceipt receipt), "Begin valid attack");
            return receipt;
        }
        private static AlienAttackReceipt Setup(out AlienCombatContract source, out AlienFakeHealthSink sink,
            double health = 100d, double damage = 25d)
        {
            source = new AlienCombatContract(Attacker);
            sink = new AlienFakeHealthSink(Target, health);
            return Begin(source, Target, 0, damage);
        }
        private static bool Hit(AlienCombatContract source, AlienFakeHealthSink sink, AlienAttackReceipt receipt, long tick) =>
            sink.TryReceive(source, receipt, tick, Good(tick), out _);

        private static void ValidStrikeAndDuplicate()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(sink.TryReceive(source, receipt, 3, Good(3), out AlienFakeDamageEvent hit), "Strike opens inclusively");
            Require(sink.Health == 75d && sink.AcceptedHitCount == 1 && hit.Receipt.Equals(receipt) &&
                hit.Tick == 3 && hit.CommittedDamage == 25d && hit.AppliedDamage == 25d && !hit.KilledTarget, "Single exact damage event");
            Require(!Hit(source, sink, receipt, 3) && !Hit(source, sink, receipt, 4), "Duplicate same/later tick rejected");
            Require(sink.Health == 75d && sink.AcceptedHitCount == 1 && sink.LastDamageEvent.Receipt.Equals(receipt), "Duplicate cannot mutate health or log");
        }
        private static void WindupBoundary()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(source.Phase == AlienCombatPhase.Windup, "Start tick belongs to windup");
            Require(!Hit(source, sink, receipt, 0) && !Hit(source, sink, receipt, 2), "Start and last windup ticks reject");
            Require(source.Phase == AlienCombatPhase.Windup && Hit(source, sink, receipt, 3), "Early attempts do not consume strike");
            Require(source.Phase == AlienCombatPhase.Strike, "Exact windup end belongs to strike");
        }
        private static void LastStrikeTick()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(Hit(source, sink, receipt, 4), "Last included strike tick accepts");
        }
        private static void StrikeEndExcluded()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(!Hit(source, sink, receipt, 5) && source.Phase == AlienCombatPhase.Recovery, "Strike upper endpoint excluded");
            Require(!Hit(source, sink, receipt, 8) && sink.Health == 100d, "Recovery never applies missed damage");
        }
        private static void RecoveryEndExcluded()
        {
            AlienAttackReceipt old = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(source.AdvanceTo(8) && source.Phase == AlienCombatPhase.Recovery, "Last recovery tick");
            Require(!source.TryBeginAttack(Target, 8, Timing, 25d, Good(8), out _), "Recovery occupies the one attack slot");
            Require(source.AdvanceTo(9) && source.Phase == AlienCombatPhase.Idle && !source.HasActiveAttack, "Recovery upper endpoint is idle");
            AlienAttackReceipt next = Begin(source, Target, 9);
            Require(next.Token.Sequence == old.Token.Sequence + 1 && !Hit(source, sink, old, 12) && Hit(source, sink, next, 12), "Old receipt cannot hit next strike");
        }
        private static void SkippedStrikeNeverCatchesUp()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(!Hit(source, sink, receipt, 1000000) && source.Phase == AlienCombatPhase.Idle, "Huge jump expires every window");
            Require(!Hit(source, sink, receipt, 3) && sink.Health == 100d, "Time reversal cannot recover skipped strike");
        }
        private static void DefaultEvidenceRejected()
        {
            var source = new AlienCombatContract(Attacker);
            Require(!source.TryBeginAttack(Target, 0, Timing, 25d, default, out _), "Default evidence cannot start");
            AlienAttackReceipt receipt = Begin(source, Target);
            var sink = new AlienFakeHealthSink(Target, 100d);
            Require(!sink.TryReceive(source, receipt, 3, default, out _), "Default evidence cannot hit");
            Require(Hit(source, sink, receipt, 3), "Missing evidence did not consume receipt");
        }
        private static void EveryEvidenceGateRequired()
        {
            for (int gate = 0; gate < 6; gate++)
            {
                AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
                var denied = new AlienHitEvidence(3, gate != 0, gate != 1, gate != 2, gate != 3, gate != 4, gate != 5);
                Require(!sink.TryReceive(source, receipt, 3, denied, out _) && sink.Health == 100d, "Each authority/alive/geometry fact is required: " + gate);
                Require(Hit(source, sink, receipt, 3), "Rejected evidence never consumes damage: " + gate);
            }
            var prepare = new AlienCombatContract(Attacker);
            var aliveOnly = new AlienHitEvidence(0, true, true, true, false, false, false);
            Require(prepare.TryBeginAttack(Target, 0, Timing, 25d, aliveOnly, out _), "Windup does not claim physical contact");
            for (int gate = 0; gate < 3; gate++)
            {
                var denied = new AlienHitEvidence(0, gate != 0, gate != 1, gate != 2, true, true, true);
                var fresh = new AlienCombatContract(Attacker);
                Require(!fresh.TryBeginAttack(Target, 0, Timing, 25d, denied, out _), "Start requires authority and both lives: " + gate);
            }
        }
        private static void EvidenceMustMatchExactTick()
        {
            var source = new AlienCombatContract(Attacker);
            Require(!source.TryBeginAttack(Target, 1, Timing, 25d, Good(0), out _), "Stale start evidence");
            Require(!source.TryBeginAttack(Target, 1, Timing, 25d, Good(2), out _), "Future start evidence");
            AlienAttackReceipt receipt = Begin(source, Target, 1);
            var sink = new AlienFakeHealthSink(Target, 100d);
            Require(!sink.TryReceive(source, receipt, 4, Good(3), out _) &&
                !sink.TryReceive(source, receipt, 4, Good(5), out _), "Past/future geometry evidence rejected");
            Require(Hit(source, sink, receipt, 4), "Fresh evidence at same validation tick accepts");
        }
        private static void OneTargetAndOneActiveAttack()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(AlienCombatContract.MaxCommittedTargets == 1 && AlienCombatContract.MaxActiveAttacks == 1, "Explicit single-target capacity");
            var other = new AlienActorLife(30, 1);
            Require(!source.TryBeginAttack(other, 0, Timing, 25d, Good(0), out _), "Windup cannot replace committed target");
            Require(Hit(source, sink, receipt, 3), "Original target preserved");
            Require(!source.TryBeginAttack(other, 3, Timing, 25d, Good(3), out _) &&
                !source.TryBeginAttack(other, 5, Timing, 25d, Good(5), out _), "Consumed strike/recovery still occupy slot");
            Require(source.LastIssuedSequence == 1, "Capacity rejects do not spend sequence numbers");
            Require(source.TryBeginAttack(other, 9, Timing, 25d, Good(9), out _), "Slot released at recovery end");
        }
        private static void TargetIdentityAndGeneration()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            var wrongId = new AlienFakeHealthSink(new AlienActorLife(21, 1), 100d);
            var wrongGeneration = new AlienFakeHealthSink(new AlienActorLife(20, 2), 100d);
            Require(!Hit(source, wrongId, receipt, 3) && !Hit(source, wrongGeneration, receipt, 3), "Sink verifies current target ID and generation");
            var switched = new AlienAttackReceipt(receipt.Token, wrongId.Life);
            Require(!Hit(source, wrongId, switched, 3), "Receipt cannot retarget committed attack");
            Require(Hit(source, sink, receipt, 3), "Wrong target attempts do not consume original");
            var fresh = new AlienCombatContract(Attacker);
            Require(!fresh.TryBeginAttack(default, 0, Timing, 25d, Good(0), out _) &&
                !fresh.TryBeginAttack(new AlienActorLife(20, 0), 0, Timing, 25d, Good(0), out _) &&
                !fresh.TryBeginAttack(Attacker, 0, Timing, 25d, Good(0), out _) &&
                !fresh.TryBeginAttack(new AlienActorLife(10, 2), 0, Timing, 25d, Good(0), out _), "Invalid and self actor targets rejected");
        }
        private static void InvalidAndForeignTokens()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            var foreignActor = new AlienAttackReceipt(new AlienAttackToken(new AlienActorLife(11, 1), 1), Target);
            var foreignLife = new AlienAttackReceipt(new AlienAttackToken(new AlienActorLife(10, 2), 1), Target);
            var futureId = new AlienAttackReceipt(new AlienAttackToken(Attacker, 2), Target);
            var zero = new AlienAttackReceipt(new AlienAttackToken(Attacker, 0), Target);
            Require(!Hit(source, sink, default, 3) && !Hit(source, sink, foreignActor, 3) &&
                !Hit(source, sink, foreignLife, 3) && !Hit(source, sink, futureId, 3) && !Hit(source, sink, zero, 3), "Invalid/foreign/old-life/future tokens rejected");
            Require(Hit(source, sink, receipt, 3), "Only exact committed tuple accepted");
        }
        private static void CancelAtStrikeTick()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(source.CancelAttack(receipt, 3) && !Hit(source, sink, receipt, 3), "Cancel first at strike tick wins");
            Require(!source.CancelAttack(receipt, 3) && source.HasActiveAttack && source.Phase == AlienCombatPhase.Recovery &&
                sink.Health == 100d, "Cancellation is one-time and holds cooldown slot");
            Require(!source.TryBeginAttack(Target, 3, Timing, 25d, Good(3), out _) &&
                !source.TryBeginAttack(Target, 8, Timing, 25d, Good(8), out _), "Strike cancellation cannot bypass cooldown");
            Require(source.TryBeginAttack(Target, 9, Timing, 25d, Good(9), out _), "Original recovery endpoint frees cancelled slot");
        }
        private static void CancelAfterConsumptionKeepsCooldown()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(Hit(source, sink, receipt, 3) && source.CancelAttack(receipt, 3), "Consumed hit can be cancelled once");
            Require(!source.CancelAttack(receipt, 3) && !Hit(source, sink, receipt, 3) &&
                source.Phase == AlienCombatPhase.Recovery && source.HasActiveAttack, "Consumed cancellation holds recovery and rejects repeat");
            Require(!source.TryBeginAttack(Target, 3, Timing, 25d, Good(3), out _) &&
                !source.TryBeginAttack(Target, 8, Timing, 25d, Good(8), out _), "Post-hit cancellation cannot accelerate attack cadence");
            Require(source.TryBeginAttack(Target, 9, Timing, 25d, Good(9), out _) &&
                sink.Health == 75d && sink.AcceptedHitCount == 1, "Original cooldown expires without undoing committed damage");
        }
        private static void RecoveryCancelKeepsCooldown()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(source.CancelAttack(receipt, 5) && !source.CancelAttack(receipt, 5), "Recovery cancellation is one-time");
            Require(source.Phase == AlienCombatPhase.Recovery && source.HasActiveAttack &&
                !Hit(source, sink, receipt, 5), "Recovery cancellation never opens a hit");
            Require(!source.TryBeginAttack(Target, 5, Timing, 25d, Good(5), out _) &&
                !source.TryBeginAttack(Target, 8, Timing, 25d, Good(8), out _), "Recovery cancellation preserves remaining deadline");
            Require(source.TryBeginAttack(Target, 9, Timing, 25d, Good(9), out _) && sink.Health == 100d, "Cancelled recovery releases at original deadline");
        }
        private static void OldCancelCannotCancelNewAttack()
        {
            AlienAttackReceipt old = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(source.CancelAttack(old, 0), "Cancel first attack");
            AlienAttackReceipt next = Begin(source, Target);
            Require(next.Token.Sequence == 2 && !source.CancelAttack(old, 0), "Old cancel cannot clear newer active attack");
            Require(!Hit(source, sink, old, 3) && Hit(source, sink, next, 3), "Old receipt rejected during newer strike");
        }
        private static void AttackerDeathAtStrikeTick()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(source.MarkDead(3) && !source.MarkDead(3), "Resolve attacker death once before strike validation");
            Require(!Hit(source, sink, receipt, 3) && source.Phase == AlienCombatPhase.Dead && !source.HasActiveAttack, "Known attacker death beats same-tick strike");
            Require(!source.TryBeginAttack(Target, 20, Timing, 25d, Good(20), out _) && sink.Health == 100d, "Evidence true cannot revive dead attacker");
        }
        private static void TargetDeathAtStrikeTick()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(sink.MarkDead(3) && !sink.MarkDead(3), "Resolve target death once before strike validation");
            Require(!Hit(source, sink, receipt, 3) && sink.Health == 0d && sink.AcceptedHitCount == 0, "Actual sink death rejects caller's optimistic alive evidence");
            Require(!Hit(source, sink, receipt, 4), "Target death remains terminal");
        }
        private static void AlreadyCommittedHitIsNotRolledBack()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(Hit(source, sink, receipt, 3) && source.MarkDead(3), "Commit precedes subsequently reported attacker death");
            Require(sink.Health == 75d && sink.AcceptedHitCount == 1 && !Hit(source, sink, receipt, 3), "Later death does not imply retroactive damage rollback");
        }
        private static void AttackerRespawnRejectsOldReceipts()
        {
            AlienAttackReceipt old = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(source.MarkDead(1) && source.ResetLife(new AlienActorLife(10, 2), 1), "Explicit attacker respawn");
            AlienAttackReceipt next = Begin(source, Target, 1);
            Require(next.Token.Attacker.Generation == 2 && next.Token.Sequence == 2 && source.LastTick == 1, "New life retains tick and sequence high-water marks");
            Require(!Hit(source, sink, old, 4) && !source.CancelAttack(old, 4) && Hit(source, sink, next, 4), "Old life cannot hit or cancel new life");
        }
        private static void TargetRespawnRejectsOldReceipts()
        {
            AlienAttackReceipt old = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(sink.MarkDead(1) && sink.ResetLife(new AlienActorLife(20, 2), 80d, 1), "Explicit target respawn");
            Require(!Hit(source, sink, old, 3) && sink.Health == 80d && sink.AcceptedHitCount == 0, "Old target generation receipt rejected");
            Require(source.CancelAttack(old, 3), "Cancel old-life commitment while preserving cooldown");
            Require(!source.TryBeginAttack(sink.Life, 8, Timing, 25d, Good(8), out _), "Target respawn cannot bypass attacker cooldown");
            AlienAttackReceipt next = Begin(source, sink.Life, 9);
            Require(Hit(source, sink, next, 12) && sink.Health == 55d, "New generation can receive newly committed attack");
            Require(sink.ResetLife(new AlienActorLife(20, 3), 90d, 12) && sink.AcceptedHitCount == 0 &&
                !sink.LastDamageEvent.Receipt.IsValid && !Hit(source, sink, next, 12), "Explicit reset invalidates prior receipt and clears diagnostics");
        }
        private static void InvalidLifeResetPreservesState()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            AlienActorLife[] invalid = { default, new AlienActorLife(10, 0), new AlienActorLife(11, 2), Attacker };
            foreach (AlienActorLife next in invalid) Require(!source.ResetLife(next, 0), "Reject invalid/changed ID/nonadvancing attacker generation");
            Require(source.Life.Equals(Attacker) && source.HasActiveAttack && Hit(source, sink, receipt, 3), "Rejected reset preserves attack");
            Require(!sink.ResetLife(Target, 100d, 3) && !sink.ResetLife(new AlienActorLife(21, 2), 100d, 3) &&
                !sink.ResetLife(new AlienActorLife(20, 2), double.NaN, 3) && !sink.ResetLife(default, 100d, 3), "Reject invalid target reset");
            Require(sink.Life.Equals(Target) && sink.Health == 75d, "Rejected target reset preserves health/life");
            var terminal = new AlienCombatContract(new AlienActorLife(10, ulong.MaxValue));
            Require(!terminal.ResetLife(new AlienActorLife(10, 0), 0) &&
                !terminal.ResetLife(new AlienActorLife(10, ulong.MaxValue), 0), "Generation must never wrap or repeat");
        }
        private static void SequenceNeverWraps()
        {
            var source = new AlienCombatContract(Attacker, 0, ulong.MaxValue - 1);
            AlienAttackReceipt last = Begin(source, Target);
            Require(last.Token.Sequence == ulong.MaxValue && source.CancelAttack(last, 0), "Maximum sequence issued once");
            Require(!source.TryBeginAttack(Target, 0, Timing, 25d, Good(0), out _), "Exhausted sequence refuses rather than wraps");
            Require(source.ResetLife(new AlienActorLife(10, 2), 0) &&
                !source.TryBeginAttack(Target, 0, Timing, 25d, Good(0), out _) && source.LastIssuedSequence == ulong.MaxValue, "Respawn cannot reset exhausted sequence");
            var exhausted = new AlienCombatContract(Attacker, 0, ulong.MaxValue);
            Require(!exhausted.TryBeginAttack(Target, 0, Timing, 25d, Good(0), out _), "Persisted exhausted high-water mark rejects");
        }
        private static void InvalidDamageRejected()
        {
            var source = new AlienCombatContract(Attacker);
            double[] invalid = { 0d, -0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity };
            foreach (double damage in invalid)
                Require(!source.TryBeginAttack(Target, 0, Timing, damage, Good(0), out _) &&
                    source.LastIssuedSequence == 0 && !source.HasActiveAttack, "Invalid damage rejected before issuance");
            Require(source.TryBeginAttack(Target, 0, Timing, double.Epsilon, Good(0), out _), "Finite strictly positive damage accepted");
        }
        private static void InvalidHealthRejected()
        {
            double[] invalid = { 0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity };
            foreach (double value in invalid) Throws<ArgumentOutOfRangeException>(() => new AlienFakeHealthSink(Target, value), "Invalid health constructor");
            var sink = new AlienFakeHealthSink(Target, 100d);
            foreach (double value in invalid)
                Require(!sink.ResetLife(new AlienActorLife(20, 2), value, 0) && sink.Health == 100d && sink.Life.Equals(Target), "Invalid reset health cannot revive/change life");
        }
        private static void InvalidTimingRejected()
        {
            AlienAttackTiming[] invalid = {
                default, new AlienAttackTiming(0, 1, 1), new AlienAttackTiming(1, 0, 1), new AlienAttackTiming(1, 1, 0),
                new AlienAttackTiming(-1, 1, 1), new AlienAttackTiming(1, -1, 1), new AlienAttackTiming(1, 1, -1)
            };
            var source = new AlienCombatContract(Attacker);
            foreach (AlienAttackTiming timing in invalid)
                Require(!source.TryBeginAttack(Target, 0, timing, 25d, Good(0), out _) && source.LastIssuedSequence == 0, "Every phase must have positive tick duration");
        }
        private static void TickOverflowAndMaximumBoundary()
        {
            AlienAttackTiming[] overflow = {
                new AlienAttackTiming(long.MaxValue, 1, 1), new AlienAttackTiming(1, long.MaxValue, 1),
                new AlienAttackTiming(1, 1, long.MaxValue)
            };
            foreach (AlienAttackTiming timing in overflow)
            {
                var rejected = new AlienCombatContract(Attacker);
                Require(!rejected.TryBeginAttack(Target, 1, timing, 25d, Good(1), out _) && rejected.LastIssuedSequence == 0, "Reject overflow at each window endpoint");
            }
            var source = new AlienCombatContract(Attacker);
            var sink = new AlienFakeHealthSink(Target, 100d);
            long start = long.MaxValue - 3;
            Require(source.TryBeginAttack(Target, start, new AlienAttackTiming(1, 1, 1), 25d, Good(start), out AlienAttackReceipt receipt), "Exact long maximum recovery end is representable");
            Require(Hit(source, sink, receipt, start + 1) && source.AdvanceTo(long.MaxValue) &&
                source.Phase == AlienCombatPhase.Idle, "Maximum boundary never wraps tick arithmetic");
            Require(!source.TryBeginAttack(Target, long.MaxValue, Timing, 25d, Good(long.MaxValue), out _), "No future window beyond tick maximum");
        }
        private static void RegressingAndNegativeTicksRejected()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(!source.AdvanceTo(-1) && source.AdvanceTo(2) && !source.AdvanceTo(1), "Negative and regressing contract clock rejected");
            Require(!source.CancelAttack(receipt, 1) && !source.MarkDead(1) &&
                !source.ResetLife(new AlienActorLife(10, 2), 1) && !source.TryBeginAttack(Target, 1, Timing, 25d, Good(1), out _), "Every state mutation respects contract time");
            Require(!Hit(source, sink, receipt, -1) && !Hit(source, sink, receipt, 2), "Negative and early sink receive reject");
            Require(!sink.MarkDead(1) && !sink.ResetLife(new AlienActorLife(20, 2), 100d, 1) &&
                sink.LastTick == 2 && sink.IsAlive && source.LastTick == 2 && source.Life.Equals(Attacker), "Rejected regressions preserve both states");
            Require(Hit(source, sink, receipt, 3), "Clock regression attempts cannot consume valid attack");
        }
        private static void ReorderedCallbacksCannotReopenStrike()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(!Hit(source, sink, receipt, 5) && !Hit(source, sink, receipt, 4), "Later recovery callback rejects reordered strike");
            Require(source.LastTick == 5 && sink.LastTick == 5 && sink.Health == 100d, "No backdated application");
            var secondSink = new AlienFakeHealthSink(Target, 100d);
            Require(!Hit(source, secondSink, receipt, 4), "Contract also rejects regression independently of sink");
        }
        private static void FatalDamageClampsAndDeathIsTerminal()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink, 10d, double.MaxValue);
            Require(sink.TryReceive(source, receipt, 3, Good(3), out AlienFakeDamageEvent death) &&
                death.KilledTarget && death.AppliedDamage == 10d && death.CommittedDamage == double.MaxValue, "Overkill records one bounded fatal event");
            Require(sink.Health == 0d && !sink.IsAlive && !Hit(source, sink, receipt, 3) && !sink.MarkDead(3), "Death clamps zero and emits once");
            AlienAttackReceipt next = Begin(source, Target, 9);
            Require(!Hit(source, sink, next, 12) && sink.AcceptedHitCount == 1 && sink.Health == 0d, "New attacks cannot revive dead health");
            var exact = new AlienFakeHealthSink(Target, 25d);
            var exactSource = new AlienCombatContract(Attacker);
            Require(Hit(exactSource, exact, Begin(exactSource, Target), 3) && exact.Health == 0d &&
                exact.LastDamageEvent.KilledTarget, "Exact lethal amount is fatal");
        }
        private static void CommittedDamageBelongsToContract()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink, 100d, 7d);
            Require(!source.TryBeginAttack(Target, 3, Timing, 90d, Good(3), out _), "Cannot replace committed damage during strike");
            Require(Hit(source, sink, receipt, 3) && sink.Health == 93d && sink.LastDamageEvent.CommittedDamage == 7d, "Sink uses stored contract damage only");
        }
        private static void SubUlpDamageConsumedOnce()
        {
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink,
                double.MaxValue, double.Epsilon);
            Require(sink.TryReceive(source, receipt, 3, Good(3), out AlienFakeDamageEvent accepted), "Finite tiny damage is still one accepted hit");
            Require(sink.Health == double.MaxValue && accepted.CommittedDamage == double.Epsilon &&
                accepted.AppliedDamage == 0d && !accepted.KilledTarget && sink.AcceptedHitCount == 1,
                "Sub-ULP subtraction rounds to unchanged finite health and reports zero actual damage");
            Require(!Hit(source, sink, receipt, 3) && !Hit(source, sink, receipt, 4) &&
                sink.AcceptedHitCount == 1 && sink.Health == double.MaxValue, "Zero rounded damage does not leave receipt reusable");
        }
        private static void MultipleAttackersStayIndependent()
        {
            var a = new AlienCombatContract(Attacker);
            var b = new AlienCombatContract(new AlienActorLife(11, 1));
            var sink = new AlienFakeHealthSink(Target, 100d);
            AlienAttackReceipt first = Begin(a, Target), second = Begin(b, Target);
            Require(first.Token.Sequence == second.Token.Sequence && !first.Token.Equals(second.Token), "Same numeric sequence on distinct actors is distinct");
            Require(!Hit(a, sink, second, 3) && Hit(a, sink, first, 3) && Hit(b, sink, second, 3) &&
                !Hit(b, sink, first, 3) && sink.Health == 50d && sink.AcceptedHitCount == 2, "Two sources each consume only their own attack");
        }
        private static void RepeatedAttacksUseBoundedState()
        {
            var source = new AlienCombatContract(Attacker);
            var sink = new AlienFakeHealthSink(Target, 100000d);
            AlienAttackReceipt first = default;
            for (int i = 0; i < 2048; i++)
            {
                long start = i * 9L;
                AlienAttackReceipt receipt = Begin(source, Target, start, 1d);
                if (i == 0) first = receipt;
                Require(receipt.Token.Sequence == (ulong)i + 1 && Hit(source, sink, receipt, start + 3), "Repeat finite-state attack lifecycle");
                if (i > 0) Require(!Hit(source, sink, first, start + 3), "Ancient receipt remains rejected without seen-set growth");
            }
            Require(source.LastIssuedSequence == 2048 && sink.AcceptedHitCount == 2048 && sink.Health == 97952d, "Long-run exact count and bounded active capacity");
        }
        private static void InvalidConstructionAndNullSource()
        {
            Throws<ArgumentException>(() => new AlienCombatContract(default), "Invalid attacker life");
            Throws<ArgumentException>(() => new AlienCombatContract(new AlienActorLife(0, 1)), "Zero attacker ID");
            Throws<ArgumentException>(() => new AlienCombatContract(new AlienActorLife(1, 0)), "Zero attacker generation");
            Throws<ArgumentOutOfRangeException>(() => new AlienCombatContract(Attacker, -1), "Negative initial contract tick");
            Throws<ArgumentException>(() => new AlienFakeHealthSink(default, 100d), "Invalid sink life");
            Throws<ArgumentOutOfRangeException>(() => new AlienFakeHealthSink(Target, 100d, -1), "Negative initial sink tick");
            AlienAttackReceipt receipt = Setup(out AlienCombatContract source, out AlienFakeHealthSink sink);
            Require(!Hit(null, sink, receipt, 3) && Hit(source, sink, receipt, 3), "Null source rejects without consuming receipt");
        }
        private static void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Combat contract check failed: " + message);
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Combat contract check failed: " + message);
        }
    }
}
