using System;

namespace Unity.MP_FPS.Survival
{
    public enum BodyRegion { Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg }

    // Explicit fields keep value copies independent; no shared mutable arrays.
    public readonly struct RegionValues
    {
        public readonly double Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg;
        public RegionValues(double head, double chest, double stomach, double leftArm,
            double rightArm, double leftLeg, double rightLeg)
        { Head = head; Chest = chest; Stomach = stomach; LeftArm = leftArm;
            RightArm = rightArm; LeftLeg = leftLeg; RightLeg = rightLeg; }
        public double this[BodyRegion region]
        {
            get
            {
                switch (region)
                {
                    case BodyRegion.Head: return Head;
                    case BodyRegion.Chest: return Chest;
                    case BodyRegion.Stomach: return Stomach;
                    case BodyRegion.LeftArm: return LeftArm;
                    case BodyRegion.RightArm: return RightArm;
                    case BodyRegion.LeftLeg: return LeftLeg;
                    case BodyRegion.RightLeg: return RightLeg;
                    default: throw new ArgumentOutOfRangeException(nameof(region));
                }
            }
        }
        internal RegionValues With(BodyRegion region, double value)
        {
            // Validate even if an invalid enum would otherwise leave every field unchanged.
            _ = this[region];
            return new RegionValues(region == BodyRegion.Head ? value : Head,
                region == BodyRegion.Chest ? value : Chest, region == BodyRegion.Stomach ? value : Stomach,
                region == BodyRegion.LeftArm ? value : LeftArm, region == BodyRegion.RightArm ? value : RightArm,
                region == BodyRegion.LeftLeg ? value : LeftLeg, region == BodyRegion.RightLeg ? value : RightLeg);
        }
    }

    public sealed class RegionalHealthConfig
    {
        public RegionValues Maximum { get; }
        public double ArmStabilityRecoveryScale { get; }
        public double ArmEquipDurationScale { get; }
        public double LegWalkSpeedScale { get; }
        public double StomachResourceDrainScale { get; }
        public RegionalHealthConfig(RegionValues maximum, double armStabilityRecoveryScale,
            double armEquipDurationScale, double legWalkSpeedScale, double stomachResourceDrainScale)
        {
            for (int i = 0; i < 7; i++)
                if (!HealthNumbers.Positive(maximum[(BodyRegion)i]))
                    throw new ArgumentOutOfRangeException(nameof(maximum));
            if (!HealthNumbers.Positive(armStabilityRecoveryScale) || armStabilityRecoveryScale > 1)
                throw new ArgumentOutOfRangeException(nameof(armStabilityRecoveryScale));
            if (!HealthNumbers.Positive(armEquipDurationScale) || armEquipDurationScale < 1)
                throw new ArgumentOutOfRangeException(nameof(armEquipDurationScale));
            if (!HealthNumbers.Positive(legWalkSpeedScale) || legWalkSpeedScale > 1)
                throw new ArgumentOutOfRangeException(nameof(legWalkSpeedScale));
            if (!HealthNumbers.Positive(stomachResourceDrainScale) || stomachResourceDrainScale < 1)
                throw new ArgumentOutOfRangeException(nameof(stomachResourceDrainScale));
            Maximum = maximum; ArmStabilityRecoveryScale = armStabilityRecoveryScale;
            ArmEquipDurationScale = armEquipDurationScale; LegWalkSpeedScale = legWalkSpeedScale;
            StomachResourceDrainScale = stomachResourceDrainScale;
        }
        // Test/play-tuning starting point, not established balance or a spawn policy.
        public static RegionalHealthConfig Prototype() => new RegionalHealthConfig(
            new RegionValues(35, 85, 70, 60, 60, 65, 65), 0.6, 1.4, 0.6, 1.5);
    }

    public readonly struct RegionalHealthState
    {
        public RegionValues Current { get; }
        public bool IsDead => Current.Head <= 0 || Current.Chest <= 0;
        internal RegionalHealthState(RegionValues current) { Current = current; }
    }

    public readonly struct DepletedPartEffects
    {
        public readonly bool CanSprint;
        public readonly double StabilityRecoveryScale, EquipDurationScale, WalkSpeedScale, ResourceDrainScale;
        internal DepletedPartEffects(RegionalHealthState state, RegionalHealthConfig config)
        {
            bool arm = state.Current.LeftArm <= 0 || state.Current.RightArm <= 0;
            bool leg = state.Current.LeftLeg <= 0 || state.Current.RightLeg <= 0;
            CanSprint = !state.IsDead && !leg;
            StabilityRecoveryScale = arm ? config.ArmStabilityRecoveryScale : 1;
            EquipDurationScale = arm ? config.ArmEquipDurationScale : 1;
            WalkSpeedScale = leg ? config.LegWalkSpeedScale : 1;
            ResourceDrainScale = state.Current.Stomach <= 0 ? config.StomachResourceDrainScale : 1;
        }
    }

    public static class RegionalHealthRules
    {
        public static RegionalHealthState Create(RegionValues values, RegionalHealthConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            for (int i = 0; i < 7; i++)
            {
                var region = (BodyRegion)i;
                values = values.With(region, HealthNumbers.Clamp(values[region], config.Maximum[region]));
            }
            return new RegionalHealthState(values);
        }
        public static RegionalHealthState Full(RegionalHealthConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return Create(config.Maximum, config);
        }
        // Invalid amounts are rejected as no-ops; excess finite damage never spills into another region.
        public static RegionalHealthState Damage(RegionalHealthState state, BodyRegion region, double amount)
        {
            double current = state.Current[region];
            if (state.IsDead || !HealthNumbers.Positive(amount)) return state;
            return new RegionalHealthState(state.Current.With(region, Math.Max(0, current - amount)));
        }
        public static RegionalHealthState Heal(RegionalHealthState state, BodyRegion region,
            double amount, RegionalHealthConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            double current = state.Current[region];
            if (state.IsDead || !HealthNumbers.Positive(amount)) return state;
            // Compare to remaining capacity before addition to avoid finite-input overflow.
            double maximum = config.Maximum[region];
            double healed = amount >= maximum - current ? maximum : current + amount;
            return new RegionalHealthState(state.Current.With(region, healed));
        }
        public static DepletedPartEffects Effects(RegionalHealthState state, RegionalHealthConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return new DepletedPartEffects(state, config);
        }
    }

    internal static class HealthNumbers
    {
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool Positive(double value) => Finite(value) && value > 0;
        // Corrupt/nonfinite initial state fails closed, rather than creating health.
        internal static double Clamp(double value, double maximum) =>
            Finite(value) ? Math.Max(0, Math.Min(maximum, value)) : 0;
    }
}
