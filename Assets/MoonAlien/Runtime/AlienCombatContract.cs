using System;

namespace Unity.MP_FPS.MoonAlien
{
    /// <summary>
    /// Identity of one actor life. The adapter owns unique nonzero actor IDs and persistent,
    /// strictly advancing generations. Reconstructing an old life is not a supported reset.
    /// These values are identifiers, not credentials or a network security boundary.
    /// </summary>
    public readonly struct AlienActorLife : IEquatable<AlienActorLife>
    {
        public readonly ulong ActorId;
        public readonly ulong Generation;
        public AlienActorLife(ulong actorId, ulong generation)
        { ActorId = actorId; Generation = generation; }
        public bool IsValid => ActorId != 0 && Generation != 0;
        public bool Equals(AlienActorLife other) => ActorId == other.ActorId && Generation == other.Generation;
        public override bool Equals(object obj) => obj is AlienActorLife other && Equals(other);
        public override int GetHashCode() => ActorId.GetHashCode() ^ Generation.GetHashCode();
    }

    public readonly struct AlienAttackToken : IEquatable<AlienAttackToken>
    {
        public readonly AlienActorLife Attacker;
        public readonly ulong Sequence;
        public AlienAttackToken(AlienActorLife attacker, ulong sequence)
        { Attacker = attacker; Sequence = sequence; }
        public bool IsValid => Attacker.IsValid && Sequence != 0;
        public bool Equals(AlienAttackToken other) => Attacker.Equals(other.Attacker) && Sequence == other.Sequence;
        public override bool Equals(object obj) => obj is AlienAttackToken other && Equals(other);
        public override int GetHashCode() => Attacker.GetHashCode() ^ Sequence.GetHashCode();
    }

    /// <summary>A reference to one committed attack. It cannot supply or override damage.</summary>
    public readonly struct AlienAttackReceipt : IEquatable<AlienAttackReceipt>
    {
        public readonly AlienAttackToken Token;
        public readonly AlienActorLife Target;
        public AlienAttackReceipt(AlienAttackToken token, AlienActorLife target)
        { Token = token; Target = target; }
        public bool IsValid => Token.IsValid && Target.IsValid;
        public bool Equals(AlienAttackReceipt other) => Token.Equals(other.Token) && Target.Equals(other.Target);
        public override bool Equals(object obj) => obj is AlienAttackReceipt other && Equals(other);
        public override int GetHashCode() => Token.GetHashCode() ^ Target.GetHashCode();
    }

    public readonly struct AlienAttackTiming
    {
        public readonly long WindupTicks;
        public readonly long StrikeTicks;
        public readonly long RecoveryTicks;
        public AlienAttackTiming(long windupTicks, long strikeTicks, long recoveryTicks)
        { WindupTicks = windupTicks; StrikeTicks = strikeTicks; RecoveryTicks = recoveryTicks; }
    }

    /// <summary>
    /// Trusted adapter facts for exactly Tick, all false by default. The future server adapter
    /// must derive authority, liveness and geometry from its own world and committed attack aim.
    /// Supplying true booleans is not authentication, server verification or anti-cheat.
    /// Resolve known same-tick fatal/death state BEFORE validating hits. A later death does not
    /// roll back an already committed hit. Geometry is required at strike, not at windup start.
    /// </summary>
    public readonly struct AlienHitEvidence
    {
        public readonly long Tick;
        public readonly bool IsAuthoritative;
        public readonly bool AttackerAlive;
        public readonly bool TargetAlive;
        public readonly bool HasLineOfSight;
        public readonly bool InRange;
        public readonly bool SweptVolumeHit;
        public AlienHitEvidence(long tick, bool isAuthoritative, bool attackerAlive, bool targetAlive,
            bool hasLineOfSight, bool inRange, bool sweptVolumeHit)
        {
            Tick = tick; IsAuthoritative = isAuthoritative; AttackerAlive = attackerAlive;
            TargetAlive = targetAlive; HasLineOfSight = hasLineOfSight; InRange = inRange;
            SweptVolumeHit = sweptVolumeHit;
        }

        internal AlienHitEvidence WithActualTargetAlive(bool actualAlive) => new AlienHitEvidence(
            Tick, IsAuthoritative, AttackerAlive, TargetAlive && actualAlive,
            HasLineOfSight, InRange, SweptVolumeHit);
    }

    public enum AlienCombatPhase { Idle, Windup, Strike, Recovery, Dead }

    /// <summary>
    /// O(1) memory: exactly one active attack and one target, with no receipt history/seen set.
    /// Windows are [start, strikeOpen), [strikeOpen, strikeClose), [strikeClose, recoveryClose).
    /// Advancing past a window never catches up a missed hit. Equal ticks are allowed; negative
    /// or regressing ticks are rejected. Valid newer ticks advance time even if the action fails.
    /// Locks serialize state; consumption is the hit commit point. No external callback runs
    /// while locked. Keep one contract per actor; a new object is not a respawn/replay barrier.
    /// </summary>
    public sealed class AlienCombatContract
    {
        public const int MaxActiveAttacks = 1;
        public const int MaxCommittedTargets = 1;
        private readonly object gate = new object();
        private AlienActorLife life;
        private ulong lastSequence;
        private long lastTick;
        private bool alive = true;
        private bool active;
        private bool consumed;
        private bool cancelled;
        private AlienAttackReceipt current;
        private double damage;
        private long strikeOpen;
        private long strikeClose;
        private long recoveryClose;

