// 官方在线战局的 ICE 客户端。先经 AccountClient 向后端交换两端候选地址，再等原生传输找到可用路线，最后交给 GameConnection 建 NetCode 连接。
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.MP_FPS.EmbeddedNetworking;
using Unity.MP_FPS.Online;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.MP_FPS {
    public static partial class AccountClient {
        internal static async Task<IceReply> IceAsync(IceClientMessage message, CancellationToken ct) {
            string token = Token;
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(s_BaseUrl))
                throw new InvalidOperationException("请先登录在线账号。");
            // 候选地址通过账号大厅接口交换。这条 HTTP 请求用账号凭据识别当前人，后面的游戏数据会走 ICE 和 NetCode。
            using var request = new HttpRequestMessage(HttpMethod.Post, Address(s_BaseUrl, "auth/lobby/ice"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content =
                new StringContent(JsonConvert.SerializeObject(message), Encoding.UTF8, "application/json");
            using var response = await Http().SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync();
            ct.ThrowIfCancellationRequested();
            if (token != Token) throw new OperationCanceledException("登录账号已变更。");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(JsonConvert.DeserializeObject<Failure>(body)?.Error ??
                                                    "P2P 协商服务不可用，请更新账号后端。");
            return JsonConvert.DeserializeObject<IceReply>(body) ?? throw new InvalidOperationException("P2P 协商响应无效。");
        }
    }

    public sealed class EmbeddedIceClientSession : IDisposable {
        public EmbeddedIceTransport Transport { get; private set; }
        public NetworkEndpoint Endpoint { get; private set; }
        private readonly string m_Instance, m_Attempt;
        private bool m_Disposed, m_Offered;

        private EmbeddedIceClientSession(string instance) {
            m_Instance = instance;
            m_Attempt = Guid.NewGuid().ToString("N");
        }

        public static async Task<EmbeddedIceClientSession> ConnectAsync(LobbySnapshot snapshot, CancellationToken ct) {
            if (snapshot == null || snapshot.NetworkProtocol != OnlineNetworkProtocol.Version || !snapshot.IceAvailable)
                throw new InvalidOperationException("本局 P2P 服务尚未就绪，请同步更新独立服务器和账号后端。");
            // 一场战局可以有多个连接尝试。每次创建自己的尝试号与超时，取消旧尝试时不会把新的一起停掉。
            var session = new EmbeddedIceClientSession(snapshot.InstanceId);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try {
                string address = EmbeddedIceNetwork.PhysicalAddress(ClientSettings.IceBindAddress);
                string stun = await EmbeddedIceNetwork.ResolveStunAsync(snapshot.IceStunHost, deadline.Token);
                if (snapshot.IceStunPort is < 1 or > 65535) throw new InvalidOperationException("P2P 地址服务端口无效。");
                // 客户端和服务器在同一台机器时关闭这次协商的私有候选，按项目给同主机测试场景规定的公开路线测试。
                bool publicOnly = EmbeddedIceNetwork.SameHost(snapshot);
                session.Transport = new EmbeddedIceTransport();
                int peer = session.Transport.AddPeer(address, stun, (ushort)snapshot.IceStunPort);
                // 等待本机 ICE 候选收齐再发 offer。发送的描述只列此次连接允许使用的地址。
                session.Transport.Gather(peer);
                await EmbeddedIceNetwork.WaitAsync(() => session.Transport.IsGatheringComplete(peer), 7,
                    deadline.Token);
                var message = new IceClientMessage {
                    Protocol = OnlineNetworkProtocol.Version, InstanceId = session.m_Instance,
                    AttemptId = session.m_Attempt, Action = "offer", PublicOnly = publicOnly,
                    Description =
                        EmbeddedIceNetwork.Description(session.Transport.GetLocalDescription(peer), publicOnly)
                };
                session.m_Offered = true;
                IceReply reply;
                do {
                    reply = await AccountClient.IceAsync(message, deadline.Token);
                    // 第一次发送候选地址后，后续用同一个尝试号查询结果，不再反复上传 offer。
                    message.Action = "poll";
                    message.Description = null;
                } while (reply.Status == "pending");
                if (reply.Status != "ready") throw new InvalidOperationException(reply.Error ?? "P2P 协商已结束，请重试。");
                // 接到对端地址才开始双方连通性检查。找到可用路线后，GameConnection 用该逻辑端点继续建加密 NetCode 连接。
                session.Transport.SetRemoteDescription(peer,
                    EmbeddedIceNetwork.Description(reply.Description, publicOnly));
                await EmbeddedIceNetwork.WaitAsync(() => {
                    var state = session.Transport.GetState(peer);
                    if (state == EmbeddedIceState.Failed)
                        throw new InvalidOperationException("P2P 直连失败，本局连接已结束。可在下一局选择 VPS 中转。");
                    return EmbeddedIceNetwork.Connected(state);
                }, 7, deadline.Token);
                session.Endpoint = session.Transport.GetEndpoint(peer);
                Debug.Log(publicOnly
                    ? "[P2P] Public-only ICE path selected for host-PC client."
                    : "[P2P] ICE path selected; starting DTLS.");
                return session;
            // 只有内部 20 秒期限到了才转换成超时。调用方主动取消则保留取消语义，两种都回收刚才开的 ICE 资源。
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                session.Dispose();
                throw new TimeoutException("P2P 直连超时，本局不会切换到 VPS。请重试，或在下一局更换连接方式。");
            } catch {
                session.Dispose();
                throw;
            }
        }

        public void Dispose() {
            if (m_Disposed) return;
            m_Disposed = true;
            Transport?.Dispose();
            // 信令已经登记过这次尝试时尽力通知后端撤销。对端也有过期机制，所以退出不会一直等网络回应。
            if (m_Offered) _ = CancelAsync();
        }

        private async Task CancelAsync() {
            try {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await AccountClient.IceAsync(new IceClientMessage {
                    Protocol = OnlineNetworkProtocol.Version,
                    InstanceId = m_Instance, AttemptId = m_Attempt, Action = "cancel"
                }, timeout.Token);
            } catch (Exception) {
                /* 信令有过期时间，ICE 也会检查对方是否仍在连接，废弃对端不会永久留存。 */
            }
        }
    }
}