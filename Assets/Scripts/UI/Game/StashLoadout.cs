// 和 StashLayout 配套的模板装备逻辑。根据演示物品类别和目标槽判断穿脱，卸下时先找格子。正式库存规则在 InventoryGraph。
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.Client {
    public enum LoadoutSlot {
        None,
        Helmet,
        Primary,
        Secondary,
        Pistol,
        ChestRig,
        Backpack,
        Shield,
        Melee
    }

    public enum EquipmentKind {
        None,
        Helmet,
        LongGun,
        Pistol,
        ChestRig,
        Backpack,
        Shield,
        Melee
    }

    // Local presentation only. Item IDs and equipment writes must eventually come from the server.
    public sealed class StashLoadout {
        private readonly StashLayout m_Storage;
        private readonly Dictionary<string, EquipmentKind> m_Kinds = new();
        private readonly Dictionary<LoadoutSlot, string> m_Equipped = new();
        public StashLoadout(StashLayout storage) { m_Storage = storage; }
        public void Register(string id, EquipmentKind kind) { m_Kinds.Add(id, kind); }
        public string At(LoadoutSlot slot) { return m_Equipped.TryGetValue(slot, out string id) ? id : null; }

        public LoadoutSlot Location(string id) {
            foreach (var pair in m_Equipped)
                if (pair.Value == id)
                    return pair.Key;
            return LoadoutSlot.None;
        }

        public bool Accepts(string id, LoadoutSlot slot) {
            // 要先登记物品类别，再判断槽位是否接受。主副武器可接受同类，其他槽位通常一类对应一槽。
            if (!m_Kinds.TryGetValue(id, out var kind)) return false;
            return (kind == EquipmentKind.Helmet && slot == LoadoutSlot.Helmet) ||
                   (kind == EquipmentKind.LongGun && (slot == LoadoutSlot.Primary || slot == LoadoutSlot.Secondary)) ||
                   (kind == EquipmentKind.Pistol && slot == LoadoutSlot.Pistol) ||
                   (kind == EquipmentKind.ChestRig && slot == LoadoutSlot.ChestRig) ||
                   (kind == EquipmentKind.Backpack && slot == LoadoutSlot.Backpack) ||
                   (kind == EquipmentKind.Shield && slot == LoadoutSlot.Shield) ||
                   (kind == EquipmentKind.Melee && slot == LoadoutSlot.Melee);
        }

        public bool CanEquip(string id, LoadoutSlot slot) {
            if (!Accepts(id, slot) || !m_Storage.Items[id].Available || m_Storage.Items[id].Locked) return false;
            string previous = At(slot);
            return previous == null || !m_Storage.Items[previous].Locked;
        }

        public bool TryEquip(string id, LoadoutSlot slot) {
            if (!CanEquip(id, slot)) return false;
            var sourceSlot = Location(id);
            if (sourceSlot == slot) return true;
            var incoming = m_Storage.Items[id];
            string displaced = At(slot);
            bool wasStored = incoming.InStorage;
            incoming.InStorage = false;
            Vector2Int destination = default;
            bool swapSlots = displaced != null && sourceSlot != LoadoutSlot.None && Accepts(displaced, sourceSlot);
            // 目标槽已有人时，先看看能否与来源槽互换。互换不了就给旧装备在仓库找格子。
            if (displaced != null && !swapSlots) {
                var old = m_Storage.Items[displaced];
                destination = new Vector2Int(incoming.X, incoming.Y);
                if (!(wasStored && m_Storage.CanPlace(displaced, destination.x, destination.y, old.Rotated)) &&
                    !m_Storage.FindSpace(displaced, out destination)) {
                    incoming.InStorage = wasStored;
                    return false;
                }
            }
            // 新旧位置都已找到才真正更新两个装备槽。这样失败能直接返回，不会发生一半脱下的情况。
            if (sourceSlot != LoadoutSlot.None) m_Equipped.Remove(sourceSlot);
            m_Equipped[slot] = id;
            if (displaced != null) {
                if (swapSlots) { m_Equipped[sourceSlot] = displaced; } else {
                    var old = m_Storage.Items[displaced];
                    old.X = destination.x;
                    old.Y = destination.y;
                    old.InStorage = true;
                }
            }
            return true;
        }

        public bool TryStore(string id, int x, int y, bool rotated) {
            if (!m_Storage.Items[id].Available) return false;
            // 物品先成功搬进仓储格，再从装备槽移除。落地不合法时保持当前穿戴。
            if (!m_Storage.TryMove(id, x, y, rotated)) return false;
            m_Storage.Items[id].InStorage = true;
            var slot = Location(id);
            if (slot != LoadoutSlot.None) m_Equipped.Remove(slot);
            return true;
        }

        public bool TryUnequip(LoadoutSlot slot) {
            string id = At(slot);
            return id != null && m_Storage.FindSpace(id, out var position) &&
                   TryStore(id, position.x, position.y, m_Storage.Items[id].Rotated);
        }
    }
}