// 一次性补齐旧档案的防护装备。优先装备或存仓，放不下可放入待领取的回收箱。空间仍不足时保留原档案，下一次再试。
using System;
using System.Linq;

namespace Unity.MP_FPS.Inventory {
    // 账号只迁移一次，不随每次出战发放。调用者将
    // 账号库存、回收箱和版本一起提交。局内持有装备时延后处理。
    public static class ProtectionKitMigration {
        public static bool TryApply(
            InventoryGraph account,
            RecoveryInbox inbox,
            out InventoryGraph stored,
            out RecoveryInbox pending) {
            stored = account;
            pending = inbox;
            if (account == null || inbox == null || inbox.Validate(account) != InventoryError.None)
                throw new InvalidOperationException("Cannot migrate an invalid protection inventory.");
            // 已有有效迁移标记就直接返回。旧档本来就佩戴合适项圈时只升级标记，不覆盖现有氧气。
            if (account.ProtectionKitVersion >= 1) return false;
            var next = account.Clone();
            var recovery = inbox.Clone();
            var helmet = next.Equipped("Helmet");
            if (helmet != null && (helmet.Code == "helmet" || helmet.Code == "energy_head_dome")) {
                Complete(next, recovery, out stored, out pending);
                return true;
            }
            // 给确实缺项圈的档案新建唯一物品。先试人物槽，失败再试仓库空间。
            var grant = new InventoryItem
                { Id = Guid.NewGuid().ToString("D"), Code = "energy_head_dome", Quantity = 1 };
            if (Place(next, grant, "equipment", "Helmet", 0, 0, true) || FindAndPlace(next, grant, "stash", false)) {
                Complete(next, recovery, out stored, out pending);
                return true;
            }
            // 仓库也塞不下才尝试已有回收批次或创建新批。容量仍需合法，不能为了迁移超过承载上限。
            foreach (var batch in recovery.Batches) {
                bool placed = Place(batch, grant, "equipment", "Helmet", 0, 0, false);
                if (!placed)
                    foreach (string container in batch.CarryContainers().ToArray())
                        if (FindAndPlace(batch, grant, container, false)) {
                            placed = true;
                            break;
                        }
                if (!placed) continue;
                recovery.Version++;
                Complete(next, recovery, out stored, out pending);
                return true;
            }
            if (recovery.Batches.Count < 2) {
                var batch = InventoryGraph.Create(false, false);
                if (Place(batch, grant, "equipment", "Helmet", 0, 0, false)) {
                    recovery.Batches.Add(batch);
                    recovery.Version++;
                    Complete(next, recovery, out stored, out pending);
                    return true;
                }
            }
            // 这次没写入，腾地方之后可以重试。回执没有登记，
            // 既有物品和可用资源也未被换掉或补满。
            return false;
        }

        private static bool FindAndPlace(InventoryGraph graph, InventoryItem item, string parent, bool checkWeight) {
            if (graph.Items.Count >= InventoryCatalog.MaxItems || graph.Find(parent) == null ||
                !graph.FindSpace(item, parent, out string region, out int x, out int y))
                return false;
            return Place(graph, item, parent, region, x, y, checkWeight);
        }

        private static bool Place(
            InventoryGraph graph,
            InventoryItem item,
            string parent,
            string region,
            int x,
            int y,
            bool checkWeight) {
            if (graph.Items.Count >= InventoryCatalog.MaxItems ||
                graph.CanPlace(item, parent, region, x, y, false) != InventoryError.None)
                return false;
            var copy = item.Clone();
            copy.Parent = parent;
            copy.Region = region;
            copy.X = x;
            copy.Y = y;
            copy.Rotated = false;
            graph.Items.Add(copy);
            if (graph.Validate() == InventoryError.None &&
                (!checkWeight || graph.CarriedWeight <= InventoryCatalog.CarryWeightLimit))
                return true;
            graph.Items.Remove(copy);
            return false;
        }

        // 只有已装备或放入某处的完整库存才升级迁移版本，由存档和回收箱一起提交。
        private static void Complete(
            InventoryGraph graph,
            RecoveryInbox inbox,
            out InventoryGraph stored,
            out RecoveryInbox pending) {
            graph.ProtectionKitVersion = 1;
            graph.Version++;
            if (inbox.Validate(graph) != InventoryError.None)
                throw new InvalidOperationException("Protection migration would lose inventory ownership.");
            stored = graph;
            pending = inbox;
        }
    }
}