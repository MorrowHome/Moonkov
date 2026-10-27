using System;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class AlienHarvestProductionChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Near(float value, float expected) => Math.Abs(value - expected) < .02f;

    [MenuItem("Moonkov/Aliens/Check Harvest and Food Rules")]
    public static void RunFromEditor() => Debug.Log(Run());

    public static string Run()
    {
        s_Checks = 0;
        var names = Enum.GetNames(typeof(AlienSpecies));
        Check(names.Length > 0 && AlienBiology.Organs.Length > 0, "species and organ catalogs are available");
        Check(AlienBiology.Organs.Select(x => x.Code).Distinct().Count() == AlienBiology.Organs.Length,
            "organ item codes are unique");
        Check(ReferenceEquals(AlienBiology.Species("StiltWalker"), AlienBiology.Species("Tentacle")),
            "the upright alien deliberately shares the tentacle heart");
        foreach (string species in names)
        {
            var organ = AlienBiology.Species(species);
            Check(organ != null && InventoryCatalog.Get(organ.Code) != null &&
                (organ.Species == species || species == "StiltWalker" && organ.Species == "Tentacle"),
                "registered physical organ for " + species);
            foreach (bool boss in new[] { false, true })
            {
                var loot = AlienHarvest.Create(boss, new System.Random(31), species);
                Check(loot.Validate() == InventoryError.None && loot.Count(organ.Code) == 1 &&
                    loot.Count("alien_tissue") == (boss ? 5 : 1) &&
                    (boss ? loot.Count("alien_raw_meat") == 4 && loot.Count("alien_core") == 1 && loot.Count("alien_neural_core") == 1 :
                        loot.Count("alien_raw_meat") >= 1 && loot.Count("alien_raw_meat") <= 2 && loot.Count("alien_core") == 0) &&
                    loot.Items.Where(i => i.Code.StartsWith("alien_")).All(i => i.FoundInRaid),
                    "valid " + (boss ? "boss" : "ordinary") + " remains for " + species);
            }
            var carried = InventoryGraph.Create(false, false);
            Check(carried.AddSupply(organ.Code, 1, InventoryCatalog.Pockets, true) == InventoryError.None,
                "organ fits the accessible player inventory for " + species);
            var item = carried.Items.Single(i => i.Code == organ.Code);
            var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
            health.Energy = 30; health.Hydration = 60; health.Satiety = 0;
            RaidHealth.Set(ref health, BodyPart.Chest, 50);
            Check(RaidNutrition.Consume(ref health, carried, item.Id, carried.Version) == InventoryError.None &&
                carried.Find(item.Id) == null && health.AlienFoodEffect == organ.Food.Effect &&
                Near(health.AlienFoodEffectSeconds, organ.Food.EffectSeconds), "food consumes the real organ for " + species);
            if (species == "Caterpillar")
                Check(health.ChestHealth > 50 && health.Energy > 30, "the nutrition sac heals and replenishes energy");
            if (species == "Slime")
            {
                var direct = health;
                Check(Near(RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 20, 1, RaidDamageKind.Bullet), 15),
                    "gel armor reduces actual combat damage");
                Check(Near(RaidHealth.Damage(ref direct, BodyPart.LeftArm, 20, 1), 20),
                    "gel armor does not reduce direct physiological damage");
            }
            float before = health.CurrentHealth;
            RaidNutrition.Tick(ref health, 2, 10000, 10000, 10000, 10000, 0, 0, 2);
            if (species == "Leech") Check(Near(health.CurrentHealth - before, 2), "regeneration heals living tissue over time");
            if (species == "Centipede") Check(Near(before - health.CurrentHealth, 1.2f), "venom power applies its own poison damage");
            if (organ.Food.EffectSeconds > 0)
            {
                RaidNutrition.Tick(ref health, 120, 10000, 10000, 10000, 10000, 0, 0, 3);
                Check(health.AlienFoodEffect == AlienFoodEffect.None && health.AlienFoodEffectSeconds == 0,
                    "food effect expires for " + species);
            }
        }
        CheckReducedHealthFood();
        bool rejected = false;
        try { AlienHarvest.Create(false, new System.Random(1), "missing-species"); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "unknown species cannot silently create the wrong organ");
        return "PASS " + s_Checks + " alien harvest and food checks";
    }

    private static void CheckReducedHealthFood()
    {
        var health = new PredictedPlayerGhost
        { ClashEnabled = true, ClashLife = ClashLife.Alive, ClashMaxHealthFraction = .5f };
        RaidHealth.Initialize(ref health);
        var carried = InventoryGraph.Create(false, false);
        Check(carried.AddSupply("alien_raw_meat", 2, InventoryCatalog.Pockets, true) == InventoryError.None,
            "meat fits inventory in reduced-health mode");
        var meat = carried.Items.Single(i => i.Code == "alien_raw_meat");
        int fullVersion = carried.Version;
        Check(!RaidNutrition.CanConsume(health, ConsumableCatalog.Get(meat.Code)),
            "healthy tissue at reduced caps does not count as injured");
        Check(RaidNutrition.Consume(ref health, carried, meat.Id, fullVersion) == InventoryError.Invalid &&
            carried.Version == fullVersion && meat.Quantity == 2,
            "full reduced-health player cannot waste a healing food");

        RaidHealth.Set(ref health, BodyPart.LeftLeg, 0);
        RaidHealth.Damage(ref health, BodyPart.Chest, 20, 1, physiological: true);
        Check(RaidNutrition.Consume(ref health, carried, meat.Id, fullVersion) == InventoryError.None &&
            meat.Quantity == 1 && Near(health.HeadHealth, 17.5f) && Near(health.ChestHealth, 42.5f) &&
            health.LeftLegHealth == 0, "meat heals injured tissue without reviving a destroyed limb");
        Check(RaidNutrition.Consume(ref health, carried, meat.Id, fullVersion) == InventoryError.Stale &&
            meat.Quantity == 1, "replayed meat use cannot consume another item");
        Check(RaidNutrition.Consume(ref health, carried, meat.Id, carried.Version) == InventoryError.Invalid &&
            meat.Quantity == 1, "a destroyed limb alone does not allow another food use");

        var regeneration = new PredictedPlayerGhost
        { ClashEnabled = true, ClashLife = ClashLife.Alive, ClashMaxHealthFraction = .5f };
        RaidHealth.Initialize(ref regeneration);
        RaidHealth.Set(ref regeneration, BodyPart.RightLeg, 0);
        RaidHealth.Damage(ref regeneration, BodyPart.Chest, 10, 2, physiological: true);
        regeneration.AlienFoodEffect = AlienFoodEffect.Regeneration;
        regeneration.AlienFoodEffectSeconds = 7;
        RaidNutrition.Tick(ref regeneration, 5, 10000, 10000, 10000, 10000, 0, 0, 3);
        Check(Near(regeneration.HeadHealth, 17.5f) && Near(regeneration.ChestHealth, 37.5f) &&
            regeneration.RightLegHealth == 0 && Near(regeneration.AlienFoodEffectSeconds, 2),
            "regeneration skips healthy capped tissue and destroyed limbs");
        RaidNutrition.Tick(ref regeneration, 5, 10000, 10000, 10000, 10000, 0, 0, 4);
        Check(Near(regeneration.ChestHealth, 39.5f) && regeneration.RightLegHealth == 0 &&
            regeneration.AlienFoodEffect == AlienFoodEffect.None && regeneration.AlienFoodEffectSeconds == 0,
            "regeneration heals only for its remaining duration");
    }
}
