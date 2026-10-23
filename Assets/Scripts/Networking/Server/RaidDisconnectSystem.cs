using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(NetworkReceiveSystemGroup))]
    [UpdateAfter(typeof(NetworkStreamReceiveSystem))]
    [UpdateBefore(typeof(NetworkGroupCommandBufferSystem))]
    public partial struct RaidDisconnectSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamDriver>();
            state.RequireForUpdate<RaidSession>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (MoonRaidMap.Active == null || BountyClashRuntime.Enabled(state.EntityManager)) return;
            // The receive job updates CurrentState before its cleanup ECB destroys
            // the connection and player. Read their inventory and pose in this gap.
            state.EntityManager.CompleteAllTrackedJobs();
            var handle = state.WorldUnmanaged.GetExistingUnmanagedSystem<ServerGameSystem>();
            if (handle == SystemHandle.Null) return;
            ref var core = ref state.WorldUnmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
            ref var coreState = ref state.WorldUnmanaged.ResolveSystemStateRef(handle);
            core.RaidDisconnectBeforeCleanup(ref coreState);
        }
    }
}
