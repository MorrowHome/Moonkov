using System;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class PmcReloadFailureChecks
{
    private static int s_Checks;
    private static void Check(bool condition,string message)
    {s_Checks++;if(!condition)throw new InvalidOperationException(message);}
    private static InventoryGraph FullAccess()
    {
        var graph=InventoryGraph.Create(false);string pack=graph.Equipped("Backpack").Id;
        if(graph.AddSupply("cells",1,pack)!=InventoryError.None)throw new InvalidOperationException("backpack battery fixture");
        for(int n=1;n<=4;n++)graph.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="dust",Parent="pockets",Region=n.ToString()});
        string rig=graph.Equipped("ChestRig").Id;
        foreach(var region in new[] {"left","center","right"})
            for(int y=0;y<2;y++)for(int x=0;x<(region=="center"?2:1);x++)
                graph.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="dust",Parent=rig,Region=region,X=x,Y=y});
        return graph;
    }
    public static string Run()
    {
        s_Checks=0;var graph=FullAccess();
        Check(graph.Validate()==InventoryError.None && graph.CellEnergy(carriedOnly:true)==100 && graph.CellEnergy(accessibleOnly:true)==0,
            "valid full pockets with inaccessible battery");
        int version=graph.Version;const uint weapon=DollSingerWeapons.Halo;const int cost=3;
        Check(!PmcReloadInventory.Prepare(graph,cost) && graph.Version==version && graph.CellEnergy(carriedOnly:true)==100,
            "real preparation fails without losing a cell");
        var failure=new PmcReloadFailure();failure.Record(graph.Version,weapon,cost);
        Check(failure.Matches(graph.Version,weapon,cost),"same inventory and weapon cannot repeatedly attempt impossible reload");
        Check(!failure.Matches(graph.Version+1,weapon,cost),"inventory change permits another attempt");
        Check(!failure.Matches(graph.Version,DollSingerWeapons.Shotgun,cost),"another weapon is checked independently");
        Check(!failure.Matches(graph.Version,weapon,cost+1),"changed energy cost permits another attempt");
        string pack=graph.Equipped("Backpack").Id;
        var item=graph.Items.First(i=>i.Parent==InventoryCatalog.Pockets);
        Check(graph.FindSpace(item,pack,out string region,out int x,out int y) && graph.TryApply(new InventoryCommand {
            ExpectedVersion=graph.Version,Operation=InventoryOperation.Move,ItemId=item.Id,Parent=pack,Region=region,X=x,Y=y})==InventoryError.None,
            "moving a supply frees access");
        Check(!failure.Matches(graph.Version,weapon,cost) && PmcReloadInventory.Prepare(graph,cost) && graph.CellEnergy(accessibleOnly:true)==100,
            "changed inventory allows the real battery to become accessible");
        Check(graph.TryRecharge(0,30,cost,out int ammo) && ammo==30 && graph.CellEnergy(accessibleOnly:true)==10,
            "prepared battery can charge the real weapon without duplicating energy");
        failure.Record(graph.Version,weapon,cost);failure.Clear();
        Check(!failure.Matches(graph.Version,weapon,cost),"successful preparation clears old failure");
        return "PASS "+s_Checks+" PMC reload availability checks";
    }
}
