// 收下 LanRoomClient 发来的房主档案快照，解码成菜单可读的库存并通知界面。换到新玩家时初始化携带数量，同一玩家刷新时保留选择。
using System.Threading;
using System.Threading.Tasks;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public static partial class PlayerProfileClient {
        private static InventoryGraph s_LanInventory;
        private static string s_LanPlayerId;
        private static int s_LanCarry;
        public static bool IsLan => LanRoomClient.Profile != null;

        public static void ApplyLanProfile() {
            var profile = LanRoomClient.Profile.Profile;
            // 房主回传 JSON 是背包正式结果。先解码，之后界面和物品队列都从这一份库存继续算。
            s_LanInventory = RaidInventoryTransport.Decode(profile.InventoryJson);
            // 换房主或玩家会变成另一个档案，需重设携带数量。房主对同一人的轮询回包不能跳回默认选择。
            if (s_LanPlayerId != profile.PlayerId)
                s_LanCarry = UnityEngine.Mathf.Clamp(s_LanInventory.Count("cells", true), 0, RaidRules.BagCapacity);
            s_LanPlayerId = profile.PlayerId;
            GameSettings.Instance.PlayerName = DisplayName;
            RefreshMenuInventory();
            s_OfflineChanged?.Invoke();
        }

        // 退房先停掉房间里的库存操作，再清掉仅在这个房间有效的物品和票据关联状态。
        public static void ClearLanProfile() {
            ResetMenuInventory();
            s_LanInventory = null;
            s_LanPlayerId = null;
            s_LanCarry = 0;
            s_OfflineChanged?.Invoke();
        }

        // 刷新从房主查询 profile。响应仍由 LanRoomClient 应用，界面不用另外保存一份房间档案。
        private static async Task RefreshLanAsync(CancellationToken ct) {
            await LanRoomClient.RequestAsync("profile", ct: ct);
        }
    }
}