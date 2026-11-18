// 给极速赏金预留断线处理时机。网络收包发现断线后、NetCode 清理实体前，把出战资料和仍在地图上的角色交给 ServerGameSystem 保存。
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(NetworkReceiveSystemGroup))]
    [UpdateAfter(typeof(NetworkStreamReceiveSystem))]
    [UpdateBefore(typeof(NetworkGroupCommandBufferSystem))]
    public partial struct BountyClashDisconnectSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        { state.RequireForUpdate<NetworkStreamDriver>(); state.RequireForUpdate<BountyClashWorld>(); }

        public void OnUpdate(ref SystemState state)
        {
            if(!BountyClashRuntime.Enabled(state.EntityManager))return;
            // Driver events are produced by a scheduled receive job. This barrier
            // precedes the same cleanup barrier NetCode executes immediately after us.
            state.EntityManager.CompleteAllTrackedJobs();
            var handle=state.WorldUnmanaged.GetExistingUnmanagedSystem<ServerGameSystem>();
            if(handle==SystemHandle.Null)return;
            ref var core=ref state.WorldUnmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
            ref var coreState=ref state.WorldUnmanaged.ResolveSystemStateRef(handle);
            core.ClashDisconnectBeforeCleanup(ref coreState);
        }
    }
}
