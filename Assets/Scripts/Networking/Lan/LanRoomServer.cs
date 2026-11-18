// 房主端的好友房规则。这里维护成员和准备状态，处理仓库请求，发本轮入场票，并用同一份快照告诉大家何时加载或退回房间。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Online;

namespace Unity.MP_FPS {
    // 这里只处理房间控制。实际游戏继续用 Unity Transport 和 NetCode。
    public sealed class LanRoomServer : IDisposable {
        private sealed class Member {
            public string Credential, Ticket;
            public int Connection;
            public LanMember View;
            public DateTime Seen;
        }

        private readonly object m_Lock = new();
        private readonly Dictionary<string, Member> m_Members = new();
        private readonly HashSet<TcpClient> m_Clients = new();
        private readonly CancellationTokenSource m_Stop = new();
        private readonly TcpListener m_Listener;
        private readonly UdpClient m_Discovery;
        private readonly LanRoomSnapshot m_Room;
        private bool m_Disposed;
        public LanPersistence Persistence { get; }
        public int Port => m_Listener == null ? 0 : ((IPEndPoint)m_Listener.LocalEndpoint).Port;
        internal string RoomId => m_Room.Id;
        public bool DiscoveryAvailable => m_Discovery != null;

        public LanRoomServer(
            LanPersistence persistence,
            string roomName,
            RaidGameMode mode,
            int capacity,
            int port,
            int gamePort,
            bool discovery = true,
            bool peerRoom = false) {
            if (mode != RaidGameMode.Expedition && mode != RaidGameMode.BountyClash &&
                !ArenaMatch.UsesTemporaryKit(mode))
                throw new ArgumentException("无效玩法。");
            int max = CapacityFor(mode);
            if (capacity < 2 || capacity > max || (!peerRoom &&
                                                   (port < 0 || port > 65535 || gamePort < 1 || gamePort > 65535 ||
                                                    port == gamePort)))
                throw new ArgumentException("房间人数或端口无效。");
            Persistence = persistence;
            m_Room = new LanRoomSnapshot {
                Id = Guid.NewGuid().ToString("N"), Name = CleanName(roomName), HostId = persistence.HostId,
                Mode = mode, Capacity = capacity, GamePort = peerRoom ? 0 : gamePort
            };
            if (peerRoom) return;
            m_Listener = new TcpListener(IPAddress.Any, port);
            m_Listener.Start(64);
            try {
                if (discovery) m_Discovery = new UdpClient(LanProtocol.DiscoveryPort);
            } catch (SocketException) {
                /* 仍可手动填写 IPv4 地址加入。 */
            }
            _ = AcceptAsync();
            if (m_Discovery != null) _ = DiscoverAsync();
        }

        public static string CleanName(string value) {
            value = new string((value ?? "").Trim().Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(20)
                .ToArray());
            return value.Length == 0 ? "Moonkov" : value;
        }

        public static int CapacityFor(RaidGameMode mode) {
            return mode switch {
                RaidGameMode.Arena => ArenaMatch.Capacity,
                RaidGameMode.Classic => ClassicMatch.Capacity,
                RaidGameMode.BountyClash => BountyClashRules.MaxPlayers,
                _ => GameManager.MaxPlayer
            };
        }

        internal int MatchMembers {
            get {
                lock (m_Lock) { return m_Members.Values.Count(member => member.View.InGame); }
            }
        }

        internal bool TryAuthorizeMatch(RaidGameMode mode, string ticket, out LanMember participant) {
            participant = null;
            if (string.IsNullOrEmpty(ticket)) return false;
            lock (m_Lock) {
                // 模式和阶段先对上才查入场票。返回给比赛的是当前成员名字和一份外观副本。
                if (m_Disposed || m_Room.Mode != mode || m_Room.Phase != LanRoomPhase.Playing) return false;
                var member =
                    m_Members.Values.FirstOrDefault(candidate => candidate.View.InGame && candidate.Ticket == ticket);
                if (member == null) return false;
                participant = new LanMember
                    { Id = member.View.Id, Name = member.View.Name, Appearance = member.View.Appearance.Copy() };
                return true;
            }
        }

        public LanReply AddHost(string credential, string name, int character = 2, int pose = -1) {
            lock (m_Lock) {
                if (m_Members.Count != 0) throw new InvalidOperationException();
                return Join(credential, name, true, character, pose);
            }
        }

