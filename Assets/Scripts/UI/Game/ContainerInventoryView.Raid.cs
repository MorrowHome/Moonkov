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
        private static bool SameCorpseSlots(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
        {
            if (ReferenceEquals(before, after)) return true;
            if (before == null || after == null || before.Count != after.Count) return false;
            foreach (var pair in before)
                if (!after.TryGetValue(pair.Key, out var value) || pair.Value != value) return false;
            return true;
        }
        private PlayerGhost PortraitPlayer()
        {
            if (m_Stash || !PlayerGhostManager.TryGetClientInstance(out var manager) || !manager ||
                !manager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players)) return null;
            foreach (var player in players) if (player) return player;
            return null;
        }
        private bool TryOperatorPortraitSnapshot(out PredictedPlayerGhost snapshot)
        {
            snapshot = default;
            var player = PortraitPlayer();
            var ghost = player ? player.GhostGameObject : null;
            if (!ghost || ghost.World == null || !ghost.World.IsCreated ||
                !ghost.EntityManager.Exists(ghost.LinkedEntity) || !ghost.GhostHasComponent<PredictedPlayerGhost>()) return false;
            snapshot = ghost.ReadGhostComponentData<PredictedPlayerGhost>();
            return true;
        }
        private void RefreshPortraitWeapon()
        {
            if (!m_PortraitShowing || m_Disposed || m_Root.panel == null || m_Root.resolvedStyle.display == DisplayStyle.None) return;
            int characterIndex = OperatorPortraitCharacter();
            if (m_Character == null || m_Character.CharacterIndex != characterIndex) { PresentOperatorPortrait(); return; }
            bool hasSnapshot = TryOperatorPortraitSnapshot(out var snapshot);
            string item = hasSnapshot ? snapshot.EquippedWeaponItem.ToString() : null;
            if (item != m_PortraitWeaponItem)
            {
                m_PortraitWeaponItem = item;
                m_Character.SetEquipmentInventory(m_Graph, item);
            }
            if (hasSnapshot) m_Character.SetEquipmentSnapshot(in snapshot);
        }
        private void PortraitCharacterChanged(int characterIndex)
        {
            if (m_PortraitShowing && !m_Disposed) PresentOperatorPortrait();
        }
        private int OperatorPortraitCharacter()
        {
            var player = PortraitPlayer();
            if (player)
            {
                if (m_PortraitAvatars == null)
                {
                    m_PortraitAvatars = new Dictionary<Avatar, int>();
                    CacheAvatar(GameSettings.HatsuneMikuCharacterIndex);
                    CacheAvatar(GameSettings.ShianCharacterIndex);
                }
                var animator = player.GetComponentInChildren<Animator>(true);
                return animator && animator.avatar && m_PortraitAvatars.TryGetValue(animator.avatar, out int characterIndex)
                    ? characterIndex : GameSettings.DollSingerCharacterIndex;
            }
            return m_PortraitSettings?.PlayerCharacter ?? GameSettings.DollSingerCharacterIndex;

            void CacheAvatar(int characterIndex)
            {
                var settings = Resources.Load<MenuCharacterSettings>(GameSettings.MenuCharacterSettingsPath(characterIndex));
                var model = settings && settings.VisualPrefab ? settings.VisualPrefab.GetComponentInChildren<Animator>(true) : null;
                if (model && model.avatar) m_PortraitAvatars[model.avatar] = characterIndex;
            }
        }
        private void PresentOperatorPortrait()
        {
            int characterIndex = OperatorPortraitCharacter();
            bool livePortrait = GameSettings.IsPlayableCharacter(characterIndex);
            m_Root.EnableInClassList("inventory-live-operator", livePortrait);
            if (!livePortrait) { ReleaseOperatorPortrait(); return; }
            if (m_Character == null || m_Character.CharacterIndex != characterIndex)
            {
                ReleaseOperatorPortrait();
                m_Character = new MenuCharacterView(idleOnly: true, characterIndex: characterIndex);
                m_Character.Element.name = "operatorLivePortrait";
                m_Character.Element.RemoveFromClassList("terminal-character-art");
                m_Character.Element.AddToClassList("field-operator-live");
                m_CharacterStage.Insert(0, m_Character.Element);
            }
            bool hasSnapshot = TryOperatorPortraitSnapshot(out var snapshot);
            // An empty ID is empty hands; null means the deployment slot priority.
            m_PortraitWeaponItem = hasSnapshot ? snapshot.EquippedWeaponItem.ToString() : null;
            m_Character.SetEquipmentInventory(m_Graph, m_PortraitWeaponItem);
            if (hasSnapshot) m_Character.SetEquipmentSnapshot(in snapshot);
        }
        private void InitializeRaidPortrait()
        {
            m_Root.Q("slotLayer").pickingMode = PickingMode.Ignore;
            if (!m_Stash)
            {
                m_PortraitTimer = m_Root.schedule.Execute(RefreshPortraitWeapon).Every(150);
                m_PortraitTimer.Pause();
            }
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
                var art = new StashItemArt(item.Code); art.AddToClassList("field-container-art"); button.Add(art);
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
                var art = new StashItemArt(item.Code); art.AddToClassList("inventory-equipment-icon"); button.Q("art").Add(art);
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
            MoonkovLocalization.Set(title, RaidFurnitureLoot.IsFurniture(m_LootId) ? "抽屉搜刮" : corpse ? "尸体搜刮" : m_DroppedItem ? "地面物品" : m_LootId >= RaidLootContainers.FirstMissionCacheId ? "任务输出槽" : "物资容器");
            MoonkovLocalization.Set(caption, RaidFurnitureLoot.IsFurniture(m_LootId) ? RaidFurnitureLoot.NameFor(m_LootId) : "");
            if (!corpse) { AddContainer(m_LootPane, loot, "可搜刽物品"); return; }
            var tops = m_Graph.Children(loot.Id).ToArray(); var shown = new HashSet<string>();
            if (GameSettings.IsPlayableCharacter(m_CorpseCharacterIndex))
                Text(m_LootPane, GameSettings.CharacterName(m_CorpseCharacterIndex), "inventory-section-title");
            bool portraitKnown = GameSettings.IsPlayableCharacter(m_CorpseCharacterIndex);
            VisualElement equipment;
            if (portraitKnown)
            {
                var stage = TerminalLayout.Clone("Components/CorpsePortrait", "corpsePortrait"); m_LootPane.Add(stage);
                stage.Q("corpseOperatorPortrait").style.display = DisplayStyle.None;
                m_CorpseStage = stage;
                equipment = stage.Q("corpseEquipmentSlots");
                equipment.pickingMode = PickingMode.Ignore;
                BindCorpsePortrait(stage, equipment);
            }
            else { equipment = new VisualElement(); equipment.AddToClassList("field-corpse-equipment"); m_LootPane.Add(equipment); }
            foreach (string slot in new[] { "Helmet", "Shield", "Primary", "Secondary", "Pistol", "Melee" })
            {
                var item = CorpseEquipment(tops, slot, shown);
                if (item != null) shown.Add(item.Id);
                var card = (Button)TerminalLayout.Clone(m_SlotTemplate, "equipmentSlot");
                card.name = "corpseSlot" + slot; card.RemoveFromClassList("inventory-slot"); card.AddToClassList("field-corpse-slot");
                if (portraitKnown) card.AddToClassList("field-corpse-body-slot");
                MoonkovLocalization.Set(card.Q<Label>("slotLabel"), SlotLabel(slot));
                MoonkovLocalization.Set(card.Q<Label>("item"), item == null ? "EMPTY" : InventoryCatalog.Get(item.Code).Name);
                if (item == null) { card.AddToClassList("field-corpse-empty"); card.SetEnabled(false); }
                else
                {
                    var captured = item;
                    var art = new StashItemArt(item.Code); art.AddToClassList("inventory-equipment-icon"); card.Q("art").Add(art);
                    card.RemoveManipulator(card.clickable);
                    card.AddManipulator(new Drag(this, () => m_Graph.Find(captured.Id), () => Describe(captured),
                        () => { if (InventoryCatalog.Get(captured.Code).Container) OpenContainer(captured); else Describe(captured); }));
                    card.tooltip = MoonkovLocalization.Text(InventoryCatalog.Get(item.Code).Name) + " · 拖动取走／Ctrl 点击转移";
                    m_Tiles[item.Id] = card;
                }
                equipment.Add(card);
            }
            foreach (string slot in new[] { "ChestRig", "Backpack" })
            {
                var item = CorpseEquipment(tops, slot, shown); if (item != null) shown.Add(item.Id);
                AddContainer(m_LootPane, item, slot == "ChestRig" ? "胸挂" : "背包", corpseSlot: slot);
            }
            AddContainer(m_LootPane, loot, "随身散装物品", shown, true);
        }
        private void PresentCorpsePortrait()
        {
            if (m_CorpseStage == null || !GameSettings.IsPlayableCharacter(m_CorpseCharacterIndex)) return;
            if (m_CorpseCharacter == null)
            {
                m_CorpseCharacter = new MenuCharacterView(idleOnly: true, characterIndex: m_CorpseCharacterIndex);
                m_CorpseCharacter.Element.name = "corpseLivePortrait";
                m_CorpseCharacter.Element.RemoveFromClassList("terminal-character-art");
                m_CorpseCharacter.Element.AddToClassList("field-operator-live");
                m_CorpseStage.Insert(0, m_CorpseCharacter.Element);
            }
            m_CorpseCharacter.SetEquipmentInventory(CorpsePortraitInventory());
        }
        private InventoryGraph CorpsePortraitInventory()
        {
            var graph = InventoryGraph.Create(false, false);
            if (m_Graph == null) return graph;
            graph.Version = m_Graph.Version;
            var tops = m_Graph.Children(LootInventoryExchange.Root).ToArray();
            var selected = new HashSet<string>();
            foreach (var slot in InventoryCatalog.Get(InventoryCatalog.Equipment).Regions)
            {
                var item = CorpseEquipment(tops, slot.Id, selected);
                if (item == null) continue;
                selected.Add(item.Id);
                var copy = item.Clone(); copy.Parent = InventoryCatalog.Equipment; copy.Region = slot.Id;
                copy.X = copy.Y = 0; copy.Rotated = false;
                graph.Items.Add(copy);
            }
            for (int depth = 0; depth < InventoryCatalog.MaxDepth; depth++)
            {
                bool added = false;
                foreach (var item in m_Graph.Items)
                    if (item.Parent != null && selected.Contains(item.Parent) && selected.Add(item.Id))
                    { graph.Items.Add(item.Clone()); added = true; }
                if (!added) break;
            }
            return graph;
        }
        private void ReleaseCorpsePortrait()
        {
            if (m_CorpseCharacter == null) return;
            m_CorpseCharacter.Element.RemoveFromHierarchy(); m_CorpseCharacter.Dispose(); m_CorpseCharacter = null;
        }
        private InventoryItem CorpseEquipment(InventoryItem[] tops, string slot, HashSet<string> shown)
        {
            var kind = InventoryCatalog.Get(InventoryCatalog.Equipment).Regions.First(r => r.Id == slot).SlotKind;
            if (m_CorpseEquipmentSlots != null)
                return m_CorpseEquipmentSlots.TryGetValue(slot, out var id) ? tops.FirstOrDefault(i => i.Id == id && InventoryCatalog.Get(i.Code).Kind == kind) : null;
            return tops.FirstOrDefault(i => !shown.Contains(i.Id) && InventoryCatalog.Get(i.Code).Kind == kind);
        }
        private void AddCorpseContainerSlot(VisualElement group, InventoryItem item, string slot)
        {
            var button = new Button { name = "corpseSlot" + slot }; button.AddToClassList("inventory-empty-container");
            if (item == null) { button.text = "空"; button.SetEnabled(false); }
            else
            {
                var art = new StashItemArt(item.Code); art.AddToClassList("field-container-art"); button.Add(art);
                button.tooltip = MoonkovItemPresentation.Details(item.Code); m_Tiles[item.Id] = button;
                button.RemoveManipulator(button.clickable);
                button.AddManipulator(new Drag(this, () => m_Graph.Find(item.Id), () => Describe(item), () => OpenContainer(item)));
            }
            var body = new VisualElement(); body.AddToClassList("field-container-body");
            var regions = group.Q("containerRegions"); regions.RemoveFromHierarchy(); body.Add(button); body.Add(regions); group.Add(body);
        }
        private static void BindCorpsePortrait(VisualElement stage, VisualElement equipment)
        {
            var layout = JsonUtility.FromJson<PortraitLayout>(Resources.Load<TextAsset>("Moonkov/UI/FieldOperatorPortraitLayout").text);
            stage.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float width = stage.contentRect.width, height = stage.contentRect.height;
                float imageWidth = Mathf.Min(width, height * layout.aspect), imageHeight = imageWidth / layout.aspect;
                var origin = new Vector2((width - imageWidth) * .5f, (height - imageHeight) * .5f);
                const float cardWidth = 108, cardHeight = 62; float[] lastBottom = { -4, -4 };
                foreach (var anchor in layout.anchors)
                {
                    var button = equipment.Q<Button>("corpseSlot" + anchor.slot); if (button == null) continue;
                    int lane = anchor.x < .5f ? 0 : 1;
                    float top = Mathf.Max(Mathf.Clamp(origin.y + anchor.y * imageHeight - cardHeight * .5f, 0, height - cardHeight), lastBottom[lane] + 4);
                    button.style.left = Mathf.Clamp(origin.x + anchor.x * imageWidth - cardWidth * .5f, 0, width - cardWidth);
                    button.style.top = top; button.style.width = cardWidth; button.style.height = cardHeight; lastBottom[lane] = top + cardHeight;
                }
            });
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
