// 创建客户端和服务端 World 时，NetCode 会请它选好底层 driver。单人和房主本机可走进程内连接，其他情况按入口选 UDP、Relay 或已打通的 ICE。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Multiplayer;
using Unity.MP_FPS.EmbeddedNetworking;

namespace Unity.MP_FPS {
    /// <summary>
    /// This struct contains the necessary information from the <see cref="NetworkConfiguration"/> instance
    /// that are required to create the right <see cref="NetworkStreamDriver"/> on world creation.
    /// </summary>
    internal struct EntityNetworkConfiguration {
        public NetworkRole Role;
        public NetworkType Type;
        public RelayServerData RelayClientData;
        public RelayServerData RelayServerData;
    }

    /// <summary>
    /// This class creates the required <see cref="NetworkStreamDriver"/> on world creation for the Server and Client world.
    /// </summary>
    /// <remarks>
    /// It is using the <see cref="NetworkConfiguration"/> information when used from a Session within <see cref="GameManager.StartGameAsync"/>.
    /// When starting from <see cref="GameBootstrap"/>, it will instead default drivers setup to <see cref="NetworkType.Direct"/>.
    /// </remarks>
    internal class EntityDriverConstructor : INetworkStreamDriverConstructor {
        private EntityNetworkConfiguration m_Configuration;
        private readonly bool m_SecureDirect;
        private readonly EmbeddedIceTransport m_EmbeddedIce;
        private readonly LanPeerRoom m_PeerRoom;

        public EntityDriverConstructor(NetworkConfiguration configuration) {
            m_SecureDirect = true;
            m_Configuration = new EntityNetworkConfiguration {
                Role = configuration.Role,
                Type = configuration.Type,
                RelayClientData = configuration.RelayClientData,
                RelayServerData = configuration.RelayServerData
            };
        }

        public EntityDriverConstructor(NetworkRole role, bool secureDirect = true) {
            m_SecureDirect = secureDirect;
            m_Configuration = new EntityNetworkConfiguration {
                Role = role,
                Type = NetworkType.Direct
            };
        }

        // The lobby-selected P2P client completes signaling before creating its World.
        public EntityDriverConstructor(NetworkRole role, EmbeddedIceTransport embeddedIce) : this(role) {
            // 官方在线的 ICE 连接跨进程。房主同进程的 IPC 路径另建，不共用这里的原生传输。
            if (role == NetworkRole.Host)
                throw new System.ArgumentException("Online ICE does not use local host/IPC.", nameof(role));
            m_EmbeddedIce = embeddedIce ?? throw new System.ArgumentNullException(nameof(embeddedIce));
        }

        internal EntityDriverConstructor(NetworkRole role, LanPeerRoom room) : this(role, false) { m_PeerRoom = room; }

        // World 建立时把对应的传输装进去。好友房的来宾借用房间留好的战局 ICE 通道，房主自己的客户端走进程内连接。
        public void CreateClientDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug) {
            var networkSettings = GameNetworkSettings(64, // Client only needs small buffers.
                64);
#if UNITY_EDITOR || NETCODE_DEBUG
            if (NetworkSimulatorSettings.Enabled) NetworkSimulatorSettings.SetSimulatorSettings(ref networkSettings);
#endif
            if (m_PeerRoom != null) {
                if (m_Configuration.Role == NetworkRole.Host) {
                    DefaultDriverBuilder.RegisterClientIpcDriver(world, ref driverStore, netDebug, networkSettings);
                } else {
                    // 已协商成功的好友房借用战局数据通道。来宾按照邀请附带的证书验证房主，房主自己的客户端走 IPC。
                    m_PeerRoom.Identity.Configure(ref networkSettings, false);
                    driverStore.RegisterDriver(TransportType.Socket,
                        DefaultDriverBuilder.CreateClientNetworkDriver(
                            m_PeerRoom.RaidTransport.BorrowNetworkInterface(), networkSettings));
                }
            } else if (m_Configuration.Type == NetworkType.Relay) {
                // 选择 Unity Relay 时用大厅给的客户端参数建 UDP driver。协议会话参数已经由 SDK 生成。
                networkSettings.WithRelayParameters(ref m_Configuration.RelayClientData);
                DefaultDriverBuilder.RegisterClientUdpDriver(world, ref driverStore, netDebug, networkSettings);
            } else {
                // 官方直连与官方 ICE 入口都要先配服务器认证。它们只是在游戏数据到底走普通 UDP 还是 ICE 上不同。
                if (m_SecureDirect) OnlineTransportSecurity.ConfigureClient(ref networkSettings);
                // A secure direct connection always targets a separate server process.
                // Do not accidentally select IPC because an Editor World is present.
                if (m_EmbeddedIce != null)
                    driverStore.RegisterDriver(TransportType.Socket,
                        DefaultDriverBuilder.CreateClientNetworkDriver(m_EmbeddedIce.CreateNetworkInterface(),
                            networkSettings));
                else if (m_SecureDirect)
                    DefaultDriverBuilder.RegisterClientUdpDriver(world, ref driverStore, netDebug, networkSettings);
                else
                    DefaultDriverBuilder.RegisterClientDriver(world, ref driverStore, netDebug, networkSettings);
            }
        }

