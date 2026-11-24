#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.MP_FPS.Inventory {
    public sealed partial class InventoryGraph {
        // 放物品先从具体容器类型查有效区块。是否装备槽、仓库可变行数、以及特殊光环挂点都影响怎样放。
        private static InventoryRegion RegionFor(string code, string region) {
            if (region == InventoryCatalog.HaloCargoRegion.Id && InventoryCatalog.CanStoreHaloCargo(code))
                return InventoryCatalog.HaloCargoRegion;
            return InventoryCatalog.Get(code)?.Regions.FirstOrDefault(r => r.Id == region) ??
                   (HaloModules.IsWeapon(code) ? HaloModules.Slots.FirstOrDefault(r => r.Id == region) : null);
        }

        // 只验证一个候选位置。检查容器和物品是否兼容、格子尺寸与重叠、禁止把容器放进自己的子孙和其他特殊限制。
        public InventoryError CanPlace(
            InventoryItem item,
            string parent,
            string region,
            int x,
            int y,
            bool rotated,
            string ignore = null) {
            var definition = InventoryCatalog.Get(item.Code);
            var target = Find(parent);
            var compartment = RegionFor(target?.Code, region);
            if (definition == null || definition.Kind == ItemKind.Root || target == null || compartment == null)
                return InventoryError.Missing;
            string ancestor = parent;
            for (int depth = 0; ancestor != null; depth++) {
                if (ancestor == item.Id) return InventoryError.Cycle;
                if (depth >= InventoryCatalog.MaxDepth) return InventoryError.Cycle;
                ancestor = Find(ancestor)?.Parent;
            }
            if (region == InventoryCatalog.HaloCargoRegion.Id && !InventoryCatalog.IsHaloResearchCargo(item.Code))
                return InventoryError.Incompatible;
            if (region != InventoryCatalog.HaloCargoRegion.Id && HaloModules.IsWeapon(target.Code) &&
                (!HaloModules.Compatible(target.Code, item.Code) ||
                 region != HaloModules.Slot(HaloModules.FromCode(item.Code))))
                return InventoryError.Incompatible;
            if (target.Code == "secure_case" && !SecureInventory.Allowed(item.Code)) return InventoryError.Incompatible;
            int width = rotated ? definition.Height : definition.Width;
            int height = rotated ? definition.Width : definition.Height;
            if (compartment.SlotKind.HasValue) {
                if (definition.Kind != compartment.SlotKind.Value || item.Quantity != 1)
                    return InventoryError.Incompatible;
                x = y = 0;
            } else {
                // 口袋和胸挂小格用于补给品，不放背包或其他穿戴装备。
                if ((target.Code == "pockets" || InventoryCatalog.Get(target.Code)?.Kind == ItemKind.Rig) &&
                    definition.Container)
                    return InventoryError.Incompatible;
                int rows = Rows(parent, compartment);
                if (x < 0 || y < 0 || x > compartment.Width - width || y > rows - height) return InventoryError.Bounds;
            }
            foreach (var other in Children(parent, region)) {
                if (other.Id == item.Id || other.Id == ignore) continue;
                if (compartment.SlotKind.HasValue) return InventoryError.Collision;
                var def = InventoryCatalog.Get(other.Code);
                int ow = other.Rotated ? def.Height : def.Width, oh = other.Rotated ? def.Width : def.Height;
                if (x < other.X + ow && x + width > other.X && y < other.Y + oh && y + height > other.Y)
                    return InventoryError.Collision;
            }
            return InventoryError.None;
        }

        // 按区域和行列顺序找第一个合规格。直接拖动和自动找位都共享 CanPlace，自动移动也不能叠物品。
        public bool FindSpace(InventoryItem item, string parent, out string region, out int x, out int y) {
            var regions = InventoryCatalog.Get(Find(parent)?.Code)?.Regions;
            if (regions != null) {
                foreach (var compartment in regions) {
                    int rows = Rows(parent, compartment);
                    for (int row = 0; row < rows; row++) {
                        for (int col = 0; col < compartment.Width; col++) {
                            if (CanPlace(item, parent, compartment.Id, col, row, item.Rotated) != InventoryError.None)
                                continue;
                            region = compartment.Id;
                            x = col;
                            y = row;
                            return true;
                        }
                    }
                }
            }
            region = null;
            x = y = 0;
            return false;
        }

        private static void Position(InventoryItem item, string parent, string region, int x, int y, bool rotated) {
            item.Parent = parent;
            item.Region = region;
            item.X = x;
            item.Y = y;
            item.Rotated = rotated;
        }

        // 拖动、拆堆和合并先改库存副本。结构、负重和版本全检查通过才替换正式物品并增加版本，失败时旧背包仍完整。
        public InventoryError TryApply(InventoryCommand request, bool enforceWeight = true) {
            // 界面操作绑定当时看到的图版本。别人先拿走、上一笔已修改或操作发了两次都会让旧版本失效。
            if (request == null || request.ExpectedVersion != Version) return InventoryError.Stale;
            // 安装、拆下和替换模块是基地仓库事务。整个武器含子物品
            // 仍可作为一棵完整物品树在局内移动。
            var source = Find(request.ItemId);
            // 任务装载槽只允许当局的合适研究物品；普通模块挂装只允许在仓库阶段。移动整个已装备光环仍可随物品树进行。
            if (source?.Region == InventoryCatalog.HaloCargoRegion.Id ||
                request.Region == InventoryCatalog.HaloCargoRegion.Id) {
                if (!InventoryCatalog.IsHaloResearchCargo(source?.Code) ||
                    request.Operation != InventoryOperation.Move || Find(InventoryCatalog.Stash) != null)
                    return InventoryError.Inaccessible;
                if (request.Region == InventoryCatalog.HaloCargoRegion.Id &&
                    (!Carried(source) || Find(request.Parent)?.Parent != InventoryCatalog.Equipment))
                    return InventoryError.Incompatible;
            } else if (HaloModules.IsWeapon(Find(source?.Parent)?.Code) ||
                       HaloModules.IsWeapon(Find(request.Parent)?.Code)) {
                if (Find(InventoryCatalog.Stash) == null || Find("loot") != null) return InventoryError.Inaccessible;
            }
            // 在副本里执行，完成后重新校验整个图。已超重的旧档可以整理或减重，但不能通过一次操作继续增加超重。
            var copy = Clone();
            var error = copy.Apply(request);
            if (error == InventoryError.None) error = copy.Validate();
            // 旧档已经超重时可以整理原装备，不能继续增加负重。
            if (error == InventoryError.None && enforceWeight && WouldIncreaseExcessWeight(copy))
                error = InventoryError.Overweight;
            if (error != InventoryError.None) return error;
            Items = copy.Items;
            Version++;
            return InventoryError.None;
        }

        // 这里在副本里执行一次移动。换装备时先给新装备占位，再找地方放卸下来的那件。没有空位会使整次操作取消。
        private InventoryError Apply(InventoryCommand r) {
            var item = Find(r.ItemId);
            var def = InventoryCatalog.Get(item?.Code);
            if (item == null || def == null || def.Kind == ItemKind.Root) return InventoryError.Missing;
            // 合堆由数量和电池余量一起决定，来源或类型不合时拒绝。来源已经空则删掉那一堆。
            if (r.Operation == InventoryOperation.Merge) {
                var target = Find(r.TargetId);
                int count = r.Quantity == 0 ? item.Quantity : r.Quantity;
                if (!CanMergeStacks(item, target, count)) return InventoryError.Invalid;
                BatteryEnergy.Merge(item, target, count);
                if (item.Quantity == 0) Items.Remove(item);
                return InventoryError.None;
            }
            // 拆堆先试放新堆，再从原数量切出去。新堆有新 ID，旧堆仍保留。
            if (r.Operation == InventoryOperation.Split) {
                if (def.Container || r.Quantity <= 0 || r.Quantity >= item.Quantity) return InventoryError.Invalid;
                var split = item.Clone();
                split.Id = Guid.NewGuid().ToString("D");
                split.Quantity = r.Quantity;
                var error = CanPlace(split, r.Parent, r.Region, r.X, r.Y, r.Rotated);
                if (error != InventoryError.None) return error;
                split = BatteryEnergy.Take(item, r.Quantity);
                Position(split, r.Parent, r.Region, r.X, r.Y, r.Rotated);
                Items.Add(split);
                return InventoryError.None;
            }
            if (r.Operation != InventoryOperation.Move) return InventoryError.Invalid;
            var slot = RegionFor(Find(r.Parent)?.Code, r.Region);
            var displaced = slot?.SlotKind.HasValue == true
                ? Children(r.Parent, r.Region).FirstOrDefault(i => i.Id != item.Id)
                : null;
            var result = CanPlace(item, r.Parent, r.Region, r.X, r.Y, r.Rotated, displaced?.Id);
            if (result != InventoryError.None) return result;
            string oldParent = item.Parent, oldRegion = item.Region;
            int oldX = item.X, oldY = item.Y;
            bool oldRotation = item.Rotated;
            Position(item, r.Parent, r.Region, slot.SlotKind.HasValue ? 0 : r.X, slot.SlotKind.HasValue ? 0 : r.Y,
                r.Rotated);
            if (displaced != null) {
                // 换装时临时腾出旧装备位置，先试回新装备原位置，再至少找到一个合法空格；失败整笔保留不变。
                displaced.Parent = null; // 先给新装备占好目标位置，再替换下来的旧装备找位置。
                if (CanPlace(displaced, oldParent, oldRegion, oldX, oldY, oldRotation) == InventoryError.None)
                    Position(displaced, oldParent, oldRegion, oldX, oldY, oldRotation);
                else if (FindSpace(displaced, oldParent, out string region, out int x, out int y))
                    Position(displaced, oldParent, region, x, y, displaced.Rotated);
                else
                    return InventoryError.Full;
            }
            return InventoryError.None;
        }

        private static bool ValidProtectionValue(float value) {
            return value == -1 || float.IsFinite(value) && value >= 0 && value <= 100;
        }

        public InventoryError Validate() {
            // 合并战利品快照同时含两边物品，数量上限更宽。普通仓库或随身图仍按单个所有者上限查。
            int limit = Items != null && Items.Any(i => i?.Id == "loot")
                ? InventoryCatalog.MaxItems * 2
                : InventoryCatalog.MaxItems;
            if (Version < 1 || ProtectionKitVersion < 0 || ProtectionKitVersion > 1 || HaloResearchLevel < 0 ||
                HaloResearchLevel > 1 || HaloResearchBranches < 0 || HaloResearchBranches > 15 || MissionFirsts < 0 ||
                MissionFirsts > 7 || Items == null || Items.Count > limit || StashRows < 1 || StashRows > 4096 ||
                LootRows < 1 || LootRows > 24)
                return InventoryError.Invalid;
            var ids = new HashSet<string>();
            foreach (var item in Items) {
                if (item == null) return InventoryError.Invalid;
                var def = InventoryCatalog.Get(item.Code);
                if (item.Id == null || !ids.Add(item.Id) || def == null || item.Quantity <= 0 ||
                    item.Quantity > def.MaxStack || item.LoadedAmmo < -1 || item.LoadedAmmo > 10000)
                    return InventoryError.Invalid;
                if (item.CellCharge != -1 && (!BatteryEnergy.IsCell(item) || item.CellCharge < 1 ||
                                              item.CellCharge > BatteryEnergy.Capacity))
                    return InventoryError.Invalid;
                if (!ValidProtectionValue(item.ShieldCharge) || !ValidProtectionValue(item.ShieldEnergy) ||
                    !ValidProtectionValue(item.SuitOxygen))
                    return InventoryError.Invalid;
                if ((item.ShieldCharge != -1 || item.ShieldEnergy != -1) && item.Code != "energy_shield_generator")
                    return InventoryError.Invalid;
                if (item.SuitOxygen != -1 && item.Code != "energy_head_dome" && item.Code != "helmet")
                    return InventoryError.Invalid;
                if (!MissionInventory.ValidSource(item)) return InventoryError.Invalid;
                if (def.Kind == ItemKind.Root) {
                    if (item.Id != item.Code || item.Parent != null || item.Quantity != 1)
                        return InventoryError.Invalid;
                } else if (!Guid.TryParse(item.Id, out _) || item.Parent == null) { return InventoryError.Invalid; }
            }
            // 先验证每件物品的身份和类型，避免附近损坏记录导致 CanPlace 异常。
            if (!ids.Contains("equipment") || !ids.Contains("pockets")) return InventoryError.Invalid;
            foreach (var item in Items)
                if (InventoryCatalog.Get(item.Code).Kind != ItemKind.Root) {
                    var error = CanPlace(item, item.Parent, item.Region, item.X, item.Y, item.Rotated);
                    if (error != InventoryError.None) return error;
                    if (RootOf(item.Id) == null) return InventoryError.Cycle;
                }
            return InventoryError.None;
        }

        // 预览和正式合堆共用来源、容量与余电规则。
        // 堆叠不仅看物品码，也要保存物品的来源等字段。电池合并还要遵守每堆最多一节有余量的规则。
        public static bool CanMergeStacks(InventoryItem source, InventoryItem target, int count) {
            var definition = InventoryCatalog.Get(source?.Code);
            return source != null && target != null && definition != null && !definition.Container &&
                   source.Id != target.Id && source.Code == target.Code && count > 0 && count <= source.Quantity &&
                   (long)target.Quantity + count <= definition.MaxStack && source.FoundInRaid == target.FoundInRaid &&
                   source.EmergencySupply == target.EmergencySupply && BatteryEnergy.CanMerge(source, target, count);
        }

        // 快捷搬东西先在接收方查现有堆和空格，只返回拟执行的库存命令。权威写入仍会重新检查版本与规则。
        public InventoryCommand PlanQuickMove(
            string itemId,
            string parent,
            bool mergeOnly = false,
            bool allowPartialMerge = false) {
            var item = Find(itemId);
            var definition = InventoryCatalog.Get(item?.Code);
            if (item == null || definition == null || definition.Kind == ItemKind.Root) return null;
            if (!definition.Container && definition.MaxStack > 1)
                foreach (var stack in Children(parent)) {
                    int count = allowPartialMerge
                        ? Math.Min(item.Quantity, definition.MaxStack - stack.Quantity)
                        : item.Quantity;
                    if (CanMergeStacks(item, stack, count))
                        return new InventoryCommand {
                            Operation = InventoryOperation.Merge, ItemId = item.Id, TargetId = stack.Id,
                            Quantity = count
                        };
                }
            if (mergeOnly) return null;
            var placement = item;
            if (!FindSpace(placement, parent, out string region, out int x, out int y)) {
                if (definition.Width == definition.Height) return null;
                placement = item.Clone();
                placement.Rotated = !item.Rotated;
                if (!FindSpace(placement, parent, out region, out x, out y)) return null;
            }
            return new InventoryCommand
                { ItemId = item.Id, Parent = parent, Region = region, X = x, Y = y, Rotated = placement.Rotated };
        }
    }
}
