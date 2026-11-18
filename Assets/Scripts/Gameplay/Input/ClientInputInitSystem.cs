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
        int connectionId = SystemAPI.GetSingleton<NetworkId>().Value;

        // 第一步，检查指令目标实体是否已经销毁，例如玩家死亡后。
        // 角色退场或输入对象重建后，旧实体 ID 可能已经失效。先清空目标，后面的查找分支才会重新接线。
        if (commandTargetEntity != Entity.Null && !EntityManager.Exists(commandTargetEntity)) {
            // 原来接收输入的实体已经没了。先将 CommandTarget 清空，
            // 这样下一次更新就能找到新的目标。
            SystemAPI.SetSingleton(new CommandTarget { targetEntity = Entity.Null });
            commandTargetEntity = Entity.Null;
        }

        // 当前没有有效输入目标时，开始查找。
        if (commandTargetEntity == Entity.Null) {
            var localInputEntity = Entity.Null;

            // 找到分配给本客户端、还没配好输入的那个实体。
            foreach (var (playerCommandTarget, entity) in SystemAPI.Query<RefRO<PlayerCommandTarget>>()
                         .WithNone<ClientInput>().WithEntityAccess())
                // 客户端可能看见不止一个带输入标记的实体。用服务器分配的连接号只选属于本连接的那一个。
                if (playerCommandTarget.ValueRO.NetworkId == connectionId &&
                    !EntityManager.HasComponent<ClientInput>(entity)) {
                    localInputEntity = entity;
                    break;
                }

            // 找到新实体后，给它配置输入接收。
            if (localInputEntity != Entity.Null) {
                Debug.Log(
                    $"[ClientInputInitSystem] New input target found. Linking client input for entity {localInputEntity.Index.ToString()}");
                EntityManager.AddComponent<ClientInput>(localInputEntity);
                EntityManager.AddComponent<ClientMovementInput>(localInputEntity);
                // 组件保存这一帧读到的状态，缓冲保存按 tick 排列的旧命令。设成 CommandTarget 后 NetCode 才会从这个实体读指令。
                EntityManager.AddBuffer<ClientCommandInput>(localInputEntity);

                // 更新单例，让后续输入发给新目标。
                SystemAPI.SetSingleton(new CommandTarget { targetEntity = localInputEntity });
            }
        }
    }
}