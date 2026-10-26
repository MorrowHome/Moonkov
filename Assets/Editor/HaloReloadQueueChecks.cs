using System;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class HaloReloadQueueChecks
{
    private static int s_Checks;
    private static void Check(bool condition,string message)
    {s_Checks++;if(!condition)throw new InvalidOperationException(message);}
    public static string Run()
    {
        s_Checks=0;var graph=InventoryGraph.Create(false);
        Check(graph.AddSupply("cells",1,InventoryCatalog.Pockets)==InventoryError.None,"battery fixture");
        var state=new PredictedPlayerGhost {CurrentHealth=100,CurrentAmmo=5,WeaponEquipRemaining=.55f};
        int energy=graph.CellEnergy(accessibleOnly:true),version=graph.Version;
        Check(!HaloModuleFire.ReadyToReload(ref state,true,30) && state.ReloadQueued,
            "reload pressed during draw is queued");
        Check(graph.CellEnergy(accessibleOnly:true)==energy && graph.Version==version && !state.ControllerState.IsReloadingState,
            "drawing never spends energy early");
        HaloModuleFire.AdvanceTimers(ref state,.3f);
        Check(!HaloModuleFire.ReadyToReload(ref state,false,30) && state.WeaponEquipRemaining>0,"queue waits while draw continues");
        HaloModuleFire.AdvanceTimers(ref state,.25f);
        Check(state.WeaponEquipRemaining==0 && HaloModuleFire.ReadyToReload(ref state,false,30),"queued reload starts after draw");
        Check(graph.TryRecharge(state.CurrentAmmo,30,3,out int target) && target==30,"queued reload reserves real rounds");
        state.ControllerState.IsReloadingState=true;state.ReloadTargetAmmo=target;
        Check(!HaloModuleFire.ReadyToReload(ref state,false,30) && graph.CellEnergy(accessibleOnly:true)==energy-75,
            "an active reload cannot reserve twice");
        state.ControllerState.IsReloadingState=false;state.WeaponEquipRemaining=.4f;
        Check(!HaloModuleFire.ReadyToReload(ref state,true,30) && state.ReloadQueued,"second draw queues intent");
        HaloModuleFire.BeginEquip(ref state,null);
        Check(!state.ReloadQueued && !HaloModuleFire.ReadyToReload(ref state,false,30),"changing weapons discards queued intent");
        state.CurrentAmmo=30;state.WeaponEquipRemaining=.5f;
        Check(!HaloModuleFire.ReadyToReload(ref state,true,30) && !state.ReloadQueued,"full magazine never queues a later charge");
        state.CurrentAmmo=5;state.CurrentHealth=0;
        Check(!HaloModuleFire.ReadyToReload(ref state,true,30) && !state.ReloadQueued,"dead actor cannot queue charge");
        state.CurrentHealth=100;state.WeaponEquipRemaining=.8f;
        Check(!HaloModuleFire.ReadyToReload(ref state,true,30),"long draw queues intent");
        HaloModuleFire.AdvanceTimers(ref state,1.2f);
        Check(HaloModuleFire.ReadyToReload(ref state,false,30),"queued press survives the complete long draw");
        Check(!HaloModuleFire.ReadyToReload(ref state,false,30),"failed reservation cannot retry indefinitely");
        state.ReloadQueued=true;RaidHealth.Initialize(ref state);
        Check(!state.ReloadQueued,"revival cannot replay a queued request from the previous life");
        return "PASS "+s_Checks+" queued reload checks";
    }
}
