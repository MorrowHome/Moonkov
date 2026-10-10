using Unity.MP_FPS.Survival;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
void Reject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, message);
}
bool SameReserve(ReserveState a, ReserveState b) => a.Amount == b.Amount && a.GraceRemainingTicks == b.GraceRemainingTicks && a.PressureExposureTicks == b.PressureExposureTicks;
bool SameState(NutritionState a, NutritionState b) => a.IsInitialized == b.IsInitialized && SameReserve(a.Food, b.Food) && SameReserve(a.Water, b.Water);
bool SameHealth(RegionalHealthState a, RegionalHealthState b) => a.IsDead == b.IsDead && Enumerable.Range(0, 7).All(i => a.Current[(BodyRegion)i] == b.Current[(BodyRegion)i]);
var healthConfig = RegionalHealthConfig.Prototype();
var full = RegionalHealthRules.Full(healthConfig);
var standard = NutritionConfig.Prototype();
var quickReserve = new ReserveConfig(10, 2, 4, 2, 2, 3, 0.1);
var quick = new NutritionConfig(100, quickReserve, quickReserve);
var initial = NutritionRules.Create(10, 10, quick);
Check(initial.IsInitialized && initial.Food.GraceRemainingTicks == 2 && initial.Water.GraceRemainingTicks == 2, "explicit initialization grants grace");
var clamped = NutritionRules.Create(-1, int.MaxValue, quick);
Check(clamped.Food.Amount == 0 && clamped.Water.Amount == 10, "reserve initialization clamps bounds");
foreach (var pair in new[] { (10, ReserveWarning.Normal), (5, ReserveWarning.Normal), (4, ReserveWarning.Low), (3, ReserveWarning.Low),
    (2, ReserveWarning.Danger), (1, ReserveWarning.Danger), (0, ReserveWarning.Depleted) })
    Check(NutritionRules.Warning(NutritionRules.Create(pair.Item1, 10, quick).Food, quick.Food) == pair.Item2, "inclusive warning threshold " + pair.Item1);
Reject(() => new ReserveConfig(0, 1, 4, 2, 2, 3, 1), "capacity positive");
Reject(() => new ReserveConfig(10, 0, 4, 2, 2, 3, 1), "drain positive");
Reject(() => new ReserveConfig(10, 1, 2, 2, 2, 3, 1), "threshold order");
Reject(() => new ReserveConfig(10, 1, 10, 2, 2, 3, 1), "full reserve does not warn");
Reject(() => new ReserveConfig(10, 1, 4, 0, 2, 3, 1), "danger threshold positive");
Reject(() => new ReserveConfig(10, 1, 4, 2, 0, 3, 1), "zero exhausted grace prohibited");
Reject(() => new ReserveConfig(10, 1, 4, 2, 2, 0, 1), "pulse interval positive");
foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0d, -1d })
    Reject(() => new ReserveConfig(10, 1, 4, 2, 2, 3, invalid), "invalid pulse damage rejected");
Reject(() => new NutritionConfig(0, quickReserve, quickReserve), "tick duration positive");
Reject(() => new NutritionConfig(100, null!, quickReserve), "null food config rejected");
Reject(() => new NutritionConfig(100, quickReserve, null!), "null water config rejected");
Reject(() => NutritionRules.Create(1, 1, null!), "null creation config rejected");
Reject(() => NutritionRules.Advance(default, full, 1, quick, healthConfig), "default state cannot bypass grace initialization");
Reject(() => NutritionRules.Advance(initial, full, -1, quick, healthConfig), "negative elapsed rejected");
Reject(() => NutritionRules.Advance(initial, full, NutritionRules.MaxTicksPerAdvance + 1, quick, healthConfig), "oversized elapsed rejected, not truncated");
Reject(() => NutritionRules.Advance(initial, full, 1, quick, null!), "null health config rejected");
Reject(() => NutritionRules.Advance(initial, full, 1, null!, healthConfig), "null nutrition config rejected");
Reject(() => NutritionRules.Restore(initial, full, (NutritionResource)99, 1, quick), "invalid restoration target rejected");
var zero = NutritionRules.Advance(initial, full, 0, quick, healthConfig);
Check(SameState(zero.Nutrition, initial) && SameHealth(zero.Health, full) && zero.FoodDamagePulses == 0 && zero.WaterDamagePulses == 0, "zero elapsed no-op");
var exhausted = NutritionRules.Advance(NutritionRules.Create(1, 1, quick), full, 1, quick, healthConfig);
Check(exhausted.FoodWarning == ReserveWarning.Depleted && exhausted.WaterWarning == ReserveWarning.Depleted &&
    exhausted.Nutrition.Food.GraceRemainingTicks == 2 && SameHealth(exhausted.Health, full), "last supplied tick warns without exhausting grace or applying damage");
