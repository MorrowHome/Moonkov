// 客户端 World 的 NetCode 连接入口。开始加载后从 ConnectionSettings 取目标端点，发起一次连接，再把握手或断线结果传给 GameManager。
using UnityEngine;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    // 传输层负责重试握手。一局只发起一次连接尝试。
    // 已建立的连接丢失后，要先结算这局再加入另一局。
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkReceiveSystemGroup))]
    public partial class NetcodeClientConnectionSystem : SystemBase {
        private bool m_ConnectRequested;
        protected override void OnCreate() { RequireForUpdate<NetworkStreamDriver>(); }

        protected override void OnUpdate() {
            var settings = ConnectionSettings.Instance;
            // 只有加载流程已经请求连接，或当前已经在维持连接时，这个系统才会操作网络驱动。
            if (settings.GameConnectionState != ConnectionState.State.Connecting &&
                settings.GameConnectionState != ConnectionState.State.Connected)
                return;
            // 接收系统可能还在任务线程上写连接事件。等它写完再读取这一 tick 的事件，先处理能说明具体原因的断开事件。
            CompleteDependency();
            ref var driver = ref SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRW;
            foreach (var evt in driver.ConnectionEventsForTick) {
                if (evt.State != ConnectionState.State.Disconnected) continue;
                Debug.LogWarning($"[{World.Name}] Disconnected: {evt.DisconnectReason}");
                Fail(SessionConnectionPolicy.DisconnectMessage(evt.DisconnectReason));
                return;
            }
            // 还找得到连接实体时，就以它的 NetCode 状态更新页面进度。实体存在本身并不表示握手完成。
            if (SystemAPI.TryGetSingleton(out NetworkStreamConnection connection)) {
                if (connection.CurrentState == ConnectionState.State.Disconnected)
                    Fail("Connection lost. Return to the ship and reconnect.");
                else
                    settings.GameConnectionState = connection.CurrentState == ConnectionState.State.Connected
                        ? ConnectionState.State.Connected
                        : ConnectionState.State.Connecting;
                return;
            }
            // 同一个加载过程已经发过连接请求，或原连接曾经建好。此时连接实体消失要退出这次连接流程，避免一断线就创建新会话。
            if (m_ConnectRequested || settings.GameConnectionState == ConnectionState.State.Connected) {
                Fail("Connection lost. Return to the ship and reconnect.");
                return;
            }
            // 先记下本局已经尝试连接，再调用 driver。网络底层处理握手重试，逐帧更新不用另发第二个 Connect。
            m_ConnectRequested = true;
            if (driver.Connect(EntityManager, settings.ConnectionEndpoint) == Entity.Null)
                Fail("Invalid server address or port.");
        }

        // 加载时错误由仍在等待的加载流程处理。局内没有加载任务等这个结果，所以这里直接请求回主菜单，并尝试提供重连入口。
        private static void Fail(string message) {
            ConnectionSettings.Instance.ConnectionError ??= message;
            ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Disconnected;
            // 加载中的等待会把失败传给调用方，局内则没有加载任务可接收失败。
            if (GameSettings.Instance.GameState == GlobalGameState.InGame && GameManager.Instance != null) {
                SessionReconnect.Offer();
                GameManager.Instance.ReturnToMainMenuAsync();
            }
        }
    }
}