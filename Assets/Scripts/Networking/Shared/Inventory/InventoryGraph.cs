#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.MP_FPS.Inventory {
    [Serializable]
    public sealed partial class InventoryGraph {
        public int Version = 1, StashRows = 80, LootRows = 5;
        public int WeaponKitVersion;
        public int ProtectionKitVersion;
        public int HaloResearchLevel;

        // 第 0 至 2 位保存已有生物研究，第 3 位只解锁月核制作知识。
        public int HaloResearchBranches;

        // 账号事实不从客户端结果或局内图合并。各位分别表示首次回收、月核与档案。
        public int MissionFirsts;
        public List<InventoryItem> Items = new();
        // ID 唯一识别某一堆物品，而不是大类。两个相同类型药箱分别拖放时，用各自 ID 来区分。
        public InventoryItem Find(string id) { return Items.Find(i => i.Id == id); }

        public InventoryGraph Clone() {
            var copy = (InventoryGraph)MemberwiseClone();
            copy.Items = Items.ConvertAll(item => item.Clone());
            return copy;
        }

        public int Rows(string parent, InventoryRegion region) {
            return Find(parent)?.Code switch {
                "stash" => StashRows,
                "loot" => LootRows,
                _ => region.Height
            };
        }

        public IEnumerable<InventoryItem> Children(string parent, string region = null) {
            return Items.Where(i => i.Parent == parent && (region == null || i.Region == region));
        }

        // 按 Parent 追到最外层，可以判断一件背包里的东西属于玩家、仓库还是地上箱子。负重和出战都要查整个父链。
        public string RootOf(string id) {
            var item = Find(id);
            for (int n = 0; item != null && n <= InventoryCatalog.MaxDepth; n++) {
                if (string.IsNullOrEmpty(item.Parent)) return item.Id;
                item = Find(item.Parent);
            }
            return null;
        }

        public bool Carried(InventoryItem item) {
            string root = RootOf(item.Id);
            return root == InventoryCatalog.Equipment || root == InventoryCatalog.Pockets;
        }

        public int Count(string code, bool carriedOnly = false) {
            return Items.Where(i => i.Code == code && (!carriedOnly || Carried(i))).Sum(i => i.Quantity);
        }

        // 随身所有层级物品均计进重量。在两层包里藏同样东西也增加相同负重，仓库里的物品不算。
        public float CarriedWeight => Items.Where(Carried).Sum(i => InventoryCatalog.Get(i.Code).Weight * i.Quantity);

        private bool WouldIncreaseExcessWeight(InventoryGraph candidate) {
            float weight = candidate.CarriedWeight;
            return weight > InventoryCatalog.CarryWeightLimit && weight > CarriedWeight + .001f;
        }

        public InventoryItem Equipped(string slot) {
            return Items.Find(i => i.Parent == InventoryCatalog.Equipment && i.Region == slot);
        }

        // 角色背包有装备与口袋根，账号档再加仓库根。局内状态不带整个账号仓库，避免一次出战接管无关装备。
        public static InventoryGraph Create(bool stash = true, bool starter = true) {
            var g = new InventoryGraph();
            foreach (string root in
                     stash ? new[] { "stash", "equipment", "pockets" } : new[] { "equipment", "pockets" })
                g.Items.Add(new InventoryItem { Id = root, Code = root });
            if (starter) {
                g.Items.Add(new InventoryItem
                    { Id = Guid.NewGuid().ToString("D"), Code = "rig", Parent = "equipment", Region = "ChestRig" });
                g.Items.Add(new InventoryItem {
                    Id = Guid.NewGuid().ToString("D"), Code = "backpack", Parent = "equipment", Region = "Backpack"
                });
                g.AddStarterWeapons(!stash);
            }
            return g;
        }

        // 开新库存或旧版迁移时按需求补武器。后续物品组合访问同一图结构，不把刀、枪和改装另存为一份。
        public void AddStarterWeapons(bool equipped = false) {
            if (WeaponKitVersion >= 1) return;
            foreach (var pair in new[] {
                         (Code: "rifle", Slot: "Primary"), (Code: "shotgun", Slot: "Secondary"),
                         (Code: "pistol", Slot: "Pistol")
                     }) {
                if (Items.Any(i => i.Code == pair.Code)) continue;
                var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = pair.Code };
                if (equipped && Equipped(pair.Slot) == null) {
                    item.Parent = "equipment";
                    item.Region = pair.Slot;
                } else {
                    if (Find("stash") == null) continue;
                    string region;
                    int x, y;
                    while (!FindSpace(item, "stash", out region, out x, out y)) {
                        if (StashRows >= 4096) throw new InvalidOperationException("No space for starter halo weapon.");
                        StashRows = Math.Min(4096, StashRows * 2);
                    }
                    Position(item, "stash", region, x, y, false);
                }
                Items.Add(item);
            }
            WeaponKitVersion = 1;
        }

        // 快捷搬运不会查套在内部的包，手动放置仍允许。
        public IEnumerable<string> QuickTransferContainers() {
            if (Find(InventoryCatalog.Pockets) != null) yield return InventoryCatalog.Pockets;
            var rig = Equipped("ChestRig");
            if (rig != null) yield return rig.Id;
            var pack = Equipped("Backpack");
            if (pack != null) yield return pack.Id;
        }

        // 遍历玩家可随身接收物品的根和槽位。快捷转移、创建补给与可访问性有不同需求，按各入口自己的顺序选。
        public IEnumerable<string> CarryContainers() {
            // 优先读一伸手能拿到的口袋和胸挂，最后再找背包和套在里面的包。
            if (Find("pockets") != null) yield return "pockets";
            var rig = Equipped("ChestRig");
            if (rig != null) yield return rig.Id;
            foreach (var i in Items)
                if (InventoryCatalog.Get(i.Code).Kind == ItemKind.Backpack && Carried(i))
                    yield return i.Id;
        }

        // 自动补给需要同时处理多个堆、可用格和原堆属性。操作成功前不能留下发了一半的物资。
        public InventoryError AddSupply(
            string code,
            int quantity,
            string parent = null,
            bool foundInRaid = false,
            string id = null,
            bool emergencySupply = false,
            int cellCharge = -1) {
            if (quantity == 0) return InventoryError.None;
            var definition = InventoryCatalog.Get(code);
            if (definition == null || definition.Container || quantity < 0) return InventoryError.Invalid;
            if (cellCharge != -1 && (code != "cells" || cellCharge < 1 || cellCharge > BatteryEnergy.Capacity))
                return InventoryError.Invalid;
            var copy = Clone();
            var supply = new InventoryItem {
                Id = id ?? Guid.NewGuid().ToString("D"), Code = code, Quantity = quantity,
                FoundInRaid = foundInRaid, EmergencySupply = emergencySupply, CellCharge = cellCharge
            };
            string[] containers = parent == null ? copy.CarryContainers().ToArray() : new[] { parent };
            foreach (string container in containers) {
                foreach (var stack in copy.Children(container).Where(i =>
                             i.Code == code && i.FoundInRaid == foundInRaid && i.EmergencySupply == emergencySupply)) {
                    int add = Math.Min(supply.Quantity, definition.MaxStack - stack.Quantity);
                    if (add > 0 && BatteryEnergy.CanMerge(supply, stack, add)) BatteryEnergy.Merge(supply, stack, add);
                }
                while (supply.Quantity > 0) {
                    var item = supply.Clone();
                    item.Quantity = Math.Min(supply.Quantity, definition.MaxStack);
                    if (!copy.FindSpace(item, container, out string region, out int x, out int y)) break;
                    item = BatteryEnergy.Take(supply, item.Quantity);
                    Position(item, container, region, x, y, false);
                    copy.Items.Add(item);
                }
            }
            if (supply.Quantity > 0) return InventoryError.Full;
            // 显式指定口袋、胸挂或背包也要满足同一负重限制。
            // 早已超重的存档仍可为仓库或世界掉落增添物品。
            if (WouldIncreaseExcessWeight(copy))
                return InventoryError.Overweight;
            var validation = copy.Validate();
            if (validation != InventoryError.None) return validation;
            Items = copy.Items;
            Version++;
            return InventoryError.None;
        }

        public bool ConsumeAccessibleCell() {
            var rig = Equipped("ChestRig");
            var item = Items.Find(i => i.Code == "cells" && (i.Parent == "pockets" || i.Parent == rig?.Id));
            if (item == null) return false;
            BatteryEnergy.Take(item, 1);
            if (item.Quantity == 0) Items.Remove(item);
            Version++;
            return true;
        }

        // 医疗、食品和电池不是随便背在仓库里就可用。按动作查带在身上的可达容器，再交给消耗事务。
        public bool MedicalAccessible(InventoryItem item) {
            return item != null && ConsumableCatalog.MedicalRestore(item.Code) > 0 &&
                   (item.Parent == InventoryCatalog.Pockets || item.Parent == Equipped("ChestRig")?.Id);
        }

        // 食物与水可以从任意随身包取。应急补氧罐
        // 至少要放在口袋或已装备胸挂等可立刻取用的区域。
        // 合并后的世界箱视图和个人仓库在这里都不能使用消耗品。
        public bool ConsumableAccessible(InventoryItem item) {
            if (item == null) return false;
            var supply = ConsumableCatalog.Get(item.Code);
            return supply != null && Carried(item) &&
                   (supply.Oxygen <= 0 || item.Parent == InventoryCatalog.Pockets ||
                    item.Parent == Equipped("ChestRig")?.Id);
        }

        // 扣除一次消耗前核对版本和物品仍有效。用完的堆删掉并递增版本，让后发的旧动作失效。
        public InventoryError ConsumeOne(string itemId, int expectedVersion) {
            if (expectedVersion != Version) return InventoryError.Stale;
            var item = Find(itemId);
            if (item == null) return InventoryError.Missing;
            if (!ConsumableAccessible(item)) return InventoryError.Inaccessible;
            if (item.Quantity < 1) return InventoryError.Invalid;
            if (--item.Quantity == 0) Items.Remove(item);
            Version++;
            return InventoryError.None;
        }

        // 这步只由战局服务器提交。身体已满、死亡或者版本过时，
        // 生命与物品堆都维持原样，重发也不会消费两份。
        public InventoryError UseMedical(
            string itemId,
            int expectedVersion,
            float health,
            float maximum,
            out float healed,
            bool allowZeroHealth = false,
            bool allowFullHealth = false) {
            healed = health;
            if (expectedVersion != Version) return InventoryError.Stale;
            var item = Find(itemId);
            if (item == null) return InventoryError.Missing;
            if (!MedicalAccessible(item)) return InventoryError.Inaccessible;
            if (float.IsNaN(health) || float.IsInfinity(health) || float.IsNaN(maximum) || float.IsInfinity(maximum) ||
                health < 0f || (!allowZeroHealth && health == 0f) || maximum <= 0 || maximum < health ||
                (!allowFullHealth && maximum == health) || item.Quantity < 1)
                return InventoryError.Invalid;
            healed = Math.Min(maximum, health + ConsumableCatalog.MedicalRestore(item.Code));
            if (--item.Quantity == 0) Items.Remove(item);
            Version++;
            return InventoryError.None;
        }

        private bool AccessibleCell(InventoryItem item) {
            return item.Code == "cells" && (item.Parent == "pockets" || item.Parent == Equipped("ChestRig")?.Id);
        }

        public int CellEnergy(bool carriedOnly = false, bool accessibleOnly = false) {
            return Items.Where(i => i.Code == "cells" &&
                                    (!carriedOnly || Carried(i)) && (!accessibleOnly || AccessibleCell(i)))
                .Sum(BatteryEnergy.Stored);
        }

        // 服务器批准换弹时只调用一次。预测只使用确定好的弹药目标值。
        // 旧武器补弹按要增加的弹数换算电量。取可访问电池，在空间和电量够时才一起改弹药结果。
        public bool TryRecharge(int currentAmmo, int magazineSize, int energyPerRound, out int targetAmmo) {
            targetAmmo = currentAmmo;
            if (currentAmmo < 0 || magazineSize <= currentAmmo || energyPerRound < 1) return false;
            int rounds = Math.Min(magazineSize - currentAmmo, CellEnergy(accessibleOnly: true) / energyPerRound);
            if (rounds == 0) return false;
            int remaining = rounds * energyPerRound;
            foreach (var item in Items.Where(AccessibleCell).ToArray()) {
                int used = Math.Min(remaining, BatteryEnergy.Stored(item));
                BatteryEnergy.Spend(item, used);
                remaining -= used;
                if (item.Quantity == 0) Items.Remove(item);
                if (remaining == 0) break;
            }
            targetAmmo += rounds;
            Version++;
            return true;
        }

        public InventoryError AddLoot(string code) {
            var def = InventoryCatalog.Get(code);
            if (def == null) return InventoryError.Invalid;
            if (!def.Container) return AddSupply(code, 1, foundInRaid: true);
            var copy = Clone();
            var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = code, FoundInRaid = true };
            string parent = null, region = null;
            int x = 0, y = 0;
            foreach (string container in new[] { "equipment" }.Concat(copy.CarryContainers()))
                if (copy.FindSpace(item, container, out region, out x, out y)) {
                    parent = container;
                    break;
                }
            if (parent == null) return InventoryError.Full;
            Position(item, parent, region, x, y, false);
            copy.Items.Add(item);
            if (copy.CarriedWeight > InventoryCatalog.CarryWeightLimit) return InventoryError.Overweight;
            var error = copy.Validate();
            if (error != InventoryError.None) return error;
            Items = copy.Items;
            Version++;
            return InventoryError.None;
        }

        // 出战只取随身装备和其中的子物品。账户的仓库根仍留在档案里，局内服务器不需要拿到整个仓库。
        public InventoryGraph ExtractLoadout() {
            var raid = Clone();
            raid.Items.RemoveAll(i => !Carried(i));
            raid.MissionFirsts = 0;
            var taken = new HashSet<string>(raid.Items.Where(i => InventoryCatalog.Get(i.Code).Kind != ItemKind.Root)
                .Select(i => i.Id));
            Items.RemoveAll(i => taken.Contains(i.Id));
            raid.Version = 1;
            Version++;
            return raid;
        }

        // 将随身树归回账号图时仍要核对同一 ID 是否重复、存放位置是否合法。仓库不够的在正式结算走回收箱。
        public InventoryError ReturnLoadout(InventoryGraph raid) {
            if (raid == null || raid.Validate() != InventoryError.None) return InventoryError.Invalid;
            var copy = Clone();
            foreach (var source in raid.Items.Where(i => InventoryCatalog.Get(i.Code).Kind != ItemKind.Root))
                if (copy.Find(source.Id) != null)
                    return InventoryError.Invalid;
            // 先尝试把随身的顶层物品恢复到装备或口袋，否则把整个物品子树一起放仓库。
            foreach (var top in raid.Items.Where(i => i.Parent == "equipment" || i.Parent == "pockets")) {
                var item = top.Clone();
                if (copy.CanPlace(item, item.Parent, item.Region, item.X, item.Y, item.Rotated) !=
                    InventoryError.None) {
                    if (!copy.FindSpace(item, "stash", out string region, out int x, out int y))
                        return InventoryError.Full;
                    Position(item, "stash", region, x, y, item.Rotated);
                }
                copy.Items.Add(item);
            }
            copy.Items.AddRange(raid.Items
                .Where(i => i.Parent != null && i.Parent != "equipment" && i.Parent != "pockets")
                .Select(i => i.Clone()));
            var missionError = MissionInventory.RecordRecovery(copy, raid);
            if (missionError != InventoryError.None) return missionError;
            var error = copy.Validate();
            if (error != InventoryError.None) return error;
            Items = copy.Items;
            MissionFirsts = copy.MissionFirsts;
            StashRows = copy.StashRows;
            Version++;
            return InventoryError.None;
        }
    }
}
