// 跨网好友房的常驻连接。一个来宾有两条 ICE 路线，房间操作走控制通道，进地图时 NetCode 借用战局通道。
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.MP_FPS.EmbeddedNetworking;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Unity.MP_FPS {
    // 房间操作用一个可靠的 DTLS 驱动，NetCode 则单独用战局的 ICE 上下文。
    // 入房前交换两条通路的候选地址。每局 NetCode 借用自己的上下文。
    internal sealed class LanPeerRoom : IDisposable {
        private const int ChunkSize = 1024, ChunksPerPeerUpdate = 16;
        private const string StunHost = "stun.cloudflare.com";
        private const double OfferSeconds = 120, JoinSeconds = 45;
        private const string ConnectFailed = "跨网直连失败。请重新加入，或换一个网络再试。";

        // 每个来宾同时有控制和战局两条 ICE 路线。这个对象还记消息拼包、正在处理的请求和等待的回执。
        private sealed class Peer {
            public int Control, Raid;
            public NetworkConnection Connection;
            public bool Confirmed, Connected, ViaCode;
            public double Deadline, FrameDeadline;
            public string Credential;
            public byte[] Incoming, Outgoing;
            public int Received, Sent;
            public Task<LanReply> Handling;
            public TaskCompletionSource<LanReply> Waiting;
        }

        private readonly LanRoomServer m_Server;
        private readonly EmbeddedIceTransport m_Control;
        private readonly CancellationTokenSource m_Stop = new();
        private readonly List<Peer> m_Peers = new();
        private NetworkDriver m_Driver;
        private NetworkPipeline m_Reliable;
        private Peer m_Pending, m_Guest;
        private LanRoomCode m_RoomCode;
        private string m_Offer, m_OfferId;
        private bool m_Disposed, m_Inviting;
        public LanPeerIdentity Identity { get; }
        public EmbeddedIceTransport RaidTransport { get; }
        public bool CanInvite => !m_Inviting && m_Pending == null;
        public bool HasManualOffer => m_Pending is { ViaCode: false };
        public bool WaitingForManualReply => m_Pending is { ViaCode: false, Confirmed: false };
        public string RoomCode => m_RoomCode?.DisplayCode;
        public bool Listed => m_RoomCode?.Listed == true;
        public LanJoinApplication[] Applications => m_RoomCode?.Applications ?? Array.Empty<LanJoinApplication>();
        public string InviteStatus { get; private set; }

        // 有 LanRoomServer 表示本机是房主。房主在控制层监听，来宾拿着房主证书等待协商，战局传输留给 NetCode 借用。
        public LanPeerRoom(LanRoomServer server, LanPeerIdentity identity) {
            m_Server = server;
            Identity = identity;
            var settings = new NetworkSettings(Allocator.Temp);
            try {
                m_Control = new EmbeddedIceTransport(server == null ? 256 : 1024);
                RaidTransport = new EmbeddedIceTransport(server == null ? 256 : 1024);
                settings.WithNetworkConfigParameters(500, 25, 15_000, sendQueueCapacity: 512,
                    receiveQueueCapacity: 256);
                settings.WithReliableStageParameters(64, maximumResendTime: 1000);
                Identity.Configure(ref settings, server != null);
                m_Driver = NetworkDriver.Create(m_Control.CreateNetworkInterface(), settings);
                m_Reliable = m_Driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                if (server != null && (m_Driver.Bind(m_Control.LocalEndpoint) != 0 || m_Driver.Listen() != 0))
                    throw new InvalidOperationException("无法创建跨网房间。");
            } catch {
                Dispose();
                throw;
            } finally { settings.Dispose(); }
        }

        public NetworkEndpoint GuestRaidEndpoint {
            get {
                if (m_Guest == null || !m_Guest.Connected || !IsReady(RaidTransport, m_Guest.Raid))
                    throw new InvalidOperationException(ConnectFailed);
                return RaidTransport.GetEndpoint(m_Guest.Raid);
            }
        }

        public async Task<string> GetRoomCodeAsync(CancellationToken ct) {
            return (await GetOrCreateCodeAsync(ct)).DisplayCode;
        }

        public async Task SetListedAsync(LanRoomSnapshot snapshot, bool listed, CancellationToken ct) {
            var code = await GetOrCreateCodeAsync(ct);
            await code.SetListedAsync(snapshot, listed, ct);
        }

        public Task ReviewAsync(string id, bool approve, CancellationToken ct) {
            return m_RoomCode?.ReviewAsync(id, approve, ct) ?? throw new InvalidOperationException("申请已失效。");
        }

        private async Task<LanRoomCode> GetOrCreateCodeAsync(CancellationToken ct) {
            ThrowIfClosed();
            if (m_RoomCode != null) return m_RoomCode;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, m_Stop.Token);
            var code = await LanRoomCode.CreateAsync(this, stop.Token);
            if (stop.IsCancellationRequested) {
                code.Dispose();
                stop.Token.ThrowIfCancellationRequested();
            }
            if (m_RoomCode != null) {
                code.Dispose();
                return m_RoomCode;
            }
            m_RoomCode = code;
            code.Start();
            return code;
        }

        public void UpdateRoomCode(LanRoomSnapshot snapshot) {
            // 离开准备室就撤掉公开列表和房间码。现成的连线仍留着打这一局，只禁止新加入。
            if (snapshot.Phase != LanRoomPhase.Lobby) { CloseRoomCode(); } else if (m_RoomCode != null) {
                m_RoomCode.Members = snapshot.Members.Length;
                m_RoomCode.Open = m_RoomCode.Members < snapshot.Capacity;
            }
        }

        public void CodeExpired(LanRoomCode code) {
            if (m_RoomCode == code) CloseRoomCode();
        }

        private void CloseRoomCode() {
            var code = m_RoomCode;
            m_RoomCode = null;
            code?.Dispose();
            CancelInvite(true);
        }

        public async Task<string> InviteAsync(CancellationToken ct, bool viaCode = false) {
            ThrowIfClosed();
            // 手动复制同一份还有效的邀请时直接复用。房主每次只协商一个来宾，避免回执不好对应。
            if (!viaCode && WaitingForManualReply) return m_Offer;
            if (!CanInvite) throw new InvalidOperationException("正在连接受邀玩家。");
            Peer peer = null;
            m_Inviting = true;
            try {
                peer = await GatherAsync(ct);
                peer.ViaCode = viaCode;
                m_OfferId = Guid.NewGuid().ToString("N");
                var invitation = LocalDescription(peer, m_OfferId);
                invitation.RoomId = m_Server.RoomId;
                invitation.HostId = m_Server.Persistence.HostId;
                // 两条候选地址连同公开证书交给来宾，来宾由此知道该和哪个房间、哪台房主握手。
                invitation.Certificate = Identity.Certificate;
                m_Offer = invitation.Encode(false);
                m_Pending = peer;
                peer.Deadline = Time.realtimeSinceStartupAsDouble + OfferSeconds;
                if (!viaCode) InviteStatus = "已复制，请私聊发给朋友。";
                return m_Offer;
            } catch {
                if (peer != null) Drop(peer);
                throw;
            } finally { m_Inviting = false; }
        }

        public void AcceptReply(string text, bool viaCode = false) {
            if (m_Pending == null || m_Pending.ViaCode != viaCode || m_Pending.Confirmed ||
                Time.realtimeSinceStartupAsDouble >= m_Pending.Deadline)
                throw new InvalidOperationException("邀请已过期，请重新复制邀请。");
            var reply = LanPeerInvitation.Decode(text, true);
            // 已过期或另一份邀请的回执不能改变当前待接入连接。收到匹配回执也还要等两条路径都完成。
            if (reply.Id != m_OfferId) throw new InvalidOperationException("这份回执与当前邀请不匹配。");
            var peer = m_Pending;
            try { SetRemote(peer, reply); } catch {
                Drop(peer);
                throw new InvalidOperationException("跨网回执无效，请重新邀请。");
            }
            peer.Deadline = Time.realtimeSinceStartupAsDouble + JoinSeconds;
            m_Offer = m_OfferId = null;
            if (!viaCode) InviteStatus = "正在连接受邀玩家…";
        }

        public void CancelInvite(bool viaCode = false) {
            if (m_Pending == null || m_Pending.ViaCode != viaCode) return;
            Drop(m_Pending);
            InviteStatus = null;
        }

        public async Task<LanReply> JoinAsync(
            LanPeerInvitation invite,
            LanRequest request,
            Func<string, CancellationToken, Task> replyReady,
            CancellationToken ct) {
            // 来宾先建立自己的两个 peer，把本机候选回给房主，再等控制连接握手成功后送正式 join。
            m_Guest = await GatherAsync(ct);
            string reply = LocalDescription(m_Guest, invite.Id).Encode(true);
            SetRemote(m_Guest, invite);
            await replyReady(reply, ct);
            double until = Time.realtimeSinceStartupAsDouble + JoinSeconds;
            while (m_Guest?.Connected != true) {
                ct.ThrowIfCancellationRequested();
                if (m_Guest == null || Time.realtimeSinceStartupAsDouble >= until)
                    throw new TimeoutException(ConnectFailed);
                await Task.Delay(20, ct);
            }
            return await SendAsync(request, ct);
        }

        // 控制和战局各自收集地址，邀请才同时带齐两份描述。中途失败时一起丢掉这个来宾的上下文。
        private async Task<Peer> GatherAsync(CancellationToken ct) {
            string address = EmbeddedIceNetwork.PhysicalAddress(ClientSettings.IceBindAddress);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, m_Stop.Token);
            string stun = await EmbeddedIceNetwork.ResolveStunAsync(StunHost, stop.Token);
            stop.Token.ThrowIfCancellationRequested();
            var peer = new Peer();
            m_Peers.Add(peer);
            try {
                peer.Control = m_Control.AddPeer(address, stun);
                peer.Raid = RaidTransport.AddPeer(address, stun);
                m_Control.Gather(peer.Control);
                RaidTransport.Gather(peer.Raid);
                double until = Time.realtimeSinceStartupAsDouble + 9;
                while (!m_Control.IsGatheringComplete(peer.Control) || !RaidTransport.IsGatheringComplete(peer.Raid)) {
                    if (Time.realtimeSinceStartupAsDouble >= until) throw new TimeoutException("获取跨网地址超时。请检查网络后重试。");
                    await Task.Delay(20, stop.Token);
                }
                stop.Token.ThrowIfCancellationRequested();
                return peer;
            } catch {
                Drop(peer);
                throw;
            }
        }

        private LanPeerInvitation LocalDescription(Peer peer, string id) {
            return new LanPeerInvitation {
                Id = id,
                Control = EmbeddedIceNetwork.Description(m_Control.GetLocalDescription(peer.Control), false),
                Raid = EmbeddedIceNetwork.Description(RaidTransport.GetLocalDescription(peer.Raid), false)
            };
        }

        private void SetRemote(Peer peer, LanPeerInvitation invite) {
            m_Control.SetRemoteDescription(peer.Control, EmbeddedIceNetwork.Description(invite.Control, false, true));
            RaidTransport.SetRemoteDescription(peer.Raid, EmbeddedIceNetwork.Description(invite.Raid, false, true));
            peer.Confirmed = true;
        }

        public async Task<LanReply> SendAsync(LanRequest request, CancellationToken ct) {
            ThrowIfClosed();
            var peer = m_Guest;
            if (peer?.Connected != true) throw new InvalidOperationException("与房主的跨网连接已断开。");
            // 一条控制通道同一时间只有一个请求等待回包。每条响应不必另带请求号，靠这个顺序规则找对应的等待者。
            if (peer.Waiting != null) throw new InvalidOperationException("上一条房间请求尚未完成。");
            ct.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, m_Stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var waiter = new TaskCompletionSource<LanReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue(peer, request);
            peer.Waiting = waiter;
            using var registration = timeout.Token.Register(() => waiter.TrySetCanceled());
            try {
                var reply = await waiter.Task;
                if (!string.IsNullOrEmpty(reply.Error)) throw new InvalidOperationException(reply.Error);
                return reply;
            // 请求超时时房主可能已经执行了操作。关掉这条通道，之后重新进房读真实档案，避免把上一条迟到的回执当成下一条的结果。
            } catch (OperationCanceledException) {
                // 服务器可能已经写入这笔操作。关闭该通道才能保证
                // 迟到的回执不会被当作下一次请求的回复。
                Drop(peer);
                ct.ThrowIfCancellationRequested();
                m_Stop.Token.ThrowIfCancellationRequested();
                throw new TimeoutException("房间响应超时。请重新加入并查看装备。");
            } finally { peer.Waiting = null; }
        }

        // 主线程每帧推进控制 driver。新连入必须对应已确认邀请的端点，猜一个端点进来不会成为房间成员。
        public void Pump() {
            if (m_Disposed || m_Peers.Count == 0) return;
            m_Driver.ScheduleUpdate().Complete();
            if (m_Server != null) {
                NetworkConnection connection;
                while ((connection = m_Driver.Accept()).IsCreated) {
                    var peer = m_Pending;
                    if (peer?.Confirmed == true && !peer.Connection.IsCreated &&
                        m_Driver.GetRemoteEndpoint(connection) == m_Control.GetEndpoint(peer.Control)) {
                        peer.Connection = connection;
                        peer.Connected = true;
                    } else { m_Driver.Disconnect(connection); }
                }
            }
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = m_Peers.Count - 1; i >= 0; i--) {
                var peer = m_Peers[i];
                try {
                    if ((peer.Deadline > 0 && now >= peer.Deadline) ||
                        (peer.FrameDeadline > 0 && now >= peer.FrameDeadline) || (peer.Confirmed && !peer.Connected &&
                            (m_Control.GetState(peer.Control) == EmbeddedIceState.Failed ||
                             RaidTransport.GetState(peer.Raid) == EmbeddedIceState.Failed)))
                        throw new TimeoutException(ConnectFailed);
                    // 等两条路线都选定才发起房间握手。否则房间看着已经连上，进入地图时战局那条线还没准备好。
                    if (peer == m_Guest && !peer.Connection.IsCreated && peer.Confirmed &&
                        IsReady(m_Control, peer.Control) && IsReady(RaidTransport, peer.Raid)) {
                        peer.Connection = m_Driver.Connect(m_Control.GetEndpoint(peer.Control));
                        if (!peer.Connection.IsCreated) throw new InvalidOperationException(ConnectFailed);
                    }
                    if (peer.Connection.IsCreated) PumpPeer(peer);
                } catch (Exception) {
                    if (peer == m_Pending && !peer.ViaCode)
                        InviteStatus = peer.Confirmed ? ConnectFailed : "邀请已过期，可以重新复制。";
                    Drop(peer);
                }
            }
        }

        // 等待 ICE 决定正式使用的候选地址对。临时连通性检查
        // 可能在房主还没收到粘贴回的应答时就成功。
        private static bool IsReady(EmbeddedIceTransport transport, int peer) {
            return transport.GetState(peer) == EmbeddedIceState.Completed;
        }

        private void PumpPeer(Peer peer) {
            // 房主异步处理档案请求。完成才把回执放入待发送缓冲，未完成时不能开始下一条。
            if (peer.Handling?.IsCompleted == true) {
                Queue(peer, peer.Handling.GetAwaiter().GetResult());
                peer.Handling = null;
            }
            NetworkEvent.Type type;
            while ((type = m_Driver.PopEventForConnection(peer.Connection, out var reader, out var pipeline)) !=
                   NetworkEvent.Type.Empty)
                if (type == NetworkEvent.Type.Connect) { peer.Connected = true; } else if
                    (type == NetworkEvent.Type.Disconnect) { throw new EndOfStreamException(); } else if
                    (type == NetworkEvent.Type.Data) {
                    // 控制消息必须来自已连好的可靠管线，而且符合一问一答顺序。乱序闯入的消息会断开该 peer。
                    if (pipeline != m_Reliable || !peer.Connected || peer.Handling != null || (m_Server == null
                            ? peer.Waiting == null || peer.Waiting.Task.IsCompleted
                            : peer.Outgoing != null))
                        throw new InvalidDataException();
                    byte[] message = Receive(peer, ref reader);
                    if (message == null) continue;
                    if (m_Server == null)
                        peer.Waiting.TrySetResult(LanProtocol.Decode<LanReply>(message));
                    else
                        peer.Handling = HandleAsync(peer, LanProtocol.Decode<LanRequest>(message));
                }
            Flush(peer);
        }

        private async Task<LanReply> HandleAsync(Peer peer, LanRequest request) {
            // 同一连接的第一条房间指令必须是 join，之后必须复用绑定的访客凭据。这阻止对面在同一连接换身份。
            bool joining = request.Action == "join";
            if (request.RoomId != m_Server.RoomId ||
                (peer.Credential == null ? !joining : joining || peer.Credential != request.Credential))
                return new LanReply { Error = "此连接没有房间访问权限。" };
            var reply = await Task.Run(() => m_Server.ReplyTo(request, peer.Control));
            if (joining && string.IsNullOrEmpty(reply.Error)) {
                // 后台开档时来宾可能掉线。Join 即使已成功也要撤掉刚登记的成员，避免占着一个幽灵席位。
                if (m_Disposed || !m_Peers.Contains(peer)) {
                    await Task.Run(() => m_Server.DropPeer(request.Credential, peer.Control));
                } else {
                    peer.Credential = request.Credential;
                    peer.Deadline = 0;
                    m_Pending = null;
                    InviteStatus = null;
                }
            }
            return reply;
        }

        private static void Queue<T>(Peer peer, T value) {
            if (peer.Outgoing != null) throw new InvalidDataException();
            peer.Outgoing = LanProtocol.Encode(value);
            peer.Sent = 0;
        }

        private void Flush(Peer peer) {
            // 大房间快照按固定块发送并限制每帧包数。发送队列满就留在当前偏移，下帧继续。
            for (int i = 0; peer.Outgoing != null && i < ChunksPerPeerUpdate; i++) {
                int length = Math.Min(ChunkSize, peer.Outgoing.Length - peer.Sent);
                int header = peer.Sent == 0 ? sizeof(int) : 0;
                int result = m_Driver.BeginSend(m_Reliable, peer.Connection, out var writer, length + header);
                if (result == (int)Networking.Transport.Error.StatusCode.NetworkSendQueueFull) return;
                if (result != 0) throw new IOException();
                if (header != 0) writer.WriteIntNetworkByteOrder(peer.Outgoing.Length);
                writer.WriteBytes(peer.Outgoing.AsSpan(peer.Sent, length));
                result = m_Driver.EndSend(writer);
                if (result == (int)Networking.Transport.Error.StatusCode.NetworkSendQueueFull) return;
                if (result <= 0) throw new IOException();
                peer.Sent += length;
                if (peer.Sent == peer.Outgoing.Length) peer.Outgoing = null;
            }
        }

        private static byte[] Receive(Peer peer, ref DataStreamReader reader) {
            if (peer.Incoming == null) {
                if (reader.Length < sizeof(int)) throw new InvalidDataException();
                // 先读整份消息的长度，合理才分配缓冲。随后每包都要求正好是下一块的大小，并有拼包截止时间。
                int size = reader.ReadIntNetworkByteOrder();
                if (size < 2 || size > LanProtocol.MaxFrame) throw new InvalidDataException();
                peer.Incoming = new byte[size];
                peer.Received = 0;
                peer.FrameDeadline = Time.realtimeSinceStartupAsDouble + 25;
            }
            int length = Math.Min(ChunkSize, peer.Incoming.Length - peer.Received);
            if (reader.Length - reader.GetBytesRead() != length) throw new InvalidDataException();
            reader.ReadBytes(peer.Incoming.AsSpan(peer.Received, length));
            peer.Received += length;
            if (peer.Received != peer.Incoming.Length) return null;
            byte[] complete = peer.Incoming;
            peer.Incoming = null;
            peer.FrameDeadline = 0;
            return complete;
        }

        private void Drop(Peer peer) {
            // 退出的来宾两条地址都解除注册，房主也撤掉该凭据的入场票。等待回执的一方尽快收到断线异常。
            if (!m_Peers.Remove(peer)) return;
            if (peer.Connection.IsCreated) m_Driver.Disconnect(peer.Connection);
            if (peer.Control > 0) m_Control.RemovePeer(peer.Control);
            if (peer.Raid > 0) RaidTransport.RemovePeer(peer.Raid);
            if (peer.Credential != null) _ = Task.Run(() => m_Server.DropPeer(peer.Credential, peer.Control));
            peer.Waiting?.TrySetException(new IOException("与房主的跨网连接已断开。"));
            if (peer == m_Pending) {
                m_Pending = null;
                m_Offer = m_OfferId = null;
            }
            if (peer == m_Guest) m_Guest = null;
        }

        private void ThrowIfClosed() {
            if (m_Disposed) throw new InvalidOperationException("跨网房间已关闭。");
        }

        public void Dispose() {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Stop.Cancel();
            CloseRoomCode();
            try {
                if (m_Driver.IsCreated) {
                    bool disconnecting = false;
                    foreach (var peer in m_Peers) {
                        if (!peer.Connection.IsCreated) continue;
                        m_Driver.Disconnect(peer.Connection);
                        disconnecting = true;
                    }
                    if (disconnecting) m_Driver.ScheduleUpdate().Complete();
                }
            } finally {
                if (m_Driver.IsCreated) m_Driver.Dispose();
                m_Control?.Dispose();
                RaidTransport?.CloseAfterDriver();
                m_Peers.Clear();
                m_Pending = m_Guest = null;
            }
        }
    }
}