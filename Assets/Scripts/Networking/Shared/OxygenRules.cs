using System;

namespace Unity.MP_FPS.Survival
{
    public enum AirEnvironment { Vacuum, OxygenatedInterior }
    public enum OxygenWarning { Normal, Low, Danger, Exhausted }

    public sealed class OxygenConfig
    {
        public int TickMilliseconds { get; }
        public int Capacity { get; }
        public int OutdoorCostPerTick { get; }
        public int IndoorRefillPerTick { get; }
        public int LowAtOrBelow { get; }
        public int DangerAtOrBelow { get; }
        public int HypoxiaIntervalTicks { get; }
        public double ChestDamagePerPulse { get; }
        public OxygenConfig(int tickMilliseconds, int capacity, int outdoorCostPerTick,
            int indoorRefillPerTick, int lowAtOrBelow, int dangerAtOrBelow,
            int hypoxiaIntervalTicks, double chestDamagePerPulse)
        {
            if (tickMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(tickMilliseconds));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (outdoorCostPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(outdoorCostPerTick));
            if (indoorRefillPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(indoorRefillPerTick));
            if (dangerAtOrBelow <= 0 || lowAtOrBelow <= dangerAtOrBelow || lowAtOrBelow >= capacity)
                throw new ArgumentOutOfRangeException(nameof(lowAtOrBelow));
            if (hypoxiaIntervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(hypoxiaIntervalTicks));
            if (!HealthNumbers.Positive(chestDamagePerPulse)) throw new ArgumentOutOfRangeException(nameof(chestDamagePerPulse));
            TickMilliseconds = tickMilliseconds; Capacity = capacity; OutdoorCostPerTick = outdoorCostPerTick;
            IndoorRefillPerTick = indoorRefillPerTick; LowAtOrBelow = lowAtOrBelow;
            DangerAtOrBelow = dangerAtOrBelow; HypoxiaIntervalTicks = hypoxiaIntervalTicks;
            ChestDamagePerPulse = chestDamagePerPulse;
        }
        // 100 ms ticks, 100 seconds of oxygen, 20-second full indoor refill, 1-second hypoxia pulses.
        public static OxygenConfig Prototype() => new OxygenConfig(100, 1000, 1, 5, 250, 100, 10, 3);
    }

    public readonly struct OxygenState
    {
        public int Amount { get; }
        public int ExhaustedExposureTicks { get; }
        internal OxygenState(int amount, int exhaustedExposureTicks)
        { Amount = amount; ExhaustedExposureTicks = exhaustedExposureTicks; }
    }

    public readonly struct OxygenStep
    {
        public readonly OxygenState Oxygen;
        public readonly RegionalHealthState Health;
        public readonly OxygenWarning Warning;
        public readonly bool IsHypoxic;
        public readonly int DamagePulses;
        public readonly double ChestDamageApplied;
        internal OxygenStep(OxygenState oxygen, RegionalHealthState before, RegionalHealthState after,
            OxygenWarning warning, bool isHypoxic, int damagePulses)
        {
            Oxygen = oxygen; Health = after; Warning = warning; IsHypoxic = isHypoxic;
            DamagePulses = damagePulses; ChestDamageApplied = before.Current.Chest - after.Current.Chest;
        }
    }

    public static class OxygenRules
    {
        // Reject oversized batches; an adapter must retain and split its backlog.
        public const int MaxTicksPerAdvance = 4096;
        public static OxygenState Create(int amount, OxygenConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return new OxygenState(Math.Max(0, Math.Min(config.Capacity, amount)), 0);
        }
        public static OxygenWarning Warning(OxygenState state, OxygenConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (state.Amount <= 0) return OxygenWarning.Exhausted;
            if (state.Amount <= config.DangerAtOrBelow) return OxygenWarning.Danger;
            return state.Amount <= config.LowAtOrBelow ? OxygenWarning.Low : OxygenWarning.Normal;
        }

        // One call represents whole authoritative simulation ticks in ONE environment.
        // Split at zone transitions. The adapter, not this model, owns real-time accumulation.
        public static OxygenStep Advance(OxygenState state, RegionalHealthState health,
            AirEnvironment environment, int elapsedTicks, OxygenConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (environment != AirEnvironment.Vacuum && environment != AirEnvironment.OxygenatedInterior)
                throw new ArgumentOutOfRangeException(nameof(environment));
            if (elapsedTicks < 0 || elapsedTicks > MaxTicksPerAdvance) throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
            if (state.Amount < 0 || state.Amount > config.Capacity || state.ExhaustedExposureTicks < 0 ||
                state.ExhaustedExposureTicks >= config.HypoxiaIntervalTicks)
                throw new ArgumentException("State must use the same configuration for its lifetime.", nameof(state));

            int amount = state.Amount, carry = state.ExhaustedExposureTicks, pulses = 0;
            var after = health;
            if (!health.IsDead && elapsedTicks > 0)
            {
                if (environment == AirEnvironment.OxygenatedInterior)
                    amount = (int)Math.Min(config.Capacity, amount + (long)config.IndoorRefillPerTick * elapsedTicks);
                else
                {
                    // A partially supplied tick counts as supplied; hypoxia starts on the next tick.
                    long suppliedTicks = (amount + (long)config.OutdoorCostPerTick - 1) / config.OutdoorCostPerTick;
                    long exposedTicks = Math.Max(0, (long)elapsedTicks - suppliedTicks);
                    amount = (int)Math.Max(0, amount - (long)config.OutdoorCostPerTick * elapsedTicks);
                    long exposure = carry + exposedTicks;
                    long due = exposure / config.HypoxiaIntervalTicks;
                    // Apply the exact same subtraction sequence as individual ticks. Multiplying
                    // pulse damage changes floating-point death boundaries (e.g. repeated 0.1).
                    // The explicit batch limit bounds this loop, without silently dropping time.
                    for (long i = 0; i < due && !after.IsDead; i++)
                    {
                        after = RegionalHealthRules.Damage(after, BodyRegion.Chest, config.ChestDamagePerPulse);
                        pulses++;
                    }
                    carry = after.IsDead ? 0 : (int)(exposure % config.HypoxiaIntervalTicks);
                }
            }
            var updated = new OxygenState(amount, carry);
            return new OxygenStep(updated, health, after, Warning(updated, config),
                !after.IsDead && environment == AirEnvironment.Vacuum && amount == 0, pulses);
        }
    }
}
