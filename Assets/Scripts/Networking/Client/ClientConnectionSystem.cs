// 客户端 World 的 NetCode 连接入口。开始加载后从 ConnectionSettings 取目标端点，发起一次连接，再把握手或断线结果传给 GameManager。
using UnityEngine;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    // Transport owns handshake retries. A raid gets one connection attempt;
    // loss of an established connection must settle the raid before another join.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkReceiveSystemGroup))]
    public partial class NetcodeClientConnectionSystem : SystemBase
    {
        private bool m_ConnectRequested;

        protected override void OnCreate() => RequireForUpdate<NetworkStreamDriver>();

        protected override void OnUpdate()
        {
            var settings = ConnectionSettings.Instance;
            if (settings.GameConnectionState != ConnectionState.State.Connecting &&
                settings.GameConnectionState != ConnectionState.State.Connected) return;

            CompleteDependency();
            ref var driver = ref SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRW;
            foreach (var evt in driver.ConnectionEventsForTick)
            {
                if (evt.State != ConnectionState.State.Disconnected) continue;
                Debug.LogWarning($"[{World.Name}] Disconnected: {evt.DisconnectReason}");
                Fail(SessionConnectionPolicy.DisconnectMessage(evt.DisconnectReason));
                return;
            }

            if (SystemAPI.TryGetSingleton(out NetworkStreamConnection connection))
            {
                if (connection.CurrentState == ConnectionState.State.Disconnected)
                    Fail("Connection lost. Return to the ship and reconnect.");
                else
                    settings.GameConnectionState = connection.CurrentState == ConnectionState.State.Connected
                        ? ConnectionState.State.Connected : ConnectionState.State.Connecting;
                return;
            }

            // 同一个加载过程已经发过连接请求，或原连接曾经建好。此时连接实体消失要退出这次连接流程，避免一断线就创建新会话。
            if (m_ConnectRequested || settings.GameConnectionState == ConnectionState.State.Connected)
            {
                Fail("Connection lost. Return to the ship and reconnect.");
                return;
            }

            m_ConnectRequested = true;
            if (driver.Connect(EntityManager, settings.ConnectionEndpoint) == Entity.Null)
                Fail("Invalid server address or port.");
        }

        // 加载时错误由仍在等待的加载流程处理。局内没有加载任务等这个结果，所以这里直接请求回主菜单，并尝试提供重连入口。
        private static void Fail(string message)
        {
            ConnectionSettings.Instance.ConnectionError ??= message;
            ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Disconnected;
            // Loading waits propagate failure to their owner; gameplay has no loading task.
            if (GameSettings.Instance.GameState == GlobalGameState.InGame && GameManager.Instance != null)
            {
                SessionReconnect.Offer();
                GameManager.Instance.ReturnToMainMenuAsync();
            }
        }
    }
}
