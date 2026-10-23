using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class ContainerInventoryView
    {
        [Serializable] private sealed class PortraitAnchor { public string slot; public float x, y; }
        [Serializable] private sealed class PortraitLayout { public PortraitAnchor[] anchors; public float aspect; }
        private void InitializeRaidPortrait()
        {
            var layout = JsonUtility.FromJson<PortraitLayout>(Resources.Load<TextAsset>("Moonkov/UI/FieldOperatorPortraitLayout").text);
            m_CharacterStage.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float width = m_CharacterStage.contentRect.width, height = m_CharacterStage.contentRect.height;
                float imageWidth = Mathf.Min(width, height * layout.aspect), imageHeight = imageWidth / layout.aspect;
                var origin = new Vector2((width - imageWidth) * .5f, (height - imageHeight) * .5f);
                float cardWidth = Mathf.Min(Mathf.Clamp(width * .36f, 96, 132), (width - 12) * .5f), cardHeight = 78;
                float[] lastBottom = { -8, -8 };
                foreach (var anchor in layout.anchors)
                {
                    var button = m_Slots[(LoadoutSlot)Enum.Parse(typeof(LoadoutSlot), anchor.slot)];
                    button.style.width = cardWidth; button.style.height = cardHeight;
                    int lane = anchor.x < .5f ? 0 : 1;
                    button.style.left = lane == 0 ? 0 : width - cardWidth;
                    float top = Mathf.Clamp(origin.y + anchor.y * imageHeight - cardHeight * .5f, 0, Mathf.Max(0, height - cardHeight));
                    top = Mathf.Max(top, lastBottom[lane] + 8);
                    button.style.top = top; lastBottom[lane] = top + cardHeight;
                }
            });
        }
        private void AddContainerEquipmentSlot(VisualElement group, InventoryItem item, string slot)
        {
            var button = new Button { name = "inventorySlot" + slot };
            button.AddToClassList("inventory-empty-container");
            if (item == null) button.text = "空";
            else
            {
                var art = new StashItemArt(Art(item.Code)); art.AddToClassList("field-container-art"); button.Add(art);
                m_Tiles[item.Id] = button;
            }
            string title = slot == "ChestRig" ? "胸挂" : slot == "Backpack" ? "背包" : "保险箱";
            button.tooltip = item == null ? title + "：拖入兼容装备" : MoonkovItemPresentation.Details(item.Code);
            var body = new VisualElement(); body.AddToClassList("field-container-body");
            var regions = group.Q("containerRegions"); regions.RemoveFromHierarchy();
            body.Add(button); body.Add(regions); group.Add(body);
            button.RemoveManipulator(button.clickable);
            button.AddManipulator(new Drag(this, () => m_Graph?.Equipped(slot), () => ChooseSlot(slot), () => ChooseSlot(slot)));
            m_Grids.Add(new Grid { Parent = InventoryCatalog.Equipment, Region = slot, Element = button,
                Definition = InventoryCatalog.Get(InventoryCatalog.Equipment).Regions.First(r => r.Id == slot) });
        }
        private void AddSpecialEquipment()
        {
            var group = new VisualElement(); group.AddToClassList("inventory-container"); m_ContainerPane.Add(group);
            Text(group, "特殊装备", "inventory-section-title");
            var row = new VisualElement(); row.AddToClassList("field-special-row"); group.Add(row);
            for (int n = 1; n <= 3; n++)
            {
                string slot = "Special" + n;
                var button = (Button)TerminalLayout.Clone(m_SlotTemplate, "equipmentSlot");
                button.name = "inventorySlot" + slot; button.RemoveFromClassList("inventory-slot"); button.AddToClassList("field-special-slot");
                TextSpecialSlot(button, slot, n);
                button.RemoveManipulator(button.clickable);
                button.AddManipulator(new Drag(this, () => m_Graph?.Equipped(slot), () => ChooseSlot(slot), () => ChooseSlot(slot)));
                row.Add(button);
                m_Grids.Add(new Grid { Parent = InventoryCatalog.Equipment, Region = slot, Element = button,
                    Definition = InventoryCatalog.Get(InventoryCatalog.Equipment).Regions.First(r => r.Id == slot) });
            }
        }
        private void TextSpecialSlot(Button button, string slot, int n)
        {
            var item = m_Graph?.Equipped(slot);
            MoonkovLocalization.Set(button.Q<Label>("slotLabel"), "工具 " + n);
            MoonkovLocalization.Set(button.Q<Label>("item"), item == null ? "空工具槽" : InventoryCatalog.Get(item.Code).Name);
            if (item == null) button.tooltip = "拖入指南针或测距仪";
            else
            {
                var art = new StashItemArt(Art(item.Code)); art.AddToClassList("inventory-equipment-icon"); button.Q("art").Add(art);
                button.tooltip = MoonkovItemPresentation.Details(item.Code); m_Tiles[item.Id] = button;
            }
        }
        private void RenderRaidLoot(InventoryItem loot)
        {
            var title = m_Root.Q<Label>("lootTitle"); var caption = m_Root.Q<Label>("lootCaption");
            if (loot == null)
            {
                MoonkovLocalization.Set(title, "搜刮目标");
                MoonkovLocalization.Set(caption, m_ReadOnlyReason ?? "靠近容器或尸体，按 E 搜索");
                var empty = new VisualElement(); empty.AddToClassList("field-empty"); m_LootPane.Add(empty);
                Text(empty, m_Graph == null ? "正在读取随身物品" : "尚未打开搜刮目标", "field-empty-title");
                Text(empty, "自己的装备和容器始终显示在左侧。\n搜刮目标打开后，物品会显示在这里。", "field-empty-copy");
                return;
            }
            bool corpse = !m_DroppedItem && m_LootId >= RaidLootContainers.FirstDeathBagId && m_LootId < RaidLootContainers.FirstMissionCacheId;
            MoonkovLocalization.Set(title, corpse ? "尸体搜刮" : m_DroppedItem ? "地面物品" : m_LootId >= RaidLootContainers.FirstMissionCacheId ? "任务输出槽" : "物资容器");
            MoonkovLocalization.Set(caption, "");
            if (!corpse) { AddContainer(m_LootPane, loot, "可搜刽物品"); return; }
            var tops = m_Graph.Children(loot.Id).ToArray(); var shown = new HashSet<string>();
            var equipment = new VisualElement(); equipment.AddToClassList("field-corpse-equipment"); m_LootPane.Add(equipment);
            foreach (string slot in new[] { "Helmet", "Shield", "Primary", "Secondary", "Pistol", "Melee", "ChestRig", "Backpack" })
            {
                var kind = InventoryCatalog.Get(InventoryCatalog.Equipment).Regions.First(r => r.Id == slot).SlotKind;
                var item = tops.FirstOrDefault(i => !shown.Contains(i.Id) && InventoryCatalog.Get(i.Code).Kind == kind);
                if (item != null) shown.Add(item.Id);
                var card = (Button)TerminalLayout.Clone(m_SlotTemplate, "equipmentSlot");
                card.name = "corpseSlot" + slot; card.RemoveFromClassList("inventory-slot"); card.AddToClassList("field-corpse-slot");
                MoonkovLocalization.Set(card.Q<Label>("slotLabel"), SlotLabel(slot));
                MoonkovLocalization.Set(card.Q<Label>("item"), item == null ? "EMPTY" : InventoryCatalog.Get(item.Code).Name);
                if (item == null) { card.AddToClassList("field-corpse-empty"); card.SetEnabled(false); }
                else
                {
                    var captured = item;
                    var art = new StashItemArt(Art(item.Code)); art.AddToClassList("inventory-equipment-icon"); card.Q("art").Add(art);
                    card.RemoveManipulator(card.clickable);
                    card.AddManipulator(new Drag(this, () => m_Graph.Find(captured.Id), () => Describe(captured),
                        () => { if (InventoryCatalog.Get(captured.Code).Container) OpenContainer(captured); else Describe(captured); }));
                    card.tooltip = MoonkovLocalization.Text(InventoryCatalog.Get(item.Code).Name) + " · 拖动取走／Ctrl 点击转移";
                    m_Tiles[item.Id] = card;
                }
                equipment.Add(card);
            }
            foreach (var item in tops.Where(i => InventoryCatalog.Get(i.Code).Kind == ItemKind.Rig || InventoryCatalog.Get(i.Code).Kind == ItemKind.Backpack))
                AddContainer(m_LootPane, item, InventoryCatalog.Get(item.Code).Kind == ItemKind.Rig ? "胸挂内容" : "背包内容");
            AddContainer(m_LootPane, loot, "随身散装物品", shown, true);
        }
        // Death caches pack gear as loot. Compact only loose-item drawing; drop
        // intents still use real IDs and authoritative free positions.
        private static Dictionary<string, InventoryItem> CompactLoot(InventoryItem[] entries, int width)
        {
            var result = new Dictionary<string, InventoryItem>(); var occupied = new HashSet<Vector2Int>();
            foreach (var entry in entries)
            {
                var shown = entry.Clone(); var def = InventoryCatalog.Get(entry.Code);
                int w = entry.Rotated ? def.Height : def.Width, h = entry.Rotated ? def.Width : def.Height;
                bool found = false;
                for (int y = 0; !found; y++)
                    for (int x = 0; x <= width - w; x++)
                    {
                        bool free = true;
                        for (int row = 0; row < h && free; row++) for (int col = 0; col < w; col++)
                            if (occupied.Contains(new Vector2Int(x + col, y + row))) { free = false; break; }
                        if (!free) continue;
                        shown.X = x; shown.Y = y; found = true;
                        for (int row = 0; row < h; row++) for (int col = 0; col < w; col++) occupied.Add(new Vector2Int(x + col, y + row));
                        break;
                    }
                result.Add(shown.Id, shown);
            }
            return result;
        }
        private static bool VisibleDropTarget(VisualElement element, Vector2 point)
        {
            if (element.panel == null || !element.enabledInHierarchy || !element.worldBound.Contains(point)) return false;
            for (var ancestor = element; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.resolvedStyle.display == DisplayStyle.None || ancestor.resolvedStyle.visibility != Visibility.Visible ||
                    ancestor is ScrollView scroll && !scroll.contentViewport.worldBound.Contains(point)) return false;
            return true;
        }
    }
}
