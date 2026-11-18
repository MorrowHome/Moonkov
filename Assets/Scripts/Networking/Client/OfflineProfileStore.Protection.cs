// 读取旧版离线档案后，用 ProtectionKitMigration 补齐新版防护装备。档案里留一条迁移回执，同一个存档以后打开时就不用重发装备。
using System.IO;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public sealed partial class OfflineProfileStore {
        // 旧版读档即使不认识 InventoryGraph 的新字段，也会
        // 保留回执字典。该配发记录存在时不能再次发装备。
        private const string ProtectionReceipt = "migration/protection-kit-v1";

        private static bool ApplyProtectionMigration(SaveData save) {
            // 出战没结束时，随身装备已经交给地图。等回仓后迁移才知道防护装备应该放进哪份背包。
            if (save.DeploymentId != null) return false;
            if (save.Receipts.TryGetValue(ProtectionReceipt, out string receipt)) {
                if (receipt != "1") throw new InvalidDataException("Invalid protection kit migration receipt.");
                if (save.Inventory.ProtectionKitVersion >= 1) return false;
                save.Inventory = save.Inventory.Clone();
                save.Inventory.ProtectionKitVersion = 1;
                save.Inventory.Version++;
                return true;
            }
            // 库存已是新版但缺回执时只补回执，防止再领一件装备。
            if (save.Inventory.ProtectionKitVersion >= 1) {
                save.Receipts.Add(ProtectionReceipt, "1");
                return true;
            }
            // 迁移和仓库空间检查由同一规则执行。成功后把新仓库、回收箱和回执一起交给上层存档。
            if (!ProtectionKitMigration.TryApply(save.Inventory, save.Recovery, out var graph, out var inbox))
                return false;
            save.Inventory = graph;
            save.Recovery = inbox;
            save.Receipts.Add(ProtectionReceipt, "1");
            return true;
        }

        private void TryMigrateProtection() {
            if (m_Save.DeploymentId != null || (m_Save.Inventory.ProtectionKitVersion >= 1 &&
                                                m_Save.Receipts.TryGetValue(ProtectionReceipt, out string receipt) &&
                                                receipt == "1"))
                return;
            // 先在副本尝试迁移。仓库没空间就留着原档，下次打开还可以重试。
            var next = Copy();
            if (ApplyProtectionMigration(next)) Commit(next);
        }
    }
}