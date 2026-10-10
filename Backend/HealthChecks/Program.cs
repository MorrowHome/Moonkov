using Unity.MP_FPS.Survival;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 1e-9, message);
void Reject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, message);
}
bool SameHealth(RegionalHealthState a, RegionalHealthState b) =>
    Enumerable.Range(0, 7).All(i => Math.Abs(a.Current[(BodyRegion)i] - b.Current[(BodyRegion)i]) < 1e-9);
bool ExactHealth(RegionalHealthState a, RegionalHealthState b) =>
    a.IsDead == b.IsDead && Enumerable.Range(0, 7).All(i => a.Current[(BodyRegion)i] == b.Current[(BodyRegion)i]);
var healthConfig = RegionalHealthConfig.Prototype();
var oxygenConfig = OxygenConfig.Prototype();
var full = RegionalHealthRules.Full(healthConfig);
Check(!full.IsDead && RegionalHealthRules.Effects(full, healthConfig).CanSprint, "full state is alive and can sprint");
foreach (BodyRegion region in Enum.GetValues<BodyRegion>())
{
    var damaged = RegionalHealthRules.Damage(full, region, double.MaxValue);
    Near(damaged.Current[region], 0, "finite huge damage saturates " + region);
    Check(damaged.IsDead == (region == BodyRegion.Head || region == BodyRegion.Chest), "lethal region contract " + region);
    foreach (BodyRegion other in Enum.GetValues<BodyRegion>())
        if (other != region) Near(damaged.Current[other], full.Current[other], "no spill " + region + " -> " + other);
    var healed = RegionalHealthRules.Heal(damaged, region, double.MaxValue, healthConfig);
    Near(healed.Current[region], damaged.IsDead ? 0 : full.Current[region], "heal clamps and cannot revive " + region);
    if (damaged.IsDead)
        Check(SameHealth(RegionalHealthRules.Damage(damaged, BodyRegion.Stomach, 9), damaged), "death is terminal and damage idempotent");
}
foreach (double amount in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, 0d })
{
    Check(SameHealth(full, RegionalHealthRules.Damage(full, BodyRegion.Head, amount)), "invalid damage is no-op");
    var hurt = RegionalHealthRules.Damage(full, BodyRegion.Head, 5);
    Check(SameHealth(hurt, RegionalHealthRules.Heal(hurt, BodyRegion.Head, amount, healthConfig)), "invalid heal is no-op");
}
var corrupt = RegionalHealthRules.Create(new RegionValues(double.NaN, double.PositiveInfinity, -2, 999, 1, 0, double.NegativeInfinity), healthConfig);
Check(corrupt.IsDead && corrupt.Current.Head == 0 && corrupt.Current.Chest == 0 && corrupt.Current.Stomach == 0 &&
    corrupt.Current.LeftArm == 60 && corrupt.Current.RightArm == 1 && corrupt.Current.RightLeg == 0, "creation clamps bounds and fails closed on nonfinite state");
