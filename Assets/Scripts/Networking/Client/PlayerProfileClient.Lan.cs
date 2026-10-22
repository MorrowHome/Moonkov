using System.Threading;
using System.Threading.Tasks;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public static partial class PlayerProfileClient
    {
        private static InventoryGraph s_LanInventory;
        private static string s_LanPlayerId;
        private static int s_LanCarry;
        public static bool IsLan => LanRoomClient.Profile != null;
        public static void ApplyLanProfile()
        {
            var profile = LanRoomClient.Profile.Profile;
            s_LanInventory = RaidInventoryTransport.Decode(profile.InventoryJson);
            if (s_LanPlayerId != profile.PlayerId)
                s_LanCarry = UnityEngine.Mathf.Clamp(s_LanInventory.Count("cells", true), 0, RaidRules.BagCapacity);
            s_LanPlayerId = profile.PlayerId;
            GameSettings.Instance.PlayerName = DisplayName;
            s_OfflineChanged?.Invoke();
        }
        public static void ClearLanProfile()
        { s_LanInventory = null; s_LanPlayerId = null; s_LanCarry = 0; s_OfflineChanged?.Invoke(); }
        private static async Task RefreshLanAsync(CancellationToken ct)
        { await LanRoomClient.RequestAsync("profile", ct: ct); }
    }
}
