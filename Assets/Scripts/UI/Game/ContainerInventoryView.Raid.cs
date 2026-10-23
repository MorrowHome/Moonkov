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
            if (item == null) button.text = slot == "ChestRig" ? "空胸挂槽 · 拖入胸挂以装备" : slot == "Backpack" ? "空背包槽 · 拖入背包以装备" : "空保险箱槽 · 拖入保险箱以装备";
            else
            {
                var art = new StashItemArt(Art(item.Code)); art.AddToClassList("field-container-art"); button.Add(art);
                Text(button, MoonkovLocalization.Text(InventoryCatalog.Get(item.Code).Name) + " · 拖动可取下", "field-container-name");
                m_Tiles[item.Id] = button;
            }
            group.Insert(1, button); button.RemoveManipulator(button.clickable);
            if (slot == "SecureContainer") Text(group, "装备后死亡保留箱内物品 · 不可放入武器、穿戴装备或任务产物", "inventory-caption");
            button.AddManipulator(new Drag(this, () => m_Graph?.Equipped(slot), () => ChooseSlot(slot), () => ChooseSlot(slot)));
            m_Grids.Add(new Grid { Parent = InventoryCatalog.Equipment, Region = slot, Element = button,
                Definition = InventoryCatalog.Get(InventoryCatalog.Equipment).Regions.First(r => r.Id == slot) });
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
            MoonkovLocalization.Set(caption, corpse ? "遗留装备与容器内容 · Ctrl／Shift 点击取走" : "拖到自己的容器，或 Ctrl／Shift 点击转移");
            if (!corpse) { AddContainer(m_LootPane, loot, "可搜刽物品"); return; }
            var tops = m_Graph.Children(loot.Id).ToArray(); var shown = new HashSet<string>();
            Text(m_LootPane, "遗留装备", "inventory-section-title");
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
            Text(m_LootPane, "拖入散装区域可放回尸体，系统自动寻找空位。", "field-pane-note");
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
