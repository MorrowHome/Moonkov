using Unity.Entities;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        // Persist resources before an equipped subtree can move into a cache/death bag/receipt.
        // Resource flushes do not bump the structural inventory version.
        private void UpdateInventoryProtection(ref SystemState state, Entity connection, bool synchronize = false)
        {
            if (!SystemAPI.Exists(connection) || !SystemAPI.HasComponent<JoinedClient>(connection) ||
                !state.EntityManager.HasComponent<RaidInventoryState>(connection)) return;
            var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            if (!SystemAPI.Exists(player) || !SystemAPI.HasComponent<PredictedPlayerGhost>(player)) return;
            var health = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
            var graph = GetRaidInventory(ref state, connection).Graph;
            if (synchronize) EnergyShieldEquipment.Synchronize(ref health.ValueRW, graph);
            else EnergyShieldEquipment.Flush(ref health.ValueRW, graph);
        }
    }
}
