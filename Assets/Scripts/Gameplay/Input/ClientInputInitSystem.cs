// 客户端连接有 NetworkId 后，找到分配给自己的输入 Ghost，为它建指令缓冲并注册成 CommandTarget。输入对象销毁时重新找。
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[UpdateInGroup(typeof(GhostInputSystemGroup), OrderFirst = true)]
public partial class ClientInputInitSystem : SystemBase {
    protected override void OnCreate() {
        // 连接编号和 NetCode 的命令目标槽都出现以后，这个系统才开始查输入对象。连接尚在握手时可以整个略过。
        RequireForUpdate<NetworkId>();
        RequireForUpdate<CommandTarget>();
    }

    protected override void OnUpdate() {
        var commandTargetEntity = SystemAPI.GetSingleton<CommandTarget>().targetEntity;
        if (commandTargetEntity != Entity.Null && EntityManager.Exists(commandTargetEntity)) return;

        int connectionId = SystemAPI.GetSingleton<NetworkId>().Value;
        var target = Entity.Null;
        foreach (var (owner, entity) in SystemAPI.Query<RefRO<PlayerCommandTarget>>()
                     .WithNone<ClientInput>().WithEntityAccess()) {
            if (owner.ValueRO.NetworkId != connectionId) continue;
            target = entity;
            break;
        }

        // 遍历结束后再加组件。结构变化会使查询失效。
        if (target != Entity.Null) {
            EntityManager.AddComponent<ClientInput>(target);
            EntityManager.AddComponent<ClientMovementInput>(target);
            EntityManager.AddBuffer<ClientCommandInput>(target);
        }
        if (target != commandTargetEntity)
            SystemAPI.SetSingleton(new CommandTarget { targetEntity = target });
    }
}
