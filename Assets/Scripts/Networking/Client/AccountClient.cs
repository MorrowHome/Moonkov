// 在线账号的入口。它用登录凭证请求后端仓库、商店和未领回收物，再把后端返回的档案交给 PlayerProfileClient 和菜单。
// 进战局时先另领一张入场票。公开账号服务和 Unity 战局服务器通过票据串起来。
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

namespace Unity.MP_FPS {
    public static partial class AccountClient {
        private sealed class LoginResult {
            public string Token, DisplayName;
            public DateTime ExpiresAt;
        }

        // 这是后端档案的响应形状。背包与待领取回收物是嵌在 JSON 里的两份数据，读取后还要检查它们是否能组成完整的档案。
        private sealed class Profile {
            public string PlayerId, DisplayName, InventoryJson, ActiveDeploymentId, RecoveryJson;
            public int Dust, Alloy, Cells;
            public ResearchQuestProgress ResearchQuests;
        }

        private sealed class Failure {
            public string Error;
        }

        private sealed class ShopSnapshot {
            public ShopOffer[] Offers;
            public Profile Profile;
        }

        public sealed class InventoryRequestException : InvalidOperationException {
            public string ErrorCode { get; }
            public InventoryRequestException(string code) : base(InventoryFailureMessage(code)) { ErrorCode = code; }
        }

        private static HttpClient s_Http;
        private static string s_Token, s_Name, s_PreferenceKey;
        private static DateTime s_Expires;
        private static string s_BaseUrl;
        internal static bool AllowLanHttp { get; set; }
        public static RecoveryInbox Recovery { get; private set; } = new();
        public static InventoryGraph Inventory { get; private set; }
        public static ResearchQuestProgress ResearchQuests { get; private set; }
        public static bool RaidActive { get; private set; }
        private static string s_VerifiedPlayerId, s_VerifiedServiceUrl;

        public static string LocalPreferenceScope {
            get {
                // 只有确认了当前服务地址和真实玩家 ID 才生成偏好作用域。用这两个值的哈希区分不同服务器上的账号。
                if (!IsLoggedIn || !Uri.TryCreate(s_BaseUrl, UriKind.Absolute, out var service) ||
                    !Guid.TryParse(s_VerifiedPlayerId, out var id) ||
                    service.AbsoluteUri.TrimEnd('/') != s_VerifiedServiceUrl)
                    return "";
                using (var hash = SHA256.Create()) {
                    return "online/" + BitConverter
                        .ToString(hash.ComputeHash(
                            Encoding.UTF8.GetBytes(s_VerifiedServiceUrl + "/" + id.ToString("D")))).Replace("-", "");
                }
            }
        }

        private static long s_ProfileRequest, s_AppliedProfileRequest;
        public static event Action InventoryChanged;
        public static int StashDust { get; private set; }
        public static int StashAlloy { get; private set; }
        public static int StashCells { get; private set; }
        public static int CarryCells { get; private set; }

        public static void SelectCarryCells(int cells) {
            CarryCells = Mathf.Clamp(cells, 0, Mathf.Min(StashCells, RaidRules.BagCapacity));
        }

        // 同步资源总数以后重新夹住携带电池数。库存被卖掉或带出后，旧的选择量可能比仓库实际剩余量大。
        public static void UpdateStash(int dust, int alloy, int cells) {
            StashDust = dust;
            StashAlloy = alloy;
            StashCells = cells;
            SelectCarryCells(CarryCells);
        }

        public static string Token {
            get {
                Load();
                return IsLoggedIn ? s_Token : "";
            }
        }

        public static string DisplayName {
            get {
                Load();
                return s_Name;
            }
        }

