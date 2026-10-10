using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        public static string LootCode(int index) => InventoryCatalog.LootCode(index);
        private RaidInventoryState SetRaidInventory(ref SystemState state, Entity connection, InventoryGraph graph, int raidId)
        {
            RaidInventoryState inventory;
            if (state.EntityManager.HasComponent<RaidInventoryState>(connection)) inventory = state.EntityManager.GetComponentObject<RaidInventoryState>(connection);
            else { inventory = new RaidInventoryState(); state.EntityManager.AddComponentObject(connection, inventory); }
            inventory.Graph = graph; inventory.RaidId=raidId; inventory.LastSentVersion = -1; inventory.OpenedLootId=-1;inventory.LastSentLootVersion=-1;
            Persistence(ref state).Inventories[connection] = inventory;
            return inventory;
        }
        private RaidInventoryState GetRaidInventory(ref SystemState state, Entity connection)
        {
            return state.EntityManager.GetComponentObject<RaidInventoryState>(connection);
        }
        private void EnsureRaidInventories(ref SystemState state)
        {
            // Structural additions happen before SystemAPI enumerators acquire component references.
            using var query=state.EntityManager.CreateEntityQuery(typeof(RaidSession));
            using var connections=query.ToEntityArray(Allocator.Temp);
            foreach (var connection in connections)
            {
                var session=state.EntityManager.GetComponentData<RaidSession>(connection);
                if (state.EntityManager.HasComponent<RaidInventoryState>(connection) && GetRaidInventory(ref state,connection).RaidId==session.RaidId) continue;
            var graph=InventoryGraph.Create(stash:false); graph.AddSupply("cells",session.Cells);
            if (!session.PersistentDeployment)
            { graph.AddSupply("medkit", 2); graph.AddSupply("ration", 2); graph.AddSupply("water", 2); }
                SetRaidInventory(ref state,connection,graph,session.RaidId);
            }
        }
        private void HandleInventoryRequests(ref SystemState state, EntityCommandBuffer ecb)
        {
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidConsumableUseRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection) && SystemAPI.HasComponent<JoinedClient>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection) && !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    var session = SystemAPI.GetComponent<RaidSession>(connection);
                    var inventory = GetRaidInventory(ref state, connection); var r = request.ValueRO;
                    // The source connection owns the player. Consumption shares the
                    // same monotonic request stream as medical use and item moves.
                    if (r.RequestId > inventory.RequestId)
                    {
                        inventory.RequestId = r.RequestId; inventory.Error = InventoryError.Inaccessible;
                        var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                        if (session.Phase == RaidPhase.Active && session.RaidId == r.RaidId && inventory.RaidId == r.RaidId &&
                            SystemAPI.Exists(player) && SystemAPI.HasComponent<PredictedPlayerGhost>(player))
                        {
                            var ghost = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                            inventory.Error = RaidNutrition.Consume(ref ghost.ValueRW, inventory.Graph, r.ItemId.ToString(), r.ExpectedVersion);
                        }
                        inventory.LastSentVersion = -1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidMedicalUseRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection) && SystemAPI.HasComponent<JoinedClient>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection) && !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    var session = SystemAPI.GetComponent<RaidSession>(connection);
                    var inventory = GetRaidInventory(ref state, connection);
                    var r = request.ValueRO;
                    // Request IDs share the inventory operation stream. Delayed/replayed
                    // intents cannot heal again, including after respawn/deployment.
                    if (r.RequestId > inventory.RequestId)
                    {
                        inventory.RequestId = r.RequestId;
                        inventory.Error = InventoryError.Inaccessible;
                        var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                        if (session.Phase == RaidPhase.Active && session.RaidId == r.RaidId &&
                            inventory.RaidId == r.RaidId && SystemAPI.Exists(player) && SystemAPI.HasComponent<PredictedPlayerGhost>(player))
                        {
                            var ghost = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                            inventory.Error = RaidHealth.UseMedical(ref ghost.ValueRW, inventory.Graph,
                                r.ItemId.ToString(), r.ExpectedVersion, r.Part);
                        }
                        inventory.LastSentVersion = -1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidInventoryMoveRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection))
                {
                    var session = SystemAPI.GetComponentRW<RaidSession>(connection); var inventory = GetRaidInventory(ref state, connection); var r = request.ValueRO;
                    if (r.RequestId > inventory.RequestId)
                    {
                        inventory.RequestId = r.RequestId;
                        inventory.Error = session.ValueRO.Phase != RaidPhase.Active || session.ValueRO.RaidId != r.RaidId ? InventoryError.Inaccessible
                            : inventory.Graph.TryApply(new InventoryCommand { ExpectedVersion=r.ExpectedVersion, Operation=r.Operation, ItemId=r.ItemId.ToString(),
                                Parent=r.Parent.ToString(), Region=r.Region.ToString(), TargetId=r.TargetId.ToString(), X=r.X, Y=r.Y, Rotated=r.Rotated, Quantity=r.Quantity });
                        inventory.LastSentVersion = -1;
                        if(session.ValueRO.Phase==RaidPhase.Active)RaidInventoryState.UpdateTotals(inventory.Graph, ref session.ValueRW);
                    }
                }
                ecb.DestroyEntity(entity);
            }
        }
        private void SendInventory(ref SystemState state, EntityCommandBuffer ecb, Entity connection, int raidId)
        {
            var inventory = GetRaidInventory(ref state, connection);
            InventoryGraph cache=null;
            if(inventory.OpenedLootId>=0)LootContainers(ref state).Containers.TryGetValue(inventory.OpenedLootId,out cache);
            int lootVersion=cache?.Version ?? 0;
            if (inventory.LastSentVersion == inventory.Graph.Version && inventory.LastSentLootVersion==lootVersion) return;
            inventory.LastSentVersion = inventory.Graph.Version; inventory.Sequence++;
            inventory.LastSentLootVersion=lootVersion;
            var snapshot=cache==null ? inventory.Graph : LootInventoryExchange.Snapshot(inventory.Graph,cache);
            var json = RaidInventoryTransport.EncodeSnapshot(snapshot,cache==null ? -1 : inventory.OpenedLootId,lootVersion);
            const int fragmentSize = RaidInventoryTransport.ChunkCharacters;
            int count = (json.Length + fragmentSize - 1) / fragmentSize;
            for (int index = 0; index < count; index++)
            {
                var entity = ecb.CreateEntity(); ecb.AddComponent(entity, new RaidInventoryChunkV2Rpc { RaidId=raidId, Sequence=inventory.Sequence,
                    RequestId=inventory.RequestId, Error=inventory.Error, Index=index, Count=count,
                    Json=json.Substring(index * fragmentSize, System.Math.Min(fragmentSize, json.Length-index*fragmentSize)) });
                ecb.AddComponent(entity, new SendRpcCommandRequest { TargetConnection=connection });
            }
        }
    }
}
