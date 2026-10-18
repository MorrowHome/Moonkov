using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Transforms;
using UnityEngine;

// Run via Pipeline run_script in Edit Mode. Uses real UDP and the existing
// ServerGameSystem handlers, without starting or saving the formal game scene.
public static class AlienHarvestTransportChecks
{
    private sealed class UdpDrivers : INetworkStreamDriverConstructor
    {
        public void CreateClientDriver(World world,ref NetworkDriverStore store,NetDebug debug)
        {
            var driver=new NetworkDriverStore.NetworkDriverInstance {driver=NetworkDriver.Create(new UDPNetworkInterface())};
            DefaultDriverBuilder.CreateClientPipelines(ref driver);store.RegisterDriver(TransportType.Socket,driver);
        }
        public void CreateServerDriver(World world,ref NetworkDriverStore store,NetDebug debug)
        {
            var driver=new NetworkDriverStore.NetworkDriverInstance {driver=NetworkDriver.Create(new UDPNetworkInterface())};
            DefaultDriverBuilder.CreateServerPipelines(ref driver);store.RegisterDriver(TransportType.Socket,driver);
        }
    }
    private static int s_Checks;
    private static double s_Time;
    private static void Check(bool ok,string name) {if(!ok)throw new InvalidOperationException(name);s_Checks++;}
    private static World Create(string name,bool server)
    {
        var world=new World(name,server ? WorldFlags.GameServer : WorldFlags.GameClient);
        var systems=DefaultWorldInitialization.GetAllSystems(server ? WorldSystemFilterFlags.ServerSimulation : WorldSystemFilterFlags.ClientSimulation)
            .Where(t=>t.Assembly.GetName().Name.StartsWith("Unity.NetCode") || t.Assembly.GetName().Name.StartsWith("Unity.Entities") ||
                t.Assembly.GetName().Name.StartsWith("Unity.Transforms") || t.Name.Contains("RpcCommandRequestSystem") ||
                typeof(IGhostComponentSerializerRegistration).IsAssignableFrom(t)).ToList();
        TypeManager.SortSystemTypesInCreationOrder(systems);
        DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world,systems);
        var updateTime=world.GetExistingSystemManaged<UpdateWorldTimeSystem>();if(updateTime!=null)updateTime.Enabled=false;
        return world;
    }
    private static void Tick(params World[] worlds)
    {
        s_Time+=1.0/60;
        foreach(var world in worlds)
        {
            world.SetTime(new TimeData(s_Time,1f/60));
            world.GetExistingSystemManaged<InitializationSystemGroup>()?.Update();
            world.GetExistingSystemManaged<SimulationSystemGroup>()?.Update();
            world.EntityManager.CompleteAllTrackedJobs();
        }
        Thread.Sleep(2);
    }
    private static Entity[] Entities<T>(World world) where T:unmanaged,IComponentData
    {
        using var query=world.EntityManager.CreateEntityQuery(typeof(T));using var found=query.ToEntityArray(Allocator.Temp);return found.ToArray();
    }
    private static Entity Singleton<T>(World world) where T:unmanaged,IComponentData=>Entities<T>(world).Single();
    private static void Send<T>(World world,T rpc,Entity target=default) where T:unmanaged,IRpcCommand
    {
        var entity=world.EntityManager.CreateEntity();world.EntityManager.AddComponentData(entity,rpc);
        world.EntityManager.AddComponentData(entity,new SendRpcCommandRequest {TargetConnection=target});
    }
    private delegate void RequestHandler(ref ServerGameSystem system,ref SystemState state,EntityCommandBuffer commands);
    private delegate void AccessHandler(ref ServerGameSystem system,ref SystemState state,Entity connection,RaidSession session);
    private delegate void SnapshotHandler(ref ServerGameSystem system,ref SystemState state,EntityCommandBuffer commands,Entity connection,int raidId);
    private static void Handler(World world,SystemHandle handle,string name,params object[] tail)
    {
        ref var system=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
        ref var state=ref world.Unmanaged.ResolveSystemStateRef(handle);
        var method=typeof(ServerGameSystem).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic);
        if(name=="RefreshLootAccess")((AccessHandler)method.CreateDelegate(typeof(AccessHandler)))(ref system,ref state,(Entity)tail[0],(RaidSession)tail[1]);
        else if(name=="SendInventory")((SnapshotHandler)method.CreateDelegate(typeof(SnapshotHandler)))(ref system,ref state,(EntityCommandBuffer)tail[0],(Entity)tail[1],(int)tail[2]);
        else ((RequestHandler)method.CreateDelegate(typeof(RequestHandler)))(ref system,ref state,(EntityCommandBuffer)tail[0]);
    }
    private static void Read(World world,RaidInventoryClientState inventory)
    {
        foreach(var entity in Entities<RaidInventoryChunkV2Rpc>(world))
        {
            inventory.Receive(world.EntityManager.GetComponentData<RaidInventoryChunkV2Rpc>(entity),RaidInventoryTransport.Decode);
            world.EntityManager.DestroyEntity(entity);
        }
    }
    public static string Run()
    {
        if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || MoonRaidMap.Active!=null || World.All.Count!=0)
            throw new InvalidOperationException("Run in an idle Edit Mode editor with no map or game Worlds loaded.");
        s_Checks=0;s_Time=0;
        var previousDrivers=NetworkStreamReceiveSystem.DriverConstructor;
        var holder=new GameObject("Alien transport checks");holder.SetActive(false);
        var map=holder.AddComponent<MoonRaidMap>();
        typeof(MoonRaidMap).GetField("m_LootPositions",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(map,new[]{new Vector3(20000,0,20000)});
        holder.SetActive(true);
        typeof(MoonRaidMap).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(map,null);
        World server=null,a=null,b=null;
        try
        {
            NetworkStreamReceiveSystem.DriverConstructor=new UdpDrivers();
            server=Create("Alien transport server",true);a=Create("Alien transport A",false);b=Create("Alien transport B",false);
            var worlds=new[]{server,a,b};Tick(worlds);
            var endpoint=NetworkEndpoint.LoopbackIpv4.WithPort(17979);
            using(var query=server.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver)))
                Check(query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Listen(endpoint),"UDP server listens");
            foreach(var client in new[]{a,b})
            {
                using var query=client.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver));
                query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(client.EntityManager,endpoint);
            }
            void Pump(Func<bool> done,Action after=null)
            {
                for(int i=0;i<400;i++){Tick(worlds);after?.Invoke();if(done())return;}
                throw new TimeoutException("UDP fixture did not reach its expected state");
            }
            Pump(()=>Entities<NetworkId>(a).Length==1 && Entities<NetworkId>(b).Length==1 && Entities<NetworkId>(server).Length==2);
            Check(true,"two independent UDP clients connected");
            var ids=new[]{a,b}.Select(w=>w.EntityManager.GetComponentData<NetworkId>(Singleton<NetworkId>(w)).Value).ToArray();
            Check(ids[0]!=ids[1],"independent network identities");
            var connections=ids.Select(id=>Entities<NetworkId>(server).Single(e=>server.EntityManager.GetComponentData<NetworkId>(e).Value==id)).ToArray();
            var manager=server.EntityManager;
            var handle=server.GetOrCreateSystem<ServerGameSystem>();
            var actors=new Entity[2];var inventories=new RaidInventoryState[2];
            var origin=new float3(20000,0,20000);
            for(int i=0;i<2;i++)
            {
                actors[i]=manager.CreateEntity(typeof(LocalTransform),typeof(PredictedPlayerGhost));
                manager.SetComponentData(actors[i],LocalTransform.FromPosition(origin));
                var health=default(PredictedPlayerGhost);RaidHealth.Initialize(ref health);manager.SetComponentData(actors[i],health);
                manager.AddComponentData(connections[i],new JoinedClient {PlayerEntity=actors[i]});
                manager.AddComponentData(connections[i],new RaidSession {RaidId=1,Phase=RaidPhase.Active});
                inventories[i]=new RaidInventoryState {RaidId=1,Graph=InventoryGraph.Create(false,false)};
                manager.AddComponentObject(connections[i],inventories[i]);
            }
            var loot=manager.GetComponentObject<RaidLootContainers>(Singleton<RaidLootWorld>(server));loot.Initialize(1);
            int bag=loot.Drop(AlienHarvest.Create(true,new System.Random(1)),origin,characterIndex:-1);
            string organ=loot.Containers[bag].Items.Single(i=>i.Code=="alien_neural_core").Id;
            var clients=new[]{new RaidInventoryClientState(),new RaidInventoryClientState()};
            void Process(string method)
            {using var ecb=new EntityCommandBuffer(Allocator.Temp);Handler(server,handle,method,ecb);ecb.Playback(manager);}
            void Snapshot()
            {
                using var ecb=new EntityCommandBuffer(Allocator.Temp);
                foreach(var connection in connections)Handler(server,handle,"SendInventory",ecb,connection,1);
                ecb.Playback(manager);
            }
            void Receive() {Read(a,clients[0]);Read(b,clients[1]);}
            Send(a,new RaidLootOpenRpc {RaidId=1,LootId=bag,RequestId=1});Send(b,new RaidLootOpenRpc {RaidId=1,LootId=bag,RequestId=1});
            Pump(()=>Entities<RaidLootOpenRpc>(server).Length==2);Process("HandleLootRequests");
            Check(inventories.All(i=>i.DissectingLootId==bag),"both open RPCs started dissection: "+string.Join(",",inventories.Select(i=>i.DissectingLootId+"/"+i.Error)));
            Snapshot();
            Pump(()=>clients.All(c=>c.DissectingLootId==bag),Receive);
            Check(clients.All(c=>c.Graph.Find("loot")==null),"server hides organs from both clients during dissection");
            Send(a,new RaidLootMoveRpc {RaidId=1,LootId=bag,RequestId=2,ExpectedVersion=1,ExpectedLootVersion=1,ItemId=organ,Parent="pockets",Region="1"});
            Pump(()=>Entities<RaidLootMoveRpc>(server).Length==1);Process("HandleLootRequests");
            Check(inventories[0].Error==InventoryError.Inaccessible && loot.RequiresDissection(bag),"server rejects early organ theft");
            var damaged=manager.GetComponentData<PredictedPlayerGhost>(actors[0]);damaged.LastHitTick=1;manager.SetComponentData(actors[0],damaged);
            Handler(server,handle,"RefreshLootAccess",connections[0],manager.GetComponentData<RaidSession>(connections[0]));
            Check(inventories[0].DissectingLootId==-1,"damage interrupts only the affected client");
            s_Time+=AlienHarvest.DissectionSeconds;server.SetTime(new TimeData(s_Time,1f/60));
            Handler(server,handle,"RefreshLootAccess",connections[1],manager.GetComponentData<RaidSession>(connections[1]));
            Check(inventories[1].OpenedLootId==bag && loot.Containers[bag].Version==2,"other client completes one shared dissection");
            Send(a,new RaidLootOpenRpc {RaidId=1,LootId=bag,RequestId=3});Pump(()=>Entities<RaidLootOpenRpc>(server).Length==1);Process("HandleLootRequests");
            Check(inventories[0].OpenedLootId==bag && inventories[0].DissectingLootId==-1,"subsequent search shares the harvested corpse");
            int cacheVersion=loot.Containers[bag].Version;
            for(int i=0;i<2;i++)Send(worlds[i+1],new RaidLootMoveRpc {RaidId=1,LootId=bag,RequestId=4,ExpectedVersion=inventories[i].Graph.Version,
                ExpectedLootVersion=cacheVersion,ItemId=organ,Parent="pockets",Region="1"});
            Pump(()=>Entities<RaidLootMoveRpc>(server).Length==2);Process("HandleLootRequests");Snapshot();
            Pump(()=>clients.All(c=>c.RequestId==4),Receive);
            Check(inventories.Sum(i=>i.Graph.Count("alien_neural_core"))==1 && loot.Containers[bag].Count("alien_neural_core")==0,"simultaneous loot cannot duplicate the organ");
            Check(clients.Count(c=>c.Error==InventoryError.Stale)==1 && clients.Sum(c=>c.Graph.Count("alien_neural_core",true))==1,"clients receive winner and stale rejection");
            int winner=inventories[0].Graph.Count("alien_neural_core")==1 ? 0 : 1;
            int consumeVersion=inventories[winner].Graph.Version;
            var use=new RaidConsumableUseRpc {RaidId=1,RequestId=5,ExpectedVersion=consumeVersion,ItemId=organ};
            Send(worlds[winner+1],use);Pump(()=>Entities<RaidConsumableUseRpc>(server).Length==1);Process("HandleInventoryRequests");
            Check(manager.GetComponentData<PredictedPlayerGhost>(actors[winner]).AlienSurgeSeconds==60 && inventories[winner].Graph.Count("alien_neural_core")==0,
                "server consumes the received organ and grants its buff");
            int usedVersion=inventories[winner].Graph.Version;
            Send(worlds[winner+1],use);Pump(()=>Entities<RaidConsumableUseRpc>(server).Length==1);Process("HandleInventoryRequests");
            Check(inventories[winner].Graph.Version==usedVersion,"replayed consume RPC has no second effect");
            string result="Alien transport checks passed: "+s_Checks+". One UDP server, two clients, production RPC handlers, dissection interruption, shared loot race and authoritative consumption. No formal-map Play or rendered ghost acceptance.";
            Directory.CreateDirectory("Library/BuildValidation/AlienHarvest");File.WriteAllText("Library/BuildValidation/AlienHarvest/transport.txt",result);return result;
        }
        finally
        {
            b?.Dispose();a?.Dispose();server?.Dispose();NetworkStreamReceiveSystem.DriverConstructor=previousDrivers;
            typeof(MoonRaidMap).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(map,null);
            UnityEngine.Object.DestroyImmediate(holder);
        }
    }
}
