using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// A disposable ECS fixture, without Play mode, drivers, sockets or real profiles.
public static class RaidDisconnectChecks
{
    private sealed class ReceiptHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var profile = request.Content == null ? new RaidPersistenceContext.Profile() :
                JsonConvert.DeserializeObject<RaidPersistenceContext.Profile>(await request.Content.ReadAsStringAsync().ConfigureAwait(false));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(profile)) };
        }
    }

    [MenuItem("Tools/Moonkov/Check Raid Disconnect Settlement")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit mode.");
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        const string scratchPrefix = "moon-raid-disconnect-";
        string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string scratch = Path.Combine(temporaryRoot, scratchPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var previousMap = MoonRaidMap.Active;
        var preview = EditorSceneManager.NewPreviewScene();
        var holder = new GameObject("Disposable expedition disconnect fixture");
        holder.SetActive(false);
        SceneManager.MoveGameObjectToScene(holder, preview);
        World world = null;
        RaidPersistenceContext persistence = null;
        try
        {
            var map = holder.AddComponent<MoonRaidMap>();
            typeof(MoonRaidMap).GetField("m_LootPositions", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, new[] { Vector3.zero });
            typeof(MoonRaidMap).GetProperty("Active").SetValue(null, map);
            world = new World("Expedition disconnect checks");
            var manager = world.EntityManager;
            var coreHandle = world.GetOrCreateSystem<ServerGameSystem>();
            ref var core = ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(coreHandle);
            var persistenceEntity = (Entity)typeof(ServerGameSystem).GetField("m_PersistenceEntity", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(core);
            string config = Path.Combine(scratch, "server.json");
            File.WriteAllText(config, JsonConvert.SerializeObject(new { BackendUrl = "http://127.0.0.1:1/", ServerKey = new string('k', 32), OutboxDirectory = "outbox" }));
            persistence = new RaidPersistenceContext(config, new ReceiptHandler());
            manager.AddComponentObject(persistenceEntity, persistence);
            var mode = manager.CreateEntity(typeof(RaidModeState));
            var hookHandle = world.GetOrCreateSystem<RaidDisconnectSystem>();
            void ReceiveGap()
            {
                ref var hook = ref world.Unmanaged.GetUnsafeSystemRef<RaidDisconnectSystem>(hookHandle);
                ref var state = ref world.Unmanaged.ResolveSystemStateRef(hookHandle);
                hook.OnUpdate(ref state);
            }
            Entity Connection(RaidPhase phase, ConnectionState.State connectionState)
            {
                var entity = manager.CreateEntity(typeof(NetworkStreamConnection), typeof(RaidSession));
                manager.SetComponentData(entity, new NetworkStreamConnection { CurrentState = connectionState });
                manager.SetComponentData(entity, new RaidSession { RaidId = 7, Phase = phase, SaveState = RaidSaveState.Saved,
                    SettlementId = Guid.NewGuid().ToString("D"), PersistentDeployment = true });
                return entity;
            }

            var connection = Connection(RaidPhase.Active, ConnectionState.State.Disconnected);
            var player = manager.CreateEntity(typeof(LocalTransform));
            var position = new float3(12345, 50000, 12345);
            manager.SetComponentData(player, LocalTransform.FromPosition(position));
            manager.AddComponentData(connection, new JoinedClient { PlayerEntity = player, CharacterIndex = GameSettings.DollSingerCharacterIndex });
            var links = manager.AddBuffer<LinkedEntityGroup>(connection);
            links.Add(new LinkedEntityGroup { Value = connection }); links.Add(new LinkedEntityGroup { Value = player });
            var graph = InventoryGraph.Create(false, false);
            string secure = Guid.NewGuid().ToString("D"), oldSafe = Guid.NewGuid().ToString("D"),
                newSafe = Guid.NewGuid().ToString("D"), loose = Guid.NewGuid().ToString("D");
            graph.Items.Add(new InventoryItem { Id = secure, Code = "secure_case", Parent = "equipment", Region = "SecureContainer" });
            Check(graph.AddSupply("dust", 1, secure, id: oldSafe) == InventoryError.None, "Cannot construct original safe inventory.");
            string originalInventory = RaidInventoryTransport.Encode(graph);
            graph.Items.RemoveAll(item => item.Id == oldSafe);
            Check(graph.AddSupply("alloy", 2, secure, true, newSafe) == InventoryError.None &&
                graph.AddSupply("dust", 4, "pockets", true, loose) == InventoryError.None, "Cannot construct current raid inventory.");
            var inventory = new RaidInventoryState { Graph = graph, RaidId = 7 };
            manager.AddComponentObject(connection, inventory); persistence.Inventories[connection] = inventory;
            var session = manager.GetComponentData<RaidSession>(connection);
            persistence.Profiles[connection] = new RaidPersistenceContext.Profile { PlayerId = Guid.NewGuid().ToString("D"),
                DeploymentId = session.SettlementId.ToString(), RaidInventoryJson = originalInventory };
            var connected = Connection(RaidPhase.Active, ConnectionState.State.Connected);
            var extracted = Connection(RaidPhase.Extracted, ConnectionState.State.Disconnected);
            var dead = Connection(RaidPhase.Dead, ConnectionState.State.Disconnected);
            using var lootQuery = manager.CreateEntityQuery(typeof(RaidLootWorld));
            var loot = manager.GetComponentObject<RaidLootContainers>(lootQuery.GetSingletonEntity());

            manager.SetComponentData(mode, new RaidModeState { Mode = RaidGameMode.BountyClash });
            ReceiveGap();
            Check(persistence.Saves.Count == 0 && loot.DeathBags.Count == 0, "Expedition hook changes Bounty Clash settlement.");
            manager.SetComponentData(mode, new RaidModeState { Mode = RaidGameMode.Expedition });
            ReceiveGap();
            session = manager.GetComponentData<RaidSession>(connection);
            Check(session.Phase == RaidPhase.Dead && session.SaveState == RaidSaveState.Saving, "Active disconnect was not settled before cleanup.");
            Check(loot.DeathBags.Count == 1 && math.distancesq(loot.DeathBags.Single().Value.Position, position) < .01f,
                "Disconnected player's current pose was lost before bag creation.");
            var bag = loot.Containers[loot.DeathBags.Single().Key];
            Check(bag.Find(loose)?.Quantity == 4 && bag.Find(newSafe) == null, "Current loose/protected inventory went to the wrong owner.");
            Check(persistence.Saves.Count == 1, "Disconnect did not queue exactly one receipt.");
            var save = persistence.Saves[session.SettlementId.ToString()];
            var savedGraph = RaidInventoryTransport.Decode(save.Payload.InventoryJson);
            Check(save.Payload.Outcome == "Dead" && savedGraph.Find(newSafe)?.Quantity == 2 &&
                savedGraph.Find(oldSafe) == null && savedGraph.Find(loose) == null, "Receipt restored deployment inventory instead of current protected contents.");
            ReceiveGap();
            Check(loot.DeathBags.Count == 1 && persistence.Saves.Count == 1 && ReferenceEquals(save, persistence.Saves[session.SettlementId.ToString()]),
                "Repeated disconnect handling duplicated a bag or receipt.");
            Check(manager.GetComponentData<RaidSession>(connected).Phase == RaidPhase.Active &&
                manager.GetComponentData<RaidSession>(extracted).Phase == RaidPhase.Extracted &&
                manager.GetComponentData<RaidSession>(dead).Phase == RaidPhase.Dead, "Connected/already settled sessions changed.");
            // Reproduce the ordinary-component and linked-character destruction
            // that makes ConnectionEventsForTick too late for raid settlement.
            using (var cleanup = new EntityCommandBuffer(Allocator.Temp))
            {
                cleanup.RemoveComponent<NetworkStreamConnection>(connection); cleanup.DestroyEntity(connection); cleanup.Playback(manager);
            }
            ReceiveGap();
            var profile = persistence.Profiles[connection];
            persistence.Abandon(new RaidPersistenceContext.Deployment { PlayerId = profile.PlayerId, DeploymentId = profile.DeploymentId });
            Check(!manager.Exists(connection) && !manager.Exists(player) && persistence.Saves.Count == 1 && save.AbandonedDeployment == null,
                "Cleanup or profile abandonment replaced the captured Dead receipt.");
            save.Task.GetAwaiter().GetResult();
            Check(Directory.GetFiles(Path.Combine(scratch, "outbox")).Length == 0, "Acknowledged fixture receipt was not cleaned.");
            Check(typeof(RaidDisconnectSystem).GetCustomAttributes<UpdateInGroupAttribute>().Any(a => a.GroupType == typeof(NetworkReceiveSystemGroup)) &&
                typeof(RaidDisconnectSystem).GetCustomAttributes<UpdateAfterAttribute>().Any(a => a.SystemType == typeof(NetworkStreamReceiveSystem)) &&
                typeof(RaidDisconnectSystem).GetCustomAttributes<UpdateBeforeAttribute>().Any(a => a.SystemType == typeof(NetworkGroupCommandBufferSystem)),
                "Receive hook ordering no longer precedes connection cleanup.");
            Debug.Log($"Raid disconnect checks passed: {checks}; current secure inventory, one death bag/Dead receipt before cleanup, idempotence and settled/mode isolation. No Play or sockets.");
        }
        finally
        {
            // Never let fixture disposal abandon artificial profiles or touch the configured user outbox.
            persistence?.Profiles.Clear();
            world?.Dispose();
            persistence?.Dispose();
            persistence?.ShutdownTask.GetAwaiter().GetResult();
            Object.DestroyImmediate(holder);
            EditorSceneManager.ClosePreviewScene(preview);
            typeof(MoonRaidMap).GetProperty("Active").SetValue(null, previousMap);
            string resolvedScratch = Path.GetFullPath(scratch);
            if (!string.Equals(Path.GetDirectoryName(resolvedScratch), temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolvedScratch).StartsWith(scratchPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to remove a directory outside the fixture's temporary root.");
            Directory.Delete(resolvedScratch, true);
        }
    }
}
