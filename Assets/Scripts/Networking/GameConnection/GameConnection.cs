// GameManager 创建战局 World 前，从这里拿监听端点和连接端点。单人、房主、普通直连、跨网 ICE 和 Unity Relay 各有一个准备入口。
using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Networking.Transport;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

namespace Unity.MP_FPS {
    /// <summary>
    /// This extension methods help with some specificities of the <see cref="ISession"/> interface.
    /// </summary>
    public static class SessionExtension {
        /// <summary>
        /// This method is used to know if the current <see cref="ISession"/> is a pure server and not a client.
        /// </summary>
        /// <returns>True if the session is only a server and does not have a client, false otherwise.</returns>
        public static bool IsServer(this ISession session) {
            return session.IsHost && session.CurrentPlayer?.Id == null;
        }
    }

    /// <summary>
    /// This class is a wrapper around <see cref="MultiplayerService.Instance"/> session API.
    /// </summary>
    /// <remarks>
    /// The purpose of this class is to encapsulate the need for a custom <see cref="INetworkHandler"/> that we are using
    /// to retrieve the Listen and Connect <see cref="NetworkEndpoint"/> while connecting through the <see cref="ISession"/> API.
    /// This is done in order to control the Worlds creation and Connection in <see cref="GameManager.StartGameAsync"/>.
    /// </remarks>
    public class GameConnection {
        // Relay 才会持有 Unity Session。直接 IP 或进程内游戏只需要端点，好友房和官方 ICE 还各带自己的连接对象。
        public ISession Session { get; private set; }
        public NetworkEndpoint ListenEndpoint { get; private set; }
        public NetworkEndpoint ConnectEndpoint { get; private set; }
        public NetworkType SessionConnectionType { get; private set; }
        public EmbeddedIceClientSession IceSession { get; private set; }
        internal LanPeerRoom PeerRoom { get; private set; }

        public void ReleaseIce() {
            IceSession?.Dispose();
            IceSession = null;
        }

        public static Task<GameConnection> CreateorJoinGameAsync(CancellationToken ct = default) {
            return OpenRelayAsync(false, ct);
        }

        public static Task<GameConnection> JoinGameAsync(CancellationToken ct = default) {
            return OpenRelayAsync(true, ct);
        }

