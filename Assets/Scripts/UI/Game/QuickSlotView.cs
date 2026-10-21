using System;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed class QuickSlotView : IDisposable
    {
        public VisualElement Element { get; }
        private readonly VisualElement m_PixelRoot;
        private Vector2 m_ViewportSize;
        private float m_ViewportScale = -1;
        private readonly Button[] m_Slots = new Button[QuickSlotBindings.Count];
        private readonly Label[] m_Counts = new Label[QuickSlotBindings.Count];
        private readonly Label[] m_Names = new Label[QuickSlotBindings.Count];
        private readonly VisualElement[] m_Icons = new VisualElement[QuickSlotBindings.Count];
        private readonly string[] m_Codes = new string[QuickSlotBindings.Count];
        private readonly Label m_Caption, m_Selected;
        public QuickSlotView(VisualElement host, Action<int> clear)
        {
            Element = new VisualElement { name = "raidQuickSlots", pickingMode = PickingMode.Ignore };
            Element.AddToClassList("quick-slots");
            m_PixelRoot = new VisualElement { name = "raidQuickSlotsPixelRoot", pickingMode = PickingMode.Ignore };
            m_PixelRoot.AddToClassList("quick-slots-pixel-root");
            m_PixelRoot.AddToClassList("moonkov-controls");
            m_PixelRoot.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/InventoryTheme"));
            m_PixelRoot.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/QuickSlots"));
            host.Add(m_PixelRoot); m_PixelRoot.Add(Element);
            var row = new VisualElement { pickingMode = PickingMode.Ignore }; row.AddToClassList("quick-slots-row"); row.AddToClassList("inventory-view"); Element.Add(row);
            var itemTemplate = Resources.Load<VisualTreeAsset>("Moonkov/UI/Components/InventoryItem");
            for (int i = 0; i < m_Slots.Length; i++)
            {
                int slot = i; var button = m_Slots[i] = (Button)TerminalLayout.Clone(itemTemplate, "inventoryItem");
                button.name = "quickSlot" + (i + 1); button.focusable = false; button.AddToClassList("quick-slot"); row.Add(button);
                var key = new Label((i + 1).ToString()) { pickingMode = PickingMode.Ignore }; key.AddToClassList("quick-slot-key"); button.Add(key);
                var icon = m_Icons[i] = new VisualElement { pickingMode = PickingMode.Ignore }; icon.AddToClassList("quick-slot-icon"); button.Insert(1, icon);
                var name = m_Names[i] = button.Q<Label>("itemLabel"); name.AddToClassList("quick-slot-name");
                var count = m_Counts[i] = button.Q<Label>("quantity"); count.AddToClassList("quick-slot-count");
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
        private void RefreshViewport()
        {
            var parent = m_PixelRoot.parent;
            if (parent == null || parent.contentRect.width < 1 || Screen.width < 1 || Screen.height < 1) return;
            // Cancel only the panel's downscaling. Preserve the existing large-screen
            // scale above its native reference width. All drawing and pointer bounds
            // share the same local transform; no per-label font multipliers are used.
            float width = Mathf.Min(Screen.width, parent.contentRect.width);
            var size = new Vector2(width, Screen.height * width / Screen.width);
            float scale = parent.contentRect.width / width;
            if (m_ViewportSize == size && Mathf.Approximately(m_ViewportScale, scale)) return;
            m_ViewportSize = size; m_ViewportScale = scale;
            m_PixelRoot.style.width = size.x; m_PixelRoot.style.height = size.y;
            m_PixelRoot.style.scale = new Scale(new Vector3(scale, scale, 1));
            m_PixelRoot.EnableInClassList("quick-slots-narrow", width < 1000);
            // Keep the existing timed medical panel above this compact row.
            var actionPanel = m_PixelRoot.Q("raidTimedAction");
            if (actionPanel != null) actionPanel.style.bottom = width < 1000 ? 210 : 145;
        }
        public void Present(QuickSlotBindings bindings, InventoryGraph graph, bool busy, bool inventoryOpen, string status)
        {
            RefreshViewport();
            Element.EnableInClassList("quick-slots-busy", busy);
            Element.EnableInClassList("quick-slots-inventory", inventoryOpen);
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
                var itemDisplay = bound && !string.IsNullOrEmpty(code) ? DisplayStyle.Flex : DisplayStyle.None;
                m_Icons[i].style.display = m_Names[i].style.display = m_Counts[i].style.display = itemDisplay;
                m_Counts[i].text = available ? item.Quantity.ToString() : bound ? "×" : "";
                MoonkovLocalization.Set(m_Names[i], string.IsNullOrEmpty(code) ? "" : InventoryCatalog.Get(code)?.Name ?? code);
                m_Slots[i].EnableInClassList("inventory-selected", bindings.Selected == i && available);
                m_Slots[i].EnableInClassList("quick-slot-empty", !bound);
                m_Slots[i].EnableInClassList("quick-slot-unavailable", bound && !available);
                m_Slots[i].tooltip = !bound ? MoonkovLocalization.Text("Hover an item in your inventory and press this number, or drag it here.") :
                    m_Names[i].text + " / " + MoonkovLocalization.Text(available ? "Right click to clear binding." : "Item unavailable. Equip it or bind another item.");
            }
            var selected = bindings.SelectedItem(graph);
            string selectedText = selected == null ? "" : MoonkovLocalization.Text(InventoryCatalog.Get(selected.Code).Name) + " ×" + selected.Quantity;
            bool showStatus = !string.IsNullOrEmpty(status);
            MoonkovLocalization.Set(m_Selected, selectedText);
            m_Selected.style.display = selected != null && (inventoryOpen || !showStatus) ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(m_Caption, inventoryOpen
                ? (showStatus ? MoonkovLocalization.Text(status) + " / " : "") + MoonkovLocalization.Text("Hover an item + 1–9 / Drag to bind / Right click to clear")
                : status ?? "");
            m_Caption.style.display = inventoryOpen || showStatus ? DisplayStyle.Flex : DisplayStyle.None;
        }
        public void Dispose() => m_PixelRoot.RemoveFromHierarchy();
    }
}
