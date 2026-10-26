using System;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;

public static class HaloChargeFeedbackChecks
{
    [MenuItem("Tools/Moonkov/Check Halo Charge Feedback")]
    public static string Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks++; }
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/HaloWeapon.asset");
        var graph = InventoryGraph.Create(false, false);
        string id = Guid.NewGuid().ToString("D");
        graph.Items.Add(new InventoryItem { Id = id, Code = "rifle", Parent = "equipment", Region = "Primary", LoadedAmmo = 0 });
        var player = new PredictedPlayerGhost { CurrentHealth = 100, InventoryWeapons = true, EquippedWeaponID = DollSingerWeapons.Halo, EquippedWeaponItem = id };
        var input = new PlayerInput(); input.SetFlag(PlayerInput.InputFlag.Reload, true);
        var feedback = new HaloChargeFeedback();
        HaloChargeStatus Read(float now, bool visible = true, InventoryGraph inventory = null)
            => feedback.Update(player, weapon, inventory ?? graph, visible, now);
        Check(Read(0).Notice == HaloChargeNotice.None, "Idle combat stays quiet");
        feedback.ObserveInput(input, player, 1);
        Check(Read(1.1f).Notice == HaloChargeNotice.Waiting, "Input is a request, not a confirmed reload");
        var low = Read(1.3f);
        Check(low.Notice == HaloChargeNotice.LowEnergy && low.Cost == weapon.EnergyPerRound && low.Energy == 0, "No accessible energy has an actionable reason");
        Check(Read(3.6f).Notice == HaloChargeNotice.None, "Transient failure expires");
        graph.AddSupply("cells", 1, cellCharge: weapon.EnergyPerRound - 1);
        feedback.ObserveInput(input, player, 4);
        Check(Read(4.3f).Notice == HaloChargeNotice.LowEnergy && Read(4.3f).Energy == weapon.EnergyPerRound - 1,
            "Partial cell below one round is not usable charge");
        var pack = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "backpack", Parent = "equipment", Region = "Backpack" }; graph.Items.Add(pack);
        graph.AddSupply("cells", 1, pack.Id);
        Check(Read(4.4f).Notice == HaloChargeNotice.MoveCells, "Sufficient energy in a backpack prompts relocation");
        string before = RaidInventoryTransport.Encode(graph); Read(4.5f);
        Check(RaidInventoryTransport.Encode(graph) == before, "Feedback never reserves energy or moves items");
        var foreign = InventoryGraph.Create(false, false);
        Check(Read(4.5f, inventory: foreign).Notice == HaloChargeNotice.Syncing, "Wrong weapon snapshot is syncing, not a false empty battery");
        Check(feedback.Update(player, weapon, null, true, 4.6f).Notice == HaloChargeNotice.Syncing, "Missing inventory does not invent a rejection");
        Check(Read(4.7f).Notice == HaloChargeNotice.MoveCells, "A matching snapshot restores the actual reason");
        Check(Read(4.8f, false).Notice == HaloChargeNotice.None && Read(4.9f).Notice == HaloChargeNotice.None,
            "Menus clear transient input feedback");
        player.CurrentAmmo = weapon.MagazineSize;
        feedback.ObserveInput(input, player, 5);
        Check(Read(5.1f).Notice == HaloChargeNotice.Full, "Manual charge on a full halo explains the no-op");

        feedback.Reset(); player.CurrentAmmo = 0;
        Read(6);
        player.ControllerState.IsReloadingState = true; player.ReloadTargetAmmo = 2; player.ReloadTimer = weapon.ReloadTime * .75f; player.LastReloadTick = 10;
        var charging = Read(6.1f);
        Check(charging.Notice == HaloChargeNotice.Charging && charging.Ammo == 2 && charging.Capacity == weapon.MagazineSize,
            "Partial target comes from the server, not a fabricated full magazine");
        Check(Mathf.Abs(charging.Progress - .25f) < .001f, "Progress follows authoritative remaining time");
        player.ReloadTimer = weapon.ReloadTime * .25f;
        Check(Read(6.2f).Progress > charging.Progress, "Reduced server time advances the progress bar");
        player.ControllerState.IsReloadingState = false; player.CurrentAmmo = 2; player.ReloadTargetAmmo = 0;
        Check(Read(6.3f).Notice == HaloChargeNotice.Ready, "Actual ammo gain confirms completion");
        Check(Read(7).Notice == HaloChargeNotice.Ready && Read(8).Notice == HaloChargeNotice.None, "Completion is brief and not retriggered by repeated snapshots");
        player.CurrentAmmo = 0; player.LastReloadTick = 11; player.ControllerState.IsReloadingState = true; Read(9);
        player.ControllerState.IsReloadingState = false;
        Check(Read(9.1f).Notice == HaloChargeNotice.None, "Interrupted charge without ammo gain is not success");
        player.LastReloadTick = 12; player.CurrentAmmo = 2;
        Check(Read(9.2f).Notice == HaloChargeNotice.Ready, "Skipped short charging frames still confirm authoritative refill");
        feedback.Reset();
        Check(Read(10).Notice == HaloChargeNotice.None, "Late join does not replay old completion");

        player.CurrentAmmo = 0; feedback.Reset(); Read(11);
        var fire = new PlayerInput(); fire.SetFlag(PlayerInput.InputFlag.Aim, true); fire.SetFlag(PlayerInput.InputFlag.Shoot, true);
        feedback.ObserveInput(fire, player, 12);
        Check(Read(12.3f).Notice == HaloChargeNotice.MoveCells, "An empty aimed trigger can explain missing accessible batteries");
        feedback.ObserveInput(fire, player, 15);
        Check(Read(15).Notice == HaloChargeNotice.None, "Holding an empty trigger cannot spam or prolong warnings");
        feedback.ObserveInput(default, player, 16); feedback.ObserveInput(fire, player, 17);
        Check(Read(17.3f).Notice == HaloChargeNotice.MoveCells, "Releasing and pressing again permits a new explanation");
        player.EquippedWeaponItem = Guid.NewGuid().ToString("D");
        Check(Read(17.4f).Notice == HaloChargeNotice.None, "Switching to another instance of the same weapon clears feedback");
        player.EquippedWeaponItem = id; feedback.ObserveInput(input, player, 18); player.CurrentHealth = 0;
        Check(Read(18.3f).Notice == HaloChargeNotice.None, "Death clears feedback");
        player.CurrentHealth = 100; player.WeaponEquipRemaining = 1; feedback.ObserveInput(input, player, 19);
        Check(Read(19.3f).Notice == HaloChargeNotice.None, "Equip recovery does not produce a spurious battery failure");

        foreach (string locale in new[] { "en", "zh-Hans" })
        {
            var table = AssetDatabase.LoadAssetAtPath<StringTable>($"Assets/Localization/Tables/MoonkovUI_{locale}.asset");
            foreach (string key in new[] { "CHARGING / {0:0.0}s\nTARGET {1}/{2}", "CHARGED / {0}/{1}", "HALO FULL",
                "Move cells to pockets or your chest rig.", "CELL ENERGY TOO LOW\nREADY {0} / NEED {1}", "Syncing battery inventory...", "Awaiting charge confirmation..." })
                Check(table.GetEntry(key) != null, "Localized charge status: " + locale + "/" + key);
        }
        return $"PASS: {checks} charge feedback, interruption, input latch and localization checks; no profile or energy writes.";
    }
}
