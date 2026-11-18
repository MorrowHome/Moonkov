// 入场请求在这里发出。网络连接已有 NetworkId 后，客户端把名字、角色、玩法和凭据发给 ServerGameSystem，由服务器决定是否生成角色。
using System;
using Unity.Burst;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Random = Unity.Mathematics.Random;

namespace Unity.MP_FPS {
    /// <summary>
    /// 这个系统处理客户端侧连接与角色生成。
    /// 它发起第一条入场请求，通知服务器生成角色。
    /// 玩家作为观战者时，由它处理 Spectator 预制体的生成。
    /// 它给已生成的其他角色创建 NameTagProxy。
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    [BurstCompile]
    public partial struct ClientGameSystem : ISystem {
        public void OnCreate(ref SystemState state) {
            state.RequireForUpdate<PlayerEntityPrefabs>();
            // 客户端 World 有单独的随机状态。这里用它给不带画面的薄客户端测试 Bot 加名称前缀。
            uint randomSeed = (uint)DateTime.Now.Millisecond;
            var randomEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(randomEntity, new FixedRandom {
                Random = Random.CreateFromIndex(randomSeed)
            });
        }

        public void OnUpdate(ref SystemState state) {
            // 客户端看到禁止角色动态接触的标记时移除它，让当前 World 的角色控制按正常接触处理。
            if (SystemAPI.HasSingleton<DisableCharacterDynamicContacts>())
                state.EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<DisableCharacterDynamicContacts>());
            HandleSendJoinRequest(ref state);
        }

        // 有 NetworkId 只表示网络接通，还需要发一次入场请求。NetworkStreamInGame 在这里也当作已发出请求的标记，真正领到角色还要等服务端。
        private void HandleSendJoinRequest(ref SystemState state) {
            // 在线账号必须先通过后端领到战局票。领票还没完成时留给下次更新，避免先发一条空凭据请求。
            if (!PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan &&
                string.IsNullOrEmpty(ConnectionSettings.Instance.GameJoinTicket))
                return;
            if (!SystemAPI.TryGetSingletonEntity<NetworkId>(out var clientEntity) ||
                SystemAPI.HasComponent<NetworkStreamInGame>(clientEntity))
                return;
            // NetCode 发送 RPC 的入口是临时实体。消息组件装内容，SendRpcCommandRequest 标记这条消息需要发送。
            var joinRequestEntity = state.EntityManager.CreateEntity(ComponentType.ReadOnly<ClientJoinRequestV2Rpc>(),
                ComponentType.ReadWrite<SendRpcCommandRequest>());
            string playerName = GameSettings.Instance.PlayerName;
            if (state.WorldUnmanaged.IsThinClient()) // 为薄客户端生成随机名字。
            {
                ref var random = ref SystemAPI.GetSingletonRW<FixedRandom>().ValueRW;
                playerName = $"[Bot {random.Random.NextInt(1, 999):000}] {playerName}";
            }
            var clientJoinRequestRpc = new ClientJoinRequestV2Rpc();
            clientJoinRequestRpc.PlayerName.CopyFromTruncated(playerName);
            clientJoinRequestRpc.CharacterIndex = GameSettings.Instance.PlayerCharacter;
            // 好友房用房间票，单人用本机模式的标记，账号联机用后端签发的游戏票。三个入口都用这条入场 RPC。
            clientJoinRequestRpc.LoginToken = PlayerProfileClient.IsOffline || PlayerProfileClient.IsLan
                ? PlayerProfileClient.Token
                : ConnectionSettings.Instance.GameJoinTicket;
            // 薄客户端不从主档带电池。请求中的携带量、玩法版本、同伴人数和重连标记都要按模式填写，服务端还会校验。
            clientJoinRequestRpc.CarryCells = state.WorldUnmanaged.IsThinClient() ? 0 : PlayerProfileClient.CarryCells;
            clientJoinRequestRpc.Mode = GameSettings.Instance.RequestedMode;
            clientJoinRequestRpc.RulesVersion = GameSettings.Instance.RequestedRulesVersion;
            clientJoinRequestRpc.ResumeOnly = ConnectionSettings.Instance.ResumeOnly;
            if (!state.WorldUnmanaged.IsThinClient() && clientJoinRequestRpc.Mode == RaidGameMode.Expedition &&
                (PlayerProfileClient.IsOffline || PlayerProfileClient.IsLan))
                clientJoinRequestRpc.ExpeditionCompanions = (byte)GameSettings.Instance.ExpeditionCompanionCount;
            state.EntityManager.SetComponentData(joinRequestEntity, clientJoinRequestRpc);
            state.EntityManager.AddComponentData(clientEntity, new NetworkStreamInGame());
        }
    }
}