using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
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
                    SettleDisconnectedRaid(ref state, connection);
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