        // lastIssuedSequence is a trusted persisted high-water mark, not a requested attack ID.
        public AlienCombatContract(AlienActorLife life, long initialTick = 0, ulong lastIssuedSequence = 0)
        {
            if (!life.IsValid) throw new ArgumentException("A nonzero actor ID and generation are required.", nameof(life));
            if (initialTick < 0) throw new ArgumentOutOfRangeException(nameof(initialTick));
            this.life = life; lastTick = initialTick; lastSequence = lastIssuedSequence;
        }

        public AlienActorLife Life { get { lock (gate) return life; } }
        public ulong LastIssuedSequence { get { lock (gate) return lastSequence; } }
        public long LastTick { get { lock (gate) return lastTick; } }
        public bool IsAlive { get { lock (gate) return alive; } }
        public bool HasActiveAttack { get { lock (gate) return active; } }
        public AlienCombatPhase Phase
        {
            get
            {
                lock (gate)
                {
                    if (!alive) return AlienCombatPhase.Dead;
                    if (!active) return AlienCombatPhase.Idle;
                    if (cancelled) return AlienCombatPhase.Recovery;
                    if (lastTick < strikeOpen) return AlienCombatPhase.Windup;
                    return lastTick < strikeClose ? AlienCombatPhase.Strike : AlienCombatPhase.Recovery;
                }
            }
        }

        public bool AdvanceTo(long tick) { lock (gate) return ObserveTick(tick); }

        public bool TryBeginAttack(AlienActorLife target, long tick, AlienAttackTiming timing,
            double committedDamage, AlienHitEvidence evidence, out AlienAttackReceipt receipt)
        {
            lock (gate)
            {
                receipt = default;
                if (!ObserveTick(tick) || !alive || active || !target.IsValid ||
                    target.ActorId == life.ActorId || !FinitePositive(committedDamage) ||
                    !EligibleAt(tick, evidence) || lastSequence == ulong.MaxValue ||
                    !TryEnd(tick, timing.WindupTicks, out long open) ||
                    !TryEnd(open, timing.StrikeTicks, out long close) ||
                    !TryEnd(close, timing.RecoveryTicks, out long end)) return false;
                lastSequence++; // The guard above makes wrapping impossible, including after respawn.
                current = new AlienAttackReceipt(new AlienAttackToken(life, lastSequence), target);
                damage = committedDamage; strikeOpen = open; strikeClose = close; recoveryClose = end;
                consumed = false; cancelled = false; active = true; receipt = current;
                return true;
            }
        }

        /// <summary>Windup cancellation frees the slot. From strike onward, cancellation preserves
        /// the original recovery deadline, even after a consumed hit; repeated cancellation rejects.</summary>
        public bool CancelAttack(AlienAttackReceipt receipt, long tick)
        {
            lock (gate)
            {
                if (!ObserveTick(tick) || !active || cancelled || !current.Equals(receipt)) return false;
                if (tick < strikeOpen) ClearAttack();
                else cancelled = true; // Disable hit now without allowing a cooldown bypass.
                return true;
            }
        }

        /// <summary>Returns true only for the first explicit death in this life.</summary>
        public bool MarkDead(long tick)
        {
            lock (gate)
            {
                if (!ObserveTick(tick) || !alive) return false;
                alive = false; ClearAttack();
                return true;
            }
        }

        public bool ResetLife(AlienActorLife nextLife, long tick)
        {
            lock (gate)
            {
                if (!ObserveTick(tick) || !nextLife.IsValid || nextLife.ActorId != life.ActorId ||
                    nextLife.Generation <= life.Generation) return false;
                life = nextLife; alive = true; ClearAttack(); // Do not reset tick or sequence high-water marks.
                return true;
            }
        }

        // Only the guarded fake sink exposes damage reception. Damage always comes from this state.
        internal bool TryConsumeHit(AlienAttackReceipt receipt, long tick, AlienHitEvidence evidence,
            out double committedDamage)
        {
            lock (gate)
            {
                committedDamage = 0d;
                if (!ObserveTick(tick) || !alive || !active || consumed || cancelled || !receipt.IsValid ||
                    !current.Equals(receipt) || tick < strikeOpen || tick >= strikeClose ||
                    !EligibleAt(tick, evidence) || !evidence.HasLineOfSight ||
                    !evidence.InRange || !evidence.SweptVolumeHit) return false;
                consumed = true;
                committedDamage = damage;
                return true;
            }
        }

