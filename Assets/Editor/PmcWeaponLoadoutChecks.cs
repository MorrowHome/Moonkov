using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public static class PmcWeaponLoadoutChecks
{
    [MenuItem("Moonkov/AI/Check Weapon Variety")]
    public static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run weapon loadout checks in Edit Mode.");
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        bool Close(string code) => code == "shotgun" || code == "cicada" || code == "thorn";
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        Check(registry != null, "The production weapon registry is missing.");
        var loadout = new PmcWeaponLoadout();
        var random = new Random(47891);
        var combinations = new HashSet<string>();
        var expected = new HashSet<string> { "rifle", "shotgun", "sniper", "cicada", "trine", "aegis", "vesper", "thorn", "rondo", "nocturne" };
        for (int batch = 0; batch < 3; batch++)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 10; i++)
            {
                var graph = loadout.Create(registry, false, false, ref random);
                var primary = graph.Equipped("Primary"); var secondary = graph.Equipped("Secondary");
                Check(graph.Validate() == InventoryError.None && graph.CarriedWeight < InventoryCatalog.CarryWeightLimit,
                    "A generated loadout is invalid or overweight.");
                Check(seen.Add(primary.Code), "A primary repeated before every available weapon was used.");
                Check(primary.Code != secondary.Code && Close(primary.Code) != Close(secondary.Code),
                    "The second weapon must cover a different range.");
                Check(graph.Equipped("Pistol")?.Code == "pistol" && graph.Equipped("Backpack") != null && graph.Equipped("ChestRig") != null,
                    "Variety removed the sidearm or storage equipment.");
                foreach (string slot in new[] { "Primary", "Secondary", "Pistol" })
                {
                    var item = graph.Equipped(slot);
                    var weapon = registry.GetWeaponData(DollSingerWeapons.WeaponId(item.Code));
                    Check(weapon != null && weapon.Behavior == ProjectileBehavior.DirectDamage && item.LoadedAmmo == weapon.MagazineSize,
                        "Loadout contains an unsupported weapon or another weapon's ammunition count.");
                }
                combinations.Add(primary.Code + "/" + secondary.Code);

                var health = new PredictedPlayerGhost { EquippedWeaponID = DollSingerWeapons.None };
                RaidHealth.Initialize(ref health);
                DollSingerWeapons.SyncEquipment(ref health, graph, registry);
                Check(health.PrimaryWeaponID == DollSingerWeapons.WeaponId(primary.Code) &&
                    health.SecondaryWeaponID == DollSingerWeapons.WeaponId(secondary.Code) && health.EquippedWeaponItem.ToString() == primary.Id,
                    "The real inventory did not bind its weapons to the actor.");
                Check(health.HaloEnergy == HaloEnergyRules.Capacity && health.CurrentAmmo > 0,
                    "A varied loadout must start with the same shared energy budget.");
                health.WeaponEquipRemaining = 0; health.WeaponRaiseProgress = 1; health.WeaponCooldown = 100;
                var held = registry.GetWeaponData(health.EquippedWeaponID);
                Check(HaloModuleFire.TryFire(ref health, held, true, true, false), "The actual firing rule rejected " + primary.Code);
                HaloEnergyRules.CommitShot(ref health, held);
                float energy = health.HaloEnergy;
                Check(Mathf.Abs(energy - (HaloEnergyRules.Capacity - held.EnergyPerRound)) < .001f,
                    "Firing a varied weapon did not spend the normal shared energy.");
                var input = new PlayerInput(); input.SetFlag(PlayerInput.InputFlag.EquipRevolver, true);
                Check(DollSingerWeapons.TryEquip(ref health, input, registry), "The real secondary weapon could not be selected.");
                DollSingerWeapons.SyncEquipment(ref health, graph, registry);
                Check(health.EquippedWeaponID == DollSingerWeapons.WeaponId(secondary.Code) && health.HaloEnergy == energy,
                    "Switching changed weapon identity or refilled the shared energy.");

                var loot = new RaidLootContainers();
                int corpse = loot.Drop(graph, Unity.Mathematics.float3.zero);
                Check(graph.Find(primary.Id) == null && loot.Containers[corpse].Find(primary.Id)?.Code == primary.Code &&
                    loot.Containers[corpse].Find(secondary.Id)?.Code == secondary.Code && loot.Containers[corpse].Validate() == InventoryError.None,
                    "Death did not transfer the actual varied weapons to the corpse.");
                var decoded = RaidInventoryTransport.Decode(RaidInventoryTransport.Encode(loot.Containers[corpse]));
                Check(decoded.Find(primary.Id)?.Code == primary.Code && decoded.Find(secondary.Id)?.Code == secondary.Code,
                    "Corpse inventory serialization changed the weapons.");
            }
            Check(seen.SetEquals(expected), "The shuffled bag did not include all ten direct-fire long guns.");
        }
        Check(combinations.Count > 10, "Secondary weapons stayed tied to fixed primary presets.");

        loadout.Reset(); random = new Random(1973);
        var first = loadout.Create(registry, false, false, ref random);
        loadout.Reset(); random = new Random(1973);
        var replay = loadout.Create(registry, false, false, ref random);
        Check(first.Equipped("Primary").Code == replay.Equipped("Primary").Code && first.Equipped("Secondary").Code == replay.Equipped("Secondary").Code,
            "Map reset retained a previous weapon bag.");
        foreach (bool tutorial in new[] { false, true })
        {
            uint before = random.state;
            var graph = loadout.Create(registry, tutorial, !tutorial, ref random);
            Check(graph.Equipped("Primary").Code == "rifle" && graph.Equipped("Secondary").Code == "shotgun" &&
                graph.Equipped("Pistol").Code == "pistol" && random.state == before,
                "Tutorial or bounty mode consumed the raid weapon bag or changed its starter kit.");
        }

        var limited = ScriptableObject.CreateInstance<WeaponRegistry>();
        limited.Weapons = Enumerable.Repeat<WeaponData>(null, 15).ToList();
        try
        {
            limited.Weapons[(int)DollSingerWeapons.Halo] = registry.GetWeaponData(DollSingerWeapons.Halo);
            limited.Weapons[(int)DollSingerWeapons.Sniper] = registry.GetWeaponData(DollSingerWeapons.Sniper);
            loadout.Reset();
            for (int i = 0; i < 4; i++)
            {
                var graph = loadout.Create(limited, false, false, ref random);
                Check(graph.Validate() == InventoryError.None && graph.Equipped("Primary").Code != graph.Equipped("Secondary").Code &&
                    graph.Equipped("Pistol") == null, "Missing close-range weapons or sidearm did not use the available fallback.");
            }
            limited.Weapons[(int)DollSingerWeapons.Sniper] = null; loadout.Reset();
            var single = loadout.Create(limited, false, false, ref random);
            Check(single.Equipped("Primary").Code == "rifle" && single.Equipped("Secondary") == null && single.Validate() == InventoryError.None,
                "A single available gun created a duplicate or unavailable secondary.");
            limited.Weapons[(int)DollSingerWeapons.Halo] = null; loadout.Reset();
            var empty = loadout.Create(limited, false, false, ref random);
            Check(empty.Equipped("Primary") == null && empty.Equipped("Secondary") == null && empty.Equipped("Pistol") == null &&
                empty.Validate() == InventoryError.None, "An empty registry produced fabricated weapons.");
        }
        finally { UnityEngine.Object.DestroyImmediate(limited); }
        return "PASS " + checks + " PMC weapon variety checks: ten-weapon bags, complementary kits, real equipment/fire/energy/death, reset and missing-asset fallbacks.";
    }
}
