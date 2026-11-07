using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using BodyPart = Unity.MP_FPS.BodyPart;
using UnityEngine.Localization.Tables;
using Newtonsoft.Json.Linq;

public static class HaloEnergyChecks
{
    [MenuItem("Tools/Moonkov/Check Shared Halo Energy")]
    public static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
        bool Near(float a, float b) => Mathf.Abs(a - b) < .002f;
        var rifle = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/HaloWeapon.asset");
        var shotgun = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/ShotgunWeapon.asset");
        var registry = ScriptableObject.CreateInstance<WeaponRegistry>();
        registry.Weapons = new System.Collections.Generic.List<WeaponData> { null, null, rifle, null, shotgun };
        try
        {
            var graph = InventoryGraph.Create(false, false);
            var primary = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "rifle", Parent = "equipment", Region = "Primary", LoadedAmmo = 0 };
            var secondary = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "shotgun", Parent = "equipment", Region = "Secondary", LoadedAmmo = 4 };
            graph.Items.Add(primary); graph.Items.Add(secondary);
            graph.AddSupply("cells", 2, "pockets");
            primary = graph.Find(primary.Id); secondary = graph.Find(secondary.Id);
            var state = new PredictedPlayerGhost { CurrentHealth = 440, MaxHealth = 440, EquippedWeaponSlot = 1 };
            RaidHealth.Initialize(ref state);
            DollSingerWeapons.SyncEquipment(ref state, graph, registry); state.WeaponEquipRemaining = 0;
            Check(Near(state.HaloEnergy, 100) && state.HaloEquipped, "Spawn starts with one full pool despite legacy empty ammo");
            HaloEnergyRules.CommitShot(ref state, rifle);
            Check(Near(state.HaloEnergy, 97) && graph.CellEnergy() == 200, "Shot spends pool energy, not a battery");
            float health = state.CurrentHealth;
            RaidHealth.CombatDamage(ref state, BodyPart.Chest, 10, 1, RaidDamageKind.Bullet);
            Check(Near(state.HaloEnergy, 97) && state.CurrentHealth < health, "No passive defense");
            HaloEnergyRules.SetDefense(ref state, true);
            Check(state.HaloDefending && state.HaloDefenseHeld, "Held defense activates");
            health = state.CurrentHealth;
            RaidHealth.CombatDamage(ref state, BodyPart.Chest, 20, 2, RaidDamageKind.Bullet);
            Check(Near(state.HaloEnergy, 77) && Near(state.CurrentHealth, health), "Defense spends the same energy as the shot");
            Check(state.ShieldHitSequence == 1, "One hit produces one feedback event");
            state.WeaponCooldown = 100; state.WeaponRaiseProgress = 1;
            var fire = new PlayerInput(); fire.SetFlag(PlayerInput.InputFlag.Shoot, true); fire.SetFlag(PlayerInput.InputFlag.Aim, true);
            Check(!HaloModuleFire.TryFire(ref state, rifle, fire, false, 1), "Defense blocks firing");
            state.HaloPendingShot = true; state.BurstRemaining = 2;
            HaloEnergyRules.SetDefense(ref state, true);
            Check(!state.HaloPendingShot && state.BurstRemaining == 0, "Defense clears queued and burst shots");
            HaloEnergyRules.Tick(ref state, 2);
            Check(Near(state.HaloEnergy, 77), "Held defense respects the delay after a hit");
            HaloEnergyRules.SetDefense(ref state, true); HaloEnergyRules.Tick(ref state, 2);
            Check(Near(state.HaloEnergy, 87) && state.HaloDefending, "Held defense recovers after the damage delay");
            HaloEnergyRules.Spend(ref state, 10);
            RaidHealth.CombatDamage(ref state, BodyPart.Chest, 90, 3, RaidDamageKind.Explosion);
            Check(Near(state.HaloEnergy, 0) && Near(health - state.CurrentHealth, 13), "Overflow reaches the body exactly once");
            Check(!state.HaloDefending && state.HaloDefenseBroken && state.ShieldBreakSequence == 1, "Exhaustion breaks defense once");
            HaloEnergyRules.SetDefense(ref state, true); HaloEnergyRules.Tick(ref state, 4);
            HaloEnergyRules.SetDefense(ref state, true);
            Check(!state.HaloDefending && Near(state.HaloEnergy, 10), "Broken defense recovers without flickering back on while held");
            HaloEnergyRules.SetDefense(ref state, false); HaloEnergyRules.SetDefense(ref state, true);
            Check(state.HaloDefending, "Release and repress re-arms the recovered defense");
            HaloEnergyRules.SetDefense(ref state, false); HaloEnergyRules.Spend(ref state, 10);
            HaloEnergyRules.Tick(ref state, 2);
            Check(Near(state.HaloEnergy, 0), "Actual energy spending restarts the recovery delay");
            HaloEnergyRules.Tick(ref state, 2);
            Check(Near(state.HaloEnergy, 10), "Large tick recovers only time beyond the delay");
            HaloEnergyRules.Tick(ref state, float.NaN); HaloEnergyRules.Tick(ref state, -10);
            Check(Near(state.HaloEnergy, 10), "Invalid time cannot create energy");
            HaloEnergyRules.Tick(ref state, 100);
            Check(Near(state.HaloEnergy, 100), "Recovery clamps to capacity");
            state.HaloEnergy = 37;
            state.EquippedWeaponSlot = 2;
            DollSingerWeapons.SyncEquipment(ref state, graph, registry); state.WeaponEquipRemaining = 0;
            Check(Near(state.HaloEnergy, 37) && state.CurrentAmmo == 3, "Switching changes cost, not energy");
            HaloEnergyRules.CommitShot(ref state, shotgun);
            Check(Near(state.HaloEnergy, 25), "Shotgun pays once per blast");
            graph.Items.Remove(primary); graph.Items.Remove(secondary);
            HaloEnergyRules.Synchronize(ref state, graph); HaloEnergyRules.Tick(ref state, 10);
            Check(!state.HaloEquipped && Near(state.HaloEnergy, 25), "Unequipped halos cannot regenerate");
            graph.Items.Add(primary); graph.Items.Add(secondary); HaloEnergyRules.Synchronize(ref state, graph);
            Check(Near(state.HaloEnergy, 25), "Re-equipping cannot refill");
            state.WeaponEquipRemaining = 0;
            var cell = graph.Items.First(i => i.Code == "cells");
            int version = graph.Version;
            Check(HaloEnergyInventory.Recharge(ref state, graph, cell.Id, version - 1) == InventoryError.Stale && Near(state.HaloEnergy, 25) && graph.CellEnergy() == 200, "Stale battery requests do nothing");
            Check(HaloEnergyInventory.Recharge(ref state, graph, cell.Id, version, shotgun) == InventoryError.None, "Accessible battery can be used");
            Check(Near(state.HaloEnergy, 25) && graph.CellEnergy() == 125 && Near(state.HaloRechargeEnergy,75) &&
                state.ControllerState.IsReloadingState && state.ReloadTimer >= .5f, "Battery is reserved while energy waits for the charge timeline");
            Check(!HaloEnergyInventory.RechargeFirst(ref state,graph) && graph.CellEnergy()==125, "Repeated requests cannot spend another cell while charging");
            Check(!HaloModuleFire.TryFire(ref state,shotgun,fire,false,1), "Charging blocks shooting even with energy remaining");
            float duration=state.ReloadTimer;
            HaloReload.Tick(ref state,shotgun,duration/2);
            HaloEnergyRules.Tick(ref state,30);
            Check(Near(state.HaloEnergy,25) && Near(HaloReload.Progress(state,shotgun),.5f), "Halfway charge cannot fire reserved or passively regenerated energy");
            var chargingFeedback=new HaloChargeFeedback();
            Check(chargingFeedback.Update(state,shotgun,graph,true,0).Notice==HaloChargeNotice.Charging, "Shared-energy HUD exposes charge progress");
            HaloReload.Tick(ref state,shotgun,duration/2);
            Check(Near(state.HaloEnergy, 100) && graph.CellEnergy() == 125 && state.HaloRechargeSequence == 1 &&
                !state.ControllerState.IsReloadingState && state.HaloRechargeEnergy==0, "Completion transfers exactly once and preserves unused charge");
            HaloReload.Tick(ref state,shotgun,10);
            Check(state.HaloRechargeSequence==1 && Near(state.HaloEnergy,100), "Repeated completion cannot duplicate energy");
            Check(HaloEnergyInventory.Recharge(ref state, graph, cell.Id, graph.Version) != InventoryError.None && graph.CellEnergy() == 125, "Full pool consumes nothing");
            state.HaloEnergy = 0;
            Check(HaloEnergyInventory.RechargeFirst(ref state, graph, shotgun) && Near(state.HaloEnergy, 0) &&
                Near(state.HaloRechargeEnergy,25) && graph.CellEnergy() == 100, "One use reserves the remaining partial cell only");
            HaloReload.Tick(ref state,shotgun,state.ReloadTimer);
            HaloEnergyRules.SetDefense(ref state, true);
            Check(HaloEnergyInventory.RechargeFirst(ref state, graph,shotgun) && graph.CellEnergy() == 25, "Held defense allows a timed battery charge");
            HaloEnergyRules.SetDefense(ref state,true);
            Check(state.HaloDefending && state.ControllerState.IsReloadingState, "Timed charge retains the held barrier");
            var simultaneous=new PlayerInput(); simultaneous.SetFlag(PlayerInput.InputFlag.HaloDefense,true); simultaneous.SetFlag(PlayerInput.InputFlag.Reload,true);
            Check(simultaneous.Sanitized().HaloDefense && simultaneous.Sanitized().Reload, "Defense and recharge survive command sanitization together");
            var deadCharge=state; deadCharge.CurrentHealth=0;
            HaloReload.Tick(ref deadCharge,shotgun,deadCharge.ReloadTimer);
            Check(Near(deadCharge.HaloEnergy,25) && deadCharge.HaloRechargeEnergy==0, "Death cannot complete a reserved energy transfer");
            var canceled=state;
            HaloEnergyRules.Synchronize(ref canceled,InventoryGraph.Create(false,false));
            Check(canceled.HaloRechargeEnergy==0 && !canceled.ControllerState.IsReloadingState, "Losing the halo cancels its pending charge");
            HaloReload.Tick(ref state,shotgun,state.ReloadTimer);
            state.HaloEnergy=25;
            HaloEnergyRules.SetDefense(ref state, false); state.ConsumingSupply = true;
            Check(!HaloEnergyInventory.RechargeFirst(ref state, graph), "Battery cannot overlap a supply action");
            state.ConsumingSupply = false;
            cell = graph.Items.First(i => i.Code == "cells"); cell.Parent = "stash"; cell.Region = "";
            Check(!HaloEnergyInventory.RechargeFirst(ref state, graph), "Stash battery is inaccessible");
            cell.Parent = "pockets"; cell.Region = "main";
            state.CurrentHealth = 0;
            Check(!HaloEnergyInventory.RechargeFirst(ref state, graph), "Dead actor cannot spend a battery");
            HaloEnergyRules.Tick(ref state, 20);
            Check(Near(state.HaloEnergy, 25) && !state.HaloDefending, "Dead actor does not regenerate");
            state.CurrentHealth = 100; state.HaloEnergy = 2; state.WeaponCooldown = 100; state.WeaponRaiseProgress = 1; state.EquippedWeaponID = DollSingerWeapons.Halo;
            Check(!HaloModuleFire.TryFire(ref state, rifle, true, true, false), "Insufficient energy blocks a whole shot");
            state.HaloEnergy = 30; state.WeaponCooldown = 100; state.WeaponRaiseProgress = 1;
            fire.SetFlag(PlayerInput.InputFlag.HaloDefense, true);
            Check(!HaloModuleFire.TryFire(ref state, rifle, fire, false, 1), "Same input cannot fire and defend");
            Check(fire.Sanitized().HaloDefense, "Defense survives input sanitization");
            fire.SetFlag(PlayerInput.InputFlag.HaloDefense, false);
            Check(HaloModuleFire.TryFire(ref state, rifle, fire, false, 1), "Release permits a fresh shot");
            var generator = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "energy_shield_generator", Parent = "equipment", Region = "Shield", ShieldCharge = 83, ShieldEnergy = 42 };
            graph.Items.Add(generator); EnergyShieldEquipment.Synchronize(ref state, graph); EnergyShieldEquipment.Flush(ref state, graph);
            Check(Near(generator.ShieldCharge, 83) && Near(generator.ShieldEnergy, 42) && Near(state.HaloEnergy, 30), "Legacy generator cannot inject or drain pool energy");
            Check(!PmcProtectionLoadout.ShouldEquip(false, false, 0) && !PmcProtectionLoadout.CanUseLootedShield(generator), "NPC no longer spawns or equips generators");
            Check(!ShopCatalog.Offers.Any(o => o.Code == "energy_shield_generator" && o.BuyDust >= 0), "Generator no longer sold");
            var capacitor = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "halo_capacitor", Parent = primary.Id, Region = "Storage" };
            graph.Items.Add(capacitor); HaloEnergyRules.Synchronize(ref state, graph);
            Check(Near(HaloEnergyRules.Maximum(state), 150) && Near(state.HaloEnergy, 30), "Capacity upgrade grants room, not free charge");
            state.EquippedWeaponSlot = 1; DollSingerWeapons.SyncEquipment(ref state, graph, registry);
            state.WeaponEquipRemaining = 0; state.ReloadTargetAmmo = 100; state.ControllerState.IsReloadingState = true;
            DollSingerWeapons.CompleteReload(ref state, rifle);
            Check(Near(state.HaloEnergy, 30), "Legacy reload completion cannot generate energy");
            state.HaloEnergy = 140; graph.Items.Remove(capacitor); HaloEnergyRules.Synchronize(ref state, graph);
            Check(Near(state.HaloEnergy, 100), "Removing capacity clamps excess energy");
            graph.Items.Remove(primary); graph.Items.Remove(secondary); HaloEnergyRules.Synchronize(ref state, graph);
            HaloEnergyRules.SetDefense(ref state, true);
            Check(!state.HaloDefending, "Legacy generator alone cannot defend");
            var feedback = new HaloChargeFeedback();
            state.HaloEquipped = true; state.InventoryWeapons = true; state.EquippedWeaponID = DollSingerWeapons.Halo;
            feedback.Update(state, rifle, graph, true, 0); state.HaloRechargeSequence++;
            Check(feedback.Update(state, rifle, graph, true, .1f).Notice == HaloChargeNotice.Ready, "HUD confirms authoritative battery use");
        }
        finally { UnityEngine.Object.DestroyImmediate(registry); }
        string result = "Shared halo energy: PASS (" + checks + " checks).";
        Directory.CreateDirectory("LocalData/HaloEnergy"); File.WriteAllText("LocalData/HaloEnergy/rules.txt", result);
        return result;
    }

    // Only append/update the feature's strings. Never rebuild fonts or the whole collection.
    public static void InstallLocalization()
    {
        var translations = JObject.Parse(File.ReadAllText("Assets/Localization/Translations.json"));
        var collection = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        string[] keys = { "BODY ENERGY", "BATTERY OUT OF REACH",
            "RECHARGE HALO", "NO BATTERY READY", "RETIRED EQUIPMENT", "HALO RECOVERY", "HALO CAPACITY",
            "Requires halo energy below maximum and a charged battery in pockets or an equipped rig.",
            "Legacy generator. Defense now uses the halo and its shared energy. Can be stored or sold.",
            "Shared halo capacity +50%; recovery speed -26%.",
            "Recovery speed +67%; energy per round +50%.",
            "Energy per round reduced by one third (rounded up); recovery speed -17%.",
            "Shotgun spread -55%; energy per round +33% (rounded up).",
            "More energy for attack and defense",
            "Spend less energy per shot",
            "Recovery speed +67%",
            "Recovery speed -26%",
            "Recovery speed -17%",
            "Energy per round +33% (rounded up)",
            "Instantly restores up to 100 halo energy. Keep batteries in pockets or an equipped rig. Unused charge is retained.",
            "Fabricated after Mooncore stabilization research. Energy per round is reduced by one third (rounded up); recovery speed -17%. Knowledge is permanent; the physical module can be lost on death.",
            "Permanent: all halo recharge times -25%.",
            "Permanent: shared halo energy capacity +40%. Upgrades do not grant free energy.",
            "Permanent: all halo energy recovery +33%." };
        foreach (var reference in collection.Tables)
        {
            var table = (StringTable)reference.asset;
            foreach (string key in keys) table.AddEntry(key, table.LocaleIdentifier.Code == "zh-Hans" ? translations[key].Value<string>() : key);
            EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
    }
}
