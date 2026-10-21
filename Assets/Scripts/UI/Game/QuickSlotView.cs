using System;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed class QuickSlotView : IDisposable
    {
        public VisualElement Element { get; }
        private readonly Button[] m_Slots = new Button[QuickSlotBindings.Count];
        private readonly Label[] m_Counts = new Label[QuickSlotBindings.Count];
        private readonly Label[] m_Names = new Label[QuickSlotBindings.Count];
        private readonly VisualElement[] m_Icons = new VisualElement[QuickSlotBindings.Count];
        private readonly string[] m_Codes = new string[QuickSlotBindings.Count];
        private readonly Label m_Caption, m_Selected;
        public QuickSlotView(VisualElement host, Action<int> clear)
        {
            Element = new VisualElement { name = "raidQuickSlots", pickingMode = PickingMode.Ignore };
            Element.AddToClassList("quick-slots"); Element.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/QuickSlots")); host.Add(Element);
            var row = new VisualElement { pickingMode = PickingMode.Ignore }; row.AddToClassList("quick-slots-row"); Element.Add(row);
            for (int i = 0; i < m_Slots.Length; i++)
            {
                int slot = i; var button = m_Slots[i] = new Button { name = "quickSlot" + (i + 1), focusable = false }; button.AddToClassList("quick-slot"); row.Add(button);
                var key = new Label((i + 1).ToString()) { pickingMode = PickingMode.Ignore }; key.AddToClassList("quick-slot-key"); button.Add(key);
                var icon = m_Icons[i] = new VisualElement { pickingMode = PickingMode.Ignore }; icon.AddToClassList("quick-slot-icon"); button.Add(icon);
                var name = m_Names[i] = new Label { pickingMode = PickingMode.Ignore }; name.AddToClassList("quick-slot-name"); button.Add(name);
                var count = m_Counts[i] = new Label { pickingMode = PickingMode.Ignore }; count.AddToClassList("quick-slot-count"); button.Add(count);
                button.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 1 && UnityEngine.Cursor.lockState != CursorLockMode.Locked)
                    { clear(slot); evt.StopImmediatePropagation(); }
                });
            }
            m_Selected = new Label { pickingMode = PickingMode.Ignore }; m_Selected.AddToClassList("quick-slot-detail"); Element.Add(m_Selected);
            m_Caption = new Label { pickingMode = PickingMode.Ignore }; m_Caption.AddToClassList("quick-slot-caption"); Element.Add(m_Caption);
        }
        public int Hit(Vector2 point)
        { for (int i = 0; i < m_Slots.Length; i++) if (m_Slots[i].panel != null && m_Slots[i].worldBound.Contains(point)) return i; return -1; }
        public void PreviewDrop(int slot, bool valid)
        {
            for (int i = 0; i < m_Slots.Length; i++)
            { m_Slots[i].EnableInClassList("quick-slot-drop-valid", i == slot && valid); m_Slots[i].EnableInClassList("quick-slot-drop-invalid", i == slot && !valid); }
        }
        public void Present(QuickSlotBindings bindings, InventoryGraph graph, bool busy, bool inventoryOpen, string status)
        {
            Element.EnableInClassList("quick-slots-busy", busy);
            for (int i = 0; i < m_Slots.Length; i++)
            {
                bool available = bindings.Available(i, graph);
                var item = graph?.Find(bindings[i]); string code = bindings.Code(i);
                bool bound = !string.IsNullOrEmpty(bindings[i]);
                if (code != m_Codes[i])
                {
                    m_Codes[i] = code; m_Icons[i].Clear();
                    if (!string.IsNullOrEmpty(code))
                    {
                        VisualElement art = TacticalItemRules.Find(code) != null ? new TacticalItemIcon(code) : new StashItemArt(ContainerInventoryView.Art(code));
                        art.AddToClassList("quick-slot-art"); m_Icons[i].Add(art);
                    }
                }
                m_Slots[i].pickingMode = inventoryOpen ? PickingMode.Position : PickingMode.Ignore;
                m_Counts[i].text = available ? item.Quantity.ToString() : bound ? "×" : "";
                m_Names[i].text = string.IsNullOrEmpty(code) ? "" : MoonkovLocalization.Text(InventoryCatalog.Get(code)?.Name ?? code);
                m_Slots[i].EnableInClassList("quick-slot-selected", bindings.Selected == i && available);
                m_Slots[i].EnableInClassList("quick-slot-empty", !bound);
                m_Slots[i].EnableInClassList("quick-slot-unavailable", bound && !available);
                m_Slots[i].tooltip = !bound ? MoonkovLocalization.Text("Hover an item in your inventory and press this number, or drag it here.") :
                    m_Names[i].text + " / " + MoonkovLocalization.Text(available ? "Right click to clear binding." : "Item unavailable. Equip it or bind another item.");
            }
            var selected = bindings.SelectedItem(graph);
            m_Selected.text = selected == null ? "" : MoonkovLocalization.Text(InventoryCatalog.Get(selected.Code).Name) + " ×" + selected.Quantity +
                " / " + MoonkovLocalization.Text(QuickSlotBindings.WeaponSlot(graph, selected) > 0 ? "WEAPON SELECTED" : "LEFT CLICK TO USE");
            m_Selected.style.display = selected != null ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(m_Caption, status ?? (inventoryOpen
                ? "Hover an item + 1–9 / Drag to bind / Right click to clear"
                : "1–9 SELECT / LEFT CLICK USE SELECTED ITEM"));
        }
        public void Dispose() => Element.RemoveFromHierarchy();
    }
}
