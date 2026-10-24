using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.MP_FPS.Online;
using UnityEngine;

namespace Unity.MP_FPS
{
    public static partial class AccountClient
    {
        internal static async Task<LobbySnapshot> LobbyAsync(LobbyAction action, CancellationToken ct)
        {
            string token = Token;
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(s_BaseUrl)) throw new InvalidOperationException("请先登录在线账号。");
            using var request = new HttpRequestMessage(action == null ? HttpMethod.Get : HttpMethod.Post,
                Address(s_BaseUrl, action == null ? "auth/lobby" : "auth/lobby/action"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (action != null) request.Content = new StringContent(JsonConvert.SerializeObject(action), Encoding.UTF8, "application/json");
            using var response = await Http().SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(); ct.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(JsonConvert.DeserializeObject<Failure>(body)?.Error ?? "大厅服务暂不可用，请重试。");
            if (token != Token) throw new OperationCanceledException("登录账号已变更。");
            return JsonConvert.DeserializeObject<LobbySnapshot>(body) ?? throw new InvalidOperationException("大厅响应无效。");
        }
    }
    public static class OnlineLobbyClient
    {
        public static LobbySnapshot Snapshot { get; private set; }
        public static string InstanceId { get; private set; }
        public static string ServerName { get; private set; }
        public static bool InInstance => !PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan &&
            !string.IsNullOrEmpty(InstanceId) && s_Account == PlayerProfileClient.LocalPreferenceScope;
        public static string LastAttempt { get; private set; }
        private static string s_Account;
        public static string Error { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Snapshot = null; InstanceId = ServerName = LastAttempt = s_Account = Error = null; }
        public static async Task RefreshAsync(CancellationToken ct)
        {
            string scope = PlayerProfileClient.LocalPreferenceScope;
            if (s_Account != scope) { Reset(); SessionReconnect.Dismiss(); s_Account = scope; }
            Snapshot = await AccountClient.LobbyAsync(null, ct);
        }
        public static async Task ActAsync(LobbyAction action, CancellationToken ct)
        { Snapshot = await AccountClient.LobbyAsync(action, ct); Error = null; }
        public static void Join(bool retry = false)
        {
            var s = Snapshot;
            if (s?.Phase != "Ready" || string.IsNullOrEmpty(s.Address) || s.InstanceId == LastAttempt && !retry) return;
            foreach (var member in s.Members) if (member.PlayerId == s.SelfId && member.Returned) return;
            InstanceId = s.InstanceId; ServerName = s.ServerName; LastAttempt = s.InstanceId;
            Error = null;
            // A localhost account-service override identifies this same host PC.
            // Keep certificate identity separate from the transport IP (DTLS SNI).
            bool local = Uri.TryCreate(ClientSettings.AccountServiceUrl, UriKind.Absolute, out var service) && service.IsLoopback;
            ConnectionSettings.Instance.IPAddress = local ? "127.0.0.1" : s.Address;
            ConnectionSettings.Instance.Port = s.Port.ToString();
            ConnectionSettings.Instance.ConnectionError = null;
            GameSettings.Instance.ConnectionMode = 1;
            GameSettings.Instance.RequestedMode = RaidGameMode.Expedition;
            ConnectionSettings.Instance.ResumeOnly = false;
            GameManager.Instance.StartGameAsync(CreationType.ConnectAndJoin);
        }
        public static async Task ReturnedAsync()
        {
            if (!InInstance || SessionReconnect.Available) return;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                await ActAsync(new LobbyAction { Action = "return" }, timeout.Token);
                InstanceId = ServerName = null;
            }
            catch (Exception ex) { Error = "等待服务器结算后返回队伍：" + ex.Message; }
        }
        public static async Task RetryReturnAsync(CancellationToken ct)
        {
            await ActAsync(new LobbyAction { Action = "return" }, ct);
            SessionReconnect.Dismiss(); Error = null;
            InstanceId = ServerName = null;
        }
    }
}
