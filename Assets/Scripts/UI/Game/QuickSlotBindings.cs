#nullable disable
using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client
{
    // Exact item identities survive UI rebuilds; unavailable items never resolve by code.
    public sealed partial class QuickSlotBindings
    {
        public const int Count = 9;
        public const int EquipmentSlots = 4;
        private bool m_Fists;
        public bool Fists(int slot) => slot == 3 && m_Fists;
        private readonly IQuickSlotStore m_Store;
        private QuickSlotSave m_Saved = new QuickSlotSave();
        private string m_Scope;
        public int Selected { get; private set; } = -1;
        public string Reason { get; private set; }
        public string this[int slot] => ValidSlot(slot) ? m_Saved.Items[slot] : null;
        public string Code(int slot) => Fists(slot) ? "fists" : ValidSlot(slot) ? m_Saved.Codes[slot] : null;
        private static bool ValidSlot(int slot) => slot >= 0 && slot < Count;
        public QuickSlotBindings(IQuickSlotStore store) => m_Store = store ?? throw new ArgumentNullException(nameof(store));
        public void UseProfile(string scope)
        {
            scope ??= "";
            if (scope == m_Scope) return;
            m_Scope = scope; Selected = -1; Reason = null; m_Fists = false;
            m_Saved = scope.Length == 0 ? new QuickSlotSave() : m_Store.Load(scope) ?? new QuickSlotSave();
            if (m_Saved.Items?.Length != Count || m_Saved.Codes?.Length != Count) m_Saved = new QuickSlotSave();
            for (int i = 0; i < Count; i++)
                if (m_Saved.Items[i]?.Length > 64 || m_Saved.Codes[i]?.Length > 64)
                { m_Saved.Items[i] = null; m_Saved.Codes[i] = null; }
        }
        public void InitializeWeapons(InventoryGraph graph)
        {
            if (string.IsNullOrEmpty(m_Scope) || graph == null) return;
            bool changed = !m_Saved.Initialized;
            m_Saved.Initialized = true;
            var slots = new[] { "Primary", "Secondary", "Pistol", MeleeInventory.Slot };
            for (int i = 0; i < slots.Length; i++)
            {
                var item = graph.Equipped(slots[i]);
                if (!CanBind(graph, item)) item = null;
                if (m_Saved.Items[i] != item?.Id || m_Saved.Codes[i] != item?.Code)
                {
                    changed = true;
                    if (Selected == i) Selected = -1;
                    m_Saved.Items[i] = item?.Id; m_Saved.Codes[i] = item?.Code;
                }
            }
            m_Fists = MeleeInventory.Equipped(graph) == null;
            if (changed) Save();
        }
        private void Save() { if (!string.IsNullOrEmpty(m_Scope)) m_Store.Save(m_Scope, m_Saved); }
        public static int WeaponSlot(InventoryGraph graph, InventoryItem item)
        {
            if (MeleeInventory.CanEquip(graph, item)) return MeleeInventory.EquipmentIndex;
            if (graph == null || item == null || (InventoryCatalog.Get(item.Code)?.Kind != ItemKind.LongGun && InventoryCatalog.Get(item.Code)?.Kind != ItemKind.Pistol)) return 0;
            return graph.Equipped("Primary")?.Id == item.Id ? 1 : graph.Equipped("Secondary")?.Id == item.Id ? 2 : graph.Equipped("Pistol")?.Id == item.Id ? 3 : 0;
        }
        public static bool CanBind(InventoryGraph graph, InventoryItem item) => item != null && item.Quantity > 0 && graph != null && graph.Carried(item) &&
            (WeaponSlot(graph, item) > 0 || graph.MedicalAccessible(item) || graph.ConsumableAccessible(item) ||
             EnergyGrenadeRules.Accessible(graph, item) || TacticalItemRules.Accessible(graph, item));
        public bool Available(int slot, InventoryGraph graph)
        {
            if (Fists(slot)) return graph != null;
            var item = graph?.Find(this[slot]);
            return item != null && item.Code == Code(slot) && CanBind(graph, item);
        }
        public bool Bind(int slot, InventoryItem item, InventoryGraph graph)
        {
            if (slot >= 0 && slot < EquipmentSlots)
            { Reason = "1–4 跟随主武器、副武器、手枪和近战装备；物品请绑定到 5–9。"; return false; }
            // A snapshot may be replaced between pointer-down and drop. Resolve the
            // current authoritative item by ID, rather than requiring object identity.
            var current = graph?.Find(item?.Id);
            if (!ValidSlot(slot) || string.IsNullOrEmpty(m_Scope) || current == null || current.Code != item.Code || !CanBind(graph, current))
            { Reason = "Equip weapons first. Keep usable supplies in your pockets or rig."; return false; }
            item = current;
            for (int i = EquipmentSlots; i < Count; i++) if (m_Saved.Items[i] == item.Id) ClearEntry(i);
            ClearEntry(slot);
            m_Saved.Items[slot] = item.Id; m_Saved.Codes[slot] = item.Code;
            m_Saved.Initialized = true; Reason = "Binding saved."; Save(); return true;
        }
        private void ClearEntry(int slot)
        { m_Saved.Items[slot] = m_Saved.Codes[slot] = null; if (Selected == slot) Selected = -1; }
        public void Clear(int slot)
        { if (!ValidSlot(slot)) return; if (slot < EquipmentSlots) { Reason = "1–4 自动跟随装备。"; return; } ClearEntry(slot); m_Saved.Initialized = true; Reason = "Binding cleared."; Save(); }
        public void Deselect() => Selected = -1;
        public void Validate(InventoryGraph graph)
        {
            if (Selected >= 0 && !Available(Selected, graph))
            { Selected = -1; Reason = "Item unavailable. Equip it or bind another item."; }
        }
        public InventoryItem Select(int slot, InventoryGraph graph)
        {
            Validate(graph);
            if (!Available(slot, graph))
            { Reason = string.IsNullOrEmpty(this[slot]) ? "Empty slot. Bind an item in your inventory." : "Item unavailable. Equip it or bind another item."; return null; }
            Selected = slot; Reason = null; return graph.Find(this[slot]);
        }
        public InventoryItem SelectedItem(InventoryGraph graph)
        { Validate(graph); return Selected >= 0 ? graph?.Find(this[Selected]) : null; }
    }
    [Serializable]
    public sealed class QuickSlotSave
    {
        public bool Initialized;
        public string[] Items = new string[QuickSlotBindings.Count];
        public string[] Codes = new string[QuickSlotBindings.Count];
    }
    public interface IQuickSlotStore
    {
        QuickSlotSave Load(string scope);
        void Save(string scope, QuickSlotSave bindings);
    }
}
