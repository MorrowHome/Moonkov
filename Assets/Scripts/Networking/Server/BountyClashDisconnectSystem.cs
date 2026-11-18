// 给极速赏金预留断线处理时机。网络收包发现断线后、NetCode 清理实体前，把出战资料和仍在地图上的角色交给 ServerGameSystem 保存。
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(NetworkReceiveSystemGroup))]
    [UpdateAfter(typeof(NetworkStreamReceiveSystem))]
    [UpdateBefore(typeof(NetworkGroupCommandBufferSystem))]
    public partial struct BountyClashDisconnectSystem : ISystem {
        public void OnCreate(ref SystemState state) {
            state.RequireForUpdate<NetworkStreamDriver>();
            state.RequireForUpdate<BountyClashWorld>();
        }

        public void OnUpdate(ref SystemState state) {
            // 比赛管理对象允许存在于普通地图，但只有极速赏金已启用才执行这条保留路径。
            if (!BountyClashRuntime.Enabled(state.EntityManager)) return;
            // 网络驱动事件由已经调度的接收 Job 写入。这里先等它完成，
            // 赶在 NetCode 随后触发的清理同步点之前处理。
            // 先等 NetCode 接收任务写好断线，再赶在清理命令缓冲前读取原连接数据。
            state.EntityManager.CompleteAllTrackedJobs();
            var handle = state.WorldUnmanaged.GetExistingUnmanagedSystem<ServerGameSystem>();
            if (handle == SystemHandle.Null) return;
            ref var core = ref state.WorldUnmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
            ref var coreState = ref state.WorldUnmanaged.ResolveSystemStateRef(handle);
            // 调用这个 World 已存在的对局系统复用参赛者、库存和角色引用，不另外开一份结算流程。
            core.ClashDisconnectBeforeCleanup(ref coreState);
        }
    }
}