using System;

namespace Unity.MP_FPS.DollSinger
{
    public enum MeleeWeaponKind : byte { None, Fists, Dagger, EnergySword }
    public enum MeleeAttackKind : byte { None, Light, Heavy }
    public enum MeleePhase : byte { Idle, Windup, Active, Recovery }
    public enum MeleeStartError : byte { None, Invalid, Inactive, Busy, Exhausted }

    public readonly struct MeleeTiming
    {
        public readonly float Windup, Active, Recovery;
        public float Total => Windup + Active + Recovery;
        public MeleeTiming(float windup, float active, float recovery)
        { Windup = windup; Active = active; Recovery = recovery; }
    }

    public readonly struct MeleeDefinition
    {
        public readonly MeleeWeaponKind Weapon;
        public readonly float Reach, SweepRadius, SweepDegrees, LightDamage, HeavyDamage, LightCost, HeavyCost;
        public readonly MeleeTiming Light, Heavy;
        public MeleeDefinition(MeleeWeaponKind weapon, float reach, float radius, float degrees,
            float lightDamage, float heavyDamage, float lightCost, float heavyCost, MeleeTiming light, MeleeTiming heavy)
        {
            Weapon = weapon; Reach = reach; SweepRadius = radius; SweepDegrees = degrees;
            LightDamage = lightDamage; HeavyDamage = heavyDamage; LightCost = lightCost; HeavyCost = heavyCost;
            Light = light; Heavy = heavy;
        }
        public MeleeTiming Timing(MeleeAttackKind attack, int combo)
        {
            if (attack == MeleeAttackKind.Heavy) return Heavy;
            // Three deliberately distinct light poses share these authoritative windows.
            return combo == 1 ? new MeleeTiming(Light.Windup * .9f, Light.Active, Light.Recovery * .85f) :
                combo == 2 ? new MeleeTiming(Light.Windup * 1.1f, Light.Active, Light.Recovery * 1.15f) : Light;
        }
        public float Damage(MeleeAttackKind attack, int combo) => attack == MeleeAttackKind.Heavy
            ? HeavyDamage : LightDamage * (combo == 2 ? 1.15f : 1);
        public float Cost(MeleeAttackKind attack) => attack == MeleeAttackKind.Heavy ? HeavyCost : LightCost;
    }

    // Runtime state only. Inventory identity, authority, physics and damage belong to the host game.
    public struct MeleeCombatState
    {
        public bool Initialized, Cancelled;
        public MeleeWeaponKind SelectedWeapon, AttackWeapon;
        public MeleeAttackKind Attack;
        public uint Sequence, CancelSequence;
        public int ComboIndex, NextComboIndex;
        public float Elapsed, RecoveryRemaining, ComboRemaining, Stamina, StaminaRest;
        public MeleePhase Phase => MeleeRules.Phase(this);
    }

    public readonly struct MeleeActiveWindow
    {
        public readonly bool Valid, Completed;
        // Normalized active interval crossed by this simulation step, including skipped frames.
        public readonly float From, To;
        public MeleeActiveWindow(bool valid, bool completed, float from, float to)
        { Valid = valid; Completed = completed; From = from; To = to; }
    }

    public static class MeleeRules
    {
        public const float StaminaCapacity = 100, StaminaRecoveryPerSecond = 25, StaminaRecoveryDelay = 1.2f;
        public const float ComboGraceSeconds = .65f, MaximumTurnDegrees = 65, MaximumSweepStepSeconds = .025f;
        public const int ComboCount = 3;
        // Initial Moonkov tuning; these are not claimed to be another game's exact damage values.
        private static readonly MeleeDefinition Fists = new MeleeDefinition(MeleeWeaponKind.Fists,
            1.25f, .17f, 70, 20, 35, 5, 15, new MeleeTiming(.14f, .12f, .24f), new MeleeTiming(.36f, .16f, .48f));
        private static readonly MeleeDefinition Dagger = new MeleeDefinition(MeleeWeaponKind.Dagger,
            1.55f, .13f, 60, 42, 90, 6, 22, new MeleeTiming(.15f, .10f, .20f), new MeleeTiming(.35f, .12f, .40f));
        private static readonly MeleeDefinition Sword = new MeleeDefinition(MeleeWeaponKind.EnergySword,
            2, .15f, 110, 60, 115, 8, 30, new MeleeTiming(.22f, .14f, .30f), new MeleeTiming(.52f, .18f, .60f));

