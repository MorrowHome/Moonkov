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

            if (m_ConnectRequested || settings.GameConnectionState == ConnectionState.State.Connected)
            {
                Fail("Connection lost. Return to the ship and reconnect.");
                return;
            }

            m_ConnectRequested = true;
            if (driver.Connect(EntityManager, settings.ConnectionEndpoint) == Entity.Null)
                Fail("Invalid server address or port.");
        }

        private static void Fail(string message)
        {
            ConnectionSettings.Instance.ConnectionError = message;
            ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Disconnected;
            // Loading waits propagate failure to their owner; gameplay has no loading task.
            if (GameSettings.Instance.GameState == GlobalGameState.InGame && GameManager.Instance != null)
                GameManager.Instance.ReturnToMainMenuAsync();
        }
    }
}
