using System;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class MoonkovRaidActionChecks
{
    private static int s_Count;
    private static void Check(bool value,string name)
    { if(!value)throw new InvalidOperationException(name);s_Count++; }
    private static PredictedPlayerGhost Injured()
    { var health=default(PredictedPlayerGhost);RaidHealth.Initialize(ref health);RaidHealth.Damage(ref health,BodyPart.RightArm,50,1);return health; }
    private static RaidInventoryState Inventory()
    {
        var graph=InventoryGraph.Create(false,false);graph.AddSupply("medkit",2,"pockets");
        return new RaidInventoryState {Graph=graph,RaidId=7};
    }
    [MenuItem("Tools/Moonkov/Check Raid Timed Actions And Drop")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run in Edit mode.");
        s_Count=0;
        var actor=Inventory();var health=Injured();float before=health.CurrentHealth;int version=actor.Graph.Version;
        string id=actor.Graph.Items.Single(i=>i.Code=="medkit").Id;
        Check(actor.BeginMedical(health,id,version,BodyPart.Auto,float3.zero,10)==InventoryError.None,"Cannot begin medical");
        Check(health.CurrentHealth==before && actor.Graph.Count("medkit")==2 && actor.Graph.Version==version,"Begin medical produced early reward");
        actor.RefreshMedical(ref health,true,float3.zero,12.99);
        Check(health.CurrentHealth==before && actor.Graph.Count("medkit")==2,"Early medical completion");
        actor.RefreshMedical(ref health,true,float3.zero,13);actor.RefreshMedical(ref health,true,float3.zero,20);
        Check(health.CurrentHealth==before+40 && actor.Graph.Count("medkit")==1 && actor.Graph.Version==version+1 && !actor.ActionPending,"Medical completion/replay not exactly once");
        Check(actor.BeginMedical(health,id,version,BodyPart.RightArm,float3.zero,21)==InventoryError.Stale,"Old version medical accepted");
        foreach(string interruption in new[] {"cancel","movement","damage","shot","grenade","reload","weapon","version","death","access"})
        {
            actor=Inventory();health=Injured();before=health.CurrentHealth;version=actor.Graph.Version;
            id=actor.Graph.Items.Single(i=>i.Code=="medkit").Id;
            Check(actor.BeginMedical(health,id,version,BodyPart.RightArm,float3.zero,0)==InventoryError.None,"Medical fixture "+interruption);
            var position=float3.zero;bool accessible=true;
            switch(interruption)
            {
                case "cancel":actor.CancelActions("cancelled");break;
                case "movement":position=new float3(1,0,0);break;
                case "damage":health.LastHitTick++;break;
                case "shot":health.LastShotTick++;break;
                case "grenade":health.LastGrenadeShotTick++;break;
                case "reload":health.LastReloadTick++;break;
                case "weapon":health.EquippedWeaponID++;break;
                case "version":actor.Graph.Version++;break;
                case "death":health.CurrentHealth=0;accessible=false;break;
                case "access":accessible=false;break;
            }
            actor.RefreshMedical(ref health,accessible,position,1);actor.RefreshMedical(ref health,true,float3.zero,10);
            Check(actor.Graph.Count("medkit")==2 && (interruption=="death" || health.CurrentHealth==before) && !actor.ActionPending,"Interrupted medical consumed/rewarded: "+interruption);
            Check(!string.IsNullOrEmpty(actor.ActionFeedback),"Cancellation reason absent: "+interruption);
        }
        var caches=new RaidLootContainers();caches.Initialize(2);actor=Inventory();
        actor.BeginDissection(0,10,float3.zero,1,2);
        actor.RefreshDissection(caches,true,float3.zero,1,11.99);
        Check(actor.OpenedLootId<0 && !actor.SearchedLoot.Contains(0),"Search exposes items before deadline");
        actor.CancelActions();actor.RefreshDissection(caches,true,float3.zero,1,15);
        Check(actor.OpenedLootId<0 && !actor.SearchedLoot.Contains(0),"Cancelled search completed");
        actor.BeginDissection(0,20,float3.zero,1,2);actor.RefreshDissection(caches,true,float3.zero,1,22);
        Check(actor.OpenedLootId==0 && actor.SearchedLoot.Contains(0) && !actor.SearchedLoot.Contains(1),"First-search state is not keyed by real loot ID");
        actor.OpenedLootId=-1;
        Check(!actor.NeedsSearch(caches,0) && actor.NeedsSearch(caches,1),"Closing cache lost completed search or searched different box");
        actor.BeginDissection(1,30,float3.zero,1,2);actor.RefreshDissection(caches,true,new float3(1,0,0),1,31);
        Check(actor.OpenedLootId<0 && !actor.SearchedLoot.Contains(1),"Moving search gained access");
        var dropOwner=InventoryGraph.Create(false);var pack=dropOwner.Equipped("Backpack");
        dropOwner.AddSupply("cells",2,pack.Id,true,emergencySupply:true,cellCharge:19);
        int old=dropOwner.Version;
        Check(caches.DropItem(dropOwner,pack.Id,old,new float3(1,2,3),50,out int dropId)==InventoryError.None,"Ground registration failed");
        Check(dropOwner.Find(pack.Id)==null && caches.Containers[dropId].Find(pack.Id)!=null &&
            caches.DeathBags[dropId].CharacterIndex==RaidLootContainers.DroppedItemCharacterIndex,"Ground owner/marker missing");
        int count=caches.Containers.Count;
        Check(caches.DropItem(dropOwner,pack.Id,old,float3.zero,51,out _)==InventoryError.Stale && caches.Containers.Count==count,"Replayed drop created second ground owner");
        // Force registration failure after preparation; source ownership must remain intact.
        var rollback=InventoryGraph.Create(false);var rollbackPack=rollback.Equipped("Backpack");old=rollback.Version;
        int next=dropId+1;caches.DeathBags.Add(next,new RaidCorpseRecord());
        Check(caches.DropItem(rollback,rollbackPack.Id,old,float3.zero,52,out _)==InventoryError.Invalid,"Registration collision was not rejected");
        Check(rollback.Find(rollbackPack.Id)!=null && rollback.Version==old && !caches.Containers.ContainsKey(next),"Failed ground registration lost or duplicated source");
        Check(caches.DropItem(rollback,rollbackPack.Id,old,new float3(float.NaN,0,0),52,out _)==InventoryError.Invalid && rollback.Version==old,"Invalid world position lost source");
        var metadata=new RaidCorpseRpc {LootId=dropId,Version=1,CharacterIndex=RaidLootContainers.DroppedItemCharacterIndex,
            Position=new float3(1,2,3),Rotation=quaternion.identity};
        var clientBags=new RaidDeathBagClientState();clientBags.Receive(metadata);
        Check(clientBags.Bags.ContainsKey(dropId) && !metadata.RequiresDissection,"Client rejects ground bag or treats it as alien");
        metadata.CharacterIndex=RaidLootContainers.DroppedItemCharacterIndex-1;metadata.LootId++;
        clientBags.Receive(metadata);Check(clientBags.Bags.Count==1,"Invalid character marker accepted");
        Check(RoundTrip(new RaidInventoryDropRpc {RaidId=7,ExpectedVersion=9,RequestId=3,ItemId="unique-drop"}).ItemId.ToString()=="unique-drop","Generated drop RPC loses ID");
        Check(RoundTrip(new RaidActionCancelRpc {RaidId=7,RequestId=4}).RequestId==4,"Generated cancellation RPC loses request");
        var snapshot=RaidInventoryTransport.EncodeSnapshot(actor.Graph,-1,0,-1,0,"medical-id",2.5f,"cancel reason");
        var incoming=new RaidInventoryClientState();int chunks=(snapshot.Length+399)/400;
        for(int i=0;i<chunks;i++)incoming.Receive(new RaidInventoryChunkV2Rpc {RaidId=7,Sequence=1,RequestId=4,Index=i,Count=chunks,
            Json=snapshot.Substring(i*400,Math.Min(400,snapshot.Length-i*400))},RaidInventoryTransport.Decode);
        Check(incoming.MedicalItemId=="medical-id" && incoming.MedicalRemaining==2.5f && incoming.ActionFeedback=="cancel reason","Snapshot loses medical progress/reason");
        Shortcut();
        var go=new GameObject("Timed map defaults");go.SetActive(false);
        try {var map=go.AddComponent<MoonRaidMap>();Check(map.MedicalSeconds==3 && map.SearchSeconds==2,"New map defaults invalid");
            var data=new SerializedObject(map);data.FindProperty("m_MedicalSeconds").floatValue=0;data.FindProperty("m_SearchSeconds").floatValue=0;
            data.ApplyModifiedPropertiesWithoutUndo();Check(map.MedicalSeconds==3 && map.SearchSeconds==2,"Zero legacy timing must use valid defaults");}
        finally {UnityEngine.Object.DestroyImmediate(go);}
        Debug.Log($"Raid action checks passed: {s_Count} assertions covering delayed/exactly-once medical, ten interruption paths, search gate/cancel/ID memory, drop/replay/registration rollback, generated RPCs, progress snapshots, shortcut and preview input.");
    }
    private delegate void Handler(ref ServerGameSystem system,ref SystemState state,EntityCommandBuffer commands);
    private delegate void RefreshHandler(ref ServerGameSystem system,ref SystemState state,Entity connection,RaidSession session);
    private static unsafe void Process(World world,SystemHandle handle,string name)
    {
        ref var system=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
        ref var state=ref world.Unmanaged.ResolveSystemStateRef(handle);
        using var ecb=new EntityCommandBuffer(Allocator.Temp);
        ((Handler)typeof(ServerGameSystem).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Handler)))(ref system,ref state,ecb);
        ecb.Playback(world.EntityManager);
    }
    private static unsafe void Refresh(World world,SystemHandle handle,Entity connection,double now)
    {
        world.SetTime(new TimeData(now,.02f));
        ref var system=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
        ref var state=ref world.Unmanaged.ResolveSystemStateRef(handle);
        ((RefreshHandler)typeof(ServerGameSystem).GetMethod("RefreshLootAccess",BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(RefreshHandler)))(
            ref system,ref state,connection,world.EntityManager.GetComponentData<RaidSession>(connection));
    }
    private static void Request<T>(World world,Entity connection,T value) where T:unmanaged,IRpcCommand
    {
        var em=world.EntityManager;var e=em.CreateEntity();em.AddComponentData(e,value);
        em.AddComponentData(e,new ReceiveRpcCommandRequest {SourceConnection=connection});
    }
    [MenuItem("Tools/Moonkov/Check Raid Action Server Requests")]
    public static void RunServerRequests()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || MoonRaidMap.Active!=null)throw new InvalidOperationException("Run in idle Edit Mode without an active raid map.");
        s_Count=0;var go=new GameObject("Raid action RPC fixture");go.SetActive(false);
        var origin=new float3(20000,0,20000);GameObject wall=null;
        try
        {
            var map=go.AddComponent<MoonRaidMap>();var data=new SerializedObject(map);
            var positions=data.FindProperty("m_LootPositions");positions.arraySize=1;positions.GetArrayElementAtIndex(0).vector3Value=(Vector3)(origin+new float3(0,0,2));
            data.ApplyModifiedPropertiesWithoutUndo();go.SetActive(true);
            typeof(MoonRaidMap).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(map,null);
            using var world=new World("Raid action server requests",WorldFlags.GameServer);
            var em=world.EntityManager;var handle=world.GetOrCreateSystem<ServerGameSystem>();
            using var lootQuery=em.CreateEntityQuery(typeof(RaidLootWorld));
            var caches=em.GetComponentObject<RaidLootContainers>(lootQuery.GetSingletonEntity());caches.Initialize(1,null,map);
            Entity Actor(out RaidInventoryState inventory)
            {
                var player=em.CreateEntity(typeof(PredictedPlayerGhost),typeof(LocalTransform));
                em.SetComponentData(player,Injured());em.SetComponentData(player,LocalTransform.FromPosition(origin));
                var connection=em.CreateEntity(typeof(RaidSession),typeof(JoinedClient));
                em.SetComponentData(connection,new RaidSession {RaidId=7,Phase=RaidPhase.Active});
                em.SetComponentData(connection,new JoinedClient {PlayerEntity=player});
                inventory=Inventory();em.AddComponentObject(connection,inventory);return connection;
            }
            var a=Actor(out var inventory);var b=Actor(out var other);var player=em.GetComponentData<JoinedClient>(a).PlayerEntity;
            var item=inventory.Graph.Items.Single(i=>i.Code=="medkit");int initial=inventory.Graph.Version;
            world.SetTime(new TimeData(10,.02f));
            Request(world,a,new RaidMedicalUseRpc {RaidId=7,RequestId=1,ExpectedVersion=initial,ItemId=item.Id,Part=BodyPart.Auto});
            Process(world,handle,"HandleInventoryRequests");float hp=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;
            Check(inventory.MedicalItemId==item.Id && inventory.Graph.Count("medkit")==2,"Medical RPC committed early");
            Refresh(world,handle,a,12.99);Check(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth==hp,"Medical server clock bypassed");
            Refresh(world,handle,a,13);Check(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth==hp+40 && inventory.Graph.Count("medkit")==1,"Medical RPC failed final atomic commit");
            Request(world,a,new RaidMedicalUseRpc {RaidId=7,RequestId=1,ExpectedVersion=initial,ItemId=item.Id,Part=BodyPart.Auto});
            Process(world,handle,"HandleInventoryRequests");Check(inventory.Graph.Count("medkit")==1,"Medical request replay consumed again");
            Request(world,a,new RaidMedicalUseRpc {RaidId=7,RequestId=2,ExpectedVersion=inventory.Graph.Version,ItemId=item.Id,Part=BodyPart.Auto});
            Process(world,handle,"HandleInventoryRequests");Request(world,a,new RaidActionCancelRpc {RaidId=7,RequestId=3});Process(world,handle,"HandleInventoryRequests");
            Refresh(world,handle,a,20);Check(inventory.Graph.Count("medkit")==1 && !inventory.ActionPending,"Cancel RPC completed pending treatment");
            Request(world,a,new RaidInventoryDropRpc {RaidId=6,RequestId=4,ExpectedVersion=inventory.Graph.Version,ItemId=item.Id});
            Process(world,handle,"HandleInventoryRequests");Check(inventory.Error==InventoryError.Inaccessible && inventory.Graph.Count("medkit")==1,"Wrong-raid drop accepted");
            Request(world,b,new RaidInventoryDropRpc {RaidId=7,RequestId=1,ExpectedVersion=other.Graph.Version,ItemId=item.Id});
            Process(world,handle,"HandleInventoryRequests");Check(other.Error==InventoryError.Missing && inventory.Graph.Find(item.Id)!=null,"Foreign connection dropped another player's item");
            em.AddComponentData(a,new NetworkStreamRequestDisconnect());
            Request(world,a,new RaidInventoryDropRpc {RaidId=7,RequestId=5,ExpectedVersion=inventory.Graph.Version,ItemId=item.Id});Process(world,handle,"HandleInventoryRequests");
            Check(inventory.Graph.Find(item.Id)!=null && inventory.Error==InventoryError.Inaccessible,"Disconnected drop accepted");em.RemoveComponent<NetworkStreamRequestDisconnect>(a);
            var dead=em.GetComponentData<PredictedPlayerGhost>(player);var alive=dead;dead.CurrentHealth=0;em.SetComponentData(player,dead);
            Request(world,a,new RaidInventoryDropRpc {RaidId=7,RequestId=6,ExpectedVersion=inventory.Graph.Version,ItemId=item.Id});Process(world,handle,"HandleInventoryRequests");
            Check(inventory.Graph.Find(item.Id)!=null && inventory.Error==InventoryError.Inaccessible,"Dead drop accepted");em.SetComponentData(player,alive);
            Request(world,a,new RaidInventoryDropRpc {RaidId=7,RequestId=7,ExpectedVersion=initial,ItemId=item.Id});Process(world,handle,"HandleInventoryRequests");
            Check(inventory.Error==InventoryError.Stale && inventory.Graph.Find(item.Id)!=null,"Stale drop mutated inventory");
            Request(world,a,new RaidInventoryDropRpc {RaidId=7,RequestId=8,ExpectedVersion=inventory.Graph.Version,ItemId=item.Id});Process(world,handle,"HandleInventoryRequests");
            int drop=caches.DeathBags.Keys.Single();Check(inventory.Graph.Find(item.Id)==null && math.distance(caches.DeathBags[drop].Position,origin)<=RaidRules.PickupRange,"Drop RPC failed or placed outside server range");
            Request(world,a,new RaidInventoryDropRpc {RaidId=7,RequestId=8,ExpectedVersion=initial,ItemId=item.Id});Process(world,handle,"HandleInventoryRequests");Check(caches.DeathBags.Count==1,"Drop replay spawned twice");
            em.SetComponentData(player,LocalTransform.FromPosition(origin+new float3(100,0,0)));
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=9,LootId=drop});Process(world,handle,"HandleLootRequests");Check(inventory.OpenedLootId<0 && !inventory.ActionPending,"Distant ground open accepted");
            em.SetComponentData(player,LocalTransform.FromPosition(origin));
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=10,LootId=drop});Process(world,handle,"HandleLootRequests");
            Check(inventory.DissectingLootId==drop && inventory.OpenedLootId<0,"First ground search bypassed");
            var take=new RaidLootMoveRpc {RaidId=7,RequestId=11,LootId=drop,ExpectedVersion=inventory.Graph.Version,ExpectedLootVersion=caches.Containers[drop].Version,ItemId=item.Id,Parent="pockets",Region="1"};
            Request(world,a,take);Process(world,handle,"HandleLootRequests");Check(inventory.Graph.Find(item.Id)==null && inventory.Error==InventoryError.Inaccessible,"Premature pickup succeeded");
            Refresh(world,handle,a,22);Check(inventory.OpenedLootId==drop,"Server search did not finish");
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=12,LootId=-1});Process(world,handle,"HandleLootRequests");
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=13,LootId=drop});Process(world,handle,"HandleLootRequests");Check(inventory.OpenedLootId==drop && !inventory.ActionPending,"Completed search repeated after close/reopen");
            // A real wall blocks shared-cache access, even with valid versions.
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=(Vector3)(origin+new float3(0,.7f,1));wall.transform.localScale=new Vector3(2,2,.2f);UnityEngine.Physics.SyncTransforms();
            Request(world,b,new RaidLootOpenRpc {RaidId=7,RequestId=2,LootId=0});Process(world,handle,"HandleLootRequests");Check(other.OpenedLootId<0 && !other.ActionPending,"Wall search accepted");
            UnityEngine.Object.DestroyImmediate(wall);wall=null;UnityEngine.Physics.SyncTransforms();
            Request(world,b,new RaidLootOpenRpc {RaidId=7,RequestId=3,LootId=drop});Process(world,handle,"HandleLootRequests");Refresh(world,handle,b,24);
            take.RequestId=14;Request(world,a,take);Process(world,handle,"HandleLootRequests");Check(inventory.Graph.Find(item.Id)!=null && caches.Containers[drop].Find(item.Id)==null,"Ground re-pickup lost item");
            take.RequestId=4;take.ExpectedVersion=other.Graph.Version;Request(world,b,take);Process(world,handle,"HandleLootRequests");
            Check(other.Error==InventoryError.Stale && other.Graph.Find(item.Id)==null,"Competing ground claimant duplicated item");
            Debug.Log($"Raid server request checks passed: {s_Count} assertions for actual handlers, server clock/cancel/replay, wrong raid/source/dead/disconnect/stale drops, server placement, range/wall/search gates, re-open and competing pickup.");
        }
        finally {if(wall!=null)UnityEngine.Object.DestroyImmediate(wall);UnityEngine.Object.DestroyImmediate(go);}
    }
    private static T RoundTrip<T>(T value) where T:unmanaged,IRpcCommand
    {
        var type=typeof(T).Assembly.GetTypes().Single(t=>t.IsValueType && typeof(IRpcCommandSerializer<T>).IsAssignableFrom(t));
        var serializer=(IRpcCommandSerializer<T>)Activator.CreateInstance(type);
        using var bytes=new NativeArray<byte>(512,Allocator.Temp);var writer=new DataStreamWriter(bytes);
        serializer.Serialize(ref writer,new RpcSerializerState {CompressionModel=StreamCompressionModel.Default},value);
        var reader=new DataStreamReader(writer.AsNativeArray());T decoded=default;
        serializer.Deserialize(ref reader,new RpcDeserializerState {CompressionModel=StreamCompressionModel.Default},ref decoded);
        Check(!writer.HasFailedWrites,"RPC packet overflow");return decoded;
    }
    private static void Shortcut()
    {
        var graph=InventoryGraph.Create(false);var pack=graph.Equipped("Backpack");
        var nested=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="small_pack",Parent=pack.Id,Region="main"};graph.Items.Add(nested);
        var cache=LootInventoryExchange.CreateCache(0);var loot=cache.Items.First(i=>i.Code=="medkit");
        foreach(var container in graph.QuickTransferContainers().ToArray())
        {
            while(true)
            {
                var filler=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="dust",Quantity=1};
                if(!graph.FindSpace(filler,container,out var region,out var x,out var y))break;
                filler.Parent=container;filler.Region=region;filler.X=x;filler.Y=y;graph.Items.Add(filler);
            }
        }
        var joined=LootInventoryExchange.Snapshot(graph,cache);InventoryCommand sent=null;
        var root=new VisualElement();using var view=new ContainerInventoryView(root,false,c=>sent=c);
        view.Present(joined,lootId:1);
        typeof(ContainerInventoryView).GetMethod("QuickTransfer",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[] {joined.Find(loot.Id)});
        Check(sent==null && !view.Busy,"Shortcut auto-packed into nested bag after direct slots filled");
        Check(joined.Clone().TryApply(new InventoryCommand {ExpectedVersion=joined.Version,ItemId=loot.Id,Parent=nested.Id,Region="main"})==InventoryError.None,"Explicit nested placement rejected");
        Check(root.Q("slotLayer").pickingMode==PickingMode.Ignore && root.Q("inventorySlotBackpack").pickingMode==PickingMode.Position,"Preview/slot input layering wrong");
    }
}
