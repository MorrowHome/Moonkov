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

        public static bool CanQueueMenuInventory =>
            !RaidActive && Inventory != null && !MenuInventoryNeedsRetry &&
            MenuInventoryPending < ProfileInventoryQueue.Capacity;

        private static void CheckMenuScope() {
            if (s_MenuInventory != null && s_MenuInventoryScope != LocalPreferenceScope) ResetMenuInventory();
        }

        private static ProfileInventoryQueue MenuQueue() {
            CheckMenuScope();
            if (s_MenuInventory != null) return s_MenuInventory;
            string scope = LocalPreferenceScope;
            if (!IsLoggedIn || Inventory == null || string.IsNullOrEmpty(scope))
                throw new AccountClient.InventoryRequestException("login_expired");
            s_MenuInventoryScope = scope;
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