        public static bool IsLoggedIn {
            get {
                Load();
                return !string.IsNullOrEmpty(s_Token) && s_Expires > DateTime.UtcNow;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() {
            // Unity 关闭重载域时，静态字段可能跨次运行保留。这里释放上次 HTTP 对象并清缓存，下次从本机偏好和后端恢复。
            s_Http?.Dispose();
            s_Http = null;
            s_Token = s_Name = s_PreferenceKey = null;
            s_Expires = default;
            StashDust = StashAlloy = StashCells = 0;
            CarryCells = 0;
            s_VerifiedPlayerId = s_VerifiedServiceUrl = null;
            Inventory = null;
            Recovery = new RecoveryInbox();
            RaidActive = false;
            InventoryChanged = null;
            s_BaseUrl = null;
            s_ProfileRequest = s_AppliedProfileRequest = 0;
            ResearchQuests = null;
            AllowLanHttp = false;
        }

        private static void Load() {
            // 本次运行只载入一次已保存的登录结果。此处仅恢复凭据，档案是否仍有效要等 auth/me 返回确认。
            if (s_PreferenceKey != null) return;
            s_PreferenceKey = "MoonRaid.Account.v1";
#if UNITY_EDITOR
            // 即使编辑器克隆共享 PlayerPrefs，也要分别保留各自的登录状态。
            using (var hash = SHA256.Create()) {
                s_PreferenceKey += BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Application.dataPath)))
                    .Replace("-", "");
            }
#endif
            string saved = PlayerPrefs.GetString(s_PreferenceKey, "");
            try {
                var result = JsonConvert.DeserializeObject<LoginResult>(saved);
                if (result == null) return;
                s_Token = result.Token;
                s_Name = result.DisplayName;
                s_Expires = result.ExpiresAt.ToUniversalTime();
            } catch (JsonException) { PlayerPrefs.DeleteKey(s_PreferenceKey); }
        }

        public static async Task AuthenticateAsync(
            string baseUrl,
            string username,
            string password,
            bool register,
            CancellationToken ct) {
            Load();
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Enter username and password.");
            using (var request = new HttpRequestMessage(HttpMethod.Post,
                       Address(baseUrl, register ? "auth/register" : "auth/login"))) {
                request.Content = new StringContent(JsonConvert.SerializeObject(new {
                    Username = username.Trim(), Password = password,
                    // 只有注册填本机的访客凭据。登录既有账号时由后端按用户名和密码认出原账号。
                    GuestToken = register ? RaidGuestIdentity.GetToken(false) : null
                }), Encoding.UTF8, "application/json");
                using (var response = await SendAccountRequestAsync(request, ct)) {
                    ThrowIfFailed(response.StatusCode);
                    var result = JsonConvert.DeserializeObject<LoginResult>(await response.Content.ReadAsStringAsync());
                    // 返回格式、凭据长度和过期时间都对上以后才能换账号。取消的请求也不能改掉本地登录状态。
                    if (result == null || result.Token == null || result.Token.Length != 64 ||
                        result.ExpiresAt <= DateTime.UtcNow)
                        throw new InvalidOperationException("Invalid login response.");
                    ct.ThrowIfCancellationRequested();
                    CarryCells = 0;
                    s_VerifiedPlayerId = s_VerifiedServiceUrl = null;
                    Inventory = null;
                    Recovery = new RecoveryInbox();
                    RaidActive = false;
                    ResearchQuests = null;
                    s_Token = result.Token;
                    s_Name = result.DisplayName;
                    s_Expires = result.ExpiresAt.ToUniversalTime();
                    // 先存登录结果，再请求完整仓库。UI 读到有效 token 还不表示档案下载已经完成。
                    PlayerPrefs.SetString(s_PreferenceKey, JsonConvert.SerializeObject(result));
                    PlayerPrefs.Save();
                    GameSettings.Instance.PlayerName = s_Name;
                    await ValidateAsync(baseUrl, ct);
                }
            }
        }

        public static Task RefreshInventoryAsync(CancellationToken ct) { return ValidateAsync(s_BaseUrl, ct); }