        private LanReply Join(
            string credential,
            string name,
            bool host = false,
            int character = 2,
            int pose = -1,
            int connection = 0) {
            // 新成员只在准备室登记。先核对容量、身份和外观范围，再为该凭据打开对应房主档。
            if (m_Room.Phase != LanRoomPhase.Lobby) throw new InvalidOperationException("房间已开始对局，请等待本局结束。");
            if (m_Members.ContainsKey(credential)) throw new InvalidOperationException("此玩家已在房间中，请先退出原连接。");
            if (m_Members.Count >= m_Room.Capacity) throw new InvalidOperationException("房间已满。");
            ValidateAppearance(character, pose);
            string id = Persistence.OpenPlayer(credential, CleanName(name));
            var member = new Member {
                Credential = credential, Connection = connection, Seen = DateTime.UtcNow, View = new LanMember {
                    Id = id, Name = CleanName(name), Host = host,
                    Appearance = new LobbyAppearance { CharacterIndex = character, PoseIndex = pose }
                }
            };
            m_Members.Add(credential, member);
            // 初次回复包含新成员快照和完整档案。若档案读失败，把刚加入的人移出名单，把名额还回去。
            try { return Reply(member, true); } catch {
                m_Members.Remove(credential);
                throw;
            }
        }

        private LanRoomSnapshot Snapshot() {
            return new LanRoomSnapshot {
                Id = m_Room.Id, HostId = m_Room.HostId, Name = m_Room.Name, Mode = m_Room.Mode,
                Capacity = m_Room.Capacity,
                GamePort = m_Room.GamePort, Phase = m_Room.Phase, Round = m_Room.Round,
                // 整个响应都是副本。尤其房主自己的 UI 没经过网络序列化，不能让它拿到可直接修改的房间成员对象。
                Members = m_Members.Values.Select(p => new LanMember {
                    Id = p.View.Id, Name = p.View.Name, Host = p.View.Host, Ready = p.View.Ready,
                    InGame = p.View.InGame,
                    Appearance = p.View.Appearance.Copy()
                }).ToArray()
            };
        }

        private LanReply Reply(Member member, bool profile = false) {
            var data = profile ? Persistence.Read(member.View.Id) : null;
            if (data != null) RefreshAppearance(member, data);
            return new LanReply
                { MemberId = member.View.Id, Ticket = member.Ticket, Room = Snapshot(), Profile = data };
        }

        private static void ValidateAppearance(int character, int pose) {
            if (character < 2 || character > 4 || pose < -1 || pose > 3) throw new ArgumentException("无效的角色或姿势。");
        }

        private static void RefreshAppearance(Member member, LanProfileSnapshot profile) {
            var graph = JsonConvert.DeserializeObject<InventoryGraph>(profile.Profile.InventoryJson, LanProtocol.Json);
            var previous = member.View.Appearance;
            // 角色和姿态由请求选择，装备外观从房主的真实库存提取。确认外观变化还会取消旧的准备状态。
            var next = LobbyAppearanceInventory.FromInventory(graph, previous.CharacterIndex, previous.PoseIndex);
            if (!next.SameAs(previous)) member.View.Ready = false;
            member.View.Appearance = next;
        }

        public LanReply Handle(LanRequest request) { return Handle(request, 0); }

