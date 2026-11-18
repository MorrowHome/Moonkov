// 战局客户端接收服务器快照的总入口。模式、任务、结算、物品和遗体各走自己的 RPC，整理进同一实体上的客户端状态后供 RaidHUD 读取。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class RaidClientSystem : SystemBase
    {
        private Entity m_State;
        private uint m_ClassicSequence;

        protected override void OnCreate()
        {
            m_State = EntityManager.CreateEntity(typeof(RaidClientState), typeof(RaidMissionClientState), typeof(ResearchQuestClientState), typeof(ArenaClientState), typeof(ClassicClientState));
            EntityManager.AddComponentObject(m_State, new RaidInventoryClientState());
            EntityManager.AddComponentObject(m_State,new RaidDeathBagClientState());
        }

        protected override void OnUpdate()
        {
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (snapshot, received, entity) in SystemAPI.Query<RefRO<ArenaSnapshotRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                EntityManager.SetComponentData(m_State, new ArenaClientState { Snapshot = snapshot.ValueRO, Received = true });
                ecb.DestroyEntity(entity);
            }
            foreach (var (snapshot, received, entity) in SystemAPI.Query<RefRO<ClassicSnapshotV4Rpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                EntityManager.SetComponentData(m_State, new ClassicClientState
                {
                    Snapshot = snapshot.ValueRO,
                    Sequence = ++m_ClassicSequence,
                    Received = true
                });
                ecb.DestroyEntity(entity);
            }
            foreach (var (quest, received, entity) in SystemAPI.Query<RefRO<ResearchQuestSnapshotRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                var current = EntityManager.GetComponentData<ResearchQuestClientState>(m_State).Snapshot;
                if (quest.ValueRO.RaidId > current.RaidId || quest.ValueRO.RaidId == current.RaidId && quest.ValueRO.Sequence > current.Sequence)
                    EntityManager.SetComponentData(m_State, new ResearchQuestClientState { Snapshot = quest.ValueRO });
                ecb.DestroyEntity(entity);
            }
            foreach(var (mission,received,entity) in SystemAPI.Query<RefRO<RaidMissionSnapshotRpc>,RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                var current=EntityManager.GetComponentData<RaidMissionClientState>(m_State).Snapshot;
                if(mission.ValueRO.Sequence>current.Sequence || !mission.ValueRO.WorldId.Equals(current.WorldId))
                    EntityManager.SetComponentData(m_State,new RaidMissionClientState {Snapshot=mission.ValueRO});
                ecb.DestroyEntity(entity);
            }
            foreach(var (bag,received,entity) in SystemAPI.Query<RefRO<RaidGroundItemMarkerRpc>,RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();EntityManager.GetComponentObject<RaidDeathBagClientState>(m_State).Receive(bag.ValueRO);ecb.DestroyEntity(entity);
            }
            foreach(var (bag,received,entity) in SystemAPI.Query<RefRO<RaidCorpseRpc>,RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();EntityManager.GetComponentObject<RaidDeathBagClientState>(m_State).Receive(bag.ValueRO);ecb.DestroyEntity(entity);
            }
            // 库存 JSON 可能比一条 RPC 能装下的内容大。这里只转交分片，RaidInventoryClientState 收齐同一编号的整份数据后才替换库存。
            foreach (var (chunk, received, entity) in SystemAPI.Query<RefRO<RaidInventoryChunkV2Rpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                EntityManager.GetComponentObject<RaidInventoryClientState>(m_State).Receive(chunk.ValueRO, RaidInventoryTransport.Decode);
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<RaidSnapshotV2Rpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                var current = EntityManager.GetComponentData<RaidClientState>(m_State).Snapshot;
                if (rpc.ValueRO.Sequence > current.Sequence)
                {
                    EntityManager.SetComponentData(m_State, new RaidClientState { Snapshot = rpc.ValueRO });
                    // 薄客户端和临时装备玩法的库存属于该连接或该局。它们不应拿局内快照去刷新主玩家的仓库数值。
                    if (!World.IsThinClient() && (!SystemAPI.TryGetSingleton<RaidModeState>(out var mode) || !ArenaMatch.UsesTemporaryKit(mode.Mode)))
                        PlayerProfileClient.UpdateStash(rpc.ValueRO.StashDust, rpc.ValueRO.StashAlloy, rpc.ValueRO.StashCells);
                }
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
        }
    }
}
