using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Unity.MP_FPS
{
    public static class AccountClient
    {
        private sealed class LoginResult { public string Token, DisplayName; public DateTime ExpiresAt; }
        private sealed class Profile { public string DisplayName; public int Dust, Alloy, Cells; }
        private static HttpClient s_Http;
        private static string s_Token, s_Name, s_PreferenceKey;
        private static DateTime s_Expires;
        public static int StashDust { get; private set; }
        public static int StashAlloy { get; private set; }
        public static int StashCells { get; private set; }
        public static int CarryCells { get; private set; }
        public static void SelectCarryCells(int cells) => CarryCells = Mathf.Clamp(cells, 0, Mathf.Min(StashCells, RaidRules.BagCapacity));
        public static void UpdateStash(int dust, int alloy, int cells)
        {
            StashDust = dust; StashAlloy = alloy; StashCells = cells;
            SelectCarryCells(CarryCells);
        }
        public static string Token { get { Load(); return IsLoggedIn ? s_Token : ""; } }
        public static string DisplayName { get { Load(); return s_Name; } }
        public static bool IsLoggedIn { get { Load(); return !string.IsNullOrEmpty(s_Token) && s_Expires > DateTime.UtcNow; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_Http?.Dispose(); s_Http = null;
            s_Token = s_Name = s_PreferenceKey = null; s_Expires = default;
            StashDust = StashAlloy = StashCells = 0;
            CarryCells = 0;
        }

        private static void Load()
        {
            if (s_PreferenceKey != null) return;
            s_PreferenceKey = "MoonRaid.Account.v1";
#if UNITY_EDITOR
            // Virtual editor clones keep independent logins even if their PlayerPrefs registry is shared.
            using (var hash = SHA256.Create())
                s_PreferenceKey += BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Application.dataPath))).Replace("-", "");
#endif
            var saved = PlayerPrefs.GetString(s_PreferenceKey, "");
            try
            {
                var result = JsonConvert.DeserializeObject<LoginResult>(saved);
                if (result == null) return;
                s_Token = result.Token; s_Name = result.DisplayName; s_Expires = result.ExpiresAt.ToUniversalTime();
            }
            catch (JsonException) { PlayerPrefs.DeleteKey(s_PreferenceKey); }
        }

        public static async Task AuthenticateAsync(string baseUrl, string username, string password, bool register, CancellationToken ct)
        {
            Load();
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) throw new InvalidOperationException("Enter username and password.");
            using (var request = new HttpRequestMessage(HttpMethod.Post, Address(baseUrl, register ? "auth/register" : "auth/login")))
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(new
                {
                    Username = username.Trim(), Password = password,
                    GuestToken = register ? RaidGuestIdentity.GetToken(false) : null
                }), Encoding.UTF8, "application/json");
                using (var response = await Http().SendAsync(request, ct))
                {
                    ThrowIfFailed(response.StatusCode);
                    var result = JsonConvert.DeserializeObject<LoginResult>(await response.Content.ReadAsStringAsync());
                    if (result == null || result.Token == null || result.Token.Length != 64 || result.ExpiresAt <= DateTime.UtcNow)
                        throw new InvalidOperationException("Invalid login response.");
                    ct.ThrowIfCancellationRequested();
                    CarryCells = 0;
                    s_Token = result.Token; s_Name = result.DisplayName; s_Expires = result.ExpiresAt.ToUniversalTime();
                    PlayerPrefs.SetString(s_PreferenceKey, JsonConvert.SerializeObject(result)); PlayerPrefs.Save();
                    GameSettings.Instance.PlayerName = s_Name;
                    await ValidateAsync(baseUrl, ct);
                }
            }
        }

        public static async Task ValidateAsync(string baseUrl, CancellationToken ct)
        {
            if (!IsLoggedIn) return;
            using (var request = new HttpRequestMessage(HttpMethod.Get, Address(baseUrl, "auth/me")))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
                using (var response = await Http().SendAsync(request, ct))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized) { Clear(); return; }
                    ThrowIfFailed(response.StatusCode);
                    var profile = JsonConvert.DeserializeObject<Profile>(await response.Content.ReadAsStringAsync());
                    if (profile == null) throw new InvalidOperationException("Invalid account response.");
                    ct.ThrowIfCancellationRequested();
                    s_Name = profile.DisplayName;
                    UpdateStash(profile.Dust, profile.Alloy, profile.Cells);
                    GameSettings.Instance.PlayerName = s_Name;
                }
            }
        }

        public static async Task LogoutAsync(string baseUrl, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, Address(baseUrl, "auth/logout")))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
                using (var response = await Http().SendAsync(request, ct)) ThrowIfFailed(response.StatusCode);
            }
            Clear();
        }

        private static void Clear()
        {
            s_Token = s_Name = ""; s_Expires = default;
            StashDust = StashAlloy = StashCells = 0;
            CarryCells = 0;
            PlayerPrefs.DeleteKey(s_PreferenceKey); PlayerPrefs.Save();
        }

        private static HttpClient Http() => s_Http ?? (s_Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        private static Uri Address(string baseUrl, string path)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var url) ||
                (url.Scheme != "https" && !(url.IsLoopback && url.Scheme == "http")))
                throw new InvalidOperationException("Account service must use HTTPS, or localhost for development.");
            return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path);
        }
        private static void ThrowIfFailed(HttpStatusCode code)
        {
            if ((int)code >= 200 && (int)code < 300) return;
            if (code == HttpStatusCode.Unauthorized) throw new InvalidOperationException("Incorrect username/password or expired login.");
            if (code == HttpStatusCode.Conflict) throw new InvalidOperationException("Username or guest profile already registered.");
            if ((int)code == 429) throw new InvalidOperationException("Too many attempts. Please wait a minute.");
            if (code == HttpStatusCode.BadRequest) throw new InvalidOperationException("Username: 3-32 letters/digits/underscores. Password: 8-128 characters.");
            throw new InvalidOperationException("Account service unavailable. Please retry.");
        }
    }
}
