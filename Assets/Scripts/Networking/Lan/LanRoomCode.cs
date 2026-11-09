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
    internal sealed class LanRoomListing
    {
        public string Id, Name;
        public bool Clash;
        public int Members, Capacity;
    }

    internal sealed class LanJoinApplication
    {
        public string Id, Name;
    }

    // Exchanges the same invitation and reply as the clipboard flow over HTTPS.
    // Room state, profiles and game packets use the existing ICE connections.
    internal sealed class LanRoomCode : IDisposable
    {
        private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        private static readonly HttpClient s_Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { MaxResponseContentBufferSize = 32_768 };
        private static readonly JsonSerializerSettings s_RequestJson = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        private sealed class Message
        {
            public string Code, Token, Attempt, Offer, Reply, Error, Status;
            public LanRoomListing[] Rooms;
            public LanJoinApplication[] Applications;
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
        private int m_DirectoryRevision;
        public string DisplayCode => m_Code.Insert(4, "-");
        public bool Open { get; set; } = true;
        public int Members { get; set; } = 1;
        public bool Listed { get; private set; }
        public LanJoinApplication[] Applications { get; private set; } = Array.Empty<LanJoinApplication>();

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

        public async Task SetListedAsync(LanRoomSnapshot room, bool listed, CancellationToken ct)
        {
            Members = room.Members.Length;
            object listing = listed ? new { name = room.Name, clash = room.Mode == RaidGameMode.BountyClash, members = Members, capacity = room.Capacity } : null;
            await PostAsync(m_Address, "publish", new { code = m_Code, listing }, m_Token, ct);
            m_DirectoryRevision++;
            Listed = listed;
            if (!listed) Applications = Array.Empty<LanJoinApplication>();
        }

        public async Task ReviewAsync(string id, bool approve, CancellationToken ct)
        {
            await PostAsync(m_Address, "review", new { code = m_Code, attempt = id, decision = approve ? "approve" : "decline" }, m_Token, ct);
            m_DirectoryRevision++;
            Applications = Applications.Where(item => item.Id != id).ToArray();
        }

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
                        int revision = m_DirectoryRevision;
                        var poll = await HostAsync(new { code = m_Code, open, listing = Listed ? new { members = Members } : null }, ct);
                        if (Listed && revision == m_DirectoryRevision)
                            Applications = poll.Applications ?? Array.Empty<LanJoinApplication>();
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

        public static async Task<LanRoomListing[]> BrowseAsync(CancellationToken ct) =>
            (await PostAsync(Address(), "list", new { }, null, ct)).Rooms ?? Array.Empty<LanRoomListing>();

        public static Task JoinAsync(string input, string playerName, CancellationToken ct)
        {
            string code = (input ?? "").Trim().Replace("-", "").ToUpperInvariant();
            if (!ValidCode(code)) throw new InvalidOperationException("请输入 8 位房间码。");
            return JoinAsync(code, null, playerName, null, ct);
        }

        public static Task ApplyAsync(string roomId, string playerName, Action<string> progress, CancellationToken ct) =>
            JoinAsync(null, roomId, playerName, progress, ct);

        private static async Task JoinAsync(string code, string roomId, string playerName, Action<string> progress, CancellationToken ct)
        {
            var address = Address();
            bool applying = roomId != null;
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(applying ? 185 : 85));
            string token = null;
            try
            {
                token = (await PostAsync(address, applying ? "apply" : "join", new { code, roomId, name = applying ? playerName : null }, null, wait.Token)).Token;
                wait.Token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(token)) throw new SignalException("invalid_response");
                if (applying) progress?.Invoke("等待房主同意…");
                bool approved = false;
                while (true)
                {
                    var poll = await PostAsync(address, "guest", new { code, roomId }, token, wait.Token);
                    wait.Token.ThrowIfCancellationRequested();
                    if (poll.Status == "declined") throw new SignalException("declined");
                    if (poll.Status == "approved" && !approved)
                    {
                        approved = true;
                        progress?.Invoke("已同意，正在连接房主…");
                    }
                    if (poll.Offer != null)
                    {
                        await LanRoomClient.JoinPeerAsync(poll.Offer, playerName, async (reply, replyCt) =>
                        {
                            await PostAsync(address, "guest", new { code, roomId, reply }, token, replyCt);
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
                if (token != null) _ = BestEffortAsync(address, "guest", new { code, roomId, cancel = true }, token);
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
            request.Content = new StringContent(JsonConvert.SerializeObject(body, s_RequestJson), Encoding.UTF8, "application/json");
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
            "room_closed" => "房间已关闭或开始对局，请向房主确认。",
            "room_busy" => "房间暂时无法加入，请稍后重试。",
            "attempt_closed" => "这次加入已结束，请重试。",
            "declined" => "房主拒绝了加入申请。",
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
