#nullable disable
using System;

namespace Unity.MP_FPS.Inventory {
    // 线上和离线的 ReturnLoadout 共用，使库存与首次奖励一起提交。
    // UI 界面是否发现任务、当前接过什么或追踪什么都不参与这项判断。
    public static class MissionInventory {
        public const int FirstRecovery = 1, FirstMooncore = 2, FirstArchive = 4;

        public static bool IsArtifact(string code) {
            return code == "mooncore_fragment" || code == "mc00_archive_drive";
        }

        // 任务样本除了物品码，还要含当初任务操作的来源标记。背包中摆一件同名物品不能作为真实任务产物。
        public static bool ValidSource(InventoryItem item) {
            if (!IsArtifact(item.Code))
                return item.MissionWorldId == null && item.MissionOperationId == null &&
                       item.MissionContentVersion == 0 && !item.MissionRecovered;
            return item.Quantity == 1 && item.FoundInRaid && !item.EmergencySupply && item.MissionContentVersion == 1 &&
                   Guid.TryParse(item.MissionWorldId, out var world) && world != Guid.Empty &&
                   Guid.TryParse(item.MissionOperationId, out var operation) && operation != Guid.Empty;
        }

        public static InventoryError RecordRecovery(InventoryGraph account, InventoryGraph raid) {
            if (account == null || raid == null || account.Find("stash") == null) return InventoryError.Invalid;
            int facts = 0;
            foreach (var item in raid.Items) {
                if (!raid.Carried(item) || item.EmergencySupply || !item.FoundInRaid) continue;
                if (!IsArtifact(item.Code)) {
                    facts |= FirstRecovery;
                    continue;
                }
                if (!ValidSource(item)) return InventoryError.Invalid;
                if (!item.MissionRecovered)
                    facts |= FirstRecovery | (item.Code == "mooncore_fragment" ? FirstMooncore : FirstArchive);
                var returned = account.Find(item.Id);
                if (returned == null) return InventoryError.Invalid;
                returned.MissionRecovered = true;
            }
            int added = facts & ~account.MissionFirsts;
            int dust = ((added & FirstRecovery) != 0 ? 10 : 0) + ((added & FirstMooncore) != 0 ? 30 : 0) +
                       ((added & FirstArchive) != 0 ? 20 : 0);
            if (dust > 0) {
                InventoryError result;
                while ((result = account.AddSupply("dust", dust, "stash")) == InventoryError.Full &&
                       account.StashRows < 4096)
                    account.StashRows = Math.Min(4096, account.StashRows * 2);
                if (result != InventoryError.None) return result;
            }
            account.MissionFirsts |= facts;
            return InventoryError.None;
        }
    }
}
