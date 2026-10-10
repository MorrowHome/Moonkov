using System.Text.Json;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Survival;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
const string MedicineId = "00000000-0000-0000-0000-000000000001";
const string ContainerId = "00000000-0000-0000-0000-000000000002";
var config = RegionalHealthConfig.Prototype();
var full = RegionalHealthRules.Full(config);
var depletedArm = RegionalHealthRules.Damage(full, BodyRegion.LeftArm, 60);
var wire = new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
string Snapshot(InventoryGraph graph) => JsonSerializer.Serialize(graph, wire);
bool SameHealth(RegionalHealthState a, RegionalHealthState b) => Enumerable.Range(0, 7)
    .All(i => a.Current[(BodyRegion)i].Equals(b.Current[(BodyRegion)i]));
InventoryGraph Fixture(int quantity = 2, string storage = "pockets")
{
    var graph = InventoryGraph.Create(starter: false);
    string parent = storage, region = storage == "pockets" ? "1" : "main";
    if (storage == "rig" || storage == "backpack")
    {
        graph.Items.Add(new InventoryItem { Id = ContainerId, Code = storage, Parent = "equipment",
            Region = storage == "rig" ? "ChestRig" : "Backpack" });
        parent = ContainerId;
        if (storage == "rig") region = "left";
    }
    graph.Items.Add(new InventoryItem { Id = MedicineId, Code = "medkit", Quantity = quantity, Parent = parent, Region = region });
    Check(graph.Validate() == InventoryError.None, "valid deterministic fixture " + storage);
    return graph;
}
void Rejected(InventoryGraph graph, RegionalHealthState health, BodyRegion region, int version,
    InventoryError expected, string message, string itemId = MedicineId, RegionalHealthConfig? selectedConfig = null)
{
    string before = Snapshot(graph);
    var result = TargetedMedicalRules.Apply(graph, itemId, version, health, region, selectedConfig ?? config,
        out var updated, out var healed);
    Check(result == expected && updated == null && SameHealth(healed, health) && Snapshot(graph) == before, message);
}
var inventory = Fixture();
string original = Snapshot(inventory);
var error = TargetedMedicalRules.Apply(inventory, MedicineId, inventory.Version, depletedArm, BodyRegion.LeftArm, config,
    out var first, out var healedArm);
Check(error == InventoryError.None && healedArm.Current.LeftArm == 40 && !healedArm.IsDead, "zero nonvital arm recovers without resurrection");
Check(first.Version == inventory.Version + 1 && first.Find(MedicineId).Quantity == 1 && first.Validate() == InventoryError.None, "one injector and one version committed in returned graph");
Check(Snapshot(inventory) == original && depletedArm.Current.LeftArm == 0, "success leaves both caller inputs unchanged");
foreach (BodyRegion region in Enum.GetValues<BodyRegion>())
    if (region != BodyRegion.LeftArm) Check(healedArm.Current[region] == full.Current[region], "only selected region changes " + region);
Rejected(first, healedArm, BodyRegion.LeftArm, inventory.Version, InventoryError.Stale, "replay against committed graph cannot consume twice");
Check(TargetedMedicalRules.Apply(first, MedicineId, first.Version, healedArm, BodyRegion.LeftArm, config,
    out var second, out var capped) == InventoryError.None && capped.Current.LeftArm == 60 && second.Find(MedicineId) == null &&
    second.Version == first.Version + 1, "second injector caps at region maximum and removes exhausted stack");
Check(first.Find(MedicineId).Quantity == 1, "returned inventory is a deep copy, not shared item mutation");
var fullTarget = Fixture();
Rejected(fullTarget, capped, BodyRegion.LeftArm, fullTarget.Version, InventoryError.Invalid, "full target does not consume");
var dead = RegionalHealthRules.Damage(full, BodyRegion.Head, 1000);
Rejected(inventory, dead, BodyRegion.Head, inventory.Version, InventoryError.Invalid, "dead head cannot be revived");
Rejected(inventory, dead, BodyRegion.LeftArm, inventory.Version, InventoryError.Invalid, "another full/nonvital target cannot bypass death");
var deadChest = RegionalHealthRules.Damage(full, BodyRegion.Chest, 1000);
Rejected(inventory, deadChest, BodyRegion.Chest, inventory.Version, InventoryError.Invalid, "dead chest cannot be revived");
foreach (var target in new[] { BodyRegion.Stomach, BodyRegion.LeftArm, BodyRegion.RightArm, BodyRegion.LeftLeg, BodyRegion.RightLeg })
{
    var zero = RegionalHealthRules.Damage(full, target, 1000);
    Check(TargetedMedicalRules.Apply(inventory, MedicineId, inventory.Version, zero, target, config, out _, out var recovered) == InventoryError.None &&
        recovered.Current[target] == 40 && !recovered.IsDead, "all depleted nonvital regions are treatable " + target);
}
foreach (var target in new[] { BodyRegion.Head, BodyRegion.Chest })
{
    var wounded = RegionalHealthRules.Damage(full, target, 5);
    Check(TargetedMedicalRules.Apply(inventory, MedicineId, inventory.Version, wounded, target, config, out _, out var recovered) == InventoryError.None &&
        recovered.Current[target] == full.Current[target], "living vital region can be treated " + target);
}
Rejected(inventory, depletedArm, (BodyRegion)(-1), inventory.Version, InventoryError.Invalid, "negative target rejected");
Rejected(inventory, depletedArm, (BodyRegion)7, inventory.Version, InventoryError.Invalid, "unknown target rejected");
Rejected(inventory, depletedArm, BodyRegion.LeftArm, inventory.Version, InventoryError.Missing, "missing injector rejected", "00000000-0000-0000-0000-000000000099");
Rejected(inventory, depletedArm, BodyRegion.LeftArm, inventory.Version - 1, InventoryError.Stale, "stale version rejected");
Rejected(inventory, depletedArm, BodyRegion.LeftArm, inventory.Version + 1, InventoryError.Stale, "future version rejected");
foreach (string storage in new[] { "stash", "backpack" })
{
    var inaccessible = Fixture(storage: storage);
    Rejected(inaccessible, depletedArm, BodyRegion.LeftArm, inaccessible.Version, InventoryError.Inaccessible, "inaccessible " + storage + " rejected");
}
var rig = Fixture(storage: "rig");
Check(TargetedMedicalRules.Apply(rig, MedicineId, rig.Version, depletedArm, BodyRegion.LeftArm, config, out var fromRig, out _) == InventoryError.None &&
    fromRig.Find(MedicineId).Quantity == 1, "equipped chest rig allows medical access");
