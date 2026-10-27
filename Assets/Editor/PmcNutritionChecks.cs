using System;
using System.Linq;
using System.Runtime.Serialization;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class PmcNutritionChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Near(float a, float b) => Math.Abs(a - b) < .01f;

    [MenuItem("Moonkov/AI/Check Food and Water Rules")]
    public static void RunFromEditor() => Debug.Log(Run());

    [MenuItem("Moonkov/AI/Check Survival Item Conservation")]
    public static void RunStressFromEditor() => Debug.Log(RunStress());

    public static string Run()
    {
        s_Checks = 0;
        var inventory = InventoryGraph.Create(false);
        Check(inventory.AddSupply("medkit", 2, InventoryCatalog.Pockets, true) == InventoryError.None &&
            inventory.AddSupply("cells", 4, foundInRaid: true) == InventoryError.None &&
            PmcLifeSupport.TryEquip(inventory, false, false) &&
            inventory.AddSupply("oxygen_canister", 1, foundInRaid: true) == InventoryError.None &&
            inventory.AddSupply("ration", 1, foundInRaid: true) == InventoryError.None &&
            inventory.AddSupply("water", 1, foundInRaid: true) == InventoryError.None &&
            inventory.Validate() == InventoryError.None, "the full ordinary loadout fits real supplies and weight limits");
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        PmcProtectionLoadout.Initialize(ref health, inventory);
        var brain = (DollSingerEnemyBrain)FormatterServices.GetUninitializedObject(typeof(DollSingerEnemyBrain));
        brain.Inventory = inventory; brain.Action = PmcAction.Scavenge; brain.LastDamage = -100;
        Check(!PmcNutrition.Low(health, PmcNutrition.UseThreshold) && !PmcNutrition.NeedsDeparture(health, inventory),
            "a supplied, fully nourished raider does not need an action or departure");
        health.Energy = health.Hydration = 34; health.Satiety = 25;
        var water = PmcNutrition.Choose(health, inventory);
        Check(water?.Code == "water", "drinking takes priority when food and water are both low");
        string waterId = water.Id, rationId = inventory.Items.First(item => item.Code == "ration").Id;
        int version = inventory.Version;
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 10);
        Check(brain.NutritionUntil == 12 && brain.NutritionItemId == waterId && health.ConsumableUseSequence == 1 && inventory.Version == version &&
            inventory.Find(waterId) != null && Near(health.Hydration, 34), "starting a drink does not consume or restore early");
        Check(!PmcResourceActions.SafeWindow(brain, health, false, true, 10) &&
            PmcResourceActions.SafeWindow(brain, health, false, true, 10, continuingNutrition: true),
            "other supply actions cannot overlap an ongoing drink");
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 11.9);
        Check(inventory.Find(waterId) != null && Near(health.Hydration, 34) && health.ConsumableUseSequence == 1,
            "the full drinking time is required without repeating the sound");
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 12);
        Check(inventory.Find(waterId) == null && inventory.Version == version + 1 && Near(health.Hydration, 84) &&
            brain.NutritionUntil == 0, "completion consumes the real water and applies its actual effect");
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 12);
        Check(inventory.Version == version + 1, "a completed drink cannot be replayed");

        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 13);
        Check(brain.NutritionUntil == 16 && brain.NutritionItemId == rationId && health.ConsumableUseSequence == 2,
            "a ration uses the normal food duration and emits one new sound");
        PmcNutrition.Advance(brain, ref health, Vector3.right * .75f, true, 13.5);
        Check(brain.NutritionUntil == 16 && inventory.Find(rationId) != null, "the player's exact movement boundary is permitted");
        PmcNutrition.Advance(brain, ref health, Vector3.right * .751f, true, 14);
        Check(brain.NutritionUntil == 0 && inventory.Find(rationId) != null && Near(health.Energy, 34),
            "moving too far interrupts food without consuming it");
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 15);
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 2, 7, RaidDamageKind.Bullet);
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 16);
        Check(brain.NutritionUntil == 0 && inventory.Find(rationId) != null,
            "real tissue damage interrupts food even when safety is otherwise unchanged");

        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 17);
        RaidHealth.Damage(ref health, BodyPart.Chest, 1, 8, physiological: true);
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 20);
        Check(inventory.Find(rationId) == null && Near(health.Energy, 79),
            "ongoing physiological damage continues while food completes");
        Check(inventory.Validate() == InventoryError.None, "consuming food leaves a valid inventory");

        Check(inventory.AddSupply("ration", 1, inventory.Equipped("Backpack").Id, true) == InventoryError.None,
            "backpack food fixture");
        health.Energy = 20; health.Hydration = 80; health.Satiety = 0;
        var backpackFood = PmcNutrition.Choose(health, inventory);
        Check(backpackFood != null && backpackFood.Parent == inventory.Equipped("Backpack").Id,
            "food can be used from a carried backpack under the player rule");
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 21);
        var shieldBefore = health.ShieldHitSequence;
        health.ShieldEquipped = true; health.ShieldCharge = 50;
        EnergyShieldRules.Absorb(ref health, 5, 9, RaidDamageKind.Bullet);
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 22);
        Check(health.ShieldHitSequence == shieldBefore + 1 && brain.NutritionUntil == 0 && inventory.Find(backpackFood.Id) != null,
            "a shield-only hit interrupts without consuming the food");

        health.ShieldEquipped = false;
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 23);
        Check(inventory.AddSupply("dust", 1, inventory.Equipped("Backpack").Id) == InventoryError.None,
            "inventory version race fixture");
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 24);
        Check(brain.NutritionUntil == 0 && inventory.Find(backpackFood.Id) != null,
            "a changed inventory cancels without consuming a stale item");
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 25);
        PmcNutrition.Advance(brain, ref health, Vector3.zero, false, 26);
        Check(brain.NutritionUntil == 0 && inventory.Find(backpackFood.Id) != null,
            "danger or an incompatible action cancels without consuming the food");
        PmcNutrition.Advance(brain, ref health, Vector3.zero, true, 27);
        health.Oxygen = 0; health.BreathableAir = false;
        bool eligible = !PmcLifeSupport.Low(health, PmcLifeSupport.DepartureThreshold) &&
            PmcResourceActions.SafeWindow(brain, health, false, true, 28, continuingNutrition: true);
        PmcNutrition.Advance(brain, ref health, Vector3.zero, eligible, 28);
        var oxygen = PmcLifeSupport.FindAccessible(inventory);
        Check(brain.NutritionUntil == 0 && inventory.Find(backpackFood.Id) != null &&
            PmcLifeSupport.TryRefill(ref health, inventory, oxygen) && Near(health.Oxygen, 40),
            "critical oxygen interrupts food and consumes the real emergency supply first");

        health.Energy = 10; health.Hydration = 80; health.Satiety = 0;
        Check(PmcNutrition.Low(health, PmcNutrition.DangerThreshold) && !PmcNutrition.NeedsDeparture(health, inventory),
            "a critically hungry raider with usable food does not immediately leave");
        health.Satiety = 100;
        Check(PmcNutrition.NeedsDeparture(health, inventory), "an unusable food supply does not prevent departure");
        health.Satiety = 0; health.Energy = 80; health.Hydration = 10;
        Check(PmcNutrition.NeedsDeparture(health, inventory), "a critically thirsty raider without a drink plans to leave");
        Check(inventory.AddSupply("water", 1, inventory.Equipped("Backpack").Id, true) == InventoryError.None &&
            !PmcNutrition.NeedsDeparture(health, inventory), "real carried water prevents premature departure");
        brain.Action = PmcAction.Retreat;
        Check(!PmcResourceActions.SafeWindow(brain, health, false, true, 29) &&
            PmcResourceActions.SafeWindow(brain, health, false, true, 29, allowCriticalNutrition: true),
            "critical hunger or thirst can be treated at a safe withdrawal point");
        brain.Action = PmcAction.Extract; brain.ExtractionProgress = 1;
        Check(!PmcResourceActions.SafeWindow(brain, health, false, true, 29, allowCriticalNutrition: true),
            "timed food use does not pause an extraction already underway");
        brain.ExtractionProgress = 0; brain.Action = PmcAction.Scavenge;
        health.BreathableAir = true; health.Oxygen = 0;
        Check(PmcNutrition.Low(health, PmcNutrition.DangerThreshold), "food and water remain relevant in breathable rooms");
        var clash = health; clash.ProtectionInitialized = false;
        Check(!PmcNutrition.Low(clash, PmcNutrition.DangerThreshold) && !PmcNutrition.NeedsDeparture(clash, inventory),
            "non-simulated loadouts do not acquire nutrition departure rules");

        health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        PmcProtectionLoadout.Initialize(ref health, inventory); health.Energy = 0; health.StarvationSeconds = 10;
        brain.LastHealth = health.CurrentHealth; brain.LastDamage = -100;
        brain.SelfStatus = brain.ObservedSelfStatus = new TacticalServerWorld.Status();
        var before = health;
        RaidNutrition.Tick(ref health, 1, 900, 720, 600, 10, .5f, 1, 10, false);
        PmcStatusDamage.Record(brain.SelfStatus, before, health);
        PmcCombatPressure.Update(brain, health, 30, 0);
        Check(health.CurrentHealth < before.CurrentHealth && brain.LastDamage == -100,
            "real starvation damage is not mistaken for another attacker");
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 2, 11, RaidDamageKind.Bullet);
        PmcCombatPressure.Update(brain, health, 31, 0);
        Check(brain.LastDamage == 31, "a bullet during starvation still causes a combat reaction");

        InventoryGraph PocketFixture()
        {
            var graph = InventoryGraph.Create(false, false);
            for (int n = 1; n <= 3; n++) graph.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"),
                Code = "dust", Parent = InventoryCatalog.Pockets, Region = n.ToString() });
            return graph;
        }
        InventoryGraph LootFixture(string supply)
        {
            var graph = InventoryGraph.Create(false, false);
            graph.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
            if (graph.AddSupply("data_drive", 1, LootInventoryExchange.Root, true) != InventoryError.None ||
                graph.AddSupply(supply, 1, LootInventoryExchange.Root, true) != InventoryError.None)
                throw new InvalidOperationException("survival loot fixture");
            return graph;
        }
        inventory = PocketFixture(); var cache = LootFixture("ration");
        Check(PmcLootRules.TakeAvailable(inventory, cache, seekSurvivalSupplies: true) == 1 &&
            inventory.Count("ration", true) == 1 && cache.Count("data_drive") == 1,
            "an ordinary PMC reserves its last free slot for missing food");
        inventory = PocketFixture(); cache = LootFixture("oxygen_canister");
        Check(PmcLootRules.TakeAvailable(inventory, cache, seekSurvivalSupplies: true) == 1 &&
            inventory.Count("oxygen_canister", true) == 1 && cache.Count("data_drive") == 1,
            "an ordinary PMC can prioritize missing life support while scavenging");
        inventory = PocketFixture(); cache = LootFixture("ration");
        Check(PmcLootRules.TakeAvailable(inventory, cache) == 1 && inventory.Count("data_drive", true) == 1 &&
            cache.Count("ration") == 1, "tutorial and other callers retain their existing loot priority");
        var wrapped = new PredictedPlayerGhost { ConsumableUseSequence = uint.MaxValue };
        RaidConsumableAcoustics.Mark(ref wrapped); uint previous = uint.MaxValue;
        Check(wrapped.ConsumableUseSequence == 1 && MoonkovPlayerAudio.NewTick(wrapped.ConsumableUseSequence, ref previous) &&
            !MoonkovPlayerAudio.NewTick(wrapped.ConsumableUseSequence, ref previous), "wrapped or replayed sound sequences behave correctly");
        var silent = new PredictedPlayerGhost(); RaidHealth.Initialize(ref silent);
        var idle = (DollSingerEnemyBrain)FormatterServices.GetUninitializedObject(typeof(DollSingerEnemyBrain));
        idle.Inventory = InventoryGraph.Create(false);
        PmcNutrition.Advance(idle, ref silent, Vector3.zero, true, 1);
        Check(silent.ConsumableUseSequence == 0 && idle.NutritionUntil == 0, "a rejected use produces no sound");
        return "PASS " + s_Checks + " PMC food, water and interruption checks";
    }

    public static string RunStress()
    {
        s_Checks = 0;
        int foodUsed = 0, drinkUsed = 0, oxygenUsed = 0, interrupted = 0;
        for (int seed = 0; seed < 500; seed++)
        {
            var random = new System.Random(seed * 7907 + 31);
            var inventory = InventoryGraph.Create(false);
            Check(PmcLifeSupport.TryEquip(inventory, false, false) &&
                inventory.AddSupply("oxygen_canister", 3, InventoryCatalog.Pockets, true) == InventoryError.None &&
                inventory.AddSupply("ration", 3, inventory.Equipped("Backpack").Id, true) == InventoryError.None &&
                inventory.AddSupply("water", 3, inventory.Equipped("Backpack").Id, true) == InventoryError.None,
                "generated survival supplies must fit");
            var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
            PmcProtectionLoadout.Initialize(ref health, inventory);
            health.Energy = 20 + random.Next(16); health.Hydration = 20 + random.Next(16);
            health.Oxygen = 10 + random.Next(26); health.Satiety = 0;
            string suitId = inventory.Equipped("Helmet").Id;
            var brain = (DollSingerEnemyBrain)FormatterServices.GetUninitializedObject(typeof(DollSingerEnemyBrain));
            var status = new TacticalServerWorld.Status();
            brain.Inventory = inventory; brain.Action = PmcAction.Scavenge; brain.LastDamage = -100;
            brain.LastHealth = health.CurrentHealth; brain.SelfStatus = brain.ObservedSelfStatus = status;
            int initialFoodUsed = foodUsed, initialDrinkUsed = drinkUsed, initialOxygenUsed = oxygenUsed;
            for (int step = 1; step <= 90 && health.CurrentHealth > 0; step++)
            {
                bool moving = random.Next(3) != 0;
                health.ControllerState.Sprinting = moving && random.Next(3) == 0;
                var atmosphere = random.Next(8) == 0 ? new AtmosphereSample("room", 101.325f, .21f, true) : default;
                PmcLifeSupport.Tick(ref health, inventory, status, atmosphere, OxygenParameters.Default, 1, (uint)step, 0, moving, flush: false);
                var beforeNutrition = health;
                RaidNutrition.Tick(ref health, 1, 900, 720, 600, 10, .5f, 1, (uint)step, moving);
                PmcStatusDamage.Record(status, beforeNutrition, health);
                bool shot = random.Next(12) == 0;
                if (shot) RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1 + random.Next(5), (uint)step, RaidDamageKind.Bullet);
                PmcCombatPressure.Update(brain, health, step, 1);
                if (health.CurrentHealth <= 0) break;
                bool safe = !shot && step - brain.LastDamage >= 3 && random.Next(6) != 0;
                if (brain.NutritionUntil > 0 && random.Next(14) == 0)
                    inventory.AddSupply("dust", 1, inventory.Equipped("Backpack").Id);
                bool eligible = !PmcLifeSupport.Low(health, PmcLifeSupport.DepartureThreshold) &&
                    PmcResourceActions.SafeWindow(brain, health, false, safe, step, continuingNutrition: true, allowCriticalNutrition: true);
                int foodBefore = inventory.Count("ration", true), waterBefore = inventory.Count("water", true), version = inventory.Version;
                double deadline = brain.NutritionUntil;
                PmcNutrition.Advance(brain, ref health, Vector3.zero, eligible, step);
                int foodAfter = inventory.Count("ration", true), waterAfter = inventory.Count("water", true);
                int consumed = foodBefore - foodAfter + waterBefore - waterAfter;
                Check(consumed >= 0 && consumed <= 1 && (consumed == 0 || eligible && deadline > 0 &&
                    step >= deadline && inventory.Version == version + 1), "food may be consumed only once at a valid completion");
                if (foodBefore > foodAfter) foodUsed++;
                if (waterBefore > waterAfter) drinkUsed++;
                if (deadline > 0 && brain.NutritionUntil == 0 && consumed == 0) interrupted++;
                if (PmcLifeSupport.Low(health, PmcLifeSupport.RefillThreshold) &&
                    PmcResourceActions.SafeWindow(brain, health, false, safe, step, allowCriticalOxygen: true))
                {
                    var bottle = PmcLifeSupport.FindAccessible(inventory);
                    int oxygenBefore = inventory.Count("oxygen_canister", true);
                    if (PmcLifeSupport.TryRefill(ref health, inventory, bottle))
                    {
                        oxygenUsed++;
                        Check(inventory.Count("oxygen_canister", true) == oxygenBefore - 1,
                            "each refill spends one real canister");
                    }
                }
                Check(float.IsFinite(health.Energy) && health.Energy >= 0 && health.Energy <= 100 &&
                    float.IsFinite(health.Hydration) && health.Hydration >= 0 && health.Hydration <= 100 &&
                    float.IsFinite(health.Oxygen) && health.Oxygen >= 0 && health.Oxygen <= 100,
                    "all survival reserves remain finite and bounded");
                if (step % 15 == 0) Check(inventory.Validate() == InventoryError.None, "mixed actions leave a valid inventory");
            }
            EnergyShieldEquipment.Flush(ref health, inventory);
            var corpse = LootInventoryExchange.DropOnDeath(inventory);
            Check(corpse.Find(suitId) != null && Near(corpse.Find(suitId).SuitOxygen, health.Oxygen) &&
                corpse.Count("ration") == 3 - (foodUsed - initialFoodUsed) &&
                corpse.Count("water") == 3 - (drinkUsed - initialDrinkUsed) &&
                corpse.Count("oxygen_canister") == 3 - (oxygenUsed - initialOxygenUsed) &&
                corpse.Validate() == InventoryError.None, "death preserves the same suit and exactly the remaining supplies");
            var player = InventoryGraph.Create(false);
            Check(LootInventoryExchange.TryApply(player, corpse, new InventoryCommand { Operation = InventoryOperation.Move,
                ExpectedVersion = player.Version, ItemId = suitId, Parent = InventoryCatalog.Equipment, Region = "Helmet" }, corpse.Version) == InventoryError.None &&
                player.Find(suitId) != null && corpse.Find(suitId) == null && Near(player.Find(suitId).SuitOxygen, health.Oxygen),
                "the player can recover the exact remaining suit resource without duplication");
        }
        return "PASS " + s_Checks + " checks across 500 mixed survival and corpse cycles; food=" + foodUsed +
            ", water=" + drinkUsed + ", oxygen=" + oxygenUsed + ", interrupted=" + interrupted;
    }
}