var grace = NutritionRules.Advance(exhausted.Nutrition, exhausted.Health, 2, quick, healthConfig);
Check(grace.Nutrition.Food.GraceRemainingTicks == 0 && grace.Nutrition.Food.PressureExposureTicks == 0 && SameHealth(grace.Health, full), "all exhausted grace ticks are damage-free");
var prePulse = NutritionRules.Advance(grace.Nutrition, grace.Health, 2, quick, healthConfig);
Check(prePulse.FoodDamagePulses == 0 && prePulse.WaterDamagePulses == 0 && prePulse.Nutrition.Water.PressureExposureTicks == 2, "periodic pressure waits full interval");
var pulse = NutritionRules.Advance(prePulse.Nutrition, prePulse.Health, 1, quick, healthConfig);
Check(pulse.FoodDamagePulses == 1 && pulse.WaterDamagePulses == 1 && pulse.Nutrition.Water.PressureExposureTicks == 0, "one due pulse per cause");
Check(Math.Abs(pulse.Health.Current.Chest - 84.8) < 1e-9 && Math.Abs(pulse.FoodChestDamageApplied + pulse.WaterChestDamageApplied - 0.2) < 1e-9, "pressure damages chest once per cause");
foreach (BodyRegion region in Enum.GetValues<BodyRegion>())
    if (region != BodyRegion.Chest) Check(pulse.Health.Current[region] == full.Current[region], "no whole-body damage " + region);
var depletedStomach = RegionalHealthRules.Damage(full, BodyRegion.Stomach, 1000);
var accelerated = NutritionRules.Advance(initial, depletedStomach, 1, quick, healthConfig);
Check(accelerated.Nutrition.Food.Amount == 7 && accelerated.Nutrition.Water.Amount == 7, "stomach multiplier 1.5 turns cost 2 into 3");
var noAcceleration = NutritionRules.Advance(initial, RegionalHealthRules.Damage(full, BodyRegion.LeftArm, 1000), 1, quick, healthConfig);
Check(noAcceleration.Nutrition.Food.Amount == 8 && noAcceleration.Nutrition.Water.Amount == 8, "other depleted regions do not increase drain");
var oneCost = new NutritionConfig(100, new ReserveConfig(10, 1, 4, 2, 2, 3, 1), quickReserve);
Check(NutritionRules.Advance(NutritionRules.Create(10, 10, oneCost), depletedStomach, 1, oneCost, healthConfig).Nutrition.Food.Amount == 8, "fractional cost rounds up per fixed tick");
var hugeScale = new RegionalHealthConfig(healthConfig.Maximum, 1, 1, 1, double.MaxValue);
Check(NutritionRules.Advance(initial, depletedStomach, 1, quick, hugeScale).Nutrition.Food.Amount == 0, "finite huge multiplier saturates without conversion overflow");
var restored = NutritionRules.Restore(prePulse.Nutrition, prePulse.Health, NutritionResource.Water, 1, quick);
Check(restored.Water.Amount == 1 && restored.Water.GraceRemainingTicks == 0 && restored.Water.PressureExposureTicks == 2 &&
    SameReserve(restored.Food, prePulse.Nutrition.Food), "restoring one resource preserves unpaid exposure and other reserve");
