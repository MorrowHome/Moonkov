// 把菜单拖格、买卖和改装交给共用的 ProfileInventoryQueue。这份临时预览给菜单显示，真正出战和落盘取已确认的 Inventory。
using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public static partial class PlayerProfileClient {
        private static ProfileInventoryQueue s_MenuInventory;
        private static string s_MenuInventoryScope;

        // Only menus use this projection. Deployment and persistence still read Inventory.
        public static InventoryGraph MenuInventory {
            get {
                CheckMenuScope();
                return s_MenuInventory?.Inventory ?? Inventory;
            }
        }

        public static int MenuInventoryPending => s_MenuInventory?.Count ?? 0;
        public static bool MenuInventoryNeedsRetry => s_MenuInventory?.NeedsRetry == true;
        public static Exception MenuInventoryError => s_MenuInventory?.Error;

        // 只有已拿到正式库存、还没出战而且队列有空位时才开放拖放。待确认异常要先同步，不能继续积累新操作。
        public static bool CanQueueMenuInventory =>
            !RaidActive && Inventory != null && !MenuInventoryNeedsRetry &&
            MenuInventoryPending < ProfileInventoryQueue.Capacity;

        private static void CheckMenuScope() {
            // 队列里的物品 ID 和背包版本只对当时的档案有效。切另一个账号、房主或本机存档后直接作废。
            if (s_MenuInventory != null && s_MenuInventoryScope != LocalPreferenceScope) ResetMenuInventory();
        }

        private static ProfileInventoryQueue MenuQueue() {
            CheckMenuScope();
            if (s_MenuInventory != null) return s_MenuInventory;
            string scope = LocalPreferenceScope;
            if (!IsLoggedIn || Inventory == null || string.IsNullOrEmpty(scope))
                throw new AccountClient.InventoryRequestException("login_expired");
            s_MenuInventoryScope = scope;
            // 队列也要区分明确拒绝与临时连接错误。在线拒绝常带业务码，房主的普通操作错误由房间协议转换。
            bool lan = IsLan;
            s_MenuInventory = new ProfileInventoryQueue(() => Inventory,
                () => scope == LocalPreferenceScope && IsLoggedIn && !RaidActive, MoveInventoryAsync, TradeAsync,
                RefreshMenuAuthorityAsync,
                ex => (ex is AccountClient.InventoryRequestException rejection &&
                       !rejection.ErrorCode.StartsWith("service_", StringComparison.Ordinal)) ||
                      (lan && ex is InvalidOperationException && !(ex is AccountClient.InventoryRequestException)));
            s_MenuInventory.Changed += MenuInventoryChanged;
            AccountClient.InventoryChanged += RefreshMenuInventory;
            return s_MenuInventory;
        }

        // 重拉权威结果时按档案来源走不同接口。单人已有存档对象，直接读当前确认状态即可。
        private static Task RefreshMenuAuthorityAsync(CancellationToken ct) {
            if (IsLan) return RefreshLanAsync(ct);
            if (!IsOffline) return AccountClient.RefreshInventoryAsync(ct);
            ct.ThrowIfCancellationRequested();
            RefreshOffline();
            return Task.CompletedTask;
        }

        private static void MenuInventoryChanged() { s_OfflineChanged?.Invoke(); }

        private static void RefreshMenuInventory() {
            CheckMenuScope();
            s_MenuInventory?.Refresh();
        }

        private static void ResetMenuInventory() {
            // 取消之前挂在账号上的刷新订阅，销毁等待中的菜单队列，避免退出后旧界面还接到变化。
            AccountClient.InventoryChanged -= RefreshMenuInventory;
            s_MenuInventory?.Dispose();
            s_MenuInventory = null;
            s_MenuInventoryScope = null;
        }

        public static Task QueueMenuMoveAsync(InventoryCommand command) { return MenuQueue().MoveAsync(command); }

        public static Task QueueMenuTradeAsync(ShopCommand command, ShopOffer[] offers) {
            return MenuQueue().TradeAsync(command, offers);
        }

        // 点准备、出战或退出前等队列里的操作结清，这时从正式库存读取的装备才能包含刚完成的拖放和购买。
        public static Task FlushMenuInventoryAsync(CancellationToken ct) {
            CheckMenuScope();
            return s_MenuInventory?.FlushAsync(ct) ?? Task.CompletedTask;
        }
    }
}