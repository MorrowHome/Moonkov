using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Unity.MP_FPS
{
    // Lightweight room control only; gameplay continues over Unity Transport/Netcode.
    public sealed class LanRoomServer : IDisposable
    {
        private sealed class Member
        {
            public string Credential, Ticket;
            public LanMember View;
            public DateTime Seen;
        }
        private readonly object m_Lock = new object();
        private readonly Dictionary<string, Member> m_Members = new Dictionary<string, Member>();
        private readonly HashSet<TcpClient> m_Clients = new HashSet<TcpClient>();
        private readonly CancellationTokenSource m_Stop = new CancellationTokenSource();
        private readonly TcpListener m_Listener;
        private readonly UdpClient m_Discovery;
        private readonly LanRoomSnapshot m_Room;
        private bool m_Disposed;
        public LanPersistence Persistence { get; }
        public int Port => ((IPEndPoint)m_Listener.LocalEndpoint).Port;
        public bool DiscoveryAvailable => m_Discovery != null;
        public LanRoomServer(LanPersistence persistence, string roomName, RaidGameMode mode, int capacity, int port, int gamePort, bool discovery = true)
        {
            if (mode != RaidGameMode.Expedition && mode != RaidGameMode.BountyClash) throw new ArgumentException("无效玩法。");
            int max = mode == RaidGameMode.BountyClash ? BountyClashRules.MaxPlayers : GameManager.MaxPlayer;
            if (capacity < 2 || capacity > max || port < 0 || port > 65535 || gamePort < 1 || gamePort > 65535 || port == gamePort)
                throw new ArgumentException("房间人数或端口无效。");
            Persistence = persistence;
            m_Room = new LanRoomSnapshot { Id = Guid.NewGuid().ToString("N"), Name = CleanName(roomName), HostId = persistence.HostId, Mode = mode, Capacity = capacity, GamePort = gamePort };
            m_Listener = new TcpListener(IPAddress.Any, port);
            m_Listener.Start(64);
            try { if (discovery) m_Discovery = new UdpClient(LanProtocol.DiscoveryPort); }
            catch (SocketException) { /* Manual IPv4 join is still available. */ }
            _ = AcceptAsync(); if (m_Discovery != null) _ = DiscoverAsync();
        }
        public static string CleanName(string value)
        {
            value = new string((value ?? "").Trim().Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(20).ToArray());
            return value.Length == 0 ? "Moonkov" : value;
        }
        public LanReply AddHost(string credential, string name)
        { lock (m_Lock) { if (m_Members.Count != 0) throw new InvalidOperationException(); return Join(credential, name, true); } }
        private LanReply Join(string credential, string name, bool host = false)
        {
            if (m_Room.Phase != LanRoomPhase.Lobby) throw new InvalidOperationException("房间已开始对局，请等待本局结束。");
            if (m_Members.ContainsKey(credential)) throw new InvalidOperationException("此玩家已在房间中，请先退出原连接。");
            if (m_Members.Count >= m_Room.Capacity) throw new InvalidOperationException("房间已满。");
            string id = Persistence.OpenPlayer(credential, CleanName(name));
            var member = new Member { Credential = credential, Seen = DateTime.UtcNow, View = new LanMember { Id = id, Name = CleanName(name), Host = host } };
            m_Members.Add(credential, member);
            return Reply(member, true);
        }
        private LanRoomSnapshot Snapshot()
        {
            return new LanRoomSnapshot { Id = m_Room.Id, HostId = m_Room.HostId, Name = m_Room.Name, Mode = m_Room.Mode, Capacity = m_Room.Capacity,
                GamePort = m_Room.GamePort, Phase = m_Room.Phase, Round = m_Room.Round,
                Members = m_Members.Values.Select(p => new LanMember { Id = p.View.Id, Name = p.View.Name, Host = p.View.Host, Ready = p.View.Ready, InGame = p.View.InGame }).ToArray() };
        }
        private LanReply Reply(Member member, bool profile = false) => new LanReply
        { MemberId = member.View.Id, Ticket = member.Ticket, Room = Snapshot(), Profile = profile ? Persistence.Read(member.View.Id) : null };
        public LanReply Handle(LanRequest request)
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new InvalidOperationException("房主已关闭房间。");
                if (request == null || request.Protocol != LanProtocol.Version) throw new InvalidOperationException("游戏版本不兼容，请双方更新到相同版本。");
                Expire(DateTime.UtcNow);
                if (request.Action == "join") return Join(request.Credential, request.Name);
                if (request.RoomId != m_Room.Id || request.Credential == null || !m_Members.TryGetValue(request.Credential, out var member))
                    throw new InvalidOperationException("房间已关闭或玩家已离开，请重新加入。");
                member.Seen = DateTime.UtcNow;
                switch (request.Action)
                {
                    case "poll": return Reply(member);
                    case "profile": return Reply(member, true);
                    case "ready":
                        RequireLobby();
                        if (Persistence.Read(member.View.Id).RaidActive) throw new InvalidOperationException("上一局装备结算尚未完成。");
                        member.View.Ready = request.Ready; break;
                    case "start":
                        RequireLobby();
                        if (!member.View.Host) throw new InvalidOperationException("只有房主可以开始对局。");
                        if (m_Members.Values.Any(m => !m.View.Host && !m.View.Ready)) throw new InvalidOperationException("请等待所有成员准备。");
                        if (m_Members.Values.Any(m => Persistence.Read(m.View.Id).RaidActive)) throw new InvalidOperationException("上一局结算尚未完成。");
                        Persistence.RevokeTickets(); m_Room.Phase = LanRoomPhase.Preparing; m_Room.Round++;
                        foreach (var player in m_Members.Values)
                        { player.Ticket = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"); player.View.InGame = true; Persistence.SetTicket(player.Ticket, player.View.Id); }
                        break;
                    case "listening":
                        if (!member.View.Host || m_Room.Phase != LanRoomPhase.Preparing) throw new InvalidOperationException("房间尚未进入地图加载阶段。");
                        m_Room.Phase = LanRoomPhase.Playing; break;
                    case "returned":
                        member.View.InGame = member.View.Ready = false; member.Ticket = null; Persistence.RevokePlayer(member.View.Id);
                        if (member.View.Host && m_Room.Phase != LanRoomPhase.Lobby) m_Room.Phase = LanRoomPhase.Returning;
                        ResetRoundIfEmpty(); break;
                    case "leave":
                        if (member.View.Host) throw new InvalidOperationException("房主请使用关闭房间。");
                        Persistence.RevokePlayer(member.View.Id); m_Members.Remove(member.Credential); ResetRoundIfEmpty(); break;
                    case "move": case "trade": case "recovery":
                        RequireLobby();
                        if (member.View.Ready) throw new InvalidOperationException("请先取消准备再修改装备。");
                        return new LanReply { Room = Snapshot(), MemberId = member.View.Id, Profile = Persistence.Mutate(member.View.Id, request.Action, request.Payload ?? throw new ArgumentException("装备操作为空。")) };
                    default: throw new InvalidOperationException("未知房间操作。");
                }
                return Reply(member);
            }
        }
        private void RequireLobby() { if (m_Room.Phase != LanRoomPhase.Lobby) throw new InvalidOperationException("对局已开始，请等待成员返回房间。"); }
        private void ResetRoundIfEmpty()
        {
            if (m_Room.Phase != LanRoomPhase.Lobby && m_Members.Values.All(m => !m.View.InGame))
            { m_Room.Phase = LanRoomPhase.Lobby; Persistence.RevokeTickets(); foreach (var m in m_Members.Values) { m.View.Ready = false; m.Ticket = null; } }
        }
        private void Expire(DateTime now)
        {
            foreach (var pair in m_Members.Where(p => !p.Value.View.Host && (now - p.Value.Seen).TotalSeconds > 45).ToArray())
            { Persistence.RevokePlayer(pair.Value.View.Id); m_Members.Remove(pair.Key); }
            ResetRoundIfEmpty();
        }
        private async Task AcceptAsync()
        {
            try
            {
                while (!m_Stop.IsCancellationRequested)
                {
                    var client = await m_Listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    lock (m_Lock)
                    {
                        if (m_Disposed || m_Clients.Count >= 64) { client.Dispose(); continue; }
                        m_Clients.Add(client);
                    }
                    _ = ServeAsync(client);
                }
            }
            catch (Exception) when (m_Stop.IsCancellationRequested) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(m_Stop.Token))
            using (timeout.Token.Register(client.Close))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    using var stream = client.GetStream();
                    var request = await LanProtocol.ReadAsync<LanRequest>(stream, timeout.Token).ConfigureAwait(false);
                    LanReply reply;
                    try { reply = Handle(request); }
                    catch (Exception ex) { reply = new LanReply { Error = ex is InvalidOperationException || ex is ArgumentException ? ex.Message : "房间存档操作失败，原始存档已保留。" }; }
                    await LanProtocol.WriteAsync(stream, reply, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception) { /* Invalid/incomplete peer messages do not stop the listener. */ }
                finally { lock (m_Lock) m_Clients.Remove(client); }
            }
        }
        private async Task DiscoverAsync()
        {
            try
            {
                while (!m_Stop.IsCancellationRequested)
                {
                    var packet = await m_Discovery.ReceiveAsync().ConfigureAwait(false);
                    if (Encoding.UTF8.GetString(packet.Buffer) != LanProtocol.DiscoveryQuery) continue;
                    byte[] bytes;
                    lock (m_Lock)
                    {
                        if (m_Room.Phase != LanRoomPhase.Lobby) continue;
                        var room = Snapshot(); room.Address = Port.ToString(); // response sender supplies IPv4, never trust an advertised address.
                        bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(room));
                    }
                    await m_Discovery.SendAsync(bytes, bytes.Length, packet.RemoteEndPoint).ConfigureAwait(false);
                }
            }
            catch (Exception) when (m_Stop.IsCancellationRequested) { }
        }
        public void Dispose()
        {
            lock (m_Lock)
            {
                if (m_Disposed) return; m_Disposed = true;
                m_Stop.Cancel(); m_Listener.Stop(); m_Discovery?.Dispose();
                foreach (var client in m_Clients.ToArray()) client.Close();
                Persistence.RevokeTickets();
            }
        }
    }
}
