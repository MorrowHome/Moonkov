using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        private void HandleRaids(ref SystemState state, EntityCommandBuffer ecb)
        {
            var map = MoonRaidMap.Active;
            if (map == null) return;
            float dt = SystemAPI.Time.DeltaTime;
            var loot = SystemAPI.GetSingletonRW<RaidLootWorld>();
            // Loot availability is shared across players; consumed caches return after a cooldown.
            while (loot.ValueRW.RespawnTimers.Length < map.LootPositions.Length)
                loot.ValueRW.RespawnTimers.Add(0);
            for (int i = 0; i < map.LootPositions.Length; i++)
            {
                if ((loot.ValueRO.TakenMask & (1u << i)) == 0) continue;
                float timer = loot.ValueRO.RespawnTimers[i] - dt;
                loot.ValueRW.RespawnTimers[i] = timer;
                if (timer <= 0) loot.ValueRW.TakenMask &= ~(1u << i);
            }

            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidPickupRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                Entity connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection) && SystemAPI.HasComponent<JoinedClient>(connection))
                {
                    var session = SystemAPI.GetComponentRW<RaidSession>(connection);
                    Entity player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                    int id = request.ValueRO.LootId;
                    if (session.ValueRO.Phase == RaidPhase.Active && session.ValueRO.RaidId == request.ValueRO.RaidId &&
                        session.ValueRO.BagCount < RaidRules.BagCapacity && id >= 0 && id < map.LootPositions.Length &&
                        (loot.ValueRO.TakenMask & (1u << id)) == 0 && TryGetLivingPosition(ref state, player, out var position) &&
                        math.distance(position, map.LootPositions[id]) <= RaidRules.PickupRange &&
                        !UnityEngine.Physics.Linecast((Vector3)position + Vector3.up * 1.4f,
                            map.LootPositions[id], LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore))
                    {
                        switch (id % 3)
                        {
                            case 0: session.ValueRW.Dust++; break;
                            case 1: session.ValueRW.Alloy++; break;
                            case 2: session.ValueRW.Cells++; break;
                        }
                        loot.ValueRW.TakenMask |= 1u << id;
                        loot.ValueRW.RespawnTimers[id] = map.LootRespawnSeconds;
                        session.ValueRW.SnapshotTimer = 0;
                    }
                }
                ecb.DestroyEntity(entity);
            }

            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidDeployRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                Entity connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection) && SystemAPI.HasComponent<JoinedClient>(connection) &&
                    !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    var session = SystemAPI.GetComponentRW<RaidSession>(connection);
                    if (RaidRules.TryDeploy(ref session.ValueRW, request.ValueRO.SettledRaidId, map.RaidDuration))
                    {
                        // Mutate immediately: duplicate requests in the same tick cannot spawn twice.
                        var joined = SystemAPI.GetComponent<JoinedClient>(connection);
                        SpawnPlayerCharacter(ref state, ecb, connection, joined.PlayerName, joined.CharacterIndex);
                    }
                }
                ecb.DestroyEntity(entity);
            }

            foreach (var (session, joined, connection) in SystemAPI.Query<RefRW<RaidSession>, RefRO<JoinedClient>>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) continue;
                if (session.ValueRO.Phase == RaidPhase.Active && TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var position))
                {
                    session.ValueRW.TimeLeft = math.max(0, session.ValueRO.TimeLeft - dt);
                    Vector3 delta = (Vector3)position - map.ExtractionPosition;
                    bool inside = new Vector2(delta.x, delta.z).sqrMagnitude <= map.ExtractionRadius * map.ExtractionRadius && math.abs(delta.y) <= 3f;
                    session.ValueRW.ExtractionProgress = inside ? session.ValueRO.ExtractionProgress + dt : 0;
                    // Expiry takes precedence over extraction; death is handled before this method.
                    if (session.ValueRO.TimeLeft <= 0)
                        FinishRaid(ref state, ecb, connection, RaidPhase.TimedOut);
                    else if (session.ValueRO.ExtractionProgress >= map.ExtractionSeconds)
                        FinishRaid(ref state, ecb, connection, RaidPhase.Extracted);
                }

                session.ValueRW.SnapshotTimer -= dt;
                if (session.ValueRO.SnapshotTimer <= 0)
                {
                    session.ValueRW.SnapshotTimer = 0.25f;
                    session.ValueRW.SnapshotSequence++;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new RaidSnapshotRpc
                    {
                        RaidId = session.ValueRO.RaidId, Sequence = session.ValueRO.SnapshotSequence, Phase = session.ValueRO.Phase,
                        SaveState = session.ValueRO.SaveState,
                        Dust = session.ValueRO.Dust, Alloy = session.ValueRO.Alloy, Cells = session.ValueRO.Cells,
                        StashDust = session.ValueRO.StashDust, StashAlloy = session.ValueRO.StashAlloy, StashCells = session.ValueRO.StashCells,
                        TimeLeft = session.ValueRO.TimeLeft, TakenMask = loot.ValueRO.TakenMask,
                        ExtractionRemaining = session.ValueRO.ExtractionProgress > 0 ? math.max(0, map.ExtractionSeconds - session.ValueRO.ExtractionProgress) : -1
                    });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
            }
        }

        private bool TryGetLivingPosition(ref SystemState state, Entity player, out float3 position)
        {
            position = default;
            if (!SystemAPI.Exists(player) || !SystemAPI.HasComponent<LocalTransform>(player) ||
                !SystemAPI.HasComponent<PredictedPlayerGhost>(player) || SystemAPI.GetComponent<PredictedPlayerGhost>(player).CurrentHealth <= 0) return false;
            position = SystemAPI.GetComponent<LocalTransform>(player).Position;
            return true;
        }

        private void FinishRaid(ref SystemState state, EntityCommandBuffer ecb, Entity connection, RaidPhase outcome)
        {
            var session = SystemAPI.GetComponentRW<RaidSession>(connection);
            var persistence = Persistence(ref state);
            if (!RaidRules.TrySettle(ref session.ValueRW, outcome, !persistence.Enabled)) return;
            if (persistence.Enabled)
            {
                session.ValueRW.SaveState = RaidSaveState.Saving;
                persistence.BeginSave(connection, session.ValueRO);
            }
            var joined = SystemAPI.GetComponentRW<JoinedClient>(connection);
            Entity player = joined.ValueRO.PlayerEntity;
            if (SystemAPI.Exists(player))
            {
                if (SystemAPI.HasComponent<PlayerClientCommandInputLookup>(player))
                {
                    Entity input = SystemAPI.GetComponent<PlayerClientCommandInputLookup>(player).ClientCommandInputEntity;
                    if (SystemAPI.Exists(input)) ecb.DestroyEntity(input);
                }
                // Remove the stale link before despawn; the connection stays alive for settlement/redeploy.
                if (state.EntityManager.HasBuffer<LinkedEntityGroup>(connection))
                {
                    var linked = state.EntityManager.GetBuffer<LinkedEntityGroup>(connection);
                    for (int i = linked.Length - 1; i >= 0; i--)
                        if (linked[i].Value == player) linked.RemoveAt(i);
                }
                ecb.DestroyEntity(player);
            }
            joined.ValueRW.PlayerEntity = Entity.Null;
            ecb.SetComponent(connection, new CommandTarget { targetEntity = Entity.Null });
            int networkId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            SystemAPI.GetSingletonBuffer<ClientsMap>().ElementAt(networkId).PlayerEntity = Entity.Null;
        }
    }
}