        public void CreateServerDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug) {
#if UNITY_EDITOR || !UNITY_WEBGL
            // 服务端为许多连接共用收发队列预留容量。这个容量估算不等于玩法允许加入的人数。
            const int expectedMaxPlayerCount = 1000;
            var networkSettings = GameNetworkSettings(expectedMaxPlayerCount * 4,
                expectedMaxPlayerCount * 16 // This is how many incoming messages per player
                // the server can cache.
            );

            // Ipc driver is not needed unless we are self-connecting.
            // 房主要同时服务自己和来宾，所以有 IPC driver 后仍要为外部连接加一个 socket driver。
            if (m_Configuration.Role == NetworkRole.Host)
                DefaultDriverBuilder.RegisterServerIpcDriver(world, ref driverStore, netDebug, networkSettings);
            if (m_PeerRoom != null) {
                m_PeerRoom.Identity.Configure(ref networkSettings, true);
                driverStore.RegisterDriver(TransportType.Socket,
                    DefaultDriverBuilder.CreateServerNetworkDriver(m_PeerRoom.RaidTransport.BorrowNetworkInterface(),
                        networkSettings));
                return;
            }
            if (m_Configuration.Type == NetworkType.Relay)
                networkSettings.WithRelayParameters(ref m_Configuration.RelayServerData);
            else if (m_SecureDirect) OnlineTransportSecurity.ConfigureServer(ref networkSettings);
            var embeddedIce = m_EmbeddedIce;
            // 官方独立服本场指定 ICE 才建原生通道。其他官方直连及有 Relay 参数的服务器都继续用自己的 UDP 路线。
            if (embeddedIce == null && m_SecureDirect && m_Configuration.Role == NetworkRole.Server &&
                OnlineIceServer.Selected)
                embeddedIce = OnlineIceServer.CreateTransport();
            if (embeddedIce != null)
                driverStore.RegisterDriver(TransportType.Socket,
                    DefaultDriverBuilder.CreateServerNetworkDriver(embeddedIce.CreateNetworkInterface(),
                        networkSettings));
            else
                DefaultDriverBuilder.RegisterServerUdpDriver(world, ref driverStore, netDebug, networkSettings);
#else
            throw new NotSupportedException(
                "Creating a server driver for a WebGL build is not supported. You can't listen on a WebSocket in the browser." +
                " WebGL builds should be ideally client-only (has UNITY_CLIENT define) and in case a Client/Server build is made, only client worlds should be created.");
#endif
        }

        // 统一底层握手、断开、重连时间，以及收发队列大小。模式或档案允许进场的时限在别处另行管理。
        private static NetworkSettings GameNetworkSettings(int sendQueueCapacity, int receiveQueueCapacity) {
            var networkSettings = new NetworkSettings(Allocator.Temp);
            networkSettings.WithNetworkConfigParameters(500, 30, // Allow DTLS/WAN handshakes fifteen seconds.
                15_000, reconnectionTimeoutMS: 3_000, sendQueueCapacity: sendQueueCapacity,
                receiveQueueCapacity: receiveQueueCapacity);
            return networkSettings;
        }
    }
}