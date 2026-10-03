using System;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class MoonkovLootChecks
{
    private static void Require(bool value,string name) {if(!value)throw new Exception(name);}
    [MenuItem("Tools/Moonkov/Check Loot Containers")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run in Edit mode.");
        for(int index=0;index<24;index++)Require(LootInventoryExchange.CreateCache(index).Validate()==InventoryError.None,"Invalid seeded cache.");
        var cache=LootInventoryExchange.CreateCache(0);var first=InventoryGraph.Create(false);var second=InventoryGraph.Create(false);
        var dust=cache.Items.Single(i=>i.Code=="dust");int quantity=dust.Quantity;int before=cache.Version;
        InventoryCommand Take(InventoryGraph owner,string id,string parent,string region) => new InventoryCommand {ExpectedVersion=owner.Version,ItemId=id,Parent=parent,Region=region};
        Require(LootInventoryExchange.TryApply(first,cache,Take(first,dust.Id,"pockets","1"),before)==InventoryError.None,"First claimant failed.");
        Require(LootInventoryExchange.TryApply(second,cache,Take(second,dust.Id,"pockets","1"),before)==InventoryError.Stale,"Second claimant duplicated the same item.");
        Require(first.Count("dust")+second.Count("dust")+cache.Count("dust")==quantity,"Claim changed total supply count.");
        Require(LootInventoryExchange.TryApply(first,cache,Take(first,dust.Id,"loot","main"),cache.Version)==InventoryError.None,"Deposit failed.");
        var split=Take(first,dust.Id,"pockets","2");split.Operation=InventoryOperation.Split;split.Quantity=1;
        Require(LootInventoryExchange.TryApply(first,cache,split,cache.Version)==InventoryError.None,"Split transfer failed.");
        var splitItem=first.Items.Single(i=>i.Code=="dust");
        var merge=Take(first,splitItem.Id,null,null);merge.Operation=InventoryOperation.Merge;merge.TargetId=dust.Id;
        Require(LootInventoryExchange.TryApply(first,cache,merge,cache.Version)==InventoryError.None && cache.Count("dust")==quantity,"Cross-owner merge lost supplies.");
        var incoming=cache.Items.Single(i=>i.Code=="small_pack");var outgoing=first.Equipped("Backpack");
        Require(cache.AddSupply("cells",1,incoming.Id,true)==InventoryError.None && first.AddSupply("dust",2,outgoing.Id)==InventoryError.None,"Nested fixture failed.");
        Require(LootInventoryExchange.TryApply(first,cache,Take(first,incoming.Id,"equipment","Backpack"),cache.Version)==InventoryError.None,"Equipment swap across cache failed.");
        Require(first.Equipped("Backpack").Id==incoming.Id && first.Items.Any(i=>i.Parent==incoming.Id && i.Code=="cells")
            && cache.Items.Any(i=>i.Id==outgoing.Id && i.Parent=="loot") && cache.Items.Any(i=>i.Parent==outgoing.Id && i.Code=="dust"),"Whole-container swap lost nested contents.");
        Require(first.Find("loot")==null && first.ExtractLoadout().Find("loot")==null,"Cache leaked into settlement cargo.");

        var heavy=InventoryGraph.Create(false);var stock=LootInventoryExchange.CreateCache(2);
        heavy.AddSupply("alloy",70,heavy.Equipped("Backpack").Id);
        string savedOwner=RaidInventoryTransport.Encode(heavy),savedCache=RaidInventoryTransport.Encode(stock);
        var alloy=stock.Items.Single(i=>i.Code=="alloy");
        Require(LootInventoryExchange.TryApply(heavy,stock,Take(heavy,alloy.Id,heavy.Equipped("ChestRig").Id,"center"),stock.Version)==InventoryError.Overweight,"Weight limit bypassed by cache transfer.");
        Require(savedOwner==RaidInventoryTransport.Encode(heavy) && savedCache==RaidInventoryTransport.Encode(stock),"Rejected move mutated an owner.");

        first=InventoryGraph.Create(false);var joined=LootInventoryExchange.Snapshot(first,cache);
        string json=RaidInventoryTransport.EncodeSnapshot(joined,0,cache.Version);var state=new RaidInventoryClientState();
        int count=(json.Length+RaidInventoryTransport.ChunkCharacters-1)/RaidInventoryTransport.ChunkCharacters;
        for(int index=count-1;index>=0;index--)
            state.Receive(new RaidInventoryChunkV2Rpc {RaidId=1,Sequence=1,Count=count,Index=index,
                Json=json.Substring(index*RaidInventoryTransport.ChunkCharacters,Math.Min(RaidInventoryTransport.ChunkCharacters,json.Length-index*RaidInventoryTransport.ChunkCharacters))},RaidInventoryTransport.Decode);
        Require(state.LootId==0 && state.LootVersion==cache.Version && state.Graph.Find("loot")!=null,"Snapshot lost cache metadata.");
        var request=new RaidLootMoveRpc {RaidId=1,LootId=0,ExpectedLootVersion=cache.Version,ExpectedVersion=first.Version,
            ItemId=dust.Id,Parent="pockets",Region="1",RequestId=1};
        var serializerType=typeof(RaidLootMoveRpc).Assembly.GetTypes().Single(t=>t.IsValueType && typeof(IRpcCommandSerializer<RaidLootMoveRpc>).IsAssignableFrom(t));
        var serializer=(IRpcCommandSerializer<RaidLootMoveRpc>)Activator.CreateInstance(serializerType);
        using var bytes=new NativeArray<byte>(1378,Allocator.Temp);var writer=new DataStreamWriter(bytes);
        serializer.Serialize(ref writer,new RpcSerializerState {CompressionModel=StreamCompressionModel.Default},request);
        var reader=new DataStreamReader(writer.AsNativeArray());var decoded=default(RaidLootMoveRpc);
        serializer.Deserialize(ref reader,new RpcDeserializerState {CompressionModel=StreamCompressionModel.Default},ref decoded);
        Require(!writer.HasFailedWrites && writer.Length+13<1378 && decoded.ItemId.Equals(request.ItemId) && decoded.ExpectedLootVersion==request.ExpectedLootVersion,"Generated loot RPC changed transfer data.");

        var root=new VisualElement();int commands=0;
        using var view=new ContainerInventoryView(root,false,command=>commands++);
        view.Present(joined,lootId:0);
        Require(root.Q<VisualElement>(className:"inventory-loot-pane").Query<Label>(className:"inventory-section-title").ToList().Single().text=="SUPPLY CACHE / 01","Loot pane not rendered.");
        Require(root.Q<VisualElement>(className:"inventory-containers").Query<Label>(className:"inventory-section-title").ToList().Count==3,"Carried containers missing.");
        typeof(ContainerInventoryView).GetMethod("QuickTransfer",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[] {joined.Find(dust.Id)});
        Require(commands==1 && view.Busy,"Ctrl transfer did not produce one authoritative intent.");
        Debug.Log("Loot checks passed: 24 cache fixtures, two-claimant rejection, deposit/split/merge, nested equipment swap, weight rollback, isolated settlement cargo, snapshot metadata, generated RPC and loot UI/transfer intent.");
    }
}
