using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    // The UI and owned client read their selected profile here. Server Worlds choose
    // persistence independently from their own OfflineRaidMode marker.
    public static class PlayerProfileClient
    {
        private static OfflineProfileStore s_Offline;
        private static InventoryGraph s_Inventory;
        private static int s_Dust, s_Alloy, s_Cells, s_Carry;
        private static Action s_OfflineChanged;
        public static bool IsOffline => s_Offline != null;
        public static OfflineProfileStore OfflineStore => s_Offline ?? throw new InvalidOperationException("No single-player profile is open.");
        public static bool IsLoggedIn => IsOffline || AccountClient.IsLoggedIn;
        public static string Token => IsOffline ? "single-player" : AccountClient.Token;
        public static string DisplayName => IsOffline ? MoonkovLocalization.Text("Single-player Operator") : AccountClient.DisplayName;
        public static InventoryGraph Inventory => IsOffline ? s_Inventory : AccountClient.Inventory;
        public static bool RaidActive => IsOffline ? s_Offline.RaidActive : AccountClient.RaidActive;
        public static int StashDust => IsOffline ? s_Dust : AccountClient.StashDust;
        public static int StashAlloy => IsOffline ? s_Alloy : AccountClient.StashAlloy;
        public static int StashCells => IsOffline ? s_Cells : AccountClient.StashCells;
        public static int CarryCells => IsOffline ? s_Carry : AccountClient.CarryCells;
        public static bool SupportsSinglePlayer
        {
            get
            {
#if UNITY_CLIENT && !UNITY_EDITOR
                return false;
#else
                return true;
#endif
            }
        }
        public static event Action InventoryChanged
        {
            add { s_OfflineChanged += value; AccountClient.InventoryChanged += value; }
            remove { s_OfflineChanged -= value; AccountClient.InventoryChanged -= value; }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { s_Offline?.Dispose(); s_Offline = null; s_Inventory = null; s_Dust = s_Alloy = s_Cells = s_Carry = 0; s_OfflineChanged = null; }

        public static async Task EnterOfflineAsync(CancellationToken ct)
        {
            if (!SupportsSinglePlayer) throw new InvalidOperationException("Single player requires a Client+Server build.");
            string path = Path.Combine(Application.persistentDataPath, "SinglePlayer", "profile-v1.json");
#if UNITY_EDITOR
            // MPPM clones must not share a mutable local profile.
            using (var hash = System.Security.Cryptography.SHA256.Create())
                path = Path.Combine(Application.persistentDataPath, "SinglePlayer", Convert.ToBase64String(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Application.dataPath))).Replace('/', '_').Replace('+', '-'), "profile-v1.json");
#endif
            var store = await Task.Run(() => new OfflineProfileStore(path), ct);
            try
            {
                // Replay acknowledged/unfinished writes before treating an open raid as lost.
                var recovery = new RaidPersistenceContext(store); recovery.Dispose(); await recovery.ShutdownTask;
                if (Directory.GetFiles(store.OutboxDirectory, "*.json").Length != 0 || Directory.GetFiles(store.OutboxDirectory, "*.deploy").Length != 0)
                    throw new IOException("Single-player settlement recovery is pending. Reopen single player to retry.");
                await Task.Run(store.RecoverAbandonedRaid, ct); ct.ThrowIfCancellationRequested();
                s_Offline = store; RefreshOffline();
            }
            catch { if (s_Offline == store) { s_Offline = null; s_Inventory = null; } store.Dispose(); throw; }
        }

        public static void LeaveOffline() { var store = s_Offline; s_Offline = null; s_Inventory = null; store?.Dispose(); s_OfflineChanged?.Invoke(); }
        private static void RefreshOffline()
        {
            var profile = s_Offline.Read(); s_Inventory = RaidInventoryTransport.Decode(profile.InventoryJson);
            s_Dust = profile.Dust; s_Alloy = profile.Alloy; s_Cells = profile.Cells;
            s_Carry = Mathf.Clamp(s_Inventory.Count("cells", true), 0, RaidRules.BagCapacity);
            GameSettings.Instance.PlayerName = DisplayName; s_OfflineChanged?.Invoke();
        }
        public static void SelectCarryCells(int cells)
        { if (IsOffline) s_Carry = Mathf.Clamp(cells, 0, Mathf.Min(s_Cells, RaidRules.BagCapacity)); else AccountClient.SelectCarryCells(cells); }
        public static void UpdateStash(int dust, int alloy, int cells)
        { if (IsOffline) { s_Dust = dust; s_Alloy = alloy; s_Cells = cells; } else AccountClient.UpdateStash(dust, alloy, cells); }
        public static Task AuthenticateAsync(string url, string username, string password, bool register, CancellationToken ct)
        { LeaveOffline(); return AccountClient.AuthenticateAsync(url, username, password, register, ct); }
        public static Task ValidateAsync(string url, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (!IsOffline) return AccountClient.ValidateAsync(url, ct); RefreshOffline(); return Task.CompletedTask; }
        public static Task LogoutAsync(string url, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (!IsOffline) return AccountClient.LogoutAsync(url, ct); LeaveOffline(); return Task.CompletedTask; }
        public static async Task MoveInventoryAsync(InventoryCommand command, CancellationToken ct)
        { if (!IsOffline) { await AccountClient.MoveInventoryAsync(command, ct); return; } var store = s_Offline; await Task.Run(() => store.Move(command), ct); ct.ThrowIfCancellationRequested(); if (store == s_Offline) RefreshOffline(); }
        public static async Task TradeAsync(ShopCommand command, CancellationToken ct)
        { if (!IsOffline) { await AccountClient.TradeAsync(command, ct); return; } var store = s_Offline; await Task.Run(() => store.Trade(command), ct); ct.ThrowIfCancellationRequested(); if (store == s_Offline) RefreshOffline(); }
        public static Task<ShopOffer[]> GetShopOffersAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (!IsOffline) return AccountClient.GetShopOffersAsync(ct); RefreshOffline(); return Task.FromResult(ShopCatalog.Offers); }
    }
}
