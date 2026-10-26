using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public struct RaidDetached : IComponentData { public double Deadline; }

    public partial struct ServerGameSystem
    {
        public void RaidDisconnectBeforeCleanup(ref SystemState state)
        {
            if (MoonRaidMap.Active == null || ClashEnabled(ref state)) return;
            using var query = state.EntityManager.CreateEntityQuery(typeof(NetworkStreamConnection), typeof(RaidSession));
            using var connections = query.ToEntityArray(Allocator.Temp);
            foreach (var connection in connections)
            {
                if (state.EntityManager.GetComponentData<NetworkStreamConnection>(connection).CurrentState == ConnectionState.State.Disconnected)
                {
                    if (!RetainDisconnectedRaid(ref state, connection)) SettleDisconnectedRaid(ref state, connection);
                }
            }
        }

        private bool RetainDisconnectedRaid(ref SystemState state, Entity connection)
        {
            var manager = state.EntityManager;
            var context = Persistence(ref state);
            if (SystemAPI.HasSingleton<OfflineRaidMode>() || SystemAPI.HasSingleton<LanRaidMode>() ||
                !manager.HasComponent<JoinedClient>(connection) || !context.Profiles.TryGetValue(connection, out var profile)) return false;
            var session = manager.GetComponentData<RaidSession>(connection);
            var joined = manager.GetComponentData<JoinedClient>(connection);
            if (session.Phase != RaidPhase.Active || !manager.Exists(joined.PlayerEntity) ||
                !manager.HasComponent<PredictedPlayerGhost>(joined.PlayerEntity) ||
                manager.GetComponentData<PredictedPlayerGhost>(joined.PlayerEntity).CurrentHealth <= 0) return false;
            var holder = manager.CreateEntity();
            manager.AddComponentData(holder, new RaidDetached { Deadline = SystemAPI.Time.ElapsedTime + 90 });
            manager.AddComponentData(holder, session);
            manager.AddComponentData(holder, joined);
            manager.AddComponentData(holder, manager.GetComponentData<NetworkId>(connection));
            manager.AddComponentData(holder, new CommandTarget());
            manager.AddComponentData(holder, new NetworkStreamRequestDisconnect());
            if (!context.RebindBountyOwner(connection, holder, profile.PlayerId, session.SettlementId.ToString()))
            { manager.DestroyEntity(holder); return false; }
            if (context.Inventories.TryGetValue(holder, out var inventory))
            {
                inventory.CancelActions(); inventory.OpenedLootId = -1;
                manager.AddComponentObject(holder, inventory);
            }
            if (manager.HasBuffer<LinkedEntityGroup>(connection))
            {
                var links = manager.GetBuffer<LinkedEntityGroup>(connection);
                for (int i = links.Length - 1; i >= 0; i--) if (links[i].Value == joined.PlayerEntity) links.RemoveAt(i);
            }
            var player = joined.PlayerEntity;
            if (manager.HasComponent<PlayerClientCommandInputLookup>(player))
            {
                var input = manager.GetComponentData<PlayerClientCommandInputLookup>(player).ClientCommandInputEntity;
                if (manager.Exists(input)) manager.DestroyEntity(input);
                manager.SetComponentData(player, new PlayerClientCommandInputLookup { ClientCommandInputEntity = Entity.Null });
            }
            manager.SetComponentData(player, new GhostOwner { NetworkId = 0 });
            // Prevent the later disconnect event from settling the transferred raid again.
            manager.RemoveComponent<RaidSession>(connection);
            return true;
        }

        private bool TryResumeRetainedRaid(ref SystemState state, EntityCommandBuffer ecb, Entity connection,
            RaidPersistenceContext.Profile authenticated, RaidPersistenceContext.Join join, bool notifyLeaderboard = true)
        {
            var manager = state.EntityManager;
            using var query = manager.CreateEntityQuery(typeof(RaidDetached), typeof(RaidSession), typeof(JoinedClient));
            using var holders = query.ToEntityArray(Allocator.Temp);
            var context = Persistence(ref state);
            foreach (var holder in holders)
            {
                if (!context.Profiles.TryGetValue(holder, out var original) || original.PlayerId != authenticated.PlayerId) continue;
                var session = manager.GetComponentData<RaidSession>(holder);
                var joined = manager.GetComponentData<JoinedClient>(holder);
                bool living = manager.Exists(joined.PlayerEntity) && manager.HasComponent<PredictedPlayerGhost>(joined.PlayerEntity) &&
                    manager.GetComponentData<PredictedPlayerGhost>(joined.PlayerEntity).CurrentHealth > 0;
                if (manager.GetComponentData<RaidDetached>(holder).Deadline <= SystemAPI.Time.ElapsedTime ||
                    session.Phase != RaidPhase.Active || !living || session.TimeLeft <= 0) return false;
                if (!context.RebindBountyOwner(holder, connection, authenticated.PlayerId, session.SettlementId.ToString())) return false;
                session.ExtractionProgress = 0; session.ExtractionIndex = -1; session.SnapshotTimer = 0;
                ecb.AddComponent(connection, session); ecb.AddComponent(connection, joined);
                if (context.Inventories.TryGetValue(connection, out var inventory))
                {
                    inventory.LastSentVersion = -1; inventory.LastSentLootVersion = -1;
                    inventory.NextDeathBagResync = 0;
                    manager.AddComponentObject(connection, inventory);
                }
                int owner = manager.GetComponentData<NetworkId>(connection).Value;
                var input = ecb.Instantiate(SystemAPI.GetSingleton<PlayerEntityPrefabs>().ClientInputEntityPrefab);
                ecb.SetComponent(input, new GhostOwner { NetworkId = owner });
                ecb.AddBuffer<ClientCommandInput>(input);
                ecb.SetComponent(input, new PlayerCommandTarget { NetworkId = owner });
                ecb.SetComponent(joined.PlayerEntity, new GhostOwner { NetworkId = owner });
                ecb.SetComponent(joined.PlayerEntity, new PlayerClientCommandInputLookup { ClientCommandInputEntity = input });
                ecb.SetComponent(connection, new CommandTarget { targetEntity = input });
                ecb.AddComponent(connection, new NetworkStreamInGame());
                ecb.AppendToBuffer(connection, new LinkedEntityGroup { Value = joined.PlayerEntity });
                SystemAPI.GetSingletonBuffer<ClientsMap>().ElementAt(owner).PlayerEntity = joined.PlayerEntity;
                ecb.DestroyEntity(holder);
                if (notifyLeaderboard) AddPlayerToLeaderboard(owner, joined.PlayerName);
                return true;
            }
            return false;
        }

        private void PollRetainedRaids(ref SystemState state, EntityCommandBuffer ecb)
        {
            foreach (var (detached, session, joined, holder) in SystemAPI.Query<RefRO<RaidDetached>, RefRW<RaidSession>, RefRO<JoinedClient>>().WithEntityAccess())
            {
                session.ValueRW.TimeLeft = Unity.Mathematics.math.max(0, session.ValueRO.TimeLeft - SystemAPI.Time.DeltaTime);
                session.ValueRW.ExtractionProgress = 0;
                session.ValueRW.ExtractionIndex = -1;
                bool dead = !SystemAPI.Exists(joined.ValueRO.PlayerEntity) ||
                    !SystemAPI.HasComponent<PredictedPlayerGhost>(joined.ValueRO.PlayerEntity) ||
                    SystemAPI.GetComponent<PredictedPlayerGhost>(joined.ValueRO.PlayerEntity).CurrentHealth <= 0;
                if (!dead && session.ValueRO.TimeLeft > 0 && detached.ValueRO.Deadline > SystemAPI.Time.ElapsedTime) continue;
                FinishRaid(ref state, ecb, holder, dead || session.ValueRO.TimeLeft > 0 ? RaidPhase.Dead : RaidPhase.TimedOut);
                ecb.DestroyEntity(holder);
            }
        }

        private void SettleDisconnectedRaid(ref SystemState state, Entity connection)
        {
            var manager = state.EntityManager;
            if (!manager.Exists(connection) || !manager.HasComponent<RaidSession>(connection)) return;
            var session = manager.GetComponentData<RaidSession>(connection);
            if (session.Phase != RaidPhase.Active) return;

            var persistence = Persistence(ref state);
            // Transfer the current carried inventory while JoinedClient and the
            // player's transform still exist. BeginSave snapshots the remainder,
            // including items moved into/out of the secure case during this raid.
            DropDeathInventory(ref state, connection);
            if (!RaidRules.TrySettle(ref session, RaidPhase.Dead, !persistence.Enabled)) return;
            if (persistence.Enabled) session.SaveState = RaidSaveState.Saving;
            manager.SetComponentData(connection, session);
            if (persistence.Enabled) persistence.BeginSave(connection, session);
        }
    }
}
