using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Unity.MP_FPS
{
    // Exchanges the same invitation and reply as the clipboard flow over HTTPS.
    // Room state, profiles and game packets use the existing ICE connections.
    internal sealed class LanRoomCode : IDisposable
    {
        private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        private static readonly HttpClient s_Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { MaxResponseContentBufferSize = 16_384 };

        private sealed class Message
        {
            public string Code, Token, Attempt, Offer, Reply, Error;
        }

        private sealed class SignalException : InvalidOperationException
        {
            public string Reason { get; }
            public SignalException(string reason) : base(ErrorText(reason)) { Reason = reason; }
        }

        private readonly LanPeerRoom m_Room;
        private readonly Uri m_Address;
        private readonly string m_Code, m_Token;
        private readonly CancellationTokenSource m_Stop = new CancellationTokenSource();
        public string DisplayCode => m_Code.Insert(4, "-");
        public bool Open { get; set; } = true;

        private LanRoomCode(LanPeerRoom room, Uri address, Message registration)
        { m_Room = room; m_Address = address; m_Code = registration.Code; m_Token = registration.Token; }

        public static async Task<LanRoomCode> CreateAsync(LanPeerRoom room, CancellationToken ct)
        {
            var address = Address();
            var result = await PostAsync(address, "create", new { }, null, ct);
            if (!ValidCode(result.Code) || string.IsNullOrEmpty(result.Token)) throw new SignalException("invalid_response");
            return new LanRoomCode(room, address, result);
        }

        public void Start() => _ = PollAsync(m_Stop.Token);

        private async Task PollAsync(CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    bool open = Open && !m_Room.HasManualOffer;
                    try
                    {
                        var poll = await HostAsync(new { code = m_Code, open }, ct);
                        if (Open && m_Room.CanInvite && poll.Attempt != null) await ExchangeAsync(poll.Attempt, ct);
                    }
                    catch (SignalException ex) when (ex.Reason != "room_closed")
                    {
                        await Task.Delay(1500, ct);
                    }
                    await Task.Delay(Open ? 1500 : 3000, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (SignalException) { m_Room.CodeExpired(this); }
        }

        private async Task ExchangeAsync(string attempt, CancellationToken ct)
        {
            using var exchange = CancellationTokenSource.CreateLinkedTokenSource(ct);
            exchange.CancelAfter(TimeSpan.FromSeconds(30));
            bool accepted = false;
            try
            {
                string offer = await m_Room.InviteAsync(exchange.Token, viaCode: true);
                var result = await HostAsync(new { code = m_Code, attempt, offer, open = Open }, exchange.Token);
                while (result.Reply == null)
                {
                    await Task.Delay(800, exchange.Token);
                    result = await HostAsync(new { code = m_Code, attempt, open = Open }, exchange.Token);
                }
                exchange.Token.ThrowIfCancellationRequested();
                m_Room.AcceptReply(result.Reply, viaCode: true);
                accepted = true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (SignalException ex) when (ex.Reason != "room_closed") { }
            catch (Exception ex) when (ex is InvalidDataException or SocketException or TimeoutException ||
                ex is InvalidOperationException and not SignalException) { }
            finally
            {
                if (!accepted) m_Room.CancelInvite(viaCode: true);
                if (!ct.IsCancellationRequested)
                    await BestEffortAsync(m_Address, "host", new { code = m_Code, attempt, finish = true, open = Open }, m_Token);
            }
        }

        private Task<Message> HostAsync(object body, CancellationToken ct) => PostAsync(m_Address, "host", body, m_Token, ct);

        public static async Task JoinAsync(string input, string playerName, CancellationToken ct)
        {
            string code = (input ?? "").Trim().Replace("-", "").ToUpperInvariant();
            if (!ValidCode(code)) throw new InvalidOperationException("请输入 8 位房间码。");
            var address = Address();
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(85));
            string token = null;
            try
            {
                token = (await PostAsync(address, "join", new { code }, null, wait.Token)).Token;
                if (string.IsNullOrEmpty(token)) throw new SignalException("invalid_response");
                while (true)
                {
                    var poll = await PostAsync(address, "guest", new { code }, token, wait.Token);
                    if (poll.Offer != null)
                    {
                        await LanRoomClient.JoinPeerAsync(poll.Offer, playerName, async (reply, replyCt) =>
                        {
                            await PostAsync(address, "guest", new { code, reply }, token, replyCt);
                        }, wait.Token);
                        token = null; // The host finishes the exchange after taking the reply.
                        return;
                    }
                    await Task.Delay(1000, wait.Token);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { throw new TimeoutException("加入跨网房间超时，请重试。"); }
            finally
            {
                if (token != null) _ = BestEffortAsync(address, "guest", new { code, cancel = true }, token);
            }
        }

        private static bool ValidCode(string code) => code?.Length == 8 && code.All(c => Alphabet.IndexOf(c) >= 0);

        private static Uri Address()
        {
            if (!Uri.TryCreate(ClientSettings.FriendSignalingUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var url) ||
                url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0 ||
                !(url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp && url.IsLoopback))
                throw new InvalidOperationException("房间码服务尚未配置，可使用手动邀请。");
            return url;
        }

        private static async Task<Message> PostAsync(Uri address, string action, object body, string token, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(address, action));
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            try
            {
                using var response = await s_Http.SendAsync(request, timeout.Token);
                var message = JsonConvert.DeserializeObject<Message>(await response.Content.ReadAsStringAsync(), LanProtocol.Json);
                if (message == null) throw new SignalException("invalid_response");
                if (!response.IsSuccessStatusCode) throw new SignalException(message.Error);
                return message;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SignalException("unavailable"); }
            catch (HttpRequestException) { throw new SignalException("unavailable"); }
            catch (JsonException) { throw new SignalException("invalid_response"); }
        }

        private static async Task BestEffortAsync(Uri address, string action, object body, string token)
        { try { await PostAsync(address, action, body, token, CancellationToken.None); } catch (SignalException) { } }

        private static string ErrorText(string reason) => reason switch
        {
            "room_closed" => "房间码已失效，请向房主获取新码。",
            "room_busy" => "房间暂时无法加入，请稍后重试。",
            "attempt_closed" => "这次加入已结束，请重新输入房间码。",
            "busy" => "房间码服务繁忙，请稍后重试。",
            _ => "房间码服务暂不可用，可使用手动邀请。"
        };

        public void Dispose()
        {
            if (m_Stop.IsCancellationRequested) return;
            m_Stop.Cancel();
            m_Stop.Dispose();
            _ = BestEffortAsync(m_Address, "close", new { code = m_Code }, m_Token);
        }
    }
}
