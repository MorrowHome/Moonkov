using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    public struct LanRaidMode : IComponentData { }
    public static class LanRoomClient
    {
        public static LanRoomServer Server { get; private set; }
        public static LanRoomSnapshot Room { get; private set; }
        public static LanProfileSnapshot Profile { get; private set; }
        public static string MemberId { get; private set; }
        public static string Ticket { get; private set; }
        public static string Error { get; private set; }
        public static string Address { get; private set; }
        public static int Port { get; private set; }
        public static bool Active => Room != null;
        public static bool IsHost => Server != null;
        public static bool Ready => Room?.Members.FirstOrDefault(m => m.Id == MemberId)?.Ready == true;
        private static string s_Credential;
        private static int s_Generation, s_LaunchedRound, s_Failures;
        private static bool s_Polling, s_Closing;
        private static CancellationTokenSource s_Stop = new CancellationTokenSource();
        private static readonly SemaphoreSlim s_Requests = new SemaphoreSlim(1, 1);
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_Stop.Cancel(); Server?.Dispose(); Server?.Persistence.Dispose(); Server = null;
            Room = null; Profile = null; MemberId = Ticket = Error = s_Credential = null;
            s_Stop = new CancellationTokenSource(); s_Generation++; s_LaunchedRound = s_Failures = 0;
            s_Polling = s_Closing = false; Changed = null;
        }
        private static void EnsurePump()
        {
            if (UnityEngine.Object.FindFirstObjectByType<LanRoomPump>() != null) return;
            var go = new GameObject("LAN room connection"); UnityEngine.Object.DontDestroyOnLoad(go); go.AddComponent<LanRoomPump>();
        }
        public static async Task CreateAsync(string name, string playerName, RaidGameMode mode, int capacity, int port, int gamePort, CancellationToken ct)
        {
            if (Active || s_Closing) throw new InvalidOperationException("请先离开当前房间。");
            if (!PlayerProfileClient.SupportsSinglePlayer) throw new InvalidOperationException("开房需要 Client+Server 游戏版本。");
            string root = Path.Combine(Application.persistentDataPath, "LAN", "host-v1");
#if UNITY_EDITOR
            // Keep Editor project copies from leasing the same host save.
            using (var hash = System.Security.Cryptography.SHA256.Create())
                root = Path.Combine(root, BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Application.dataPath))).Replace("-", ""));