        public static bool TryDefinition(MeleeWeaponKind weapon, out MeleeDefinition definition)
        {
            definition = weapon == MeleeWeaponKind.Fists ? Fists : weapon == MeleeWeaponKind.Dagger ? Dagger :
                weapon == MeleeWeaponKind.EnergySword ? Sword : default;
            return definition.Weapon != MeleeWeaponKind.None;
        }
        public static MeleeTiming Timing(in MeleeCombatState state) => TryDefinition(state.AttackWeapon, out var definition)
            ? definition.Timing(state.Attack, state.ComboIndex) : default;
        public static MeleePhase Phase(in MeleeCombatState state)
        {
            if (!state.Initialized || state.Cancelled || state.Attack == MeleeAttackKind.None ||
                !TryDefinition(state.AttackWeapon, out var definition)) return MeleePhase.Idle;
            var timing = definition.Timing(state.Attack, state.ComboIndex);
            return state.Elapsed < timing.Windup ? MeleePhase.Windup :
                state.Elapsed < timing.Windup + timing.Active ? MeleePhase.Active :
                state.Elapsed < timing.Total ? MeleePhase.Recovery : MeleePhase.Idle;
        }
        public static void Initialize(ref MeleeCombatState state)
        {
            if (state.Initialized) return;
            state = new MeleeCombatState { Initialized = true, Stamina = StaminaCapacity };
        }
        public static void Select(ref MeleeCombatState state, MeleeWeaponKind weapon)
        {
            Initialize(ref state);
            if (state.SelectedWeapon == weapon) return;
            Cancel(ref state);
            state.SelectedWeapon = weapon; state.NextComboIndex = 0; state.ComboRemaining = 0;
        }
        public static void Cancel(ref MeleeCombatState state)
        {
            Initialize(ref state);
            if (Phase(state) != MeleePhase.Idle)
            {
                state.Cancelled = true;
                unchecked { state.CancelSequence++; }
                // Keep the original recovery lock and spent stamina when interrupted or switched.
                state.StaminaRest = Math.Max(state.StaminaRest, StaminaRecoveryDelay);
            }
            state.NextComboIndex = 0; state.ComboRemaining = 0;
        }
        public static bool TryStart(ref MeleeCombatState state, MeleeAttackKind attack, bool canAct,
            bool stimulated, out MeleeStartError error)
        {
            Initialize(ref state);
            error = MeleeStartError.Invalid;
            if ((attack != MeleeAttackKind.Light && attack != MeleeAttackKind.Heavy) ||
                !TryDefinition(state.SelectedWeapon, out var definition)) return false;
            if (!canAct) { error = MeleeStartError.Inactive; return false; }
            if (state.RecoveryRemaining > .00001f || Phase(state) != MeleePhase.Idle)
            { error = MeleeStartError.Busy; return false; }
            float cost = definition.Cost(attack);
            if (!float.IsFinite(state.Stamina)) state.Stamina = 0;
            if (!stimulated && state.Stamina + .00001f < cost)
            { error = MeleeStartError.Exhausted; return false; }
            state.ComboIndex = attack == MeleeAttackKind.Light && state.ComboRemaining > 0 ? state.NextComboIndex : 0;
            state.NextComboIndex = attack == MeleeAttackKind.Light ? (state.ComboIndex + 1) % ComboCount : 0;
            state.AttackWeapon = state.SelectedWeapon; state.Attack = attack; state.Elapsed = 0;
            state.Cancelled = false; state.ComboRemaining = 0;
            state.RecoveryRemaining = definition.Timing(attack, state.ComboIndex).Total;
            state.Stamina = stimulated ? StaminaCapacity : Math.Max(0, Math.Min(StaminaCapacity, state.Stamina) - cost);
            state.StaminaRest = StaminaRecoveryDelay;
            unchecked { state.Sequence++; if (state.Sequence == 0) state.Sequence++; }
            error = MeleeStartError.None; return true;
        }
        public static MeleeActiveWindow Tick(ref MeleeCombatState state, float dt, bool stimulated)
        {
            Initialize(ref state);
            if (!float.IsFinite(dt) || dt <= 0) return default;
            var previousPhase = Phase(state);
            var timing = Timing(state);
            float before = state.Elapsed;
            float busyTime = Math.Min(dt, Math.Max(0, state.RecoveryRemaining));
            state.RecoveryRemaining = Math.Max(0, state.RecoveryRemaining - dt);
            bool wasAttacking = previousPhase != MeleePhase.Idle;
            if (wasAttacking) state.Elapsed = Math.Min(timing.Total, state.Elapsed + dt);
            bool completed = wasAttacking && Phase(state) == MeleePhase.Idle;
            float freeTime = dt - busyTime;
            if (completed) state.ComboRemaining = ComboGraceSeconds;
            state.ComboRemaining = Math.Max(0, state.ComboRemaining - freeTime);
            if (state.ComboRemaining <= 0 && Phase(state) == MeleePhase.Idle) state.NextComboIndex = 0;
            if (busyTime > 0) state.StaminaRest = StaminaRecoveryDelay;
            float recoveryTime = Math.Max(0, freeTime - state.StaminaRest);
            state.StaminaRest = Math.Max(0, state.StaminaRest - freeTime);
            state.Stamina = stimulated ? StaminaCapacity : Math.Min(StaminaCapacity,
                Math.Max(0, float.IsFinite(state.Stamina) ? state.Stamina : 0) + StaminaRecoveryPerSecond * recoveryTime);
            if (!wasAttacking || state.Cancelled || timing.Active <= 0) return new MeleeActiveWindow(false, completed, 0, 0);
            float from = Math.Max(before, timing.Windup), to = Math.Min(state.Elapsed, timing.Windup + timing.Active);
            return new MeleeActiveWindow(to > from, completed,
                Math.Max(0, Math.Min(1, (from - timing.Windup) / timing.Active)),
                Math.Max(0, Math.Min(1, (to - timing.Windup) / timing.Active)));
        }
    }
}
