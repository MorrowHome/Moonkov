#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        sealed class ReconnectCheckHttp : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"features\":[\"bounty-clash-v1\"]}") });
        }

        // No drivers, peers, scene changes, PlayerPrefs or real profile writes.
        public static string RunIsolatedReconnectChecks()
        {
            if (UnityEngine.Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode.");
            string root = Path.Combine(Path.GetTempPath(), "moon-resume-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string config = Path.Combine(root, "server.json"), outbox = Path.Combine(root, "outbox");
            File.WriteAllText(config, JsonConvert.SerializeObject(new { BackendUrl = "http://127.0.0.1:1/", ServerKey = new string('k', 32), OutboxDirectory = outbox }));
            var context = new RaidPersistenceContext(config, new ReconnectCheckHttp());
            int checks = 0;
            void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); checks++; }
            using var world = new World("IsolatedRaidResumeChecks", WorldFlags.None);
            try
            {
                world.SetTime(new TimeData(1, .016f));
                var handle = world.GetOrCreateSystem<ServerGameSystem>();
                ref var core = ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
                ref var state = ref world.Unmanaged.ResolveSystemStateRef(handle);
                var em = world.EntityManager;
                em.AddComponentObject(core.m_PersistenceEntity, context);
                var prefab = em.CreateEntity(typeof(Prefab), typeof(GhostOwner), typeof(PlayerCommandTarget));
                em.CreateSingleton(new PlayerEntityPrefabs { ClientInputEntityPrefab = prefab });
                var input = em.CreateEntity();
                var player = em.CreateEntity(typeof(PredictedPlayerGhost), typeof(GhostOwner), typeof(PlayerClientCommandInputLookup), typeof(LocalTransform));
                em.SetComponentData(player, new PredictedPlayerGhost { CurrentHealth = 73, MaxHealth = 100 });
                em.SetComponentData(player, new GhostOwner { NetworkId = 1 });
                em.SetComponentData(player, new PlayerClientCommandInputLookup { ClientCommandInputEntity = input });
                em.SetComponentData(player, LocalTransform.FromPosition(17, 4, 23));
                var original = em.CreateEntity(typeof(NetworkId), typeof(RaidSession), typeof(JoinedClient));
                em.SetComponentData(original, new NetworkId { Value = 1 });
                var receipt = Guid.NewGuid().ToString("D");
                em.SetComponentData(original, new RaidSession { Phase = RaidPhase.Active, TimeLeft = 300, PersistentDeployment = true, SettlementId = receipt });
                em.SetComponentData(original, new JoinedClient { PlayerEntity = player, PlayerName = "Resume check" });
                var links = em.AddBuffer<LinkedEntityGroup>(original);
                links.Add(new LinkedEntityGroup { Value = original }); links.Add(new LinkedEntityGroup { Value = player });
                var profile = new RaidPersistenceContext.Profile { PlayerId = Guid.NewGuid().ToString("D"), DeploymentId = receipt };
                context.Profiles[original] = profile;
                var inventory = new RaidInventoryState { Graph = Inventory.InventoryGraph.Create(false, false), RaidId = 1 };
                context.Inventories[original] = inventory;
                Check(core.RetainDisconnectedRaid(ref state, original), "Active authenticated raid was not retained");
                using var retained = em.CreateEntityQuery(typeof(RaidDetached));
                var holder = retained.GetSingletonEntity();
                Check(!em.HasComponent<RaidSession>(original) && em.Exists(player) && !em.Exists(input), "Disconnect did not detach the original body/input safely");
                Check(em.GetBuffer<LinkedEntityGroup>(original).Length == 1, "Old connection still owns the retained player");
                var newConnection = em.CreateEntity(typeof(NetworkId), typeof(CommandTarget));
                em.SetComponentData(newConnection, new NetworkId { Value = 2 });
                em.AddBuffer<LinkedEntityGroup>(newConnection).Add(new LinkedEntityGroup { Value = newConnection });
                using (var maps = em.CreateEntityQuery(typeof(ClientsMap)))
                {
                    var map = em.GetBuffer<ClientsMap>(maps.GetSingletonEntity()); map.ResizeUninitialized(3);
                    map[2] = new ClientsMap { ConnectionEntity = newConnection };
                }
                using (var commands = new EntityCommandBuffer(Allocator.Temp))
                {
                    Check(!core.TryResumeRetainedRaid(ref state, commands, newConnection,
                        new RaidPersistenceContext.Profile { PlayerId = Guid.NewGuid().ToString("D") }, new RaidPersistenceContext.Join(), false), "Other account resumed a retained body");
                    world.SetTime(new TimeData(92, .016f));
                    Check(!core.TryResumeRetainedRaid(ref state, commands, newConnection, profile, new RaidPersistenceContext.Join(), false), "Expired resumption accepted");
                    world.SetTime(new TimeData(2, .016f));
                    Check(core.TryResumeRetainedRaid(ref state, commands, newConnection, profile, new RaidPersistenceContext.Join(), false), "Original authenticated owner could not resume");
                    commands.Playback(em);
                }
                Check(!em.Exists(holder) && em.GetComponentData<JoinedClient>(newConnection).PlayerEntity == player, "Resumption replaced or lost the original body");
                Check(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth == 73 && em.GetComponentData<LocalTransform>(player).Position.x == 17, "Health or position reset during resumption");
                Check(em.GetComponentData<GhostOwner>(player).NetworkId == 2 && em.Exists(em.GetComponentData<CommandTarget>(newConnection).targetEntity), "New owner/input was not restored");
                Check(ReferenceEquals(context.Inventories[newConnection], inventory) && context.Profiles[newConnection].DeploymentId == receipt && context.Deployments.Count == 0, "Resumption changed equipment or debited a new deployment");
                return $"PASS {checks} isolated ECS resumption checks: detach, authenticated owner, deadline, original body/health/position/inventory and no redeployment.";
            }
            finally
            {
                context.Profiles.Clear(); context.Inventories.Clear();
                // No HTTP writes were queued; world disposal disposes the context.
                context.Dispose();
                if (File.Exists(config)) File.Delete(config);
                if (Directory.Exists(outbox)) Directory.Delete(outbox);
                Directory.Delete(root);
            }
        }
    }
}
#endif