        private LanReply Handle(LanRequest request, int connection) {
            lock (m_Lock) {
                if (m_Disposed) throw new InvalidOperationException("房主已关闭房间。");
                if (request == null || request.Protocol != LanProtocol.Version)
                    throw new InvalidOperationException("游戏版本不兼容，请双方更新到相同版本。");
                Expire(DateTime.UtcNow);
                if (request.Action == "join")
                    return Join(request.Credential, request.Name, false, request.CharacterIndex, request.PoseIndex,
                        connection);
                // 除首次加入外，房间 ID、玩家凭据和实际连接编号必须同时匹配。不能用另一个房间的同名玩家取结果。
                if (request.RoomId != m_Room.Id || request.Credential == null ||
                    !m_Members.TryGetValue(request.Credential, out var member) || member.Connection != connection)
                    throw new InvalidOperationException("房间已关闭或玩家已离开，请重新加入。");
                member.Seen = DateTime.UtcNow;
                switch (request.Action) {
                    case "poll": return Reply(member);
                    case "profile": return Reply(member, true);
                    case "appearance":
                        RequireLobby();
                        ValidateAppearance(request.CharacterIndex, request.PoseIndex);
                        if (member.View.Appearance.CharacterIndex != request.CharacterIndex ||
                            member.View.Appearance.PoseIndex != request.PoseIndex) {
                            member.View.Appearance.CharacterIndex = request.CharacterIndex;
                            member.View.Appearance.PoseIndex = request.PoseIndex;
                            member.View.Ready = false;
                        }
                        return Reply(member, true);
                    // 准备前核对旧局已结算。临时装备模式不需要占玩家真实仓库，所以不查这一条件。
                    case "ready":
                        RequireLobby();
                        if (!ArenaMatch.UsesTemporaryKit(m_Room.Mode) && Persistence.Read(member.View.Id).RaidActive)
                            throw new InvalidOperationException("上一局装备结算尚未完成。");
                        member.View.Ready = request.Ready;
                        break;
                    // 房主决定开局时先在同一把锁里确认名单和就绪状态。随后给本轮成员各发一张新票，防止上一局的票被继续拿来入场。
                    case "start":
                        RequireLobby();
                        if (!member.View.Host) throw new InvalidOperationException("只有房主可以开始对局。");
                        if (m_Room.Mode == RaidGameMode.Arena && m_Members.Count < 2)
                            throw new InvalidOperationException("竞技场至少需要两名玩家。");
                        if (m_Members.Values.Any(m => !m.View.Host && !m.View.Ready))
                            throw new InvalidOperationException("请等待所有成员准备。");
                        if (!ArenaMatch.UsesTemporaryKit(m_Room.Mode) &&
                            m_Members.Values.Any(m => Persistence.Read(m.View.Id).RaidActive))
                            throw new InvalidOperationException("上一局结算尚未完成。");
                        Persistence.RevokeTickets();
                        m_Room.Phase = LanRoomPhase.Preparing;
                        m_Room.Round++;
                        foreach (var player in m_Members.Values) {
                            player.Ticket = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                            player.View.InGame = true;
                            Persistence.SetTicket(player.Ticket, player.View.Id);
                        }
                        break;
                    // 房主 Unity 服务端确认已开始监听后进入 Playing。来宾看到这个阶段才能拿刚签的票启动本局。
                    case "listening":
                        if (!member.View.Host || m_Room.Phase != LanRoomPhase.Preparing)
                            throw new InvalidOperationException("房间尚未进入地图加载阶段。");
                        m_Room.Phase = LanRoomPhase.Playing;
                        break;
                    // 成员返回或离开时标记其本局结束并收回票。房主要结束本轮时，其他成员会在轮询里看到返回状态。
                    case "returned":
                        member.View.InGame = member.View.Ready = false;
                        member.Ticket = null;
                        Persistence.RevokePlayer(member.View.Id);
                        if (member.View.Host && m_Room.Phase != LanRoomPhase.Lobby)
                            m_Room.Phase = LanRoomPhase.Returning;
                        ResetRoundIfEmpty();
                        break;
                    case "leave":
                        if (member.View.Host) throw new InvalidOperationException("房主请使用关闭房间。");
                        Persistence.RevokePlayer(member.View.Id);
                        m_Members.Remove(member.Credential);
                        ResetRoundIfEmpty();
                        break;
                    // 仓库写只允许在准备室，并要求本人取消准备。改完再从正式背包重新提取外观。
                    case "move":
                    case "trade":
                    case "recovery":
                    case "research":
                        RequireLobby();
                        if (member.View.Ready) throw new InvalidOperationException("请先取消准备再修改装备。");
                        var changed = Persistence.Mutate(member.View.Id, request.Action,
                            request.Payload ?? throw new ArgumentException("装备操作为空。"));
                        RefreshAppearance(member, changed);
                        return new LanReply { Room = Snapshot(), MemberId = member.View.Id, Profile = changed };
                    default: throw new InvalidOperationException("未知房间操作。");
                }
                return Reply(member);
            }
        }

        private void RequireLobby() {
            if (m_Room.Phase != LanRoomPhase.Lobby) throw new InvalidOperationException("对局已开始，请等待成员返回房间。");
        }

