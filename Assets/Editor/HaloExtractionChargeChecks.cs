using System;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class HaloExtractionChargeChecks
{
    private static int s_Checks;
    private static void Check(bool condition,string message)
    {s_Checks++;if(!condition)throw new InvalidOperationException(message);}

    public static string Run()
    {
        s_Checks=0;
        var graph=InventoryGraph.Create(false);
        Check(graph.AddSupply("cells",1,InventoryCatalog.Pockets)==InventoryError.None,"battery fixture");
        var rifle=graph.Equipped("Primary");rifle.LoadedAmmo=5;
        int before=graph.CellEnergy(accessibleOnly:true);
        Check(graph.TryRecharge(5,30,3,out int target) && target==30,"prepay real charge");
        int paid=graph.CellEnergy(accessibleOnly:true);
        var state=new PredictedPlayerGhost {CurrentHealth=100,InventoryWeapons=true,EquippedWeaponSlot=1,
            EquippedWeaponItem=rifle.Id,EquippedWeaponID=DollSingerWeapons.Halo,CurrentAmmo=5,ReloadTargetAmmo=target,ReloadTimer=1.5f};
        state.ControllerState.IsReloadingState=true;
        Check(paid==before-75 && graph.Find(rifle.Id).LoadedAmmo==5,"reservation does not grant early ammunition");
        Check(DollSingerWeapons.CompleteExtractedCharge(ref state,graph,30),"successful extraction settles paid rounds");
        Check(state.CurrentAmmo==30 && !state.ControllerState.IsReloadingState && state.ReloadTargetAmmo==0 &&
            graph.Find(rifle.Id).LoadedAmmo==30 && graph.CellEnergy(accessibleOnly:true)==paid,"rounds and remaining energy are saved together");
        var saved=RaidInventoryTransport.Decode(RaidInventoryTransport.Encode(graph));
        Check(saved.Validate()==InventoryError.None && saved.Find(rifle.Id).LoadedAmmo==30 && saved.CellEnergy(accessibleOnly:true)==paid,
            "settled ammunition survives the actual inventory serialization");
        Check(!DollSingerWeapons.CompleteExtractedCharge(ref state,graph,30) && graph.CellEnergy(accessibleOnly:true)==paid,
            "repeated settlement does not create energy or rounds");
        var partial=InventoryGraph.Create(false);
        Check(partial.AddSupply("cells",1,InventoryCatalog.Pockets,cellCharge:8)==InventoryError.None,"partial battery fixture");
        var partialRifle=partial.Equipped("Primary");partialRifle.LoadedAmmo=5;
        Check(partial.TryRecharge(5,30,3,out int partialTarget) && partialTarget==7,"partial charge target");
        var pending=new PredictedPlayerGhost {CurrentHealth=100,InventoryWeapons=true,EquippedWeaponSlot=1,
            EquippedWeaponItem=partialRifle.Id,EquippedWeaponID=DollSingerWeapons.Halo,CurrentAmmo=5,ReloadTargetAmmo=partialTarget};
        pending.ControllerState.IsReloadingState=true;
        var invalid=pending;invalid.CurrentHealth=0;
        Check(!DollSingerWeapons.CompleteExtractedCharge(ref invalid,partial,30),"dead player cannot settle an extraction charge");
        invalid=pending;invalid.EquippedWeaponItem=Guid.NewGuid().ToString("D");
        Check(!DollSingerWeapons.CompleteExtractedCharge(ref invalid,partial,30),"different item cannot receive paid rounds");
        invalid=pending;invalid.EquippedWeaponID=DollSingerWeapons.Shotgun;
        Check(!DollSingerWeapons.CompleteExtractedCharge(ref invalid,partial,30),"different weapon cannot receive paid rounds");
        invalid=pending;invalid.InventoryWeapons=false;
        Check(!DollSingerWeapons.CompleteExtractedCharge(ref invalid,partial,30),"non-inventory weapon is unaffected");
        invalid=pending;
        Check(!DollSingerWeapons.CompleteExtractedCharge(ref invalid,partial,0),"unknown capacity cannot mint ammunition");
        Check(partial.Find(partialRifle.Id).LoadedAmmo==5 && partial.CellEnergy(accessibleOnly:true)==2,"rejected settlement leaves inventory unchanged");
        Check(DollSingerWeapons.CompleteExtractedCharge(ref pending,partial,30) && pending.CurrentAmmo==7 &&
            partial.Find(partialRifle.Id).LoadedAmmo==7 && partial.CellEnergy(accessibleOnly:true)==2,"partial charge saves only paid rounds");
        return "PASS "+s_Checks+" extraction charge checks";
    }
}