        // Relay 会话建立和底层网络回调是两件事。先等会话，再等回调提供两个端点。超时后如果 SDK 又迟到建好了房间，会补做清理。
        private static async Task<GameConnection> OpenRelayAsync(bool joinByCode, CancellationToken ct) {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(SessionConnectionPolicy.TimeoutSeconds));
            var handler = new EntityNetworkHandler();
            Task<ISession> pending = null;
            try {
                await BoundedAsync(StartServicesAsync(), timeout.Token);
                // 有房间码时加入现有 Relay 会话。新开会话则使用配置容量和 DTLS 创建，再等底层参数。
                if (joinByCode) {
                    pending = MultiplayerService.Instance.JoinSessionByCodeAsync(
                        ConnectionSettings.Instance.SessionCode, new JoinSessionOptions().WithNetworkHandler(handler));
                } else {
                    string name = GameSettings.Instance.SessionName?.Trim();
                    if (string.IsNullOrEmpty(name) || name.Length > 64)
                        throw new InvalidOperationException("房间名称须为 1–64 个字符。 ");
                    pending = AsSessionAsync(MultiplayerService.Instance.CreateSessionAsync(new SessionOptions {
                            Name = name, MaxPlayers = GameManager.RequestedMaxPlayers
                        }.WithRelayNetwork()
                        .WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.DTLS })
                        .WithNetworkHandler(handler)));
                }
                var session = await BoundedAsync(pending, timeout.Token);
                return new GameConnection {
                    Session = session,
                    // SDK 先返回会话也不代表端点已经填好。还要等 EntityNetworkHandler 安装完驱动所需参数。
                    ConnectEndpoint = await BoundedAsync(handler.ConnectEndpoint, timeout.Token),
                    ListenEndpoint = await BoundedAsync(handler.ListenEndpoint, timeout.Token),
                    SessionConnectionType = await BoundedAsync(handler.SessionConnectionType, timeout.Token)
                };
            } catch (Exception ex) {
                // 超时先停回调，再对仍在创建的会话登记补清理。迟到新房间要删掉，迟到加入要退房。
                await handler.StopAsync();
                if (pending != null) _ = CleanupLateSessionAsync(pending);
                if (ex is OperationCanceledException && !ct.IsCancellationRequested)
                    throw new TimeoutException("中继服务连接超时，请检查网络后重试。", ex);
                throw;
            }
        }

        // The SDK does not expose cancellation for every operation. Bound our wait,
        // observe late failures, and remove late-created sessions rather than leaking rooms.
        private static async Task BoundedAsync(Task operation, CancellationToken ct) {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // 给不支持取消的 SDK 任务套上外部等待期限。返回超时不会强夺 SDK 的内部任务，也要把迟到异常观察掉。
            var delay = Task.Delay(Timeout.Infinite, deadline.Token);
            if (await Task.WhenAny(operation, delay) != operation) {
                _ = ObserveAsync(operation);
                ct.ThrowIfCancellationRequested();
            }
            deadline.Cancel();
            await operation;
            ct.ThrowIfCancellationRequested();
        }

        private static async Task<T> BoundedAsync<T>(Task<T> operation, CancellationToken ct) {
            await BoundedAsync((Task)operation, ct);
            return await operation;
        }

        private static async Task<ISession> AsSessionAsync(Task<IHostSession> task) { return await task; }

        private static async Task ObserveAsync(Task task) {
            try { await task; } catch (Exception) { }
        }

        private static async Task CleanupLateSessionAsync(Task<ISession> pending) {
            try {
                var session = await pending;
                if (session.IsHost)
                    await session.AsHost().DeleteAsync();
                else
                    await session.LeaveAsync();
            } catch (Exception ex) {
                UnityEngine.Debug.LogWarning("[Session] Late session cleanup: " + ex.GetType().Name);
            }
        }

        // 纯服务器只需要监听端点，不在这个 World 发起客户端连接。这里准备通配 IPv4 或由调用方指定的监听地址。
        public static GameConnection GetServerConnectionSettings(ushort port) {
            ConnectionSettings.Instance.Port = port.ToString();
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
            gameConnection.ConnectEndpoint = default;
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return gameConnection;
        }

        public static GameConnection GetServerConnectionSettings(NetworkEndpoint listenEndpoint) {
            ConnectionSettings.Instance.Port = listenEndpoint.Port.ToString();
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = listenEndpoint;
            gameConnection.ConnectEndpoint = default;
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return gameConnection;
        }

        // 跨网好友房已有战局 ICE，房主本人仍走本机端点。局域网普通房对外监听端口，房主的客户端用回环地址连接。
        public static Task<GameConnection> HostGameAsync() {
            if (LanRoomClient.PeerRoom is { } room)
                return Task.FromResult(new GameConnection {
                    PeerRoom = room, ListenEndpoint = NetworkEndpoint.AnyIpv4,
                    ConnectEndpoint = NetworkEndpoint.LoopbackIpv4, SessionConnectionType = NetworkType.Direct
                });
            ushort port = ushort.Parse(ConnectionSettings.Instance.Port);
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
            gameConnection.ConnectEndpoint = NetworkEndpoint.LoopbackIpv4.WithPort(port);
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return Task.FromResult(gameConnection);
        }

        // 单人也有一套客户端和服务端 World，因此同样需要端点数据。默认用本机回环，两边在同一进程通信。
        public static Task<GameConnection> OfflineGameAsync() {
            return Task.FromResult(new GameConnection {
                ListenEndpoint = NetworkEndpoint.LoopbackIpv4,
                ConnectEndpoint = NetworkEndpoint.LoopbackIpv4,
                SessionConnectionType = NetworkType.Direct
            });
        }

        public void UseLocalServerEndpoint(NetworkEndpoint endpoint) { ConnectEndpoint = endpoint; }

        // 优先用已有好友房的战局路线，官方大厅分配到 P2P 时再新建 ICE 会话。普通直连走域名解析和公网端口。
        public static async Task<GameConnection> ConnectGameAsync(CancellationToken ct = default) {
            if (LanRoomClient.PeerRoom is { } room)
                return new GameConnection {
                    PeerRoom = room, ListenEndpoint = room.RaidTransport.LocalEndpoint,
                    ConnectEndpoint = room.GuestRaidEndpoint, SessionConnectionType = NetworkType.Direct
                };
            if (!PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan && OnlineLobbyClient.InInstance) {
                var snapshot = OnlineLobbyClient.Snapshot;
                if (snapshot?.InstanceId != OnlineLobbyClient.InstanceId ||
                    !Online.OnlineNetworkProtocol.ValidPath(snapshot.ConnectionPath))
                    throw new InvalidOperationException("战局连接方式无效，请返回小队刷新。");
                // 官方大厅分配的路线不能只从地址文本猜。如果要求 P2P，就先把 ICE 建起来，TCP 账号服务只协助交换参数。
                if (snapshot.ConnectionPath == Online.OnlineNetworkProtocol.P2P) {
                    var ice = await EmbeddedIceClientSession.ConnectAsync(snapshot, ct);
                    return new GameConnection {
                        IceSession = ice, ListenEndpoint = ice.Transport.LocalEndpoint,
                        ConnectEndpoint = ice.Endpoint, SessionConnectionType = NetworkType.Direct
                    };
                }
            }
            ushort port = ushort.Parse(ConnectionSettings.Instance.Port);
            // A host name cannot be stored in a NetworkEndpoint, so resolve one here. This is what
            // lets the shipped settings file name the server by domain instead of by address.
            // 普通 IP 直连在加载时把域名变成具体地址。取消后不会把一条刚解析完的旧连接设为目标。
            string address = await ClientSettings.ResolveAsync(ConnectionSettings.Instance.IPAddress);
            ct.ThrowIfCancellationRequested();
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = NetworkEndpoint.AnyIpv4;
            gameConnection.ConnectEndpoint = NetworkEndpoint.Parse(address, port);
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return gameConnection;
        }

        private static async Task StartServicesAsync() {
            // Unity Relay 属于自己的服务体系，先初始化再匿名授权。账号后端登录和 Unity Relay 认证是两套不同的状态。
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsAuthorized)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private static SessionOptions CreateSessionOptions(ConnectionType connectionType, string address, string port) {
            var options = new SessionOptions { MaxPlayers = GameManager.MaxPlayer };
            switch (connectionType) {
                case ConnectionType.Relay: options.WithRelayNetwork(); break;
                case ConnectionType.Direct: options.WithDirectNetwork("0.0.0.0", address, ushort.Parse(port)); break;
            }
            return options;
        }
    }
}