#endif
            var persistence = await Task.Run(() => new LanPersistence(root), ct);
            LanRoomServer server = null;
            try
            {
                await persistence.RecoverAsync(); ct.ThrowIfCancellationRequested();
                server = new LanRoomServer(persistence, name, mode, capacity, port, gamePort);
                s_Credential = GetCredential();
                var reply = server.AddHost(s_Credential, playerName);
                Server = server; Address = "127.0.0.1"; Port = server.Port;
                Enter(reply); EnsurePump();
            }
            catch { server?.Dispose(); persistence.Dispose(); throw; }
        }
        public static async Task JoinAsync(string address, int port, string playerName, CancellationToken ct)
        {
            if (Active || s_Closing) throw new InvalidOperationException("请先离开当前房间。");
            string credential = GetCredential();
            var reply = await LanProtocol.SendAsync(address, port, new LanRequest { Action = "join", Credential = credential, Name = playerName }, ct);
            ct.ThrowIfCancellationRequested();
            if (reply.Room == null || reply.Room.Protocol != LanProtocol.Version || reply.Profile?.Profile == null) throw new InvalidDataException("房间响应无效。");
            s_Credential = credential; Address = address; Port = port; Enter(reply); EnsurePump();
        }
        private static void Enter(LanReply reply)
        {
            PlayerProfileClient.LeaveOffline();
            s_Generation++; s_LaunchedRound = 0; s_Failures = 0; Error = null;
            Apply(reply);
        }
        private static string GetCredential()
        {
            const string key = "Moonkov.LAN.Credential.v1";
            string value = PlayerPrefs.GetString(key, "");
            if (value.Length == 64 && value.All(Uri.IsHexDigit)) return value;
            var bytes = new byte[32];
            using (var random = System.Security.Cryptography.RandomNumberGenerator.Create()) random.GetBytes(bytes);
            value = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            PlayerPrefs.SetString(key, value); PlayerPrefs.Save(); return value;
        }
        private static void Apply(LanReply reply)
        {
            Room = reply.Room ?? Room; MemberId = reply.MemberId ?? MemberId; Ticket = reply.Ticket;
            if (reply.Profile != null) { Profile = reply.Profile; PlayerProfileClient.ApplyLanProfile(); }
            if (Room != null) GameSettings.Instance.RequestedMode = Room.Mode;
            Changed?.Invoke();
        }
        public static async Task<LanReply> RequestAsync(string action, object payload = null, bool ready = false, CancellationToken ct = default)
        {
            if (!Active || s_Closing) throw new InvalidOperationException("尚未加入局域网房间。");
            int generation = s_Generation;
            var request = new LanRequest { Action = action, Credential = s_Credential, RoomId = Room.Id, Ready = ready,
                Payload = payload == null ? null : JObject.FromObject(payload) };
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, s_Stop.Token);
            await s_Requests.WaitAsync(linked.Token);
            try
            {
                if (generation != s_Generation) throw new OperationCanceledException();
                var server = Server;
                var reply = server != null ? await Task.Run(() => server.Handle(request), linked.Token) : await LanProtocol.SendAsync(Address, Port, request, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                if (generation != s_Generation) throw new OperationCanceledException();
                Apply(reply); return reply;
            }
            finally { s_Requests.Release(); }
        }
        internal static async void Poll()
        {
            if (!Active || s_Polling || s_Closing) return;
            s_Polling = true;
            try
            {
                await RequestAsync("poll"); s_Failures = 0; Error = null;
                var state = GameSettings.Instance.GameState;
                if ((Room.Phase == LanRoomPhase.Playing || IsHost && Room.Phase == LanRoomPhase.Preparing) && Room.Round != s_LaunchedRound && !string.IsNullOrEmpty(Ticket) && state == GlobalGameState.MainMenu)
                {
                    s_LaunchedRound = Room.Round;
                    ConnectionSettings.Instance.IPAddress = Address;
                    ConnectionSettings.Instance.Port = Room.GamePort.ToString();
                    GameManager.Instance.StartGameAsync(IsHost ? CreationType.LanHost : CreationType.LanJoin);
                }
                else if (Room.Phase == LanRoomPhase.Returning)
                {
                    if (state != GlobalGameState.MainMenu) GameManager.Instance.ReturnToMainMenuAsync();
                    else if (Room.Members.Any(m=>m.Id==MemberId && m.InGame)) await ReturnedToMenuAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (++s_Failures >= 3)
                {
                    Error = "与房主的连接已中断。" + ex.Message; Changed?.Invoke();
                    if (GameSettings.Instance.GameState != GlobalGameState.MainMenu) GameManager.Instance.ReturnToMainMenuAsync();
                    else await LeaveAsync();
                }
            }
            finally { s_Polling = false; }
        }
        public static async Task ReturnedToMenuAsync()
        {
            if (!Active) return;
            try { await RequestAsync("returned"); await RequestAsync("profile"); }
            catch (Exception) { await LeaveAsync(afterWorldShutdown:true); }
        }
        public static async Task LeaveAsync(bool afterWorldShutdown = false)
        {
            if (s_Closing) return;
            // Worlds must have released their deployment receipts before the host store closes.
            if (!afterWorldShutdown && Active && GameSettings.Instance.GameState != GlobalGameState.MainMenu)
                throw new InvalidOperationException("请先返回主菜单，再关闭或离开房间。");
            if (Active && !IsHost) { try { await RequestAsync("leave"); } catch (Exception) { /* best effort; lease expires */ } }
            s_Closing = true;
            try
            {
                s_Generation++; var server = Server; Server = null; Room = null; Profile = null; Ticket = MemberId = null;
                server?.Dispose(); server?.Persistence.Dispose(); PlayerProfileClient.ClearLanProfile(); Changed?.Invoke();
            }
            finally { s_Closing = false; }
        }
        internal static void StopAfterPumpDestroyed()
        {
            if (!Active) return;
            // Editor Stop/native quit can bypass the menu's orderly shutdown.
            // Capture ECS ownership on this thread, then finish data-only writes before closing leases.
            var server = Server; server?.Dispose(); s_Stop.Cancel(); s_Generation++;
            Server = null; Room = null; Profile = null; Ticket = MemberId = null;
            if (server == null) return;
            var shutdowns = new List<Task>();
            foreach (var world in World.All)
            {
                if (!world.IsCreated) continue;
                using var query = world.EntityManager.CreateEntityQuery(typeof(RaidPersistenceContext));
                if (query.IsEmptyIgnoreFilter) continue;
                var context = world.EntityManager.GetComponentObject<RaidPersistenceContext>(query.GetSingletonEntity());
                context.Dispose(); shutdowns.Add(context.ShutdownTask);
            }
            _ = DisposeStoreAsync(server.Persistence, shutdowns);
        }
        private static async Task DisposeStoreAsync(LanPersistence persistence, List<Task> shutdowns)
        { try { await Task.WhenAll(shutdowns).ConfigureAwait(false); } finally { persistence.Dispose(); } }
        public static string[] LocalAddresses()
        {
            try { return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString()).Distinct().ToArray(); }
            catch { return Array.Empty<string>(); }
        }
        public static async Task<LanRoomSnapshot[]> DiscoverAsync(CancellationToken ct)
        {
            var rooms = new Dictionary<string, LanRoomSnapshot>();
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)); socket.EnableBroadcast = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(1800);
            using var cancel = timeout.Token.Register(socket.Close);
            var query = Encoding.UTF8.GetBytes(LanProtocol.DiscoveryQuery);
            try
            {
                var destinations = new HashSet<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
                    foreach (var ip in adapter.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && a.IPv4Mask != null))
                    { byte[] address = ip.Address.GetAddressBytes(), mask = ip.IPv4Mask.GetAddressBytes(); destinations.Add(new IPAddress(address.Select((b, i) => (byte)(b | ~mask[i])).ToArray())); }
                foreach (var ip in destinations)
                    try { await socket.SendAsync(query, query.Length, new IPEndPoint(ip, LanProtocol.DiscoveryPort)); } catch (SocketException) { }
                while (!timeout.IsCancellationRequested)
                {
                    var packet = await socket.ReceiveAsync();
                    if (packet.Buffer.Length > 12000) continue;
                    try
                    {
                        var room = JsonConvert.DeserializeObject<LanRoomSnapshot>(Encoding.UTF8.GetString(packet.Buffer), LanProtocol.Json);
                        if (room == null || room.Protocol != LanProtocol.Version || room.Members == null || room.Members.Length > 32 ||
                            !int.TryParse(room.Address, out var port) || port < 1 || port > 65535 || string.IsNullOrEmpty(room.Id)) continue;
                        room.Address = packet.RemoteEndPoint.Address + ":" + port;
                        rooms[room.Id] = room;
                    }
                    catch (JsonException) { }
                }
            }
            catch (Exception) when (timeout.IsCancellationRequested) { }
            ct.ThrowIfCancellationRequested(); return rooms.Values.ToArray();
        }
    }
}
