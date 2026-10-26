using System;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class HaloTransientChargeChecks
{
    private static int s_Checks;
    private static void Check(bool condition,string message)
    {s_Checks++;if(!condition)throw new InvalidOperationException(message);}

    public static string Run()
    {
        s_Checks=0;
        var graph=InventoryGraph.Create(false);
        Check(graph.AddSupply("cells",1,InventoryCatalog.Pockets,cellCharge:100)==InventoryError.None,"Create real battery charge");
        var rifle=graph.Equipped("Primary");
        int beforeEnergy=graph.CellEnergy(accessibleOnly:true);
        Check(graph.TryRecharge(5,30,3,out int target) && target==30 && graph.CellEnergy(accessibleOnly:true)==beforeEnergy-75,
            "Reserve actual energy before the timed charge");
        var state=new PredictedPlayerGhost {CurrentHealth=100,InventoryWeapons=true,EquippedWeaponSlot=1,
            EquippedWeaponItem=rifle.Id,EquippedWeaponID=DollSingerWeapons.Halo,CurrentAmmo=5,
            EquippedModules=(ushort)(HaloModule.Capacitor|HaloModule.AlienSurge),ReloadTimer=1.5f,ReloadTargetAmmo=target,
            WeaponEquipRemaining=.2f,WeaponCooldown=.17f,BurstRemaining=2,BurstSequence=1};
        state.ControllerState.IsReloadingState=true;
        Check(DollSingerWeapons.RefreshTemporaryModules(ref state,rifle.Id,DollSingerWeapons.Halo,(ushort)HaloModule.Capacitor),
            "Surge expiry keeps the same physical weapon");
        Check(state.ControllerState.IsReloadingState && state.ReloadTimer==1.5f && state.ReloadTargetAmmo==30 && state.CurrentAmmo==5 &&
              graph.CellEnergy(accessibleOnly:true)==beforeEnergy-75,"Expiry cannot discard the prepaid reload or mint ammunition");
        Check(state.WeaponEquipRemaining==.2f && state.WeaponCooldown==.17f && state.BurstRemaining==2 && state.BurstSequence==1,
            "Temporary change cannot restart equipment or reset a firing sequence");
        ushort food=(ushort)((ushort)HaloModule.Capacitor|((ushort)AlienFoodEffect.NeuralOverdrive<<12));
        Check(DollSingerWeapons.RefreshTemporaryModules(ref state,rifle.Id,DollSingerWeapons.Halo,food) && state.EquippedModules==food &&
              state.ControllerState.IsReloadingState && state.ReloadTargetAmmo==30,"New organ activity updates the mask without cancelling charge");
        Check(DollSingerWeapons.RefreshTemporaryModules(ref state,rifle.Id,DollSingerWeapons.Halo,(ushort)HaloModule.Capacitor) &&
              state.ControllerState.IsReloadingState && state.ReloadTargetAmmo==30,"Organ expiry preserves the charge");
        ushort actual=(ushort)HaloModule.Capacitor;
        Check(!DollSingerWeapons.RefreshTemporaryModules(ref state,rifle.Id,DollSingerWeapons.Halo,(ushort)HaloModule.ChargeBridge) &&
              state.EquippedModules==actual,"A physical attachment change still requires equipment synchronization");
        Check(!DollSingerWeapons.RefreshTemporaryModules(ref state,Guid.NewGuid().ToString("D"),DollSingerWeapons.Halo,actual) &&
              state.EquippedModules==actual,"A different physical weapon is not treated as a temporary buff");
        Check(!DollSingerWeapons.RefreshTemporaryModules(ref state,rifle.Id,DollSingerWeapons.Shotgun,actual) &&
              state.EquippedModules==actual,"A changed weapon type is not treated as a temporary buff");
        return "PASS "+s_Checks+" temporary weapon charge checks";
    }
}
