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
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public static class AccountClient
    {
        private sealed class LoginResult { public string Token, DisplayName; public DateTime ExpiresAt; }
        private sealed class Profile { public string DisplayName, InventoryJson, ActiveDeploymentId; public int Dust, Alloy, Cells; }
        private sealed class Failure { public string Error; }
        public sealed class InventoryRequestException : InvalidOperationException
        {
            public string ErrorCode { get; }
            public InventoryRequestException(string code) : base(InventoryFailureMessage(code)) { ErrorCode = code; }
        }
        private static HttpClient s_Http;
        private static string s_Token, s_Name, s_PreferenceKey;
        private static DateTime s_Expires;
        private static string s_BaseUrl;
        public static InventoryGraph Inventory { get; private set; }
        public static bool RaidActive { get; private set; }
        private static long s_ProfileRequest, s_AppliedProfileRequest;
        public static event Action InventoryChanged;
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
            Inventory = null; RaidActive = false; InventoryChanged = null; s_BaseUrl = null;
            s_ProfileRequest = s_AppliedProfileRequest = 0;
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
                    Inventory = null; RaidActive = false;
                    s_Token = result.Token; s_Name = result.DisplayName; s_Expires = result.ExpiresAt.ToUniversalTime();
                    PlayerPrefs.SetString(s_PreferenceKey, JsonConvert.SerializeObject(result)); PlayerPrefs.Save();
                    GameSettings.Instance.PlayerName = s_Name;
                    await ValidateAsync(baseUrl, ct);
                }
            }
        }

        public static async Task ValidateAsync(string baseUrl, CancellationToken ct)
        {
            s_BaseUrl = baseUrl;
            if (!IsLoggedIn) return;
            string token = Token;
            long requestId = ++s_ProfileRequest;
            using (var request = new HttpRequestMessage(HttpMethod.Get, Address(baseUrl, "auth/me")))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await Http().SendAsync(request, ct))
                {
                    if (token != Token) return;
                    if (response.StatusCode == HttpStatusCode.Unauthorized) { Clear(); return; }
                    ThrowIfFailed(response.StatusCode);
                    var profile = JsonConvert.DeserializeObject<Profile>(await response.Content.ReadAsStringAsync());
                    if (profile == null) throw new InvalidOperationException("Invalid account response.");
                    ct.ThrowIfCancellationRequested();
                    if (token != Token) return;
                    ApplyProfile(profile, requestId);
                }
            }
        }

        public static async Task LogoutAsync(string baseUrl, CancellationToken ct)
        {
            string token = Token;
            using (var request = new HttpRequestMessage(HttpMethod.Post, Address(baseUrl, "auth/logout")))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await Http().SendAsync(request, ct)) ThrowIfFailed(response.StatusCode);
            }
            if (token == Token) Clear();
        }

        private static void Clear()
        {
            s_Token = s_Name = ""; s_Expires = default;
            StashDust = StashAlloy = StashCells = 0;
            CarryCells = 0;
            Inventory = null; RaidActive = false; InventoryChanged?.Invoke();
            PlayerPrefs.DeleteKey(s_PreferenceKey); PlayerPrefs.Save();
        }

        private static HttpClient Http() => s_Http ?? (s_Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        private static void ApplyProfile(Profile profile, long requestId)
        {
            if (profile == null || string.IsNullOrEmpty(profile.InventoryJson)) throw new InvalidOperationException("Account service returned no inventory. Update the persistence service.");
            var graph = JsonConvert.DeserializeObject<InventoryGraph>(profile.InventoryJson);
            if (graph == null || graph.Validate() != InventoryError.None || graph.Find("stash") == null)
                throw new InvalidOperationException("Invalid account inventory snapshot. Existing inventory was preserved.");
            // A delayed background refresh cannot roll back a successful move or a newer raid state.
            if (Inventory != null && (graph.Version < Inventory.Version ||
                graph.Version == Inventory.Version && requestId < s_AppliedProfileRequest)) return;
            Inventory = graph; s_AppliedProfileRequest = requestId;
            RaidActive = !string.IsNullOrEmpty(profile.ActiveDeploymentId);
            s_Name = profile.DisplayName;
            UpdateStash(profile.Dust, profile.Alloy, profile.Cells);
            CarryCells = Mathf.Clamp(graph.Count("cells", true), 0, RaidRules.BagCapacity);
            GameSettings.Instance.PlayerName = s_Name;
            InventoryChanged?.Invoke();
        }
        public static async Task MoveInventoryAsync(InventoryCommand command, CancellationToken ct)
        {
            if (!IsLoggedIn) throw new InventoryRequestException("login_expired");
            string token = Token;
            long requestId = ++s_ProfileRequest;
            using var request = new HttpRequestMessage(HttpMethod.Post, Address(s_BaseUrl, "auth/inventory/move"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonConvert.SerializeObject(command), Encoding.UTF8, "application/json");
            using var response = await Http().SendAsync(request, ct);
            ct.ThrowIfCancellationRequested();
            if (token != Token) throw new OperationCanceledException("Account changed while moving inventory.");
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                string code = JsonConvert.DeserializeObject<Failure>(await response.Content.ReadAsStringAsync())?.Error ?? "unknown";
                // Refresh failure must not replace the original, actionable rejection.
                try { await ValidateAsync(s_BaseUrl, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is HttpRequestException || ex is InvalidOperationException) { }
                throw new InventoryRequestException(code);
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized) { Clear(); throw new InventoryRequestException("login_expired"); }
            if (!response.IsSuccessStatusCode) throw new InventoryRequestException("service_" + (int)response.StatusCode);
            var profile = JsonConvert.DeserializeObject<Profile>(await response.Content.ReadAsStringAsync()); ct.ThrowIfCancellationRequested();
            if (token == Token) ApplyProfile(profile, requestId);
        }
        public static string InventoryFailureMessage(string code)
        {
            switch (code)
            {
                case "raid_active": return "An unfinished raid owns the carried equipment. Finish/disconnect the raid, then refresh storage. [raid_active]";
                case "inventory_Stale": return "Inventory changed during this move. Storage refreshed; retry the move. [inventory_Stale]";
                case "inventory_Incompatible": return "This equipment type does not fit this slot. [inventory_Incompatible]";
                case "inventory_Collision": case "inventory_Full": return "No free space at the destination. [" + code + "]";
                case "inventory_Bounds": return "The item extends beyond the container. [inventory_Bounds]";
                case "inventory_Cycle": return "A container cannot be placed inside itself or its descendants. [inventory_Cycle]";
                case "inventory_Overweight": return "This move exceeds the carried weight limit. [inventory_Overweight]";
                case "inventory_Missing": return "The item or container no longer exists. Refresh storage. [inventory_Missing]";
                case "login_expired": return "Login expired. Sign in again before moving equipment. [login_expired]";
                default: return "Inventory request rejected. Items remain unchanged. [" + code + "]";
            }
        }
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