var exhausted = Fixture(); exhausted.Find(MedicineId).Quantity = 0;
Rejected(exhausted, depletedArm, BodyRegion.LeftArm, exhausted.Version, InventoryError.Invalid, "malformed zero stack rejected");
var duplicate = Fixture(); duplicate.Items.Add(duplicate.Find(MedicineId).Clone());
Rejected(duplicate, depletedArm, BodyRegion.LeftArm, duplicate.Version, InventoryError.Invalid, "duplicate identity rejected");
var badItems = Fixture(); badItems.Items = null!;
Rejected(badItems, depletedArm, BodyRegion.LeftArm, badItems.Version, InventoryError.Invalid, "null item collection rejected");
var maxVersion = Fixture(); maxVersion.Version = int.MaxValue;
Rejected(maxVersion, depletedArm, BodyRegion.LeftArm, maxVersion.Version, InventoryError.Invalid, "version overflow cannot consume or wrap");
Check(TargetedMedicalRules.Apply(null!, MedicineId, 1, depletedArm, BodyRegion.LeftArm, config, out var nullGraph, out var unchanged) == InventoryError.Invalid &&
    nullGraph == null && SameHealth(unchanged, depletedArm), "null graph rejected");
Check(TargetedMedicalRules.Apply(inventory, MedicineId, inventory.Version, depletedArm, BodyRegion.LeftArm, null!, out nullGraph, out unchanged) == InventoryError.Invalid &&
    nullGraph == null && SameHealth(unchanged, depletedArm) && Snapshot(inventory) == original, "null config rejected");
// Internal construction deliberately probes corrupt-state boundaries in the shared-source check assembly.
foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, 999d })
{
    var malformed = new RegionalHealthState(new RegionValues(35, 85, 70, 0, bad, 65, 65));
    Rejected(inventory, malformed, BodyRegion.LeftArm, inventory.Version, InventoryError.Invalid, "malformed nontarget region cannot bypass validation");
}
var smallConfig = new RegionalHealthConfig(new RegionValues(30, 80, 60, 50, 50, 50, 50), 1, 1, 1, 1);
Rejected(inventory, depletedArm, BodyRegion.LeftArm, inventory.Version, InventoryError.Invalid, "config mismatch rejected", selectedConfig: smallConfig);
var lethalFirst = RegionalHealthRules.Damage(RegionalHealthRules.Damage(full, BodyRegion.Chest, 84), BodyRegion.Chest, 1);
Rejected(inventory, lethalFirst, BodyRegion.Chest, inventory.Version, InventoryError.Invalid, "damage-before-medical does not resurrect or consume");
var nonmedical = Fixture(); nonmedical.Find(MedicineId).Code = "cells";
Rejected(nonmedical, depletedArm, BodyRegion.LeftArm, nonmedical.Version, InventoryError.Inaccessible, "nonmedical item cannot heal");
var hugeConfig = new RegionalHealthConfig(new RegionValues(double.MaxValue, 85, 70, 60, 60, 65, 65), 1, 1, 1, 1);
var hugeHealth = RegionalHealthRules.Damage(RegionalHealthRules.Full(hugeConfig), BodyRegion.Head, double.MaxValue / 2);
Rejected(inventory, hugeHealth, BodyRegion.Head, inventory.Version, InventoryError.Invalid, "rounded-away healing cannot consume", selectedConfig: hugeConfig);
// Original scalar API remains present and unchanged, including its zero-health rejection.
var scalar = Fixture(); int scalarVersion = scalar.Version;
Check(scalar.UseMedical(MedicineId, scalarVersion, 80f, 100f, out float scalarHealth) == InventoryError.None && scalarHealth == 100f &&
    scalar.Find(MedicineId).Quantity == 1 && scalar.Version == scalarVersion + 1, "legacy scalar capped healing preserved");
string scalarBefore = Snapshot(scalar);
Check(scalar.UseMedical(MedicineId, scalar.Version, 0f, 100f, out scalarHealth) == InventoryError.Invalid && scalarHealth == 0f &&
    Snapshot(scalar) == scalarBefore, "legacy scalar zero-health behavior preserved");
Console.WriteLine($"Targeted medical checks passed: {checks}. Pure transaction preparation only; no RPC, Unity or live inventory integration validated.");
