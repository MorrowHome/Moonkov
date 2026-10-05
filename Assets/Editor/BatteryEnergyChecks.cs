using System;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;

public static class BatteryEnergyChecks
{
    [MenuItem("Tools/Moonkov/Check Battery Energy")]
    public static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit mode.");
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var rifle = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/HaloWeapon.asset");
        var pistol = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/RevolverWeapon.asset");
        var shotgun = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/ShotgunWeapon.asset");
        Check(rifle && pistol && shotgun && rifle.EnergyPerRound == 3 && pistol.EnergyPerRound == 1 && shotgun.EnergyPerRound == 12, "Serialized weapon costs");
        var graph = InventoryGraph.Create(false, false); graph.AddSupply("cells", 1, cellCharge: 8, emergencySupply: true);
        Check(graph.TryRecharge(0, rifle.MagazineSize, rifle.EnergyPerRound, out int target) && target == 2 && graph.CellEnergy() == 2, "Configured rifle partial recharge");
        var state = new PredictedPlayerGhost { CurrentAmmo = 0, ReloadTargetAmmo = target,
            ControllerState = new FirstPersonController.ControllerState { IsReloadingState = true } };
        DollSingerWeapons.CompleteReload(ref state, rifle);
        Check(state.CurrentAmmo == 2 && !state.ControllerState.IsReloadingState && state.ReloadTargetAmmo == 0, "Server and prediction finish at reserved ammo, not full magazine");
        DollSingerWeapons.CompleteReload(ref state, rifle);
        Check(state.CurrentAmmo == 2, "Repeated completion cannot refill or erase ammo");
        string wire = RaidInventoryTransport.Encode(graph); var decoded = RaidInventoryTransport.Decode(wire);
        var cell = decoded.Items.Single(i => i.Code == "cells");
        Check(decoded.Validate() == InventoryError.None && cell.CellCharge == 2 && cell.EmergencySupply && decoded.CellEnergy() == 2, "Network inventory preserves charge/provenance");
        var legacy = RaidInventoryTransport.Decode(wire.Replace("\"CellCharge\":2,", ""));
        Check(legacy.CellEnergy() == 100 && legacy.Validate() == InventoryError.None, "Legacy network snapshots default to full cells");
        Check(graph.TryRecharge(0, pistol.MagazineSize, pistol.EnergyPerRound, out target) && target == 2 && graph.Count("cells") == 0, "Configured pistol can use rifle remainder");
        var visualObject = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/ShotgunHalo.prefab"));
        try
        {
            var visual = visualObject.GetComponent<ShotgunHaloVisual>(); visual.SetEquipped(true, 0);
            visual.SetNetworkState(0, true, 1f, 0, 1, 2);
            Check(visual.shell.sprite == visual.shellFrames[2], "Partial shotgun recharge shows two rounds, not four");
            visual.SetNetworkState(0, true, .5f, 0, 1, 2);
            Check(visual.shell.sprite == visual.shellFrames[2], "Older reload progress cannot refill or jitter shell frames");
        }
        finally { UnityEngine.Object.DestroyImmediate(visualObject); }
        return $"Battery Unity checks passed: {checks}. Serialized costs, reload completion and network charge round-trip.";
    }
}
