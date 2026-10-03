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
            inventory.Graph = graph; inventory.RaidId=raidId; inventory.LastSentVersion = -1;
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
                SetRaidInventory(ref state,connection,graph,session.RaidId);
            }
        }
        private void HandleInventoryRequests(ref SystemState state, EntityCommandBuffer ecb)
        {
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
                        RaidInventoryState.UpdateTotals(inventory.Graph, ref session.ValueRW);
                    }
                }
                ecb.DestroyEntity(entity);
            }
        }
        private void SendInventory(ref SystemState state, EntityCommandBuffer ecb, Entity connection, int raidId)
        {
            var inventory = GetRaidInventory(ref state, connection);
            if (inventory.LastSentVersion == inventory.Graph.Version) return;
            inventory.LastSentVersion = inventory.Graph.Version; inventory.Sequence++;
            var json = RaidInventoryTransport.Encode(inventory.Graph);
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
