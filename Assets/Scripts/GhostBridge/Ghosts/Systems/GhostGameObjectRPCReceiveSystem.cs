// 每个客户端或服务端 World 各自收集本帧收到的 RPC 实体。GhostGameObject 从这个列表按消息类型和目标取数据。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class GhostGameObjectRPCReceiveSystem : ClientServerSingletonSystem<GhostGameObjectRPCReceiveSystem> {
    public NativeList<Entity> ReceivedRPCEntities { get; private set; }

    protected override void OnCreate() {
        base.OnCreate();
        ReceivedRPCEntities = new NativeList<Entity>(Allocator.Persistent);
    }

    protected override void OnDestroy() {
        ReceivedRPCEntities.Dispose();
        base.OnDestroy();
    }

    protected override void OnUpdate() {
        if (ReceivedRPCEntities.Length > 0) ReceivedRPCEntities.Clear();
        var query = SystemAPI.QueryBuilder().WithAll<ReceiveRpcCommandRequest>().Build();
        ReceivedRPCEntities.AddRange(query.ToEntityArray(Allocator.Temp));
    }
}