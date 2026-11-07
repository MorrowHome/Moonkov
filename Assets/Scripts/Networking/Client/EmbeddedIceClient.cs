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

namespace Unity.MP_FPS
{
    public static partial class AccountClient
    {
        internal static async Task<IceReply> IceAsync(IceClientMessage message, CancellationToken ct)
        {
            string token = Token;
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(s_BaseUrl)) throw new InvalidOperationException("请先登录在线账号。");
            using var request = new HttpRequestMessage(HttpMethod.Post, Address(s_BaseUrl, "auth/lobby/ice"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonConvert.SerializeObject(message), Encoding.UTF8, "application/json");
            using var response = await Http().SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync();
            ct.ThrowIfCancellationRequested();
            if (token != Token) throw new OperationCanceledException("登录账号已变更。");
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(JsonConvert.DeserializeObject<Failure>(body)?.Error ?? "P2P 协商服务不可用，请更新账号后端。");
            return JsonConvert.DeserializeObject<IceReply>(body) ?? throw new InvalidOperationException("P2P 协商响应无效。");
        }
    }
    public sealed class EmbeddedIceClientSession : IDisposable
    {
        public EmbeddedIceTransport Transport { get; private set; }
        public NetworkEndpoint Endpoint { get; private set; }
        readonly string m_Instance, m_Attempt;
        bool m_Disposed, m_Offered;
        EmbeddedIceClientSession(string instance) { m_Instance = instance; m_Attempt = Guid.NewGuid().ToString("N"); }

        public static async Task<EmbeddedIceClientSession> ConnectAsync(LobbySnapshot snapshot, CancellationToken ct)
        {
            if (snapshot == null || snapshot.NetworkProtocol != OnlineNetworkProtocol.Version || !snapshot.IceAvailable)
                throw new InvalidOperationException("本局 P2P 服务尚未就绪，请同步更新独立服务器和账号后端。");
            var session = new EmbeddedIceClientSession(snapshot.InstanceId);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                string address = EmbeddedIceNetwork.PhysicalAddress(ClientSettings.IceBindAddress);
                string stun = await EmbeddedIceNetwork.ResolveStunAsync(snapshot.IceStunHost, deadline.Token);
                if (snapshot.IceStunPort is < 1 or > 65535) throw new InvalidOperationException("P2P 地址服务端口无效。");
                bool publicOnly = EmbeddedIceNetwork.SameHost(snapshot);
                session.Transport = new EmbeddedIceTransport();
                int peer = session.Transport.AddPeer(address, stun, (ushort)snapshot.IceStunPort);
                session.Transport.Gather(peer);
                await EmbeddedIceNetwork.WaitAsync(() => session.Transport.IsGatheringComplete(peer), 7, deadline.Token);
                var message = new IceClientMessage { Protocol = OnlineNetworkProtocol.Version, InstanceId = session.m_Instance,
                    AttemptId = session.m_Attempt, Action = "offer", PublicOnly = publicOnly,
                    Description = EmbeddedIceNetwork.Description(session.Transport.GetLocalDescription(peer), publicOnly) };
                session.m_Offered = true;
                IceReply reply;
                do
                {
                    reply = await AccountClient.IceAsync(message, deadline.Token);
                    message.Action = "poll"; message.Description = null;
                } while (reply.Status == "pending");
                if (reply.Status != "ready") throw new InvalidOperationException(reply.Error ?? "P2P 协商已结束，请重试。");
                session.Transport.SetRemoteDescription(peer, EmbeddedIceNetwork.Description(reply.Description, publicOnly));
                await EmbeddedIceNetwork.WaitAsync(() =>
                {
                    var state = session.Transport.GetState(peer);
                    if (state == EmbeddedIceState.Failed) throw new InvalidOperationException("P2P 直连失败，本局连接已结束。可在下一局选择 VPS 中转。");
                    return EmbeddedIceNetwork.Connected(state);
                }, 7, deadline.Token);
                session.Endpoint = session.Transport.GetEndpoint(peer);
                Debug.Log(publicOnly ? "[P2P] Public-only ICE path selected for host-PC client." : "[P2P] ICE path selected; starting DTLS.");
                return session;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                session.Dispose();
                throw new TimeoutException("P2P 直连超时，本局不会切换到 VPS。请重试，或在下一局更换连接方式。");
            }
            catch { session.Dispose(); throw; }
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            Transport?.Dispose();
            if (m_Offered) _ = CancelAsync();
        }
        async Task CancelAsync()
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await AccountClient.IceAsync(new IceClientMessage { Protocol = OnlineNetworkProtocol.Version,
                    InstanceId = m_Instance, AttemptId = m_Attempt, Action = "cancel" }, timeout.Token);
            }
            catch (Exception) { /* Expiring signaling and ICE consent also bound abandoned peers. */ }
        }
    }
}