        public static async Task ValidateAsync(string baseUrl, CancellationToken ct) {
            s_BaseUrl = baseUrl;
            if (!IsLoggedIn) return;
            string token = Token;
            long requestId = ++s_ProfileRequest;
            // 请求发出时记录序号，使晚返回的旧仓库结果不会覆盖先返回的新操作结果。
            using (var request = new HttpRequestMessage(HttpMethod.Get, Address(baseUrl, "auth/me"))) {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                // 请求发出后用户可能已换账号。等 HTTP 完成后再核对旧 token，只给仍然属于当前账号的响应更新界面。
                using (var response = await SendAccountRequestAsync(request, ct, true)) {
                    if (token != Token) return;
                    // 服务器明确说旧凭据无效时清掉登录缓存；网络连不上则保留凭据，让界面能够重试。
                    if (response.StatusCode == HttpStatusCode.Unauthorized) {
                        Clear();
                        return;
                    }
                    ThrowIfFailed(response.StatusCode);
                    var profile = JsonConvert.DeserializeObject<Profile>(await response.Content.ReadAsStringAsync());
                    if (profile == null) throw new InvalidOperationException("Invalid account response.");
                    ct.ThrowIfCancellationRequested();
                    if (token != Token) return;
                    ApplyProfile(profile, requestId);
                }
            }
        }

