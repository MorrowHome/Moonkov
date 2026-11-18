// 菜单、物品栏和出战流程都通过这里读当前玩家档案。同一套界面可切到在线账号、单人档案或好友房，档案读写会交给对应的一端。
// 服务端 World 另有自己的持久化上下文。界面当前显示哪个档案，由这里选择。
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // UI 和本地拥有角色的客户端在这里读选择的档案。服务端 World
    // 则按自己有没有 OfflineRaidMode 标记来选择持久化。
    public static partial class PlayerProfileClient {
        private static OfflineProfileStore s_Offline;
        private static InventoryGraph s_Inventory;
        private static RecoveryInbox s_Recovery;
        private static int s_Dust, s_Alloy, s_Cells, s_Carry;
        private static Action s_OfflineChanged;
        public static bool IsOffline => s_Offline != null;

        // 同一台机器可以切账号、单人档案和好友房。各自要用不同的本地标识，快捷栏和未完成操作才不会跟着账号串过去。
        public static string LocalPreferenceScope {
            get {
                if (IsLan) return "lan/" + LanRoomClient.Room.HostId + "/" + LanRoomClient.Profile.Profile.PlayerId;
                if (!IsOffline) return AccountClient.LocalPreferenceScope;
                using (var hash = System.Security.Cryptography.SHA256.Create()) {
                    return "offline/" + BitConverter
                        .ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s_Offline.FilePath)))
                        .Replace("-", "");
                }
            }
        }

        public static OfflineProfileStore OfflineStore =>
            s_Offline ?? throw new InvalidOperationException("No single-player profile is open.");

        public static bool IsLoggedIn => IsLan || IsOffline || AccountClient.IsLoggedIn;
        // 这里选择三种入场凭据。房主票、单人占位值和在线账号凭据的签发途径不同，实际使用的位置也不同。
        public static string Token => IsLan ? LanRoomClient.Ticket : IsOffline ? "single-player" : AccountClient.Token;

        public static string DisplayName =>
            IsLan ? LanRoomClient.Profile.Profile.DisplayName :
            IsOffline ? MoonkovLocalization.Text("Single-player Operator") : AccountClient.DisplayName;

        public static RecoveryInbox Recovery =>
            IsLan ? LanRoomClient.Profile.Recovery : IsOffline ? s_Recovery : AccountClient.Recovery;

        public static InventoryGraph Inventory =>
            IsLan ? s_LanInventory : IsOffline ? s_Inventory : AccountClient.Inventory;

        public static bool RaidActive =>
            IsLan ? LanRoomClient.Profile.RaidActive : IsOffline ? s_Offline.RaidActive : AccountClient.RaidActive;

        public static int StashDust =>
            IsLan ? LanRoomClient.Profile.Profile.Dust : IsOffline ? s_Dust : AccountClient.StashDust;

        public static int StashAlloy =>
            IsLan ? LanRoomClient.Profile.Profile.Alloy : IsOffline ? s_Alloy : AccountClient.StashAlloy;

        public static int StashCells =>
            IsLan ? LanRoomClient.Profile.Profile.Cells : IsOffline ? s_Cells : AccountClient.StashCells;

        public static int CarryCells => IsLan ? s_LanCarry : IsOffline ? s_Carry : AccountClient.CarryCells;

        public static bool SupportsSinglePlayer {
            get {
#if UNITY_CLIENT && !UNITY_EDITOR
                return false;
#else
                return true;
#endif
            }
        }

        public static event Action InventoryChanged {
            add {
                s_OfflineChanged += value;
                AccountClient.InventoryChanged += value;
            }
            remove {
                s_OfflineChanged -= value;
                AccountClient.InventoryChanged -= value;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() {
            ResetMenuInventory();
            s_Offline?.Dispose();
            s_TutorialReturnStore?.Dispose();
            s_TutorialReturnStore = null;
            s_Tutorial = null;
            s_Offline = null;
            s_Inventory = null;
            s_LanInventory = null;
            s_LanPlayerId = null;
            s_Recovery = null;
            s_Campaign = null;
            s_Dust = s_Alloy = s_Cells = s_Carry = 0;
            s_OfflineChanged = null;
        }

        public static async Task EnterOfflineAsync(CancellationToken ct) {
            await FlushMenuInventoryAsync(ct);
            if (!SupportsSinglePlayer)
                throw new InvalidOperationException("Single player requires a Client+Server build.");
            string path = Path.Combine(Application.persistentDataPath, "SinglePlayer", "profile-v1.json");
#if UNITY_EDITOR
            // MPPM 的编辑器克隆各自需要独立的可写本机档案。
            using (var hash = System.Security.Cryptography.SHA256.Create()) {
                path = Path.Combine(Application.persistentDataPath, "SinglePlayer",
                    Convert.ToBase64String(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Application.dataPath)))
                        .Replace('/', '_').Replace('+', '-'), "profile-v1.json");
            }
#endif
            // 读档在后台进行。只有待保存记录都恢复好、而且请求未取消之后，才正式把菜单切进单人档。
            var store = await Task.Run(() => new OfflineProfileStore(path), ct);
            try {
                // 先重放已确认或未写完的记录，再判断是否把仍开启的出战视作失败。
                // 先补写上次没发完的结算。完成后如果还有未结束的出战，才按遗留战局处理，避免本该撤离成功的装备被清掉。
                var recovery = new RaidPersistenceContext(store);
                recovery.Dispose();
                await recovery.ShutdownTask;
                if (Directory.GetFiles(store.OutboxDirectory, "*.json").Length != 0 ||
                    Directory.GetFiles(store.OutboxDirectory, "*.deploy").Length != 0)
                    throw new IOException(
                        "Single-player settlement recovery is pending. Reopen single player to retry.");
                await Task.Run(store.RecoverAbandonedRaid, ct);
                ct.ThrowIfCancellationRequested();
                // 此时才让界面看见这份档案。此前加载如果失败，会释放锁和临时状态，免得下次点击单人打不开档。
                s_Offline = store;
                RefreshOffline();
            } catch {
                if (s_Offline == store) {
                    s_Offline = null;
                    s_Inventory = null;
                    s_Recovery = null;
                }
                store.Dispose();
                throw;
            }
        }

        public static void LeaveOffline() {
            ResetMenuInventory();
            var store = s_Offline;
            s_TutorialReturnStore?.Dispose();
            s_TutorialReturnStore = null;
            s_Tutorial = null;
            s_Offline = null;
            s_Inventory = null;
            s_Recovery = null;
            s_Campaign = null;
            store?.Dispose();
            s_OfflineChanged?.Invoke();
        }

        // 从当前档案拿一份正式快照，重建库存、战役、教学和携带电池的数据，再让订阅的页面统一刷新。
        private static void RefreshOffline() {
            var profile = s_Offline.Read();
            s_Recovery = s_Offline.ReadRecovery();
            s_Inventory = RaidInventoryTransport.Decode(profile.InventoryJson);
            RefreshCampaign(profile);
            s_Tutorial = s_Offline.ReadTutorial();
            s_Dust = profile.Dust;
            s_Alloy = profile.Alloy;
            s_Cells = profile.Cells;
            s_Carry = Mathf.Clamp(s_Inventory.Count("cells", true), 0, RaidRules.BagCapacity);
            GameSettings.Instance.PlayerName = DisplayName;
            RefreshMenuInventory();
            s_OfflineChanged?.Invoke();
        }

        public static void SelectCarryCells(int cells) {
            if (IsLan) {
                if (!LanRoomClient.Ready && LanRoomClient.Room.Phase == LanRoomPhase.Lobby)
                    s_LanCarry = Mathf.Clamp(cells, 0, Mathf.Min(StashCells, RaidRules.BagCapacity));
                return;
            }
            if (IsOffline)
                s_Carry = Mathf.Clamp(cells, 0, Mathf.Min(s_Cells, RaidRules.BagCapacity));
            else
                AccountClient.SelectCarryCells(cells);
        }

        public static void UpdateStash(int dust, int alloy, int cells) {
            if (IsLan) return;
            if (IsOffline) {
                s_Dust = dust;
                s_Alloy = alloy;
                s_Cells = cells;
            } else { AccountClient.UpdateStash(dust, alloy, cells); }
        }

        // 切在线账号前要把菜单库存队列处理完。单人正式档和在线地址的数据不能互相写入。
        public static async Task AuthenticateAsync(
            string url,
            string username,
            string password,
            bool register,
            CancellationToken ct) {
            if (IsLan) throw new InvalidOperationException("请先离开好友房间。");
            await FlushMenuInventoryAsync(ct);
            LeaveOffline();
            await AccountClient.AuthenticateAsync(url, username, password, register, ct);
        }

        public static Task ValidateAsync(string url, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            if (IsLan) return RefreshLanAsync(ct);
            if (!IsOffline) return AccountClient.ValidateAsync(url, ct);
            RefreshOffline();
            return Task.CompletedTask;
        }

        public static async Task LogoutAsync(string url, CancellationToken ct) {
            await FlushMenuInventoryAsync(ct);
            if (IsLan) {
                await LanRoomClient.LeaveAsync();
                return;
            }
            if (!IsOffline) {
                await AccountClient.LogoutAsync(url, ct);
                return;
            }
            LeaveOffline();
        }

        // 同一个拖格入口在好友房请求房主，在线请求账号后端，单人在后台调本机档案。回调要确认仍显示同一份档才刷新。
        public static async Task MoveInventoryAsync(InventoryCommand command, CancellationToken ct) {
            if (IsLan) {
                await LanRoomClient.RequestAsync("move", command, ct: ct);
                return;
            }
            if (!IsOffline) {
                await AccountClient.MoveInventoryAsync(command, ct);
                return;
            }
            var store = s_Offline;
            await Task.Run(() => store.Move(command), ct);
            ct.ThrowIfCancellationRequested();
            if (store == s_Offline) RefreshOffline();
        }

        // 回收箱领取和普通拖格分别发请求。回收物仍由对应档案系统验证，菜单只能读最新回执。
        public static async Task ClaimRecoveryAsync(RecoveryClaim request, CancellationToken ct) {
            if (IsLan) {
                await LanRoomClient.RequestAsync("recovery", request, ct: ct);
                return;
            }
            if (!IsOffline) {
                await AccountClient.ClaimRecoveryAsync(request, ct);
                return;
            }
            var store = s_Offline;
            await Task.Run(() => store.ClaimRecovery(request), ct);
            ct.ThrowIfCancellationRequested();
            if (store == s_Offline) RefreshOffline();
        }

        // 报价页面只提交交易意图。本机、房主或在线服务负责真正算资源和写档。
        public static async Task TradeAsync(ShopCommand command, CancellationToken ct) {
            if (IsLan) {
                await LanRoomClient.RequestAsync("trade", command, ct: ct);
                return;
            }
            if (!IsOffline) {
                await AccountClient.TradeAsync(command, ct);
                return;
            }
            var store = s_Offline;
            await Task.Run(() => store.Trade(command), ct);
            ct.ThrowIfCancellationRequested();
            if (store == s_Offline) RefreshOffline();
        }

        public static Task<ShopOffer[]> GetShopOffersAsync(CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            if (IsLan) return Task.FromResult(ShopCatalog.Offers);
            if (!IsOffline) return AccountClient.GetShopOffersAsync(ct);
            RefreshOffline();
            return Task.FromResult(ShopCatalog.Offers);
        }
    }
}