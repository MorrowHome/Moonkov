// 远征的提前断线钩子。等接收任务更新完断线状态，在 NetCode 删除连接和角色前让 ServerGameSystem 读取位置、库存与出战记录。
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(NetworkReceiveSystemGroup))]
    [UpdateAfter(typeof(NetworkStreamReceiveSystem))]
    [UpdateBefore(typeof(NetworkGroupCommandBufferSystem))]
    public partial struct RaidDisconnectSystem : ISystem {
        public void OnCreate(ref SystemState state) {
            state.RequireForUpdate<NetworkStreamDriver>();
            // 服务器实际有正在管理的远征会话后才打开这条更新，空场景每帧无需查询断线。
            state.RequireForUpdate<RaidSession>();
        }

        public void OnUpdate(ref SystemState state) {
            if (MoonRaidMap.Active == null || BountyClashRuntime.Enabled(state.EntityManager)) return;
            // 接收 Job 会先更新 CurrentState，之后清理用的 ECB 才删除
            // 连接和角色。趁这个间隔读取它们的库存和姿态。
            // 接收事件是上一批网络任务写的。先等写入，之后才能读连接的断开状态，NetCode 的清理缓冲还没回放。
            state.EntityManager.CompleteAllTrackedJobs();
            var handle = state.WorldUnmanaged.GetExistingUnmanagedSystem<ServerGameSystem>();
            if (handle == SystemHandle.Null) return;
            // 拿同一个 World 已在运行的服务端系统。断线窗口只负责找合适时机，库存与结算规则仍用主系统。
            ref var core = ref state.WorldUnmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
            ref var coreState = ref state.WorldUnmanaged.ResolveSystemStateRef(handle);
            core.RaidDisconnectBeforeCleanup(ref coreState);
        }
    }
}