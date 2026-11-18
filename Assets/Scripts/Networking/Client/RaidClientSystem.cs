// 战局客户端接收服务器快照的总入口。模式、任务、结算、物品和遗体各走自己的 RPC，整理进同一实体上的客户端状态后供 RaidHUD 读取。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class RaidClientSystem : SystemBase {
        private Entity m_State;
        private uint m_ClassicSequence;

        protected override void OnCreate() {
            // 局内状态集中到一个实体，界面不用每帧从临时 RPC 找最后一条。库存和遗体状态用托管对象持有数组和物品树。
            m_State = EntityManager.CreateEntity(typeof(RaidClientState), typeof(RaidMissionClientState),
                typeof(ResearchQuestClientState), typeof(ArenaClientState), typeof(ClassicClientState));
            EntityManager.AddComponentObject(m_State, new RaidInventoryClientState());
            EntityManager.AddComponentObject(m_State, new RaidDeathBagClientState());
        }

        protected override void OnUpdate() {
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            // 竞技场和经典爆破读取各自的分数及回合数据。经典爆破还在客户端递增一次版本，方便 HUD 知道这一帧收到新快照。
            foreach (var (snapshot, received, entity) in SystemAPI
                         .Query<RefRO<ArenaSnapshotRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                EntityManager.SetComponentData(m_State,
                    new ArenaClientState { Snapshot = snapshot.ValueRO, Received = true });
                ecb.DestroyEntity(entity);
            }
            foreach (var (snapshot, received, entity) in SystemAPI
                         .Query<RefRO<ClassicSnapshotV4Rpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                EntityManager.SetComponentData(m_State, new ClassicClientState {
                    Snapshot = snapshot.ValueRO,
                    Sequence = ++m_ClassicSequence,
                    Received = true
                });
                ecb.DestroyEntity(entity);
            }
            foreach (var (quest, received, entity) in SystemAPI
                         .Query<RefRO<ResearchQuestSnapshotRpc>, RefRW<ReceiveRpcCommandRequest>>()
                         .WithEntityAccess()) {
                received.ValueRW.Consume();
                var current = EntityManager.GetComponentData<ResearchQuestClientState>(m_State).Snapshot;
                // 研究任务按玩家本人出战 ID 与序号判断新旧。下一局序号可以重新开始，只看序号会误丢第一批消息。
                if (quest.ValueRO.RaidId > current.RaidId || (quest.ValueRO.RaidId == current.RaidId &&
                                                              quest.ValueRO.Sequence > current.Sequence))
                    EntityManager.SetComponentData(m_State, new ResearchQuestClientState { Snapshot = quest.ValueRO });
                ecb.DestroyEntity(entity);
            }
            foreach (var (mission, received, entity) in SystemAPI
                         .Query<RefRO<RaidMissionSnapshotRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                var current = EntityManager.GetComponentData<RaidMissionClientState>(m_State).Snapshot;
                // 地图任务另外使用 World ID。首次换地图时用新 World 的快照，之后按该 World 自己的序号更新。
                if (mission.ValueRO.Sequence > current.Sequence || !mission.ValueRO.WorldId.Equals(current.WorldId))
                    EntityManager.SetComponentData(m_State, new RaidMissionClientState { Snapshot = mission.ValueRO });
                ecb.DestroyEntity(entity);
            }
            // 地面物品和遗体位置分别同步。这里只消费消息并更新标记，能否打开和实际取物仍要向服务端请求。
            foreach (var (bag, received, entity) in SystemAPI
                         .Query<RefRO<RaidGroundItemMarkerRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                EntityManager.GetComponentObject<RaidDeathBagClientState>(m_State).Receive(bag.ValueRO);
                ecb.DestroyEntity(entity);
            }
            foreach (var (bag, received, entity) in SystemAPI
                         .Query<RefRO<RaidCorpseRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                EntityManager.GetComponentObject<RaidDeathBagClientState>(m_State).Receive(bag.ValueRO);
                ecb.DestroyEntity(entity);
            }
            // 库存 JSON 可能比一条 RPC 能装下的内容大。这里只转交分片，RaidInventoryClientState 收齐同一编号的整份数据后才替换库存。
            foreach (var (chunk, received, entity) in SystemAPI
                         .Query<RefRO<RaidInventoryChunkV2Rpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                EntityManager.GetComponentObject<RaidInventoryClientState>(m_State)
                    .Receive(chunk.ValueRO, RaidInventoryTransport.Decode);
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<RaidSnapshotV2Rpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                var current = EntityManager.GetComponentData<RaidClientState>(m_State).Snapshot;
                // 时间、撤离和结算主页只采用更新的状态。在局内网络等待过程中，晚收到的旧状态不会让已结算画面回到进行中。
                if (rpc.ValueRO.Sequence > current.Sequence) {
                    EntityManager.SetComponentData(m_State, new RaidClientState { Snapshot = rpc.ValueRO });
                    // 薄客户端和临时装备玩法的库存属于该连接或该局。它们不应拿局内快照去刷新主玩家的仓库数值。
                    if (!World.IsThinClient() && (!SystemAPI.TryGetSingleton<RaidModeState>(out var mode) ||
                                                  !ArenaMatch.UsesTemporaryKit(mode.Mode)))
                        PlayerProfileClient.UpdateStash(rpc.ValueRO.StashDust, rpc.ValueRO.StashAlloy,
                            rpc.ValueRO.StashCells);
                }
                ecb.DestroyEntity(entity);
            }
            // 收消息时先标记已消费，再统一删除临时实体。这样遍历 ECS 查询时不用边读同一组实体边改结构。
            ecb.Playback(EntityManager);
        }
    }
}