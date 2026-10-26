using System;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class HaloReloadInventoryChecks
{
    private static int s_Checks;
    private static void Check(bool condition,string message)
    {s_Checks++;if(!condition)throw new InvalidOperationException(message);}

    public static string Run()
    {
        s_Checks=0;
        var graph=InventoryGraph.Create(false);
        Check(graph.AddSupply("cells",1,InventoryCatalog.Pockets)==InventoryError.None,"battery fixture");
        Check(graph.AddSupply("dust",1,InventoryCatalog.Pockets)==InventoryError.None,"supply fixture");
        string active=graph.Equipped("Primary").Id,other=graph.Equipped("Secondary").Id,pack=graph.Equipped("Backpack").Id;
        int before=graph.CellEnergy(accessibleOnly:true);
        Check(graph.TryRecharge(5,30,3,out int target) && target==30,"reserve prepaid rounds");
        int paid=graph.CellEnergy(accessibleOnly:true),version=graph.Version;
        var state=new PredictedPlayerGhost {InventoryWeapons=true,EquippedWeaponSlot=1,EquippedWeaponItem=active,
            EquippedWeaponID=DollSingerWeapons.Halo,CurrentAmmo=5,ReloadTargetAmmo=target,ReloadTimer=1.5f};
        state.ControllerState.IsReloadingState=true;
        Check(paid==before-75,"energy deducted exactly once");
        Check(DollSingerWeapons.BlocksReloadWeaponMove(state,InventoryOperation.Move,active,pack,"main"),"cannot move the charging weapon");
        Check(DollSingerWeapons.BlocksReloadWeaponMove(state,InventoryOperation.Move,active,null,null),"cannot drop the charging weapon");
        Check(DollSingerWeapons.BlocksReloadWeaponMove(state,InventoryOperation.Move,other,InventoryCatalog.Equipment,"Primary"),"cannot swap out the charging weapon");
        Check(!DollSingerWeapons.BlocksReloadWeaponMove(state,InventoryOperation.Move,other,pack,"main"),"other weapon remains movable");
        string dust=graph.Items.Single(i=>i.Code=="dust").Id;
        Check(!DollSingerWeapons.BlocksReloadWeaponMove(state,InventoryOperation.Move,dust,pack,"main"),"ordinary supplies remain movable");
        Check(graph.TryApply(new InventoryCommand {ExpectedVersion=version,Operation=InventoryOperation.Move,ItemId=dust,Parent=pack,Region="main"})==InventoryError.None,
            "unrelated inventory operation still succeeds");
        Check(graph.Equipped("Primary").Id==active && state.ReloadTargetAmmo==target && graph.CellEnergy(accessibleOnly:true)==paid,
            "unrelated move preserves the prepaid target");
        Check(!DollSingerWeapons.BlocksReloadWeaponMove(state,InventoryOperation.Split,dust,pack,"main"),"split remains governed by inventory rules");
        Check(!DollSingerWeapons.BlocksReloadWeaponMove(state,InventoryOperation.Move,other,InventoryCatalog.Equipment,"Secondary"),"other slot is not blocked");
        state.ControllerState.IsReloadingState=false;
        Check(!DollSingerWeapons.BlocksReloadWeaponMove(state,InventoryOperation.Move,active,pack,"main"),"weapon is movable after charging");
        Check(graph.FindSpace(graph.Find(active),pack,out string region,out int x,out int y) &&
            graph.TryApply(new InventoryCommand {ExpectedVersion=graph.Version,Operation=InventoryOperation.Move,ItemId=active,Parent=pack,Region=region,X=x,Y=y})==InventoryError.None,
            "weapon actually moves once charge completes");
        state.ControllerState.IsReloadingState=true;state.InventoryWeapons=false;
        Check(!DollSingerWeapons.BlocksReloadWeaponMove(state,InventoryOperation.Move,active,pack,"main"),"non-inventory weapon is unaffected");
        return "PASS "+s_Checks+" prepaid weapon inventory checks";
    }
}
