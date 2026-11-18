// Unity Multiplayer Services 通知 Session 网络配置后，由这个回调安装 EntityDriverConstructor。GameConnection 通过任务等它给出监听点和连接点，再创建 ECS World。
using System.Threading.Tasks;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Services.Multiplayer;

namespace Unity.MP_FPS {
    /// <summary>
    /// The NetworkHandler is setting up the correct driver constructors for the entity worlds
    /// and keep a copy of the NetworkEndpoints required to connect the clients and server.
    /// </summary>
    /// <remarks>
    /// The <see cref="ConnectEndpoint"/> and <see cref="ListenEndpoint"/> APIs are using an async TaskCompletionSource
    /// instead of a direct value because the <see cref="IMultiplayerService"/> APIs using the <see cref="INetworkHandler"/>
    /// might return before the handler is being called.
    /// Using a TaskCompletionSource to access the connection endpoint ensure that the Session is setup properly before
    /// accessing any of these values.
    /// </remarks>
    internal class EntityNetworkHandler : INetworkHandler {
        public Task<NetworkEndpoint> ConnectEndpoint => m_ConnectEndpoint.Task;
        public Task<NetworkEndpoint> ListenEndpoint => m_ListenEndpoint.Task;
        public Task<NetworkType> SessionConnectionType => m_SessionConnectionType.Task;
        // 服务 SDK 回调晚于会话 Promise 时，端点不能先返回默认值。用三个 TaskCompletionSource 让 GameConnection 等真正的配置。
        private readonly TaskCompletionSource<NetworkEndpoint> m_ConnectEndpoint = new();
        private readonly TaskCompletionSource<NetworkEndpoint> m_ListenEndpoint = new();
        private readonly TaskCompletionSource<NetworkType> m_SessionConnectionType = new();
        private bool m_Stopped;

        public Task StartAsync(NetworkConfiguration configuration) {
            // 加载已取消就不再替换全局 driver 构造器。否则迟来的 SDK 回调可能把下一个战局的传输配置盖过去。
            if (m_Stopped) throw new System.OperationCanceledException("Session initialization was cancelled.");
            NetworkStreamReceiveSystem.DriverConstructor = new EntityDriverConstructor(configuration);
            // 先装好 driver 的构造方法，再开放端点任务。上层取到这些结果后才能创建相配套的 World。
            m_ConnectEndpoint.SetResult(GetConnectEndpoint(configuration));
            m_ListenEndpoint.SetResult(GetListenEndpoint(configuration));
            m_SessionConnectionType.SetResult(configuration.Type);
            return Task.CompletedTask;
        }

        private static NetworkEndpoint GetConnectEndpoint(NetworkConfiguration configuration) {
            switch (configuration.Type, configuration.Role) {
                // 房主给同进程客户端返回回环端点。加入者使用发布的对外端点，两边接入同一个服务器的地址可以不同。
                case (NetworkType.Direct, NetworkRole.Host):
                    return NetworkEndpoint.LoopbackIpv4.WithPort(configuration.DirectNetworkListenAddress.Port);
                case (NetworkType.Direct, NetworkRole.Client): return configuration.DirectNetworkPublishAddress;
                case (NetworkType.Relay, NetworkRole.Host):
                    return NetworkEndpoint.LoopbackIpv4.WithPort(configuration.RelayServerData.Endpoint.Port);
                case (NetworkType.Relay, NetworkRole.Client): return configuration.RelayClientData.Endpoint;
                default: return default;
            }
        }

        private static NetworkEndpoint GetListenEndpoint(NetworkConfiguration configuration) {
            switch (configuration.Type) {
                case NetworkType.Direct: return configuration.DirectNetworkListenAddress;
                // 中继端由 Relay 参数说明去哪里转发。服务端只需通配监听，具体的会话连接数据已放入网络构造器。
                case NetworkType.Relay: return NetworkEndpoint.AnyIpv4;
                default: return default;
            }
        }

        public Task StopAsync() {
            m_Stopped = true;
            return Task.CompletedTask;
        }
    }
}