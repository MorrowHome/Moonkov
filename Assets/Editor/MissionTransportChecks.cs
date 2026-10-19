using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Missions;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Transforms;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Real generated RPC serializers + authoritative handlers, in isolated Worlds. No Play/scene save.
public static class MissionTransportChecks
{
    private sealed class UdpDrivers : INetworkStreamDriverConstructor
    {
        public void CreateClientDriver(World world, ref NetworkDriverStore store, NetDebug debug)
        { var d = new NetworkDriverStore.NetworkDriverInstance { driver = NetworkDriver.Create(new UDPNetworkInterface()) }; DefaultDriverBuilder.CreateClientPipelines(ref d); store.RegisterDriver(TransportType.Socket, d); }
        public void CreateServerDriver(World world, ref NetworkDriverStore store, NetDebug debug)
        { var d = new NetworkDriverStore.NetworkDriverInstance { driver = NetworkDriver.Create(new UDPNetworkInterface()) }; DefaultDriverBuilder.CreateServerPipelines(ref d); store.RegisterDriver(TransportType.Socket, d); }
    }
    private static double s_Time;
    private static int s_Checks;
    private static void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); s_Checks++; }
    private static Entity[] All<T>(World w) where T : unmanaged, IComponentData
    { using var q = w.EntityManager.CreateEntityQuery(typeof(T)); using var e = q.ToEntityArray(Allocator.Temp); return e.ToArray(); }
    private static Entity Single<T>(World w) where T : unmanaged, IComponentData => All<T>(w).Single();
    private static World Create(string name, bool server)
    {
        var w = new World(name, server ? WorldFlags.GameServer : WorldFlags.GameClient);
        var types = DefaultWorldInitialization.GetAllSystems(server ? WorldSystemFilterFlags.ServerSimulation : WorldSystemFilterFlags.ClientSimulation)
            .Where(t => t.Assembly.GetName().Name.StartsWith("Unity.NetCode") || t.Assembly.GetName().Name.StartsWith("Unity.Entities") ||
                t.Assembly.GetName().Name.StartsWith("Unity.Transforms") || t.Name.Contains("RpcCommandRequestSystem") || typeof(IGhostComponentSerializerRegistration).IsAssignableFrom(t))
            .Where(t => !t.Assembly.GetName().Name.Contains("Editor") && !t.Name.Contains("Physics")).ToList();
        TypeManager.SortSystemTypesInCreationOrder(types); DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(w, types);
        var clock = w.GetExistingSystemManaged<UpdateWorldTimeSystem>(); if (clock != null) clock.Enabled = false;
        return w;
    }
    private static void Tick(params World[] worlds)
    {
        s_Time += 1.0 / 60;
        foreach (var w in worlds) { w.SetTime(new TimeData(s_Time, 1f / 60)); w.GetExistingSystemManaged<InitializationSystemGroup>()?.Update(); w.GetExistingSystemManaged<SimulationSystemGroup>()?.Update(); w.EntityManager.CompleteAllTrackedJobs(); }
        Thread.Sleep(2);
    }
    private static void Send<T>(World w, T value) where T : unmanaged, IRpcCommand
    { var e = w.EntityManager.CreateEntity(); w.EntityManager.AddComponentData(e, value); w.EntityManager.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = Single<NetworkId>(w) }); }
    private delegate void HandlerDelegate(ref ServerGameSystem system, ref SystemState state, EntityCommandBuffer commands);
    private delegate void DropDelegate(ref ServerGameSystem system, ref SystemState state, Entity connection);
    private static void Process(World w, SystemHandle handle, string method)
    {
        ref var system = ref w.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
        ref var state = ref w.Unmanaged.ResolveSystemStateRef(handle);
        using var ecb = new EntityCommandBuffer(Allocator.Temp);
        ((HandlerDelegate)typeof(ServerGameSystem).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(HandlerDelegate)))(ref system, ref state, ecb);
        ecb.Playback(w.EntityManager);
    }
    public static string Run()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || MoonRaidMap.Active || RaidMissionSite.Active || World.All.Count != 0)
            throw new InvalidOperationException("Mission transport checks require idle Edit Mode, no active map or game Worlds.");
        s_Time = 0; s_Checks = 0;
        var drivers = NetworkStreamReceiveSystem.DriverConstructor;
        var preview = EditorSceneManager.NewPreviewScene();
        var holder = new GameObject("Mission transport isolated fixture"); holder.SetActive(false); SceneManager.MoveGameObjectToScene(holder, preview);
        var map = holder.AddComponent<MoonRaidMap>(); var site = holder.AddComponent<RaidMissionSite>();
        var origin = new Vector3(20000, 5000, 20000);
        typeof(MoonRaidMap).GetField("m_LootPositions", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, new[] { origin });
        var definition = MissionCatalog.Mooncore(); definition.Objectives[1].Seconds = .2f; definition.Objectives[2].Seconds = .1f; definition.Objectives[3].Seconds = .4f;
        var anchors = new[] { "entry", "power_a", "power_b", "sampler", "archive", "tag_a", "tag_b" }.Select(id =>
            new RaidMissionAnchor { Id = id, Label = id, Position = origin + new Vector3(0, 1.4f, 1.5f), Approach = origin }).ToArray();
        typeof(RaidMissionSite).GetField("m_Anchors", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(site, anchors);
        typeof(RaidMissionSite).GetField("m_Definitions", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(site, new[] { definition, MissionCatalog.Archive() });
        holder.SetActive(true);
        // These are runtime-only components; Edit Mode fixtures invoke their registration explicitly.
        typeof(MoonRaidMap).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(map, null);
        typeof(RaidMissionSite).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(site, null);
        World server = null, a = null, b = null;
        try
        {
            NetworkStreamReceiveSystem.DriverConstructor = new UdpDrivers();
            server = Create("Mission UDP server", true); a = Create("Mission UDP A", false); b = Create("Mission UDP B", false);
            var worlds = new[] { server, a, b }; Tick(worlds);
            var endpoint = NetworkEndpoint.LoopbackIpv4.WithPort(17981);
            using (var q = server.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver))) Check(q.GetSingletonRW<NetworkStreamDriver>().ValueRW.Listen(endpoint), "listen");
            void Connect(World w) { using var q = w.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver)); q.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(w.EntityManager, endpoint); }
            void Pump(Func<bool> done) { for (int i = 0; i < 400; i++) { Tick(worlds); if (done()) return; } throw new TimeoutException("Mission UDP fixture timed out."); }
            Connect(a); Pump(() => All<NetworkId>(a).Length == 1 && All<NetworkId>(server).Length == 1);
            var manager = server.EntityManager; var handle = server.GetOrCreateSystem<ServerGameSystem>();
            var loot = manager.GetComponentObject<RaidLootContainers>(Single<RaidLootWorld>(server)); loot.Initialize(1, null, map);
            var context = manager.GetComponentObject<RaidMissionWorld>(Single<RaidLootWorld>(server));
            context.Site = site; context.WorldId = Guid.NewGuid().ToString("D"); context.Director = new MissionDirector(37, s_Time);
            context.Epoch = 1; context.DefinitionIndex = 0;
            context.Operation = new MissionOperation(context.WorldId, Guid.NewGuid().ToString("D"), 1, definition, s_Time);
            Entity AddActor(World client)
            {
                int id = client.EntityManager.GetComponentData<NetworkId>(Single<NetworkId>(client)).Value;
                var connection = All<NetworkId>(server).Single(e => manager.GetComponentData<NetworkId>(e).Value == id);
                var player = manager.CreateEntity(typeof(LocalTransform), typeof(PredictedPlayerGhost));
                manager.SetComponentData(player, LocalTransform.FromPosition(origin));
                manager.SetComponentData(player, new PredictedPlayerGhost { CurrentHealth = 100, MaxHealth = 100 });
                manager.AddComponentData(connection, new JoinedClient { PlayerEntity = player });
                manager.AddComponentData(connection, new RaidSession { RaidId = 1, Phase = RaidPhase.Active, TimeLeft = 600, SettlementId = Guid.NewGuid().ToString("D") });
                manager.AddComponentObject(connection, new RaidInventoryState { RaidId = 1, Graph = InventoryGraph.Create(false, false) });
                return connection;
            }
            var ca = AddActor(a);
            void Action(World client, uint request, int node, MissionIntent intent, int choice = 0)
            {
                Send(client, new RaidMissionActionRpc { RaidId = 1, Epoch = 1, OperationId = context.Operation.Id, RequestId = request, Node = node, Intent = intent, Choice = choice });
                Pump(() => All<RaidMissionActionRpc>(server).Length > 0); Process(server, handle, "HandleMissions");
            }
            Action(a, 1, 0, MissionIntent.Configure, 2); Check(context.Operation.CompletedMask == 0, "wrong circuit choice refused over UDP");
            Action(a, 2, 0, MissionIntent.Configure, 1); Check(context.Operation.CompletedMask == 1, "direct participation without accepting or tracking");
            Connect(b); Pump(() => All<NetworkId>(b).Length == 1 && All<NetworkId>(server).Length == 2); var cb = AddActor(b);
            Process(server, handle, "HandleMissions");
            s_Time += .3; Tick(worlds); Process(server, handle, "HandleMissions");
            Pump(() => All<RaidMissionSnapshotRpc>(b).Length > 0);
            var late = All<RaidMissionSnapshotRpc>(b).Select(e => b.EntityManager.GetComponentData<RaidMissionSnapshotRpc>(e)).OrderByDescending(s => s.Sequence).First();
            Check(late.CompletedMask == 1 && late.OperationId.ToString() == context.Operation.Id, "late UDP client receives existing facility progress");
            Action(a, 3, 1, MissionIntent.Begin); Action(b, 1, 1, MissionIntent.Begin);
            Check(context.Actors[cb].Result == MissionActionResult.Busy, "shared device contention");
            s_Time += .25; Tick(worlds); Action(a, 3, 1, MissionIntent.Renew); Check(context.Operation.CompletedMask == 3, "held maintenance step completes once");
            Action(a, 4, 2, MissionIntent.Begin);
            var playerA = manager.GetComponentData<JoinedClient>(ca).PlayerEntity;
            var ghost = manager.GetComponentData<PredictedPlayerGhost>(playerA); ghost.LastHitTick = 1; manager.SetComponentData(playerA, ghost);
            s_Time += .15; Tick(worlds); Action(a, 4, 2, MissionIntent.Renew);
            Check(context.Operation.CompletedMask == 3 && context.Actors[ca].Node == -1, "server damage cancels a held action");
            Action(a, 5, 2, MissionIntent.Begin); s_Time += .15; Tick(worlds); Action(a, 5, 2, MissionIntent.Renew);
            s_Time += .5; Tick(worlds); Process(server, handle, "HandleMissions");
            Check(context.Operation.Phase == MissionPhase.OutputReady && context.OutputLootId >= 1000000000, "automatic machine process produces a real output container");
            var cache = loot.Containers[context.OutputLootId]; Check(cache.Items.Count(i => i.Code == "mooncore_fragment") == 2, "two unique physical artifacts");
            string artifact = cache.Items.First(i => i.Code == "mooncore_fragment").Id;
            Send(a, new RaidLootOpenRpc { RaidId = 1, LootId = context.OutputLootId, RequestId = 1 });
            Send(b, new RaidLootOpenRpc { RaidId = 1, LootId = context.OutputLootId, RequestId = 1 });
            Pump(() => All<RaidLootOpenRpc>(server).Length == 2); Process(server, handle, "HandleLootRequests");
            foreach (var client in new[] { a, b }) Send(client, new RaidLootMoveRpc { RaidId = 1, LootId = context.OutputLootId, RequestId = 2,
                ExpectedVersion = 1, ExpectedLootVersion = cache.Version, ItemId = artifact, Parent = "pockets", Region = "1" });
            Pump(() => All<RaidLootMoveRpc>(server).Length == 2); Process(server, handle, "HandleLootRequests");
            var ia = manager.GetComponentObject<RaidInventoryState>(ca); var ib = manager.GetComponentObject<RaidInventoryState>(cb);
            Check(new[] { ia, ib }.Count(i => i.Graph.Find(artifact) != null) == 1 && cache.Find(artifact) == null, "UDP artifact race has one owner");
            var owner = ia.Graph.Find(artifact) != null ? ca : cb;
            ref var system = ref server.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle); ref var state = ref server.Unmanaged.ResolveSystemStateRef(handle);
            ((DropDelegate)typeof(ServerGameSystem).GetMethod("DropDeathInventory", BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(DropDelegate)))(ref system, ref state, owner);
            Check(loot.DeathBags.Count == 1 && loot.Containers.Values.Count(g => g.Find(artifact) != null) == 1, "authoritative death drop preserves one artifact");
            string operationId = context.Operation.Id;
            var redeploy = manager.GetComponentData<RaidSession>(ca); redeploy.RaidId++; manager.SetComponentData(ca, redeploy);
            Process(server, handle, "HandleMissions"); Check(context.Operation.Id == operationId && context.Operation.CompletedMask == 15, "personal redeploy does not reset facility");
            s_Time = context.Operation.Deadline + .1; Tick(worlds); Process(server, handle, "HandleMissions");
            Check(!loot.Containers.Values.Any(g => g.Items.Any(i => MissionInventory.IsArtifact(i.Code))) &&
                !ia.Graph.Items.Any(i => MissionInventory.IsArtifact(i.Code)) && !ib.Graph.Items.Any(i => MissionInventory.IsArtifact(i.Code)), "expiry clears unbanked artifacts across owners");
            return "PASS " + s_Checks + " mission UDP checks: direct participation, late join, circuit choice, contention, damage, output, race, death, redeploy and expiry.";
        }
        finally
        {
            b?.Dispose(); a?.Dispose(); server?.Dispose(); NetworkStreamReceiveSystem.DriverConstructor = drivers;
            typeof(RaidMissionSite).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(site, null);
            typeof(MoonRaidMap).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(map, null);
            UnityEngine.Object.DestroyImmediate(holder); EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
