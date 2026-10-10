using System;

namespace Unity.MP_FPS.Survival
{
    public enum NutritionResource { Food, Water }
    public enum ReserveWarning { Normal, Low, Danger, Depleted }

    public sealed class ReserveConfig
    {
        public int Capacity { get; }
        public int CostPerTick { get; }
        public int LowAtOrBelow { get; }
        public int DangerAtOrBelow { get; }
        public int ExhaustedGraceTicks { get; }
        public int PressureIntervalTicks { get; }
        public double ChestDamagePerPulse { get; }
        public ReserveConfig(int capacity, int costPerTick, int lowAtOrBelow, int dangerAtOrBelow,
            int exhaustedGraceTicks, int pressureIntervalTicks, double chestDamagePerPulse)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (costPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(costPerTick));
            if (dangerAtOrBelow <= 0 || lowAtOrBelow <= dangerAtOrBelow || lowAtOrBelow >= capacity)
                throw new ArgumentOutOfRangeException(nameof(lowAtOrBelow));
            if (exhaustedGraceTicks <= 0) throw new ArgumentOutOfRangeException(nameof(exhaustedGraceTicks));
            if (pressureIntervalTicks <= 0) throw new ArgumentOutOfRangeException(nameof(pressureIntervalTicks));
            if (!HealthNumbers.Positive(chestDamagePerPulse)) throw new ArgumentOutOfRangeException(nameof(chestDamagePerPulse));
            Capacity = capacity; CostPerTick = costPerTick; LowAtOrBelow = lowAtOrBelow;
            DangerAtOrBelow = dangerAtOrBelow; ExhaustedGraceTicks = exhaustedGraceTicks;
            PressureIntervalTicks = pressureIntervalTicks; ChestDamagePerPulse = chestDamagePerPulse;
        }
    }

    public sealed class NutritionConfig
    {
        public int TickMilliseconds { get; }
        public ReserveConfig Food { get; }
        public ReserveConfig Water { get; }
        public NutritionConfig(int tickMilliseconds, ReserveConfig food, ReserveConfig water)
        {
            if (tickMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(tickMilliseconds));
            TickMilliseconds = tickMilliseconds;
            Food = food ?? throw new ArgumentNullException(nameof(food));
            Water = water ?? throw new ArgumentNullException(nameof(water));
        }
        // Fictional play-tuning defaults: food 20 min, water 10 min. Not physiology or final balance.
        public static NutritionConfig Prototype() => new NutritionConfig(100,
            new ReserveConfig(24000, 2, 6000, 2400, 600, 100, 1),
            new ReserveConfig(12000, 2, 3000, 1200, 300, 50, 1));
    }

    public readonly struct ReserveState
    {
        public int Amount { get; }
        public int GraceRemainingTicks { get; }
        public int PressureExposureTicks { get; }
        internal ReserveState(int amount, int graceRemainingTicks, int pressureExposureTicks)
        { Amount = amount; GraceRemainingTicks = graceRemainingTicks; PressureExposureTicks = pressureExposureTicks; }
    }

    public readonly struct NutritionState
    {
        public bool IsInitialized { get; }
        public ReserveState Food { get; }
        public ReserveState Water { get; }
        internal NutritionState(ReserveState food, ReserveState water)
        { Food = food; Water = water; IsInitialized = true; }
    }

    public readonly struct NutritionStep
    {
        public readonly NutritionState Nutrition;
        public readonly RegionalHealthState Health;
        public readonly ReserveWarning FoodWarning, WaterWarning;
        public readonly int FoodDamagePulses, WaterDamagePulses;
        public readonly double FoodChestDamageApplied, WaterChestDamageApplied;
        internal NutritionStep(NutritionState nutrition, RegionalHealthState health, NutritionConfig config,
            int foodPulses, int waterPulses, double foodDamage, double waterDamage)
        {
            Nutrition = nutrition; Health = health;
            FoodWarning = NutritionRules.Warning(nutrition.Food, config.Food);
            WaterWarning = NutritionRules.Warning(nutrition.Water, config.Water);
            FoodDamagePulses = foodPulses; WaterDamagePulses = waterPulses;
            FoodChestDamageApplied = foodDamage; WaterChestDamageApplied = waterDamage;
        }
    }

    public static class NutritionRules
    {
        // Preserve and split an oversized backlog; never clamp elapsed time.
        public const int MaxTicksPerAdvance = 4096;
        public static NutritionState Create(int food, int water, NutritionConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return new NutritionState(CreateReserve(food, config.Food), CreateReserve(water, config.Water));
        }
        private static ReserveState CreateReserve(int amount, ReserveConfig config) =>
            new ReserveState(Math.Max(0, Math.Min(config.Capacity, amount)), config.ExhaustedGraceTicks, 0);
        public static ReserveWarning Warning(ReserveState state, ReserveConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (state.Amount == 0) return ReserveWarning.Depleted;
            if (state.Amount <= config.DangerAtOrBelow) return ReserveWarning.Danger;
            return state.Amount <= config.LowAtOrBelow ? ReserveWarning.Low : ReserveWarning.Normal;
        }
        public static NutritionState Restore(NutritionState state, RegionalHealthState health,
            NutritionResource resource, int amount, NutritionConfig config)
        {
            Validate(state, config);
            ValidateHealth(health);
            if (resource != NutritionResource.Food && resource != NutritionResource.Water)
                throw new ArgumentOutOfRangeException(nameof(resource));
            if (health.IsDead || amount <= 0) return state;
            var reserve = resource == NutritionResource.Food ? state.Food : state.Water;
            var settings = resource == NutritionResource.Food ? config.Food : config.Water;
            var restored = new ReserveState((int)Math.Min(settings.Capacity, reserve.Amount + (long)amount),
                reserve.GraceRemainingTicks, reserve.PressureExposureTicks);
            return resource == NutritionResource.Food ? new NutritionState(restored, state.Water) : new NutritionState(state.Food, restored);
        }

        // Per authoritative tick: water first, then food only if still alive. No oxygen integration here.
        public static NutritionStep Advance(NutritionState state, RegionalHealthState health, int elapsedTicks,
            NutritionConfig config, RegionalHealthConfig healthConfig)
        {
            Validate(state, config);
            ValidateHealth(health);
            if (healthConfig == null) throw new ArgumentNullException(nameof(healthConfig));
            if (elapsedTicks < 0 || elapsedTicks > MaxTicksPerAdvance) throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
            var food = state.Food; var water = state.Water;
            int foodPulses = 0, waterPulses = 0;
            double foodDamage = 0, waterDamage = 0;
            // Stomach depletion changes costs, not clocks or damage. Integer costs round up per tick.
            double scale = health.Current.Stomach <= 0 ? healthConfig.StomachResourceDrainScale : 1;
            int foodCost = ScaledCost(config.Food.CostPerTick, scale);
            int waterCost = ScaledCost(config.Water.CostPerTick, scale);
            for (int tick = 0; tick < elapsedTicks && !health.IsDead; tick++)
            {
                water = AdvanceReserve(water, config.Water, waterCost, ref health, ref waterPulses, ref waterDamage);
                if (!health.IsDead)
                    food = AdvanceReserve(food, config.Food, foodCost, ref health, ref foodPulses, ref foodDamage);
            }
            return new NutritionStep(new NutritionState(food, water), health, config,
                foodPulses, waterPulses, foodDamage, waterDamage);
        }
        private static int ScaledCost(int cost, double scale)
        {
            double scaled = cost * scale;
            return scaled >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(scaled);
        }
        private static ReserveState AdvanceReserve(ReserveState state, ReserveConfig config, int cost,
            ref RegionalHealthState health, ref int pulses, ref double appliedDamage)
        {
            if (state.Amount > 0)
                return new ReserveState(Math.Max(0, state.Amount - cost), state.GraceRemainingTicks, state.PressureExposureTicks);
            // Depletion warns first; neither the last supplied tick nor grace ticks cause damage.
            if (state.GraceRemainingTicks > 0)
                return new ReserveState(0, state.GraceRemainingTicks - 1, state.PressureExposureTicks);
            int exposure = state.PressureExposureTicks + 1;
            if (exposure == config.PressureIntervalTicks)
            {
                double before = health.Current.Chest;
                health = RegionalHealthRules.Damage(health, BodyRegion.Chest, config.ChestDamagePerPulse);
                appliedDamage = Math.Min(double.MaxValue, appliedDamage + (before - health.Current.Chest));
                pulses++;
                exposure = 0;
            }
            return new ReserveState(0, state.GraceRemainingTicks, exposure);
        }
        private static void ValidateHealth(RegionalHealthState health)
        {
            for (int i = 0; i < 7; i++)
            {
                double value = health.Current[(BodyRegion)i];
                if (!HealthNumbers.Finite(value) || value < 0)
                    throw new ArgumentException("Health must contain finite nonnegative values.", nameof(health));
            }
        }
        private static void Validate(NutritionState state, NutritionConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!state.IsInitialized) throw new ArgumentException("Use Create to initialize reserves and their grace periods.", nameof(state));
            ValidateReserve(state.Food, config.Food);
            ValidateReserve(state.Water, config.Water);
        }
        private static void ValidateReserve(ReserveState state, ReserveConfig config)
        {
            if (state.Amount < 0 || state.Amount > config.Capacity || state.GraceRemainingTicks < 0 ||
                state.GraceRemainingTicks > config.ExhaustedGraceTicks || state.PressureExposureTicks < 0 ||
                state.PressureExposureTicks >= config.PressureIntervalTicks ||
                (state.GraceRemainingTicks > 0 && state.PressureExposureTicks != 0))
                throw new ArgumentException("State must use its original configuration.", nameof(state));
        }
    }
}