var usedRestore = NutritionRules.Advance(restored, prePulse.Health, 1, quick, healthConfig);
Check(usedRestore.WaterDamagePulses == 0 && usedRestore.Nutrition.Water.PressureExposureTicks == 2, "supplied tick pauses pressure carry");
Check(NutritionRules.Advance(usedRestore.Nutrition, usedRestore.Health, 1, quick, healthConfig).WaterDamagePulses == 1, "tiny refill cannot reset owed pulse");
Check(NutritionRules.Restore(restored, full, NutritionResource.Water, int.MaxValue, quick).Water.Amount == 10, "huge restoration saturates without overflow");
foreach (int amount in new[] { 0, -1, int.MinValue })
    Check(SameState(NutritionRules.Restore(restored, full, NutritionResource.Food, amount, quick), restored), "nonpositive restoration no-op");
var lethalReserve = new ReserveConfig(10, 1, 4, 2, 1, 1, 100);
var lethalConfig = new NutritionConfig(100, lethalReserve, lethalReserve);
var waterKills = NutritionRules.Advance(NutritionRules.Create(10, 1, lethalConfig), full, 100, lethalConfig, healthConfig);
Check(waterKills.Health.IsDead && waterKills.WaterDamagePulses == 1 && waterKills.FoodDamagePulses == 0 &&
    waterKills.Nutrition.Food.Amount == 8 && waterKills.WaterChestDamageApplied == 85, "water-first lethal pulse skips food in same tick and all later ticks");
var foodKills = NutritionRules.Advance(NutritionRules.Create(1, 10, lethalConfig), full, 100, lethalConfig, healthConfig);
Check(foodKills.Health.IsDead && foodKills.FoodDamagePulses == 1 && foodKills.WaterDamagePulses == 0 && foodKills.Nutrition.Water.Amount == 7, "food lethal pulse follows only this tick's water advance");
var bothDue = NutritionRules.Advance(NutritionRules.Create(0, 0, lethalConfig), full, 100, lethalConfig, healthConfig);
Check(bothDue.WaterDamagePulses == 1 && bothDue.FoodDamagePulses == 0 && bothDue.FoodChestDamageApplied == 0, "simultaneous lethal causes cannot double-apply after death");
var postmortem = NutritionRules.Advance(waterKills.Nutrition, waterKills.Health, NutritionRules.MaxTicksPerAdvance, lethalConfig, healthConfig);
Check(SameState(postmortem.Nutrition, waterKills.Nutrition) && SameHealth(postmortem.Health, waterKills.Health) &&
    postmortem.WaterDamagePulses == 0 && postmortem.FoodDamagePulses == 0, "terminal state is idempotent");
Check(SameState(NutritionRules.Restore(waterKills.Nutrition, waterKills.Health, NutritionResource.Water, 10, lethalConfig), waterKills.Nutrition), "dead actor cannot restore reserves");
var tiny = RegionalHealthRules.Create(new RegionValues(1, double.Epsilon, 1, 1, 1, 1, 1), healthConfig);
var enormousReserve = new ReserveConfig(int.MaxValue, int.MaxValue, 4, 2, 1, 1, double.MaxValue);
var enormous = new NutritionConfig(100, enormousReserve, enormousReserve);
var stillSupplied = NutritionRules.Advance(NutritionRules.Create(1, 1, enormous), tiny, 1, enormous, healthConfig);
Check(!stillSupplied.Health.IsDead && stillSupplied.WaterDamagePulses == 0, "tiny health and huge pulse do not bypass supplied tick");
Check(!NutritionRules.Advance(stillSupplied.Nutrition, tiny, 1, enormous, healthConfig).Health.IsDead, "huge pulse cannot bypass grace");
var enormousDeath = NutritionRules.Advance(stillSupplied.Nutrition, tiny, 2, enormous, healthConfig);
Check(enormousDeath.Health.IsDead && enormousDeath.WaterDamagePulses == 1 && enormousDeath.FoodDamagePulses == 0, "huge finite pulse kills only when due");

foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
{
    var corruptHealth = new RegionalHealthState(new RegionValues(1, 1, 1, value, 1, 1, 1));
    Reject(() => NutritionRules.Advance(initial, corruptHealth, 1, quick, healthConfig), "corrupt nontarget health rejects advance");
    Reject(() => NutritionRules.Restore(initial, corruptHealth, NutritionResource.Food, 1, quick), "corrupt nontarget health rejects restoration");
}
foreach (var invalid in new[] { new ReserveState(-1, 2, 0), new ReserveState(11, 2, 0), new ReserveState(0, -1, 0),
    new ReserveState(0, 3, 0), new ReserveState(0, 0, -1), new ReserveState(0, 0, 3), new ReserveState(0, 1, 1) })
{
    var corruptState = new NutritionState(invalid, initial.Water);
    Reject(() => NutritionRules.Advance(corruptState, full, 1, quick, healthConfig), "corrupt reserve rejected");
}
// Exact partition checks preserve fixed water/food ordering and floating subtraction sequence.
foreach (var config in new[] { quick, lethalConfig, standard })
foreach (var health in new[] { full, depletedStomach, RegionalHealthRules.Create(new RegionValues(1, 0.6, 1, 1, 1, 1, 1), healthConfig) })
foreach (int ticks in new[] { 0, 1, 2, 3, 5, 6, 7, 20, 101, 500, 4096 })
{
    var state = NutritionRules.Create(3, 1, config);
    var batch = NutritionRules.Advance(state, health, ticks, config, healthConfig);
    var single = NutritionRules.Advance(state, health, 0, config, healthConfig);
    int foodPulses = 0, waterPulses = 0;
    for (int i = 0; i < ticks; i++)
    {
        single = NutritionRules.Advance(single.Nutrition, single.Health, 1, config, healthConfig);
        foodPulses += single.FoodDamagePulses; waterPulses += single.WaterDamagePulses;
    }
    Check(SameState(batch.Nutrition, single.Nutrition) && SameHealth(batch.Health, single.Health) && batch.FoodWarning == single.FoodWarning &&
        batch.WaterWarning == single.WaterWarning && batch.FoodDamagePulses == foodPulses && batch.WaterDamagePulses == waterPulses,
        "batch equals individual ticks " + ticks + "/" + config.Food.Capacity + "/" + health.Current.Chest);
}
// Mixed restore / injury changes are split at the same event boundaries on both timelines.
var mixedBatch = NutritionRules.Advance(NutritionRules.Create(0, 0, quick), full, 4, quick, healthConfig);
var mixedSingle = mixedBatch;
foreach (int ticks in new[] { 1, 2, 7, 9, 13 })
{
    var a = NutritionRules.Restore(mixedBatch.Nutrition, mixedBatch.Health, NutritionResource.Water, 3, quick);
    var b = NutritionRules.Restore(mixedSingle.Nutrition, mixedSingle.Health, NutritionResource.Water, 3, quick);
    var batchHealth = ticks == 7 ? RegionalHealthRules.Damage(mixedBatch.Health, BodyRegion.Stomach, 1000) : mixedBatch.Health;
    var singleHealth = ticks == 7 ? RegionalHealthRules.Damage(mixedSingle.Health, BodyRegion.Stomach, 1000) : mixedSingle.Health;
    mixedBatch = NutritionRules.Advance(a, batchHealth, ticks, quick, healthConfig);
    mixedSingle = NutritionRules.Advance(b, singleHealth, 0, quick, healthConfig);
    for (int i = 0; i < ticks; i++) mixedSingle = NutritionRules.Advance(mixedSingle.Nutrition, mixedSingle.Health, 1, quick, healthConfig);
    Check(SameState(mixedBatch.Nutrition, mixedSingle.Nutrition) && SameHealth(mixedBatch.Health, mixedSingle.Health), "mixed refill partition preserves carried exposure " + ticks);
}
Console.WriteLine($"Nutrition checks passed: {checks}. Pure food/water rules only; oxygen composition and Unity/runtime integration not validated.");
