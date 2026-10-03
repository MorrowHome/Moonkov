using System;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class MoonkovDeathBagChecks
{
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    [MenuItem("Tools/Moonkov/Check Death Bags")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run in Edit mode.");
        var dead=InventoryGraph.Create(false);
        Require(dead.AddLoot("small_pack")==InventoryError.None,"Nested bag fixture failed.");
        var nested=dead.Items.Single(i=>i.Code=="small_pack");
        Require(dead.AddSupply("dust",2,nested.Id,true)==InventoryError.None && dead.AddSupply("cells",2)==InventoryError.None,"Supplies fixture failed.");
        foreach(var slot in new[] {"Helmet","Primary","Secondary","Pistol"})
            dead.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code=slot=="Helmet" ? "helmet" : slot=="Pistol" ? "pistol" : "rifle",Parent="equipment",Region=slot});
        Require(dead.Validate()==InventoryError.None,"Full loadout fixture invalid.");
        var before=dead.Clone();int version=dead.Version;
        var world=new RaidLootContainers();world.Initialize(24);
        int id=world.Drop(dead,new float3(1,2,3));var bag=world.Containers[id];
        Require(id>=24 && bag.Validate()==InventoryError.None && dead.Validate()==InventoryError.None,"Invalid world drop.");
        Require(dead.Items.All(i=>InventoryCatalog.Get(i.Code).Kind==ItemKind.Root) && dead.Version>version,"Dead player retained ownership.");
        var original=before.Items.Where(i=>i.Parent!=null).ToArray();
        Require(original.Length==bag.Items.Count(i=>i.Parent!=null) && original.All(i=>bag.Find(i.Id)?.Quantity==i.Quantity && bag.Find(i.Id).FoundInRaid==i.FoundInRaid),"Drop changed identity, quantity or provenance.");
        Require(original.Where(i=>i.Parent!="equipment" && i.Parent!="pockets").All(i=>RaidInventoryTransport.Encode(new InventoryGraph {Items=new System.Collections.Generic.List<InventoryItem>{i}})==RaidInventoryTransport.Encode(new InventoryGraph {Items=new System.Collections.Generic.List<InventoryItem>{bag.Find(i.Id)}})),"Nested placement changed.");
        var oldMove=new InventoryCommand {ExpectedVersion=version,ItemId=nested.Id,Parent="pockets",Region="1"};
        Require(dead.TryApply(oldMove)==InventoryError.Stale,"Pre-death move reused dead cargo.");

        var looter=InventoryGraph.Create(false,false);var other=InventoryGraph.Create(false,false);
        var pack=before.Equipped("Backpack");int cacheVersion=bag.Version;
        var take=new InventoryCommand {ExpectedVersion=looter.Version,ItemId=pack.Id,Parent="equipment",Region="Backpack"};
        Require(LootInventoryExchange.TryApply(looter,bag,take,cacheVersion)==InventoryError.None,"Cannot equip fallen player's bag.");
        take.ExpectedVersion=other.Version;
        Require(LootInventoryExchange.TryApply(other,bag,take,cacheVersion)==InventoryError.Stale,"Two claimants received the same gear.");
        Require(looter.Find(nested.Id)?.Parent==pack.Id && looter.Count("dust",true)==2 && bag.Find(nested.Id)==null,"Looting detached nested contents.");
        var storage=InventoryGraph.Create(true,false);
        Require(storage.ReturnLoadout(looter)==InventoryError.None && storage.Find(nested.Id)!=null && storage.ReturnLoadout(looter)==InventoryError.Invalid,"Extraction return duplicated nested kit.");
        string saved=RaidInventoryTransport.Encode(before);var malformed=before.Clone();malformed.Items.Add(null);
        try{LootInventoryExchange.DropOnDeath(malformed);throw new Exception("Invalid drop accepted.");}catch(InvalidOperationException){}
        Require(RaidInventoryTransport.Encode(before)==saved && malformed.Items.Last()==null,"Failed drop mutated source.");

        var update=new RaidCorpseRpc {LootId=id,Version=bag.Version,Position=world.DeathBags[id].Position,Rotation=world.DeathBags[id].Rotation,CharacterIndex=2,Age=1,Empty=false};
        var serializerType=typeof(RaidCorpseRpc).Assembly.GetTypes().Single(t=>t.IsValueType && typeof(IRpcCommandSerializer<RaidCorpseRpc>).IsAssignableFrom(t));
        var serializer=(IRpcCommandSerializer<RaidCorpseRpc>)Activator.CreateInstance(serializerType);
        using var bytes=new NativeArray<byte>(1378,Allocator.Temp);var writer=new DataStreamWriter(bytes);
        serializer.Serialize(ref writer,new RpcSerializerState {CompressionModel=StreamCompressionModel.Default},update);
        var reader=new DataStreamReader(writer.AsNativeArray());var decoded=default(RaidCorpseRpc);
        serializer.Deserialize(ref reader,new RpcDeserializerState {CompressionModel=StreamCompressionModel.Default},ref decoded);
        Require(!writer.HasFailedWrites && decoded.LootId==id && math.all(decoded.Position==update.Position) && decoded.CharacterIndex==2 && decoded.Age==1 && math.all(decoded.Rotation.value==update.Rotation.value),"Death bag RPC lost position.");
        var client=new RaidDeathBagClientState();client.Receive(decoded);decoded.Version++;decoded.Empty=true;client.Receive(decoded);client.Receive(update);
        Require(client.Bags[id].Empty && client.Bags.Count==1,"Late join or stale metadata replaced newer bag state.");
        var joined=LootInventoryExchange.Snapshot(looter,bag);
        Require(RaidInventoryTransport.Decode(RaidInventoryTransport.Encode(joined)).LootRows==24,"Snapshot lost bag dimensions.");
        var root=new VisualElement();using var view=new ContainerInventoryView(root,false,_=>{});view.Present(joined,lootId:id);
        Require(root.Q<VisualElement>(className:"inventory-loot-pane").Query<Label>(className:"inventory-section-title").ToList().Single().text=="FALLEN EXPEDITION / GEAR"
            && root.Q<VisualElement>(className:"inventory-loot-pane").Q<ScrollView>()!=null,"Death bag pane missing or cannot scroll.");
        var maximum=InventoryGraph.Create(false);var level=new System.Collections.Generic.List<string>{maximum.Equipped("Backpack").Id};
        for(int depth=2;depth<=7;depth++)
        {
            var next=new System.Collections.Generic.List<string>();
            foreach(var parent in level)for(int index=0;index<(depth==2 ? 4 : 2);index++)
            {
                var item=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="small_pack",Parent=parent,Region="main",X=index%2*2,Y=index/2*3};
                maximum.Items.Add(item);next.Add(item.Id);
            }
            level=next;
        }
        foreach(var parent in level)for(int cell=0;cell<16 && maximum.Items.Count<InventoryCatalog.MaxItems;cell++)
            maximum.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="dust",Parent=parent,Region="main",X=cell%4,Y=cell/4});
        Require(maximum.Items.Count==InventoryCatalog.MaxItems && maximum.Validate()==InventoryError.None,"Maximum/deep inventory fixture invalid.");
        var maximumBag=LootInventoryExchange.DropOnDeath(maximum);
        var maximumSnapshot=LootInventoryExchange.Snapshot(other,maximumBag);
        Require(maximumBag.Validate()==InventoryError.None && maximumSnapshot.Validate()==InventoryError.None
            && RaidInventoryTransport.EncodeSnapshot(maximumSnapshot,id,maximumBag.Version).Length<RaidInventoryTransport.MaxJsonCharacters,"Extra world root invalidated a maximum-size death drop.");
        Debug.Log("Death bag checks passed: full loadout/nested ownership, stale death move, competing claimants, extraction identity, invalid drop rollback, generated RPC/stale metadata, snapshot dimensions and scrollable loot pane.");
    }
}
