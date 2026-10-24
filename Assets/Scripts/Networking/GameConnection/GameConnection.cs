using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Networking.Transport;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

namespace Unity.MP_FPS
{
    /// <summary>
    /// This extension methods help with some specificities of the <see cref="ISession"/> interface.
    /// </summary>
    public static class SessionExtension
    {
        /// <summary>
        /// This method is used to know if the current <see cref="ISession"/> is a pure server and not a client.
        /// </summary>
        /// <returns>True if the session is only a server and does not have a client, false otherwise.</returns>
        public static bool IsServer(this ISession session)
        {
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
    public class GameConnection
    {
        public ISession Session { get; private set; }
        public NetworkEndpoint ListenEndpoint { get; private set; }
        public NetworkEndpoint ConnectEndpoint { get; private set; }
        public NetworkType SessionConnectionType { get; private set; }

        public static Task<GameConnection> CreateorJoinGameAsync(CancellationToken ct = default) => OpenRelayAsync(false, ct);
        public static Task<GameConnection> JoinGameAsync(CancellationToken ct = default) => OpenRelayAsync(true, ct);

        static async Task<GameConnection> OpenRelayAsync(bool joinByCode, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(SessionConnectionPolicy.TimeoutSeconds));
            var handler = new EntityNetworkHandler();
            Task<ISession> pending = null;
            try
            {
                await BoundedAsync(StartServicesAsync(), timeout.Token);
                if (joinByCode)
                    pending = MultiplayerService.Instance.JoinSessionByCodeAsync(ConnectionSettings.Instance.SessionCode,
                        new JoinSessionOptions().WithNetworkHandler(handler));
                else
                {
                    string name = GameSettings.Instance.SessionName?.Trim();
                    if (string.IsNullOrEmpty(name) || name.Length > 64) throw new InvalidOperationException("房间名称须为 1–64 个字符。 ");
                    pending = AsSessionAsync(MultiplayerService.Instance.CreateSessionAsync(new SessionOptions
                    {
                        Name = name, MaxPlayers = GameManager.RequestedMaxPlayers
                    }.WithRelayNetwork().WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.DTLS }).WithNetworkHandler(handler)));
                }
                var session = await BoundedAsync(pending, timeout.Token);
                return new GameConnection
                {
                    Session = session,
                    ConnectEndpoint = await BoundedAsync(handler.ConnectEndpoint, timeout.Token),
                    ListenEndpoint = await BoundedAsync(handler.ListenEndpoint, timeout.Token),
                    SessionConnectionType = await BoundedAsync(handler.SessionConnectionType, timeout.Token)
                };
            }
            catch (Exception ex)
            {
                await handler.StopAsync();
                if (pending != null) _ = CleanupLateSessionAsync(pending);
                if (ex is OperationCanceledException && !ct.IsCancellationRequested)
                    throw new TimeoutException("中继服务连接超时，请检查网络后重试。", ex);
                throw;
            }
        }

        // The SDK does not expose cancellation for every operation. Bound our wait,
        // observe late failures, and remove late-created sessions rather than leaking rooms.
        static async Task BoundedAsync(Task operation, CancellationToken ct)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task delay = Task.Delay(Timeout.Infinite, deadline.Token);
            if (await Task.WhenAny(operation, delay) != operation)
            {
                _ = ObserveAsync(operation);
                ct.ThrowIfCancellationRequested();
            }
            deadline.Cancel();
            await operation;
            ct.ThrowIfCancellationRequested();
        }
        static async Task<T> BoundedAsync<T>(Task<T> operation, CancellationToken ct)
        { await BoundedAsync((Task)operation, ct); return await operation; }
        static async Task<ISession> AsSessionAsync(Task<IHostSession> task) => await task;
        static async Task ObserveAsync(Task task) { try { await task; } catch (Exception) { } }
        static async Task CleanupLateSessionAsync(Task<ISession> pending)
        {
            try
            {
                var session = await pending;
                if (session.IsHost) await session.AsHost().DeleteAsync(); else await session.LeaveAsync();
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[Session] Late session cleanup: " + ex.GetType().Name); }
        }
        
        public static GameConnection GetServerConnectionSettings(ushort port)
        {
            ConnectionSettings.Instance.Port = port.ToString();
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
            gameConnection.ConnectEndpoint = default;
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return gameConnection;
        }

        public static GameConnection GetServerConnectionSettings(NetworkEndpoint listenEndpoint)
        {
            ConnectionSettings.Instance.Port = listenEndpoint.Port.ToString();
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = listenEndpoint;
            gameConnection.ConnectEndpoint = default;
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return gameConnection;
        }
        
        public static Task<GameConnection> HostGameAsync()
        {
            ushort port = ushort.Parse(ConnectionSettings.Instance.Port);
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = NetworkEndpoint.AnyIpv4.WithPort(port);
            gameConnection.ConnectEndpoint = NetworkEndpoint.LoopbackIpv4.WithPort(port);
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return Task.FromResult(gameConnection);
        }

        public static Task<GameConnection> OfflineGameAsync()
        {
            return Task.FromResult(new GameConnection
            {
                ListenEndpoint = NetworkEndpoint.LoopbackIpv4,
                ConnectEndpoint = NetworkEndpoint.LoopbackIpv4,
                SessionConnectionType = NetworkType.Direct
            });
        }

        public void UseLocalServerEndpoint(NetworkEndpoint endpoint) => ConnectEndpoint = endpoint;

        public static async Task<GameConnection> ConnectGameAsync()
        {
            ushort port = ushort.Parse(ConnectionSettings.Instance.Port);
            // A host name cannot be stored in a NetworkEndpoint, so resolve one here. This is what
            // lets the shipped settings file name the server by domain instead of by address.
            string address = await ClientSettings.ResolveAsync(ConnectionSettings.Instance.IPAddress);
            var gameConnection = new GameConnection();
            gameConnection.ListenEndpoint = NetworkEndpoint.AnyIpv4;
            gameConnection.ConnectEndpoint = NetworkEndpoint.Parse(address, port);
            gameConnection.SessionConnectionType = NetworkType.Direct;
            return gameConnection;
        }

        static async Task StartServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsAuthorized)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        static SessionOptions CreateSessionOptions(ConnectionType connectionType, string address, string port)
        {
            SessionOptions options = new SessionOptions { MaxPlayers = GameManager.MaxPlayer };
            switch (connectionType)
            {
                case ConnectionType.Relay:
                    options.WithRelayNetwork();
                    break;
                case ConnectionType.Direct:
                    options.WithDirectNetwork("0.0.0.0", address, ushort.Parse(port));
                    break;
            }
            return options;
        }
    }
}