        public static async Task LogoutAsync(string baseUrl, CancellationToken ct) {
            string token = Token;
            using (var request = new HttpRequestMessage(HttpMethod.Post, Address(baseUrl, "auth/logout"))) {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await Http().SendAsync(request, ct)) { ThrowIfFailed(response.StatusCode); }
            }
            // 退出请求异步返回。只清它发出时的那个账号，防止晚到响应把后来登录的账号退出。
            if (token == Token) Clear();
        }

        private static void Clear() {
            s_Token = s_Name = "";
            s_Expires = default;
            StashDust = StashAlloy = StashCells = 0;
            CarryCells = 0;
            ResearchQuests = null;
            s_VerifiedPlayerId = s_VerifiedServiceUrl = null;
            Inventory = null;
            Recovery = new RecoveryInbox();
            RaidActive = false;
            InventoryChanged?.Invoke();
            // 内存和本机保存的登录状态都清掉。否则这次退出了，下次启动还会读回同一个 token。
            PlayerPrefs.DeleteKey(s_PreferenceKey);
            PlayerPrefs.Save();
        }

        // 超时设置要覆盖完整的仓库下载，登录响应本身小得多。
        private static HttpClient Http() {
            return s_Http ?? (s_Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        }

        private static async Task<HttpResponseMessage> SendAccountRequestAsync(
            HttpRequestMessage request,
            CancellationToken ct,
            bool loadingStash = false) {
            // 登录接口和加载仓库失败的提示分别处理。外部取消由调用方决定，客户端自己的 HTTP 超时才会转成用户可读的提示。
            string timeoutMessage = loadingStash
                ? "Loading your stash timed out. Please retry Login."
                : "Account service timed out. Please retry.";
            try { return await Http().SendAsync(request, ct); } catch (OperationCanceledException ex) when
                (!ct.IsCancellationRequested) { throw new TimeoutException(timeoutMessage, ex); }
            // Unity 的 Mono 传输层也可能用 WebException 报告超时。
            catch (WebException ex) when (!ct.IsCancellationRequested && (ex.Status == WebExceptionStatus.Timeout ||
                                                                          ex.Status == WebExceptionStatus
                                                                              .RequestCanceled)) {
                throw new TimeoutException(timeoutMessage, ex);
            }
        }

        // 仓库、商店等请求可以同时发出。先按请求序号和库存版本挡掉晚到的旧结果，再用校验过的档案整份替换界面读到的数据。
        private static void ApplyProfile(Profile profile, long requestId) {
            if (profile == null || string.IsNullOrEmpty(profile.InventoryJson))
                throw new InvalidOperationException(
                    "Account service returned no inventory. Update the persistence service.");
            // 完整账号库存要有仓库根，并且只属于该玩家。带着局内箱子根的临时合并视图不算有效的账号响应。
            var graph = JsonConvert.DeserializeObject<InventoryGraph>(profile.InventoryJson);
            if (graph == null || graph.Validate() != InventoryError.None || graph.Find("stash") == null ||
                graph.Find("loot") != null)
                throw new InvalidOperationException(
                    "Invalid account inventory snapshot. Existing inventory was preserved.");
            // 旧响应没带回收箱时用空箱。带了就检查回收物与仓库的物品 ID 能否同时存在。
            var recovery = string.IsNullOrEmpty(profile.RecoveryJson)
                ? new RecoveryInbox()
                : JsonConvert.DeserializeObject<RecoveryInbox>(profile.RecoveryJson);
            if (recovery == null || recovery.Validate(graph) != InventoryError.None)
                throw new InvalidOperationException("Invalid recovery inbox. Existing inventory was preserved.");
            var research = profile.ResearchQuests;
            if (research != null && !ResearchQuestRules.Valid(research))
                throw new InvalidOperationException("Invalid account research quest snapshot.");
            // 研究进度和库存使用各自的修订号。
            // 研究进度有自己的版本号。先按研究版本更新它，再看下方的库存版本和请求先后次序。
            if (research != null && (ResearchQuests == null || research.Revision > ResearchQuests.Revision))
                ResearchQuests = research;
            // 延迟返回的后台刷新不能撤销已成功的库存移动，也不能盖掉较新的战局状态。
            if (Inventory != null && (graph.Version < Inventory.Version ||
                                      (graph.Version == Inventory.Version && requestId < s_AppliedProfileRequest)))
                return;
            s_VerifiedPlayerId = Guid.TryParse(profile.PlayerId, out var verified) ? verified.ToString("D") : null;
            s_VerifiedServiceUrl = Uri.TryCreate(s_BaseUrl, UriKind.Absolute, out var service)
                ? service.AbsoluteUri.TrimEnd('/')
                : null;
            // 确认响应能用于本账号后再一起换背包与回收数据，最后通知界面重画，并更新是否仍有活动出战。
            Inventory = graph;
            Recovery = recovery;
            s_AppliedProfileRequest = requestId;
            RaidActive = !string.IsNullOrEmpty(profile.ActiveDeploymentId);
            s_Name = profile.DisplayName;
            UpdateStash(profile.Dust, profile.Alloy, profile.Cells);
            CarryCells = Mathf.Clamp(graph.Count("cells", true), 0, RaidRules.BagCapacity);
            GameSettings.Instance.PlayerName = s_Name;
            InventoryChanged?.Invoke();
        }

        public static Task MoveInventoryAsync(InventoryCommand command, CancellationToken ct) {
            return PostInventoryAsync("auth/inventory/move", command, ct);
        }

        public static Task ClaimRecoveryAsync(RecoveryClaim request, CancellationToken ct) {
            return PostInventoryAsync("auth/inventory/recovery/claim", request, ct);
        }

        public static Task TradeAsync(ShopCommand command, CancellationToken ct) {
            return PostInventoryAsync("auth/shop/trade", command, ct);
        }

        public static Task AdvanceResearchQuestAsync(ResearchQuestCommand command, CancellationToken ct) {
            return PostInventoryAsync("auth/research/advance", command, ct);
        }

        private sealed class GameTicket {
            public string Ticket;
        }

        // 大厅已经分配了实例时，用实例号申请专用入场票。ClientGameSystem 把票放进入场 RPC，账号登录凭证留在 HTTP 流程里。
        public static async Task<string> CreateGameTicketAsync(CancellationToken ct) {
            string token = Token;
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Sign in before starting a raid.");
            bool allocated = OnlineLobbyClient.InInstance;
            using var request = new HttpRequestMessage(HttpMethod.Post,
                Address(s_BaseUrl, allocated ? "auth/lobby/game-ticket" : "auth/game-ticket"));
            // 已开在线实例时，请求体带实例 ID 和网络协议版本。后端据此确认玩家有资格领这台战局服务器的票。
            if (allocated)
                request.Content =
                    new StringContent(
                        JsonConvert.SerializeObject(new Online.InstanceCredential {
                            InstanceId = OnlineLobbyClient.InstanceId, Protocol = Online.OnlineNetworkProtocol.Version
                        }), Encoding.UTF8, "application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await Http().SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(JsonConvert.DeserializeObject<Failure>(body)?.Error ??
                                                    "服务器拒绝入场，请刷新队伍状态后重试。");
            var ticket = JsonConvert.DeserializeObject<GameTicket>(body);
            ct.ThrowIfCancellationRequested();
            if (token != Token) throw new OperationCanceledException("Account changed while joining.");
            // 返回值只留给本次网络入场。账号 HTTP 凭据仍保存在自己的通路里，不能当作战局票替代。
            if (ticket?.Ticket?.Length != 64)
                throw new InvalidOperationException("Invalid game join ticket. Update the account service.");
            return ticket.Ticket;
        }

        public static async Task<ShopOffer[]> GetShopOffersAsync(CancellationToken ct) {
            if (!IsLoggedIn) throw new InventoryRequestException("login_expired");
            string token = Token;
            long requestId = ++s_ProfileRequest;
            using var request = new HttpRequestMessage(HttpMethod.Get, Address(s_BaseUrl, "auth/shop"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await Http().SendAsync(request, ct);
            ct.ThrowIfCancellationRequested();
            if (token != Token) throw new OperationCanceledException("Account changed while loading shop.");
            if (response.StatusCode == HttpStatusCode.Unauthorized) {
                Clear();
                throw new InventoryRequestException("login_expired");
            }
            if (!response.IsSuccessStatusCode)
                throw new InventoryRequestException("service_" + (int)response.StatusCode);
            // 商店报价与可操作的档案一起返回。先验证目录，再应用同一响应的档案，避免用坏报价尝试购买。
            var snapshot = JsonConvert.DeserializeObject<ShopSnapshot>(await response.Content.ReadAsStringAsync());
            if (snapshot?.Profile == null || !ShopCatalog.ValidOffers(snapshot.Offers))
                throw new InvalidOperationException("Invalid supplier catalogue. Existing inventory was preserved.");
            ct.ThrowIfCancellationRequested();
            if (token != Token) throw new OperationCanceledException("Account changed while loading shop.");
            ApplyProfile(snapshot.Profile, requestId);
            return snapshot.Offers;
        }

        // 库存版本不符等冲突交给后端裁决。收到拒绝后先刷新仓库，最后仍把原来的拒绝原因交给操作界面。
        private static async Task PostInventoryAsync(string path, object command, CancellationToken ct) {
            if (!IsLoggedIn) throw new InventoryRequestException("login_expired");
            string token = Token;
            long requestId = ++s_ProfileRequest;
            using var request = new HttpRequestMessage(HttpMethod.Post, Address(s_BaseUrl, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content =
                new StringContent(JsonConvert.SerializeObject(command), Encoding.UTF8, "application/json");
            using var response = await Http().SendAsync(request, ct);
            ct.ThrowIfCancellationRequested();
            if (token != Token) throw new OperationCanceledException("Account changed while moving inventory.");
            // 业务拒绝和连接异常分开处理。冲突后补刷新当前仓库，但界面仍需要收到最初那条拒绝原因。
            if (response.StatusCode == HttpStatusCode.Conflict) {
                string code =
                    JsonConvert.DeserializeObject<Failure>(await response.Content.ReadAsStringAsync())?.Error ??
                    "unknown";
                // 即使刷新失败，也要保留最初可用于处理问题的拒绝原因。
                try { await ValidateAsync(s_BaseUrl, ct); } catch (OperationCanceledException) { throw; } catch
                    (Exception ex) when (ex is HttpRequestException || ex is InvalidOperationException) { }
                throw new InventoryRequestException(code);
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized) {
                Clear();
                throw new InventoryRequestException("login_expired");
            }
            if (!response.IsSuccessStatusCode)
                throw new InventoryRequestException("service_" + (int)response.StatusCode);
            var profile = JsonConvert.DeserializeObject<Profile>(await response.Content.ReadAsStringAsync());
            ct.ThrowIfCancellationRequested();
            if (token == Token) ApplyProfile(profile, requestId);
        }

        // 保留后端的短错误码供队列判断能否重试，界面则根据同一个码显示和操作相关的提示。
        public static string InventoryFailureMessage(string code) {
            switch (code) {
                case "recovery_pending": return "Make room and claim pending recovery items before deploying again.";
                case "recovery_Full":
                    return
                        "Storage space or item count is full. Make room or sell items, then claim again. Pending items remain saved.";
                case "recovery_Stale": return "Storage or recovery changed and was refreshed. Claim again.";
                case "recovery_Missing":
                    return "The item was already claimed or is no longer pending. Refresh storage.";
                case "recovery_request_conflict": return "Claim request ID conflict. Refresh and retry.";
                case "raid_active":
                    return
                        "An unfinished raid owns the carried equipment. Finish/disconnect the raid, then refresh storage. [raid_active]";
                case "inventory_Stale":
                    return "Inventory changed during this move. Storage refreshed; retry the move. [inventory_Stale]";
                case "inventory_Incompatible":
                    return "This equipment type does not fit this slot. [inventory_Incompatible]";
                case "inventory_Collision":
                case "inventory_Full":
                    return "No free space at the destination. [" + code + "]";
                case "inventory_Bounds": return "The item extends beyond the container. [inventory_Bounds]";
                case "inventory_Cycle":
                    return "A container cannot be placed inside itself or its descendants. [inventory_Cycle]";
                case "inventory_Overweight":
                    return "This move exceeds the carried weight limit. [inventory_Overweight]";
                case "inventory_Missing":
                    return "The item or container no longer exists. Refresh storage. [inventory_Missing]";
                case "login_expired": return "Login expired. Sign in again before moving equipment. [login_expired]";
                case "shop_insufficient_dust":
                    return "Not enough moon dust for this order. Sell stored equipment or recover dust in a raid.";
                case "shop_has_weapon":
                    return "Emergency supply is available only when no halo weapon remains in your inventory.";
                case "shop_emergency_supply": return "Emergency supplies cannot be sold.";
                case "shop_store_first": return "Return this item to personal storage before selling it.";
                case "shop_request_conflict":
                    return "This order ID already belongs to a different transaction. Refresh the supplier.";
                case "shop_invalid": return "This supplier order is no longer valid. Refresh the catalogue.";
                case "research_complete": return "This permanent halo research is already complete.";
                case "research_materials":
                    return
                        "Store the required materials before researching. Mooncore fragments must have been successfully extracted.";
                case "fabrication_locked": return "Research Mooncore stabilization before fabricating this module.";
                case "fabrication_materials":
                    return
                        "Not enough fabrication materials in storage. Recover a Mooncore fragment and store a copper coil.";
                default: return "Inventory request rejected. Items remain unchanged. [" + code + "]";
            }
        }

        private static Uri Address(string baseUrl, string path) {
            AccountServiceSettings.ValidateAddress(baseUrl, AllowLanHttp);
            return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path);
        }

        private static void ThrowIfFailed(HttpStatusCode code) {
            if ((int)code >= 200 && (int)code < 300) return;
            if (code == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Incorrect username/password or expired login.");
            if (code == HttpStatusCode.Conflict)
                throw new InvalidOperationException("Username or guest profile already registered.");
            if ((int)code == 429) throw new InvalidOperationException("Too many attempts. Please wait a minute.");
            if (code == HttpStatusCode.BadRequest)
                throw new InvalidOperationException(
                    "Username: 3-32 letters/digits/underscores. Password: 8-128 characters.");
            throw new InvalidOperationException("Account service unavailable. Please retry.");
        }
    }
}