Reject(() => RegionalHealthRules.Damage(full, (BodyRegion)7, 1), "invalid region rejected");
Reject(() => new RegionalHealthConfig(new RegionValues(1, 1, double.NaN, 1, 1, 1, 1), 1, 1, 1, 1), "invalid maximum rejected");
foreach (double bad in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
    Reject(() => new RegionalHealthConfig(new RegionValues(bad, 1, 1, 1, 1, 1, 1), 1, 1, 1, 1), "nonpositive/nonfinite config rejected");
Reject(() => new RegionalHealthConfig(healthConfig.Maximum, 1.1, 1, 1, 1), "arm recovery cannot accelerate");
Reject(() => new RegionalHealthConfig(healthConfig.Maximum, 1, 0.9, 1, 1), "equip penalty cannot accelerate");
Reject(() => new RegionalHealthConfig(healthConfig.Maximum, 1, 1, 0, 1), "leg penalty cannot erase walking");
Reject(() => new RegionalHealthConfig(healthConfig.Maximum, 1, 1, 1, 0.9), "stomach penalty cannot reduce pressure");
foreach (var region in new[] { BodyRegion.LeftArm, BodyRegion.RightArm })
{
    var effects = RegionalHealthRules.Effects(RegionalHealthRules.Damage(full, region, 1000), healthConfig);
    Check(effects.StabilityRecoveryScale == 0.6 && effects.EquipDurationScale == 1.4 && effects.CanSprint, "arm effect " + region);
}
foreach (var region in new[] { BodyRegion.LeftLeg, BodyRegion.RightLeg })
{
    var hurt = RegionalHealthRules.Damage(full, region, 1000);
    Check(!RegionalHealthRules.Effects(hurt, healthConfig).CanSprint && RegionalHealthRules.Effects(hurt, healthConfig).WalkSpeedScale == 0.6, "leg effect " + region);
    Check(RegionalHealthRules.Effects(RegionalHealthRules.Heal(hurt, region, 1, healthConfig), healthConfig).CanSprint, "healing restores depleted limb function");
}
Check(RegionalHealthRules.Effects(RegionalHealthRules.Damage(full, BodyRegion.Stomach, 1000), healthConfig).ResourceDrainScale == 1.5, "stomach exposes pressure multiplier only");

foreach (var pair in new[] { (1000, OxygenWarning.Normal), (251, OxygenWarning.Normal), (250, OxygenWarning.Low),
    (101, OxygenWarning.Low), (100, OxygenWarning.Danger), (1, OxygenWarning.Danger), (0, OxygenWarning.Exhausted) })
    Check(OxygenRules.Warning(OxygenRules.Create(pair.Item1, oxygenConfig), oxygenConfig) == pair.Item2, "warning threshold " + pair.Item1);
Check(OxygenRules.Create(-1, oxygenConfig).Amount == 0 && OxygenRules.Create(int.MaxValue, oxygenConfig).Amount == 1000, "oxygen creation saturates");
Reject(() => new OxygenConfig(0, 1000, 1, 1, 250, 100, 10, 3), "zero tick duration rejected");
Reject(() => new OxygenConfig(100, 0, 1, 1, 250, 100, 10, 3), "zero capacity rejected");
Reject(() => new OxygenConfig(100, 1000, 0, 1, 250, 100, 10, 3), "zero drain rejected");
Reject(() => new OxygenConfig(100, 1000, 1, 0, 250, 100, 10, 3), "zero refill rejected");
Reject(() => new OxygenConfig(100, 1000, 1, 1, 100, 100, 10, 3), "unordered warnings rejected");
Reject(() => new OxygenConfig(100, 1000, 1, 1, 1000, 100, 10, 3), "full capacity cannot warn");
Reject(() => new OxygenConfig(100, 1000, 1, 1, 250, 0, 10, 3), "zero danger threshold rejected");
Reject(() => new OxygenConfig(100, 1000, 1, 1, 250, 100, 0, 3), "zero pulse interval rejected");
Reject(() => new OxygenConfig(100, 1000, 1, 1, 250, 100, 10, double.NaN), "nonfinite pulse damage rejected");
var start = OxygenRules.Create(10, oxygenConfig);
Reject(() => OxygenRules.Advance(start, full, AirEnvironment.Vacuum, -1, oxygenConfig), "negative ticks rejected without time loss");
Reject(() => OxygenRules.Advance(start, full, (AirEnvironment)8, 1, oxygenConfig), "invalid environment rejected");
var step = OxygenRules.Advance(start, full, AirEnvironment.Vacuum, 10, oxygenConfig);
Check(step.Oxygen.Amount == 0 && step.IsHypoxic && step.DamagePulses == 0 && SameHealth(step.Health, full), "depletion warns without immediate damage");
step = OxygenRules.Advance(step.Oxygen, step.Health, AirEnvironment.Vacuum, 9, oxygenConfig);
Check(step.DamagePulses == 0 && step.Oxygen.ExhaustedExposureTicks == 9, "periodic exposure accumulates");
var interior = OxygenRules.Advance(step.Oxygen, step.Health, AirEnvironment.OxygenatedInterior, 1, oxygenConfig);
Check(!interior.IsHypoxic && interior.Oxygen.Amount == 5 && interior.DamagePulses == 0 && interior.Oxygen.ExhaustedExposureTicks == 9, "indoor stops harm and gradually refills without erasing exposure");
var outside = OxygenRules.Advance(interior.Oxygen, interior.Health, AirEnvironment.Vacuum, 5, oxygenConfig);
Check(outside.DamagePulses == 0 && outside.Oxygen.Amount == 0, "boundary does not reset oxygen or charge damage during supplied ticks");
outside = OxygenRules.Advance(outside.Oxygen, outside.Health, AirEnvironment.Vacuum, 1, oxygenConfig);
Check(outside.DamagePulses == 1 && outside.ChestDamageApplied == 3 && outside.Oxygen.ExhaustedExposureTicks == 0, "boundary cannot erase owed pulse");
Check(OxygenRules.Advance(start, full, AirEnvironment.OxygenatedInterior, OxygenRules.MaxTicksPerAdvance, oxygenConfig).Oxygen.Amount == 1000, "large refill saturates without overflow");
var lethal = OxygenRules.Advance(start, full, AirEnvironment.Vacuum, OxygenRules.MaxTicksPerAdvance, oxygenConfig);
Check(lethal.Health.IsDead && lethal.DamagePulses == 29 && lethal.ChestDamageApplied == 85 && lethal.Oxygen.ExhaustedExposureTicks == 0, "large elapsed tick batch terminates on lethal pulse");
var postmortem = OxygenRules.Advance(lethal.Oxygen, lethal.Health, AirEnvironment.OxygenatedInterior, 999, oxygenConfig);
Check(postmortem.DamagePulses == 0 && postmortem.Oxygen.Amount == lethal.Oxygen.Amount && SameHealth(lethal.Health, postmortem.Health), "dead state neither refills nor mutates");
var zero = OxygenRules.Advance(step.Oxygen, step.Health, AirEnvironment.Vacuum, 0, oxygenConfig);
Check(zero.Oxygen.Amount == step.Oxygen.Amount && zero.Oxygen.ExhaustedExposureTicks == step.Oxygen.ExhaustedExposureTicks && zero.DamagePulses == 0, "zero ticks do not progress");

// Deterministic partition checks include non-divisible oxygen cost, fractional damage,
// low/danger/exhausted boundaries, carried exposure, lethal batches and indoor refill.
var configs = new[] { oxygenConfig, new OxygenConfig(100, 101, 3, 7, 30, 10, 7, 0.5),
    new OxygenConfig(100, 101, 3, 7, 30, 10, 1, 0.1) };
foreach (var config in configs)
foreach (var environment in Enum.GetValues<AirEnvironment>())
foreach (int ticks in new[] { 0, 1, 6, 7, 8, 33, 34, 35, 101, 500, 884, 885, 1300 })
{
    var initial = OxygenRules.Create(config.Capacity, config);
    var batched = OxygenRules.Advance(initial, full, environment, ticks, config);
    var repeated = OxygenRules.Advance(initial, full, environment, 0, config);
    int pulses = 0;
    for (int i = 0; i < ticks; i++)
    { repeated = OxygenRules.Advance(repeated.Oxygen, repeated.Health, environment, 1, config); pulses += repeated.DamagePulses; }
    Check(batched.Oxygen.Amount == repeated.Oxygen.Amount && batched.Oxygen.ExhaustedExposureTicks == repeated.Oxygen.ExhaustedExposureTicks &&
        batched.Warning == repeated.Warning && batched.IsHypoxic == repeated.IsHypoxic && batched.DamagePulses == pulses && ExactHealth(batched.Health, repeated.Health),
        "batch equals single ticks " + environment + "/" + ticks + "/" + config.Capacity);
}
Reject(() => OxygenRules.Advance(start, full, AirEnvironment.Vacuum, int.MaxValue, oxygenConfig), "oversized batch rejects, never discards time");
Reject(() => OxygenRules.Advance(start, full, AirEnvironment.Vacuum, OxygenRules.MaxTicksPerAdvance + 1, oxygenConfig), "batch bound is explicit");
var largeConfig = new OxygenConfig(100, int.MaxValue, int.MaxValue, int.MaxValue, 250, 100, 10, 3);
Check(OxygenRules.Advance(OxygenRules.Create(1, largeConfig), full, AirEnvironment.OxygenatedInterior,
    OxygenRules.MaxTicksPerAdvance, largeConfig).Oxygen.Amount == int.MaxValue, "large integer products saturate safely");
var tinyHealth = RegionalHealthRules.Create(new RegionValues(1, double.Epsilon, 1, 1, 1, 1, 1), healthConfig);
var hugePulse = new OxygenConfig(100, 1000, 1, 5, 250, 100, 10, double.MaxValue);
var supplied = OxygenRules.Advance(OxygenRules.Create(1, hugePulse), tinyHealth, AirEnvironment.Vacuum, 1, hugePulse);
Check(!supplied.Health.IsDead && supplied.DamagePulses == 0 && supplied.ChestDamageApplied == 0, "tiny health/huge pulse cannot kill before any pulse is due");
var hugeHit = OxygenRules.Advance(supplied.Oxygen, supplied.Health, AirEnvironment.Vacuum, 10, hugePulse);
Check(hugeHit.Health.IsDead && hugeHit.DamagePulses == 1, "huge finite pulse kills only when due");
var splitBacklog = OxygenRules.Advance(OxygenRules.Create(0, oxygenConfig), full, AirEnvironment.OxygenatedInterior, OxygenRules.MaxTicksPerAdvance, oxygenConfig);
splitBacklog = OxygenRules.Advance(splitBacklog.Oxygen, splitBacklog.Health, AirEnvironment.OxygenatedInterior, 1, oxygenConfig);
Check(splitBacklog.Oxygen.Amount == oxygenConfig.Capacity && SameHealth(splitBacklog.Health, full), "retained oversized backlog can be split explicitly");
var fractionalConfig = new OxygenConfig(100, 1000, 1, 5, 250, 100, 1, 0.1);
var fractionalHealth = RegionalHealthRules.Create(new RegionValues(1, 0.6, 1, 1, 1, 1, 1), healthConfig);
var six = OxygenRules.Advance(OxygenRules.Create(0, fractionalConfig), fractionalHealth, AirEnvironment.Vacuum, 6, fractionalConfig);
var oneAtATime = OxygenRules.Advance(OxygenRules.Create(0, fractionalConfig), fractionalHealth, AirEnvironment.Vacuum, 0, fractionalConfig);
for (int i = 0; i < 6; i++) oneAtATime = OxygenRules.Advance(oneAtATime.Oxygen, oneAtATime.Health, AirEnvironment.Vacuum, 1, fractionalConfig);
Check(!six.Health.IsDead && ExactHealth(six.Health, oneAtATime.Health), "0.6/0.1 preserves the identical positive rounding remainder after six pulses");
Check(OxygenRules.Advance(six.Oxygen, six.Health, AirEnvironment.Vacuum, 1, fractionalConfig).Health.IsDead, "seventh fractional pulse is terminal");
// Compare every segment of a mixed route, starting with unpaid exposure and empty oxygen.
var routeBatch = OxygenRules.Advance(OxygenRules.Create(0, oxygenConfig), full, AirEnvironment.Vacuum, 9, oxygenConfig);
var routeSingle = routeBatch;
foreach (var segment in new[] { (AirEnvironment.OxygenatedInterior, 3), (AirEnvironment.Vacuum, 16),
    (AirEnvironment.OxygenatedInterior, 1), (AirEnvironment.Vacuum, 18),
    (AirEnvironment.OxygenatedInterior, 7), (AirEnvironment.Vacuum, 150) })
{
    routeBatch = OxygenRules.Advance(routeBatch.Oxygen, routeBatch.Health, segment.Item1, segment.Item2, oxygenConfig);
    int pulses = 0;
    for (int i = 0; i < segment.Item2; i++)
    { routeSingle = OxygenRules.Advance(routeSingle.Oxygen, routeSingle.Health, segment.Item1, 1, oxygenConfig); pulses += routeSingle.DamagePulses; }
    Check(routeBatch.Oxygen.Amount == routeSingle.Oxygen.Amount && routeBatch.Oxygen.ExhaustedExposureTicks == routeSingle.Oxygen.ExhaustedExposureTicks &&
        routeBatch.Warning == routeSingle.Warning && routeBatch.IsHypoxic == routeSingle.IsHypoxic && routeBatch.DamagePulses == pulses && ExactHealth(routeBatch.Health, routeSingle.Health),
        "mixed route partition preserves refill, unpaid exposure, health and pulses " + segment);
}
Console.WriteLine($"Health rules checks passed: {checks}. Pure rules only; no Unity, network, save or gameplay integration validated.");
