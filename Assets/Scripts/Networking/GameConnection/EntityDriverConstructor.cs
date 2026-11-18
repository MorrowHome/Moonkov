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
                    m_PeerRoom.Identity.Configure(ref networkSettings, false);
                    driverStore.RegisterDriver(TransportType.Socket,
                        DefaultDriverBuilder.CreateClientNetworkDriver(
                            m_PeerRoom.RaidTransport.BorrowNetworkInterface(), networkSettings));
                }
            } else if (m_Configuration.Type == NetworkType.Relay) {
                networkSettings.WithRelayParameters(ref m_Configuration.RelayClientData);
                DefaultDriverBuilder.RegisterClientUdpDriver(world, ref driverStore, netDebug, networkSettings);
            } else {
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
            const int expectedMaxPlayerCount = 1000;
            var networkSettings = GameNetworkSettings(expectedMaxPlayerCount * 4,
                expectedMaxPlayerCount * 16 // This is how many incoming messages per player
                // the server can cache.
            );

            // Ipc driver is not needed unless we are self-connecting.
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

        private static NetworkSettings GameNetworkSettings(int sendQueueCapacity, int receiveQueueCapacity) {
            var networkSettings = new NetworkSettings(Allocator.Temp);
            networkSettings.WithNetworkConfigParameters(500, 30, // Allow DTLS/WAN handshakes fifteen seconds.
                15_000, reconnectionTimeoutMS: 3_000, sendQueueCapacity: sendQueueCapacity,
                receiveQueueCapacity: receiveQueueCapacity);
            return networkSettings;
        }
    }
}