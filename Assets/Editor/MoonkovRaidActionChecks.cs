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
using Unity.MP_FPS.Missions;
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
        actor.BeginDissection(0,10,float3.zero,health,2);
        actor.RefreshDissection(caches,true,float3.zero,health,11.99);
        Check(actor.OpenedLootId<0 && !actor.SearchedLoot.Contains(0),"Search exposes items before deadline");
        actor.CancelActions();actor.RefreshDissection(caches,true,float3.zero,health,15);
        Check(actor.OpenedLootId<0 && !actor.SearchedLoot.Contains(0),"Cancelled search completed");
        actor.BeginDissection(0,20,float3.zero,health,2);actor.RefreshDissection(caches,true,float3.zero,health,22);
        Check(actor.OpenedLootId==0 && actor.SearchedLoot.Contains(0) && !actor.SearchedLoot.Contains(1),"First-search state is not keyed by real loot ID");
        actor.OpenedLootId=-1;
        Check(!actor.NeedsSearch(caches,0) && actor.NeedsSearch(caches,1),"Closing cache lost completed search or searched different box");
        actor.BeginDissection(1,30,float3.zero,health,2);actor.RefreshDissection(caches,true,new float3(1,0,0),health,31);
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
        CheckGroundLifecycle();
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
        var origin=new float3(20000,0,20000);GameObject wall=null,floor=null,missionObject=null;
        try
        {
            floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Ground validation fixture";
            floor.transform.position=(Vector3)origin-Vector3.up*.5f;floor.transform.localScale=new Vector3(10,1,10);
            UnityEngine.Physics.SyncTransforms();
            Check(ServerGameSystem.TryFindDropGround(origin,out var grounded) && math.distance(grounded,origin)<.01f,"Flat ground rejected");
            Check(ServerGameSystem.TryFindDropGround(origin+new float3(0,.8f,0),out _),"Small airborne drop rejected");
            Check(!ServerGameSystem.TryFindDropGround(origin+new float3(0,2,0),out _),"Long falling drop accepted");
            Check(!ServerGameSystem.TryFindDropGround(origin+new float3(4.95f,0,0),out _),"Cliff edge lacks full support");
            floor.transform.localScale=new Vector3(.2f,1,10);UnityEngine.Physics.SyncTransforms();
            Check(!ServerGameSystem.TryFindDropGround(origin,out _),"Narrow gap support accepted");
            floor.transform.localScale=new Vector3(10,1,10);
            floor.transform.position=(Vector3)origin-Vector3.up*3.5f;UnityEngine.Physics.SyncTransforms();
            Check(!ServerGameSystem.TryFindDropGround(origin,out _),"Another floor through a gap accepted");
            floor.transform.position=(Vector3)origin-Vector3.up*.5f;UnityEngine.Physics.SyncTransforms();
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
            var eater=Actor(out var eatingInventory);
            CheckNutritionRequests(world,handle,eater,eatingInventory);
            var searcher=Actor(out var searchingInventory);
            CheckSearchCombatRequests(world,handle,searcher,searchingInventory,caches,origin);
            var reloader=Actor(out var reloadingInventory);
            CheckReloadInventoryRequests(world,handle,reloader,reloadingInventory,caches,origin);
            var item=inventory.Graph.Items.Single(i=>i.Code=="medkit");int initial=inventory.Graph.Version;
            floor.SetActive(false);UnityEngine.Physics.SyncTransforms();
            Request(world,a,new RaidInventoryDropRpc {RaidId=7,RequestId=1,ExpectedVersion=initial,ItemId=item.Id});
            Process(world,handle,"HandleInventoryRequests");
            Check(inventory.Graph.Find(item.Id)!=null && caches.DeathBags.Count==0 && inventory.Graph.Version==initial,"Missing ground removed inventory");
            floor.SetActive(true);UnityEngine.Physics.SyncTransforms();inventory.RequestId=0;
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
            var markers=new RaidDeathBagClientState();SendBags(world,handle,a,markers);
            Check(markers.Bags.ContainsKey(drop),"Nearby drop marker not sent");
            em.SetComponentData(player,LocalTransform.FromPosition(origin+new float3(100,0,0)));
            var hidden=SendBags(world,handle,a,markers);
            Check(!markers.Bags.ContainsKey(drop) && hidden.Any(u=>u.LootId==drop && u.Removed && !u.Deleted),"Interest exit did not hide marker");
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=9,LootId=drop});Process(world,handle,"HandleLootRequests");Check(inventory.OpenedLootId<0 && !inventory.ActionPending,"Distant ground open accepted");
            Check(em.GetComponentData<PredictedPlayerGhost>(player).LootSearchSequence==0,"Invalid loot request produced a sound");
            em.SetComponentData(player,LocalTransform.FromPosition(origin));
            SendBags(world,handle,a,markers);
            Check(markers.Bags.ContainsKey(drop),"Interest re-entry did not reconstruct marker");
            var lateJoin=new RaidDeathBagClientState();SendBags(world,handle,b,lateJoin);
            Check(lateJoin.Bags.ContainsKey(drop),"Late join missed existing drop");
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=10,LootId=drop});Process(world,handle,"HandleLootRequests");
            Check(inventory.DissectingLootId==drop && inventory.OpenedLootId<0,"First ground search bypassed");
            Check(em.GetComponentData<PredictedPlayerGhost>(player).LootSearchSequence==1,"Accepted loot search did not produce a server sound");
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=10,LootId=drop});Process(world,handle,"HandleLootRequests");
            Check(em.GetComponentData<PredictedPlayerGhost>(player).LootSearchSequence==1,"Replayed open produced another sound");
            var take=new RaidLootMoveRpc {RaidId=7,RequestId=11,LootId=drop,ExpectedVersion=inventory.Graph.Version,ExpectedLootVersion=caches.Containers[drop].Version,ItemId=item.Id,Parent="pockets",Region="1"};
            Request(world,a,take);Process(world,handle,"HandleLootRequests");Check(inventory.Graph.Find(item.Id)==null && inventory.Error==InventoryError.Inaccessible,"Premature pickup succeeded");
            Refresh(world,handle,a,22);Check(inventory.OpenedLootId==drop,"Server search did not finish");
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=12,LootId=-1});Process(world,handle,"HandleLootRequests");
            Check(em.GetComponentData<PredictedPlayerGhost>(player).LootSearchSequence==1,"Closing loot produced a search sound");
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=13,LootId=drop});Process(world,handle,"HandleLootRequests");Check(inventory.OpenedLootId==drop && !inventory.ActionPending,"Completed search repeated after close/reopen");
            Check(em.GetComponentData<PredictedPlayerGhost>(player).LootSearchSequence==2,"Reopening a searched container was silent");
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=14,LootId=drop});Process(world,handle,"HandleLootRequests");
            Check(em.GetComponentData<PredictedPlayerGhost>(player).LootSearchSequence==2,"Rapid repeated opens bypassed the sound cooldown");
            // A real wall blocks shared-cache access, even with valid versions.
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=(Vector3)(origin+new float3(0,.7f,1));wall.transform.localScale=new Vector3(2,2,.2f);UnityEngine.Physics.SyncTransforms();
            Request(world,b,new RaidLootOpenRpc {RaidId=7,RequestId=2,LootId=0});Process(world,handle,"HandleLootRequests");Check(other.OpenedLootId<0 && !other.ActionPending,"Wall search accepted");
            Check(em.GetComponentData<PredictedPlayerGhost>(em.GetComponentData<JoinedClient>(b).PlayerEntity).LootSearchSequence==0,
                "Blocked search produced a server sound");
            UnityEngine.Object.DestroyImmediate(wall);wall=null;UnityEngine.Physics.SyncTransforms();
            Request(world,b,new RaidLootOpenRpc {RaidId=7,RequestId=3,LootId=drop});Process(world,handle,"HandleLootRequests");Refresh(world,handle,b,24);
            take.RequestId=15;Request(world,a,take);Process(world,handle,"HandleLootRequests");Check(inventory.Graph.Find(item.Id)!=null && caches.Containers[drop].Find(item.Id)==null,"Ground re-pickup lost item");
            take.RequestId=4;take.ExpectedVersion=other.Graph.Version;Request(world,b,take);Process(world,handle,"HandleLootRequests");
            Check(other.Error==InventoryError.Stale && other.Graph.Find(item.Id)==null,"Competing ground claimant duplicated item");
            Request(world,a,new RaidLootOpenRpc {RaidId=7,RequestId=16,LootId=-1});
            Request(world,b,new RaidLootOpenRpc {RaidId=7,RequestId=5,LootId=-1});Process(world,handle,"HandleLootRequests");
            caches.RecycleEmptyDrops(25,new System.Collections.Generic.HashSet<int>());
            caches.RecycleEmptyDrops(35,new System.Collections.Generic.HashSet<int>());
            var deleted=SendBags(world,handle,a,markers);SendBags(world,handle,b,lateJoin);
            Check(deleted.Any(u=>u.LootId==drop && u.Removed && u.Deleted) && !markers.Bags.ContainsKey(drop) &&
                !lateJoin.Bags.ContainsKey(drop) && !inventory.SearchedLoot.Contains(drop),"Recycled drop retained ghost marker/search state");
            CheckManualMissionRequests(world,handle,a,inventory,origin,out missionObject);
            Debug.Log($"Raid server request checks passed: {s_Count} assertions for actual handlers, server clock/cancel/replay, wrong raid/source/dead/disconnect/stale drops, server placement, range/wall/search gates, re-open and competing pickup.");
        }
        finally
        {
            if(wall!=null)UnityEngine.Object.DestroyImmediate(wall);
            if(missionObject!=null)
            {
                typeof(RaidMissionSite).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(missionObject.GetComponent<RaidMissionSite>(),null);
                UnityEngine.Object.DestroyImmediate(missionObject);
            }
            if(floor!=null)UnityEngine.Object.DestroyImmediate(floor);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
    private static void CheckNutritionRequests(World world,SystemHandle handle,Entity connection,RaidInventoryState inventory)
    {
        var em=world.EntityManager;var player=em.GetComponentData<JoinedClient>(connection).PlayerEntity;
        Check(inventory.Graph.AddSupply("ration",2,InventoryCatalog.Pockets)==InventoryError.None,"Nutrition fixture supply");
        var food=inventory.Graph.Items.Single(item=>item.Code=="ration");var health=em.GetComponentData<PredictedPlayerGhost>(player);
        health.Energy=40;health.ControllerState.IsReloadingState=true;em.SetComponentData(player,health);int version=inventory.Graph.Version;
        world.SetTime(new TimeData(1,.02f));
        Request(world,connection,new RaidConsumableUseRpc {RaidId=7,RequestId=1,ExpectedVersion=version,ItemId=food.Id});
        Process(world,handle,"HandleInventoryRequests");
        Check(!inventory.ActionPending && inventory.Graph.Count("ration",true)==2,"Reloading started food RPC");
        health.ControllerState.IsReloadingState=false;em.SetComponentData(player,health);
        Request(world,connection,new RaidConsumableUseRpc {RaidId=6,RequestId=2,ExpectedVersion=version,ItemId=food.Id});
        Process(world,handle,"HandleInventoryRequests");
        Check(!inventory.ActionPending && inventory.Graph.Count("ration",true)==2,"Wrong raid started food action");
        var use=new RaidConsumableUseRpc {RaidId=7,RequestId=3,ExpectedVersion=version,ItemId=food.Id};
        Request(world,connection,use);Process(world,handle,"HandleInventoryRequests");
        Check(inventory.ActionPending && inventory.MedicalIsConsumable && inventory.Graph.Count("ration",true)==2,"Food RPC committed early");
        Request(world,connection,use);Process(world,handle,"HandleInventoryRequests");
        Refresh(world,handle,connection,3.99);
        Check(inventory.ActionPending && em.GetComponentData<PredictedPlayerGhost>(player).Energy==40,"Food bypassed server time or replayed");
        Refresh(world,handle,connection,4);
        Check(!inventory.ActionPending && inventory.Graph.Count("ration",true)==1 && em.GetComponentData<PredictedPlayerGhost>(player).Energy==85,"Food did not commit exactly once");
        Request(world,connection,use);Process(world,handle,"HandleInventoryRequests");
        Check(inventory.Graph.Count("ration",true)==1 && !inventory.ActionPending,"Food replay started another use");
        use.RequestId=4;use.ExpectedVersion=inventory.Graph.Version;
        Request(world,connection,use);Process(world,handle,"HandleInventoryRequests");
        Request(world,connection,new RaidActionCancelRpc {RaidId=7,RequestId=5});Process(world,handle,"HandleInventoryRequests");
        Refresh(world,handle,connection,8);
        Check(!inventory.ActionPending && inventory.Graph.Count("ration",true)==1 && em.GetComponentData<PredictedPlayerGhost>(player).Energy==85,"Cancelled food granted resources");
        use.RequestId=6;Request(world,connection,use);Process(world,handle,"HandleInventoryRequests");
        health=em.GetComponentData<PredictedPlayerGhost>(player);health.ShieldHitSequence++;em.SetComponentData(player,health);
        Refresh(world,handle,connection,8.1);
        Check(!inventory.ActionPending && inventory.Error==InventoryError.Inaccessible && inventory.Graph.Count("ration",true)==1,"Shield impact did not interrupt food");
    }
    private static void CheckSearchCombatRequests(World world,SystemHandle handle,Entity connection,RaidInventoryState inventory,
        RaidLootContainers caches,float3 origin)
    {
        var em=world.EntityManager;var player=em.GetComponentData<JoinedClient>(connection).PlayerEntity;
        int id=RaidLootContainers.FirstMissionCacheId+1;
        caches.Containers.Add(id,LootInventoryExchange.CreateCache(0));caches.MissionCaches.Add(id,origin+new float3(0,.25f,2));
        caches.RefillStates.Add(id,new RaidLootRefillState(RaidLootProfile.Supplies));
        var health=em.GetComponentData<PredictedPlayerGhost>(player);
        health.ShieldEquipped=true;health.ShieldCharge=100;health.LastHitTick=42;health.ControllerState.IsReloadingState=true;em.SetComponentData(player,health);
        world.SetTime(new TimeData(4,.02f));
        var open=new RaidLootOpenRpc {RaidId=7,RequestId=1,LootId=id};
        Request(world,connection,open);Process(world,handle,"HandleLootRequests");
        Check(inventory.DissectingLootId<0 && inventory.OpenedLootId<0 && !inventory.SearchedLoot.Contains(id),"Reloading started search RPC");
        health.ControllerState.IsReloadingState=false;em.SetComponentData(player,health);
        open.RequestId=2;Request(world,connection,open);Process(world,handle,"HandleLootRequests");
        Check(inventory.DissectingLootId==id && inventory.OpenedLootId<0,"Search RPC did not start");
        health=em.GetComponentData<PredictedPlayerGhost>(player);
        Check(EnergyShieldRules.Absorb(ref health,25,42,RaidDamageKind.Bullet,new float3(1,1,1))==0 && health.LastHitTick==42,
            "Shield fixture did not preserve the same tick");
        em.SetComponentData(player,health);Refresh(world,handle,connection,6);
        Check(inventory.OpenedLootId<0 && !inventory.SearchedLoot.Contains(id),"Same-tick shield hit revealed loot");
        open.RequestId=3;Request(world,connection,open);Process(world,handle,"HandleLootRequests");
        health=em.GetComponentData<PredictedPlayerGhost>(player);health.LastShotTick++;em.SetComponentData(player,health);
        Refresh(world,handle,connection,8);
        Check(inventory.OpenedLootId<0 && !inventory.SearchedLoot.Contains(id),"Shooting during search revealed loot");
        open.RequestId=4;Request(world,connection,open);Process(world,handle,"HandleLootRequests");Refresh(world,handle,connection,10);
        Check(inventory.OpenedLootId==id && inventory.SearchedLoot.Contains(id),"Safe search did not recover after interruptions");
        Request(world,connection,new RaidLootOpenRpc {RaidId=7,RequestId=5,LootId=-1});Process(world,handle,"HandleLootRequests");
        var cache=caches.Containers[id];cache.Items.RemoveAll(item=>InventoryCatalog.Get(item.Code).Kind!=ItemKind.Root);cache.Version++;
        var refill=caches.RefillStates[id];var random=new System.Random(8);
        Check(!refill.TryRefill(cache,10,1,1,false,random) && refill.TryRefill(cache,11,1,1,false,random),"Refill RPC fixture");
        world.SetTime(new TimeData(11,.02f));open.RequestId=6;Request(world,connection,open);Process(world,handle,"HandleLootRequests");
        Check(inventory.DissectingLootId==id && inventory.OpenedLootId<0,"Refilled cache reopened without searching");
        Refresh(world,handle,connection,13);
        Check(inventory.OpenedLootId==id && !inventory.NeedsSearch(caches,id),"Refilled cache search did not complete");
        caches.Containers.Remove(id);caches.MissionCaches.Remove(id);caches.RefillStates.Remove(id);inventory.OpenedLootId=-1;
    }
    private static void CheckReloadInventoryRequests(World world,SystemHandle handle,Entity connection,RaidInventoryState inventory,
        RaidLootContainers caches,float3 origin)
    {
        var em=world.EntityManager;var player=em.GetComponentData<JoinedClient>(connection).PlayerEntity;
        var graph=InventoryGraph.Create(false);inventory.Graph=graph;
        Check(graph.AddSupply("cells",1,"pockets")==InventoryError.None && graph.AddSupply("dust",1,"pockets")==InventoryError.None,"reload inventory fixture");
        string active=graph.Equipped("Primary").Id,other=graph.Equipped("Secondary").Id,pack=graph.Equipped("Backpack").Id;
        Check(graph.TryRecharge(5,30,3,out int target) && target==30,"reload inventory reservation");
        int paid=graph.CellEnergy(accessibleOnly:true),version=graph.Version;
        var actor=em.GetComponentData<PredictedPlayerGhost>(player);
        actor.InventoryWeapons=true;actor.EquippedWeaponSlot=1;actor.EquippedWeaponItem=active;actor.EquippedWeaponID=DollSingerWeapons.Halo;
        actor.CurrentAmmo=5;actor.ReloadTargetAmmo=target;actor.ReloadTimer=1.5f;actor.ControllerState.IsReloadingState=true;em.SetComponentData(player,actor);
        var move=new RaidInventoryMoveRpc {RaidId=7,RequestId=1,ExpectedVersion=version,Operation=InventoryOperation.Move,ItemId=active,Parent=pack,Region="main"};
        Request(world,connection,move);Process(world,handle,"HandleInventoryRequests");
        Check(inventory.Error==InventoryError.Inaccessible && graph.Equipped("Primary").Id==active && graph.Version==version,"move replaced charging weapon");
        move.RequestId=2;move.ItemId=other;move.Parent=InventoryCatalog.Equipment;move.Region="Primary";
        Request(world,connection,move);Process(world,handle,"HandleInventoryRequests");
        Check(inventory.Error==InventoryError.Inaccessible && graph.Equipped("Primary").Id==active && graph.Version==version,"swap replaced charging weapon");
        Request(world,connection,new RaidInventoryDropRpc {RaidId=7,RequestId=3,ExpectedVersion=version,ItemId=active});
        Process(world,handle,"HandleInventoryRequests");
        Check(inventory.Error==InventoryError.Inaccessible && graph.Find(active)!=null && graph.Version==version && caches.DeathBags.Count==0,"drop removed charging weapon");
        string dust=graph.Items.Single(i=>i.Code=="dust").Id;
        move.RequestId=4;move.ItemId=dust;move.Parent=pack;move.Region="main";
        Request(world,connection,move);Process(world,handle,"HandleInventoryRequests");
        Check(inventory.Error==InventoryError.None && graph.Find(dust).Parent==pack,"unrelated item move blocked during charge");
        int id=RaidLootContainers.FirstMissionCacheId+2;
        var cache=LootInventoryExchange.CreateCache(0);caches.Containers.Add(id,cache);caches.MissionCaches.Add(id,origin+new float3(0,.25f,2));
        inventory.OpenedLootId=id;
        var loot=new RaidLootMoveRpc {RaidId=7,RequestId=5,LootId=id,ExpectedVersion=graph.Version,ExpectedLootVersion=cache.Version,
            Operation=InventoryOperation.Move,ItemId=active,Parent=LootInventoryExchange.Root,Region="main"};
        Request(world,connection,loot);Process(world,handle,"HandleLootRequests");
        Check(inventory.Error==InventoryError.Inaccessible && graph.Find(active)!=null && cache.Find(active)==null,"loot transfer removed charging weapon");
        Check(cache.FindSpace(graph.Find(other),LootInventoryExchange.Root,out string region,out int x,out int y),"loot swap fixture space");
        loot.RequestId=6;loot.ItemId=other;loot.Region=region;loot.X=x;loot.Y=y;
        Request(world,connection,loot);Process(world,handle,"HandleLootRequests");
        Check(inventory.Error==InventoryError.None && cache.Find(other)!=null,"unrelated loot transfer blocked during charge");
        loot.RequestId=7;loot.ExpectedVersion=graph.Version;loot.ExpectedLootVersion=cache.Version;
        loot.Parent=InventoryCatalog.Equipment;loot.Region="Primary";loot.X=loot.Y=0;
        Request(world,connection,loot);Process(world,handle,"HandleLootRequests");
        Check(inventory.Error==InventoryError.Inaccessible && graph.Equipped("Primary").Id==active && cache.Find(other)!=null,"loot swap replaced charging weapon");
        actor=em.GetComponentData<PredictedPlayerGhost>(player);
        Check(actor.ControllerState.IsReloadingState && actor.ReloadTargetAmmo==target && graph.CellEnergy(accessibleOnly:true)==paid,"blocked requests lost prepaid charge");
        actor.ControllerState.IsReloadingState=false;em.SetComponentData(player,actor);
        move.RequestId=8;move.ExpectedVersion=graph.Version;move.ItemId=active;move.Parent=pack;
        Check(graph.FindSpace(graph.Find(active),pack,out region,out x,out y),"post-charge fixture space");
        move.Region=region;move.X=x;move.Y=y;Request(world,connection,move);Process(world,handle,"HandleInventoryRequests");
        Check(inventory.Error==InventoryError.None && graph.Find(active).Parent==pack,"weapon still blocked after charge");
        caches.Containers.Remove(id);caches.MissionCaches.Remove(id);inventory.OpenedLootId=-1;
    }
    private delegate void BagHandler(ref ServerGameSystem system,ref SystemState state,EntityCommandBuffer commands,Entity connection);
    private static unsafe RaidGroundItemMarkerRpc[] SendBags(World world,SystemHandle handle,Entity connection,RaidDeathBagClientState client)
    {
        ref var system=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
        ref var state=ref world.Unmanaged.ResolveSystemStateRef(handle);
        using var ecb=new EntityCommandBuffer(Allocator.Temp);
        ((BagHandler)typeof(ServerGameSystem).GetMethod("SendDeathBags",BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(BagHandler)))(ref system,ref state,ecb,connection);
        ecb.Playback(world.EntityManager);
        using var query=world.EntityManager.CreateEntityQuery(typeof(RaidGroundItemMarkerRpc));
        using var updates=query.ToComponentDataArray<RaidGroundItemMarkerRpc>(Allocator.Temp);
        var result=updates.ToArray();foreach(var update in result)client.Receive(update);
        world.EntityManager.DestroyEntity(query);return result;
    }
    private static void CheckGroundLifecycle()
    {
        var loot=new RaidLootContainers();loot.Initialize(1);
        var owner=Inventory();var item=owner.Graph.Items.Single(i=>i.Code=="medkit");
        Check(loot.DropItem(owner.Graph,item.Id,owner.Graph.Version,float3.zero,0,out int id)==InventoryError.None,"Lifecycle drop fixture");
        var held=new System.Collections.Generic.HashSet<int>();
        loot.RecycleEmptyDrops(10000,held);
        Check(loot.DeathBags.ContainsKey(id),"Non-empty drop expired by age");
        loot.Containers[id].Items.RemoveAll(i=>i.Parent==LootInventoryExchange.Root);loot.Containers[id].Version++;
        held.Add(id);loot.RecycleEmptyDrops(10001,held);loot.RecycleEmptyDrops(10020,held);
        Check(loot.DeathBags.ContainsKey(id),"Current viewer lost empty container");
        held.Clear();loot.RecycleEmptyDrops(10021,held);loot.RecycleEmptyDrops(10030.99,held);
        Check(loot.DeathBags.ContainsKey(id),"Empty drop expired before grace boundary");
        loot.RecycleEmptyDrops(10031,held);
        Check(!loot.DeathBags.ContainsKey(id) && !loot.Containers.ContainsKey(id),"Empty drop remains registered");
        var update=new RaidGroundItemMarkerRpc {ItemCode=new FixedString64Bytes("oxygen_reserve"),Sequence=1,LootId=id,Version=2,CharacterIndex=RaidLootContainers.DroppedItemCharacterIndex,Rotation=quaternion.identity};
        var client=new RaidDeathBagClientState();client.Receive(RoundTrip(update));
        Check(client.Bags.ContainsKey(id) && client.ItemCodes[id]=="oxygen_reserve","Ground marker protocol lost item model identity");
        update.Sequence=3;update.Removed=true;client.Receive(RoundTrip(update));
        update.Sequence=2;update.Removed=false;client.Receive(update);
        Check(!client.Bags.ContainsKey(id) && !client.ItemCodes.ContainsKey(id),"Out-of-order update resurrected removed marker or item identity");
        update.Sequence=4;client.Receive(update);
        Check(client.Bags.ContainsKey(id),"Interest re-entry rejected same graph version");
        update.Sequence=5;update.Removed=true;client.Receive(update);
        for(uint sequence=6;sequence<5006;sequence++)client.Receive(new RaidGroundItemMarkerRpc {Sequence=sequence,LootId=(int)sequence+100,Removed=true,Deleted=true});
        update.Sequence=1;client.Receive(update);
        var sequences=(System.Collections.IDictionary)typeof(RaidDeathBagClientState).GetField("m_Sequences",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(client);
        Check(sequences.Count<=4096 && !client.Bags.ContainsKey(id),"Client tombstones grow or old packet resurrects marker");
        var position=new Vector3(25000,0,25000);var overlaps=new RaidDeathBagClientState();
        var bagA=new RaidCorpseRpc {LootId=100,Version=1,CharacterIndex=RaidLootContainers.DroppedItemCharacterIndex,Position=position,Rotation=quaternion.identity};
        var bagB=bagA;bagB.LootId=101;overlaps.Receive(bagA);overlaps.Receive(bagB);
        var full=InventoryGraph.Create(false,false);
        for(int region=1;region<=4;region++)full.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="ration",Quantity=3,Parent="pockets",Region=region.ToString()});
        var pickup=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="medkit",Quantity=1};
        Check(full.Validate()==InventoryError.None && full.QuickTransferContainers().All(container=>!full.FindSpace(pickup,container,out _,out _,out _)),"Full carried inventory fixture has pickup space");
        Check(RaidHUD.SelectNearbyDrop(overlaps,position,100)==101 && RaidHUD.SelectNearbyDrop(overlaps,position,101)==100,"Overlapping nonempty bags cannot cycle when inventory is full");
        bagB.Version++;bagB.Empty=true;overlaps.Receive(bagB);
        Check(RaidHUD.SelectNearbyDrop(overlaps,position,100)<0,"Empty drop blocks switch candidate");
        var actor=Inventory();var health=Injured();string medicine=actor.Graph.Items.Single(i=>i.Code=="medkit").Id;
        actor.BeginDissection(0,0,float3.zero,health,2);
        Check(actor.BeginMedical(health,"foreign",actor.Graph.Version,BodyPart.Auto,float3.zero,0)==InventoryError.Missing && actor.DissectingLootId==0,"Invalid medical interrupted search");
        Check(actor.BeginMedical(health,medicine,actor.Graph.Version,BodyPart.Auto,float3.zero,0)==InventoryError.None && actor.DissectingLootId<0,"Medical did not replace search");
        actor.BeginDissection(0,0,float3.zero,health,2);actor.RefreshMedical(ref health,true,float3.zero,10);
        Check(actor.MedicalItemId==null && actor.Graph.Count("medkit")==2,"Search replacement consumed medical item");
    }
    private static void CheckManualMissionRequests(World world,SystemHandle handle,Entity connection,RaidInventoryState inventory,float3 origin,out GameObject siteObject)
    {
        siteObject=new GameObject("Manual action mission fixture");siteObject.SetActive(false);
        var site=siteObject.AddComponent<RaidMissionSite>();
        site.Author(new[] {"entry","sampler","archive","power_a","power_b"}.Select(id=>new RaidMissionAnchor {
            Id=id,Position=(Vector3)origin+new Vector3(0,1.4f,2),Approach=origin}).ToArray(),Array.Empty<RaidMissionDevice>(),null,null);
        siteObject.SetActive(true);
        Check(site.Validate()==null,"Mission fixture anchors/definitions invalid: "+site.Validate());
        typeof(RaidMissionSite).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(site,null);
        Check(RaidMissionSite.Active==site,"Mission fixture did not register active site");
        var em=world.EntityManager;using var query=em.CreateEntityQuery(typeof(RaidLootWorld));
        var context=em.GetComponentObject<RaidMissionWorld>(query.GetSingletonEntity());
        context.Site=site;context.WorldId=Guid.NewGuid().ToString("D");context.Director=new MissionDirector(1,24);
        context.Operation=new MissionOperation(context.WorldId,Guid.NewGuid().ToString("D"),1,MissionCatalog.Archive(),24);
        context.DefinitionIndex=1;context.Epoch=1;
        var player=em.GetComponentData<JoinedClient>(connection).PlayerEntity;em.SetComponentData(player,Injured());
        var session=em.GetComponentData<RaidSession>(connection);session.SettlementId="manual-action-fixture";em.SetComponentData(connection,session);
        RaidMissionActionRpc Mission(uint request,int node=0,MissionIntent intent=MissionIntent.Begin) => new RaidMissionActionRpc {
            RaidId=7,RequestId=request,Node=node,Intent=intent,Epoch=1,OperationId=context.Operation.Id};
        string medicine=inventory.Graph.Items.Single(i=>i.Code=="medkit").Id;
        void Medical(uint request,string id) {Request(world,connection,new RaidMedicalUseRpc {RaidId=7,RequestId=request,
            ExpectedVersion=inventory.Graph.Version,ItemId=id,Part=BodyPart.Auto});Process(world,handle,"HandleInventoryRequests");}
        Request(world,connection,Mission(30));Process(world,handle,"HandleMissions");
        Check(context.Actors[connection].Node==0 && !inventory.ActionPending,"Mission start fixture failed");
        Medical(31,"foreign");Check(context.Actors[connection].Node==0,"Illegal medicine cancelled mission");
        Medical(32,medicine);Check(context.Actors[connection].Node<0 && inventory.MedicalItemId==medicine,"Medical did not replace mission");
        Request(world,connection,new RaidLootOpenRpc {RaidId=7,RequestId=33,LootId=999999});Process(world,handle,"HandleLootRequests");
        Check(inventory.MedicalItemId==medicine,"Illegal loot target cancelled treatment");
        Request(world,connection,Mission(33,99));Process(world,handle,"HandleMissions");
        Check(inventory.MedicalItemId==medicine,"Illegal mission cancelled treatment");
        Request(world,connection,Mission(34));Process(world,handle,"HandleMissions");
        Check(context.Actors[connection].Node==0 && !inventory.ActionPending && inventory.Graph.Count("medkit")==1,"Mission did not replace medical without consumption");
        // Both starts arrive in the same tick; the higher shared request wins in either handler order.
        Medical(36,medicine);Request(world,connection,Mission(35));Process(world,handle,"HandleMissions");
        Check(context.Actors[connection].Node<0 && inventory.MedicalItemId==medicine,"Delayed mission replaced newer medical");
        Request(world,connection,Mission(38));Process(world,handle,"HandleMissions");Medical(37,medicine);
        Check(context.Actors[connection].Node==0 && !inventory.ActionPending,"Delayed medical replaced newer mission");
        var damaged=em.GetComponentData<PredictedPlayerGhost>(player);damaged.LastHitTick++;em.SetComponentData(player,damaged);
        Request(world,connection,Mission(38,0,MissionIntent.Renew));Process(world,handle,"HandleMissions");
        Check(context.Actors[connection].Node<0 && !context.Operation.Completed(0) && inventory.Graph.Count("medkit")==1,"Damage completed mission or consumed medicine");

        var busy=em.GetComponentData<PredictedPlayerGhost>(player);busy.ControllerState.IsReloadingState=true;em.SetComponentData(player,busy);
        Request(world,connection,Mission(39));Process(world,handle,"HandleMissions");
        Check(context.Actors[connection].Node<0 && !context.Operation.Completed(0),"Mission began while reloading");
        busy.ControllerState.IsReloadingState=false;em.SetComponentData(player,busy);
        double now=world.Time.ElapsedTime;
        for(int kind=0;kind<8;kind++)
        {
            uint request=(uint)(40+kind);
            Request(world,connection,Mission(request));Process(world,handle,"HandleMissions");
            Check(context.Actors[connection].Node==0,"Combat interruption mission did not begin: "+kind);
            now+=1.5;world.SetTime(new TimeData(now,.02f));
            Request(world,connection,Mission(request,0,MissionIntent.Renew));Process(world,handle,"HandleMissions");
            Check(context.Actors[connection].Node==0,"Mission failed before combat event: "+kind);
            var changed=em.GetComponentData<PredictedPlayerGhost>(player);
            switch(kind)
            {
                case 0:changed.LastShotTick++;break;
                case 1:changed.LastGrenadeShotTick++;break;
                case 2:changed.ShieldHitSequence++;break;
                case 3:changed.LastReloadTick++;break;
                case 4:changed.EquippedWeaponID++;break;
                case 5:changed.ControllerState.IsReloadingState=true;break;
                case 6:changed.WeaponEquipRemaining=.2f;break;
                case 7:em.SetComponentData(player,LocalTransform.FromPosition(origin+new float3(.76f,0,0)));break;
            }
            em.SetComponentData(player,changed);now+=1.6;world.SetTime(new TimeData(now,.02f));
            Request(world,connection,Mission(request,0,MissionIntent.Renew));Process(world,handle,"HandleMissions");
            Check(context.Actors[connection].Node<0 && !context.Operation.Completed(0),"Combat event completed mission at deadline: "+kind);
            changed.ControllerState.IsReloadingState=false;changed.WeaponEquipRemaining=0;em.SetComponentData(player,changed);
            em.SetComponentData(player,LocalTransform.FromPosition(origin));
        }
        Request(world,connection,Mission(50));Process(world,handle,"HandleMissions");
        now+=1.5;world.SetTime(new TimeData(now,.02f));Request(world,connection,Mission(50,0,MissionIntent.Renew));Process(world,handle,"HandleMissions");
        now+=1.6;world.SetTime(new TimeData(now,.02f));Request(world,connection,Mission(50,0,MissionIntent.Renew));Process(world,handle,"HandleMissions");
        Check(context.Operation.Completed(0),"Uninterrupted mission no longer completes");
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