        internal LanReply ReplyTo(LanRequest request, int connection = 0) {
            try { return Handle(request, connection); } catch (Exception ex) {
                return new LanReply {
                    Error = ex is InvalidOperationException || ex is ArgumentException
                        ? ex.Message
                        : "房间存档操作失败，原始存档已保留。"
                };
            }
        }

        internal void DropPeer(string credential, int connection) {
            lock (m_Lock) {
                if (m_Disposed || !m_Members.TryGetValue(credential, out var member) || member.View.Host ||
                    member.Connection != connection)
                    return;
                Persistence.RevokePlayer(member.View.Id);
                m_Members.Remove(credential);
                ResetRoundIfEmpty();
            }
        }

        // 所有参加本局的成员都离开战局后，房间才回到可准备状态，并把该轮的旧票全部撤销。
        private void ResetRoundIfEmpty() {
            if (m_Room.Phase != LanRoomPhase.Lobby && m_Members.Values.All(m => !m.View.InGame)) {
                m_Room.Phase = LanRoomPhase.Lobby;
                Persistence.RevokeTickets();
                foreach (var m in m_Members.Values) {
                    m.View.Ready = false;
                    m.Ticket = null;
                }
            }
        }

        private void Expire(DateTime now) {
            // 轮询也相当于心跳，长时间没回应的非房主释放席位和票。房主自己的生命周期由开关房管理。
            foreach (var pair in m_Members.Where(p => !p.Value.View.Host && (now - p.Value.Seen).TotalSeconds > 45)
                         .ToArray()) {
                Persistence.RevokePlayer(pair.Value.View.Id);
                m_Members.Remove(pair.Key);
            }
            ResetRoundIfEmpty();
        }

        private async Task AcceptAsync() {
            try {
                while (!m_Stop.IsCancellationRequested) {
                    var client = await m_Listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    lock (m_Lock) {
                        // 房间请求使用短 TCP 连接。监听端限制同时处理的请求数，每个连接也有读取和回复时限。
                        if (m_Disposed || m_Clients.Count >= 64) {
                            client.Dispose();
                            continue;
                        }
                        m_Clients.Add(client);
                    }
                    _ = ServeAsync(client);
                }
            } catch (Exception) when (m_Stop.IsCancellationRequested) { }
        }

        private async Task ServeAsync(TcpClient client) {
            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(m_Stop.Token))
            using (timeout.Token.Register(client.Close)) {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                try {
                    using var stream = client.GetStream();
                    var request = await LanProtocol.ReadAsync<LanRequest>(stream, timeout.Token).ConfigureAwait(false);
                    var reply = ReplyTo(request);
                    await LanProtocol.WriteAsync(stream, reply, timeout.Token).ConfigureAwait(false);
                } catch (Exception) {
                    /* 来宾发来无效或不完整消息时，只结束这次连接，监听继续运行。 */
                } finally {
                    lock (m_Lock) { m_Clients.Remove(client); }
                }
            }
        }

        private async Task DiscoverAsync() {
            try {
                while (!m_Stop.IsCancellationRequested) {
                    var packet = await m_Discovery.ReceiveAsync().ConfigureAwait(false);
                    // 局域网 UDP 广播只查房间列表，回复仍要处于准备室。实际加入时再连房主提供的 TCP 端口。
                    if (Encoding.UTF8.GetString(packet.Buffer) != LanProtocol.DiscoveryQuery) continue;
                    byte[] bytes;
                    lock (m_Lock) {
                        if (m_Room.Phase != LanRoomPhase.Lobby) continue;
                        var room = Snapshot();
                        room.Address =
                            Port.ToString(); // IPv4 从回复包的发送端取得，正文中自报的地址不作为依据。
                        bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(room));
                    }
                    await m_Discovery.SendAsync(bytes, bytes.Length, packet.RemoteEndPoint).ConfigureAwait(false);
                }
            } catch (Exception) when (m_Stop.IsCancellationRequested) { }
        }

        public void Dispose() {
            lock (m_Lock) {
                if (m_Disposed) return;
                m_Disposed = true;
                m_Stop.Cancel();
                m_Listener?.Stop();
                m_Discovery?.Dispose();
                foreach (var client in m_Clients.ToArray()) client.Close();
                Persistence.RevokeTickets();
            }
        }
    }
}