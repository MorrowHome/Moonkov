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
    /// This system handles the client side of the player connection and character spawning.
    /// It creates the first player join request so the server knows it has to spawn a character.
    /// It handles the Spectator prefab spawn if the player is a spectator.
    /// It creates the NameTagProxy on any spawned character that is not the active player.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    [BurstCompile]
    public partial struct ClientGameSystem : ISystem {
        public void OnCreate(ref SystemState state) {
            state.RequireForUpdate<PlayerEntityPrefabs>();
            uint randomSeed = (uint)DateTime.Now.Millisecond;
            var randomEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(randomEntity, new FixedRandom {
                Random = Random.CreateFromIndex(randomSeed)
            });
        }

        public void OnUpdate(ref SystemState state) {
            if (SystemAPI.HasSingleton<DisableCharacterDynamicContacts>())
                state.EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<DisableCharacterDynamicContacts>());
            HandleSendJoinRequest(ref state);
        }

        // 有 NetworkId 只表示网络接通，还需要发一次入场请求。NetworkStreamInGame 在这里也当作已发出请求的标记，真正领到角色还要等服务端。
        private void HandleSendJoinRequest(ref SystemState state) {
            if (!PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan &&
                string.IsNullOrEmpty(ConnectionSettings.Instance.GameJoinTicket))
                return;
            if (!SystemAPI.TryGetSingletonEntity<NetworkId>(out var clientEntity) ||
                SystemAPI.HasComponent<NetworkStreamInGame>(clientEntity))
                return;
            var joinRequestEntity = state.EntityManager.CreateEntity(ComponentType.ReadOnly<ClientJoinRequestV2Rpc>(),
                ComponentType.ReadWrite<SendRpcCommandRequest>());
            string playerName = GameSettings.Instance.PlayerName;
            if (state.WorldUnmanaged.IsThinClient()) // Random names for thin clients.
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