        internal static bool FinitePositive(double value) => value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool EligibleAt(long tick, AlienHitEvidence evidence) => evidence.Tick == tick &&
            evidence.IsAuthoritative && evidence.AttackerAlive && evidence.TargetAlive;
        private static bool TryEnd(long start, long duration, out long end)
        {
            end = 0;
            if (duration <= 0 || start > long.MaxValue - duration) return false;
            end = start + duration;
            return true;
        }
        private bool ObserveTick(long tick)
        {
            if (tick < 0 || tick < lastTick) return false;
            lastTick = tick;
            if (active && tick >= recoveryClose) ClearAttack();
            return true;
        }
        private void ClearAttack()
        {
            active = false; consumed = false; cancelled = false; current = default; damage = 0d;
            strikeOpen = strikeClose = recoveryClose = 0;
        }
    }

    public readonly struct AlienFakeDamageEvent
    {
        public readonly AlienAttackReceipt Receipt;
        public readonly long Tick;
        public readonly double CommittedDamage;
        public readonly double AppliedDamage;
        public readonly bool KilledTarget;
        internal AlienFakeDamageEvent(AlienAttackReceipt receipt, long tick, double damage,
            double appliedDamage, bool killedTarget)
        { Receipt = receipt; Tick = tick; CommittedDamage = damage; AppliedDamage = appliedDamage; KilledTarget = killedTarget; }
    }

    /// <summary>
    /// Isolated diagnostic health; never resolves or touches real player data. There is no raw
    /// ApplyDamage method. The sink lock covers target validation, one-time contract consumption
    /// and health mutation. Current sink liveness is required in addition to trusted evidence.
    /// Death is terminal until explicit newer-generation reset. Known same-tick deaths must be
    /// marked before receiving strikes; a committed hit is not retrospectively undone.
    /// </summary>
    public sealed class AlienFakeHealthSink
    {
        private readonly object gate = new object();
        private AlienActorLife life;
        private double maximumHealth;
        private double health;
        private long lastTick;
        private ulong acceptedHitCount;
        private AlienFakeDamageEvent lastDamageEvent;
        public AlienFakeHealthSink(AlienActorLife life, double maximumHealth, long initialTick = 0)
        {
            if (!life.IsValid) throw new ArgumentException("A nonzero actor ID and generation are required.", nameof(life));
            if (!AlienCombatContract.FinitePositive(maximumHealth)) throw new ArgumentOutOfRangeException(nameof(maximumHealth));
            if (initialTick < 0) throw new ArgumentOutOfRangeException(nameof(initialTick));
            this.life = life; this.maximumHealth = health = maximumHealth; lastTick = initialTick;
        }

        public AlienActorLife Life { get { lock (gate) return life; } }
        public double Health { get { lock (gate) return health; } }
        public double MaximumHealth { get { lock (gate) return maximumHealth; } }
        public bool IsAlive { get { lock (gate) return health > 0d; } }
        public long LastTick { get { lock (gate) return lastTick; } }
        // Diagnostic counter saturates rather than wrapping; reset clears this per-life counter.
        public ulong AcceptedHitCount { get { lock (gate) return acceptedHitCount; } }
        public AlienFakeDamageEvent LastDamageEvent { get { lock (gate) return lastDamageEvent; } }

        public bool TryReceive(AlienCombatContract source, AlienAttackReceipt receipt, long tick,
            AlienHitEvidence evidence, out AlienFakeDamageEvent accepted)
        {
            lock (gate)
            {
                accepted = default;
                if (!ObserveTick(tick) || source == null || !receipt.IsValid ||
                    !life.Equals(receipt.Target) || health <= 0d) return false;
                if (!source.TryConsumeHit(receipt, tick, evidence.WithActualTargetAlive(health > 0d),
                    out double committedDamage)) return false;
                double previousHealth = health;
                health = Math.Max(0d, health - committedDamage);
                accepted = new AlienFakeDamageEvent(receipt, tick, committedDamage,
                    previousHealth - health, health == 0d);
                lastDamageEvent = accepted;
                if (acceptedHitCount < ulong.MaxValue) acceptedHitCount++;
                return true;
            }
        }

        public bool MarkDead(long tick)
        {
            lock (gate)
            {
                if (!ObserveTick(tick) || health <= 0d) return false;
                health = 0d;
                return true;
            }
        }

        public bool ResetLife(AlienActorLife nextLife, double nextMaximumHealth, long tick)
        {
            lock (gate)
            {
                if (!ObserveTick(tick) || !nextLife.IsValid || nextLife.ActorId != life.ActorId ||
                    nextLife.Generation <= life.Generation || !AlienCombatContract.FinitePositive(nextMaximumHealth)) return false;
                life = nextLife; maximumHealth = health = nextMaximumHealth;
                acceptedHitCount = 0; lastDamageEvent = default;
                return true;
            }
        }
        private bool ObserveTick(long tick)
        {
            if (tick < 0 || tick < lastTick) return false;
            lastTick = tick;
            return true;
        }
    }
}
