// 仓库和战局背包共用的格子界面。它把 InventoryGraph 的容器、物品和槽位画出来，把玩家选中的落位作为 InventoryCommand 交给调用方。
// 菜单操作走 StashScreen.Containers，局内操作走 RaidHUD。想看格子、重量和容器能不能放，从 InventoryGraph.TryApply 往下读。
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client {
    // 用已确认的库存快照显示。拖动物品只提交移动意图，库存数量由处理方更新。
    public sealed partial class ContainerInventoryView : IDisposable {
        // 仓储区域共用一个逻辑尺寸比例，不受内容与布局次数影响。
        private const float StorageCellSize = 64;

        private sealed class Grid {
            public string Parent, Region;
            public InventoryRegion Definition;
            public VisualElement Element;
            public float Cell = StorageCellSize;
            public bool AutoPlace;
        }

        private readonly VisualElement m_Root,
            m_Body,
            m_CharacterStage,
            m_CharacterPane,
            m_LootPane,
            m_ContainerPane,
            m_StashPane,
            m_Ghost,
            m_Preview;

        private bool m_HaloCargoOpen;
        private readonly Label m_Weight, m_Message;
        private readonly Action<InventoryCommand> m_Send;
        private readonly Action<InventoryItem> m_UseMedical;
        private readonly Action<InventoryItem> m_Drop;
        private readonly Action<InventoryItem> m_UseConsumable;
        private readonly TerminalWindows m_Windows;
        private readonly VisualTreeAsset m_ItemTemplate, m_ContainerTemplate, m_SlotTemplate;
        private readonly bool m_Stash;
        private readonly List<Grid> m_Grids = new();
        private readonly Dictionary<LoadoutSlot, Button> m_Slots = new();
        private readonly Dictionary<string, VisualElement> m_Tiles = new();
        private readonly HashSet<string> m_Open = new();
        private MenuCharacterView m_Character, m_CorpseCharacter;
        private VisualElement m_CorpseStage;
        private IVisualElementScheduledItem m_PortraitTimer;
        private string m_PortraitWeaponItem;
        private readonly GameSettings m_PortraitSettings;
        private Dictionary<Avatar, int> m_PortraitAvatars;
        private bool m_PortraitShowing;
        private InventoryGraph m_Graph;
        private Drag m_Drag;
        private bool m_Busy, m_Disposed, m_Rebuilding;
        private SoundDef m_OperationSound;

        private sealed class QuickTransferIntent {
            public string ItemId;
            public bool FromCarried;
        }

        private readonly Queue<QuickTransferIntent> m_QuickTransfers = new();
        private IVisualElementScheduledItem m_QuickTransferPump;
        private bool m_ProcessingQuickTransfer, m_QuickTransferSpaceBlocked;
        private string m_ReadOnlyReason;
        private int m_SearchTotal = -1;
        private IReadOnlyList<RaidSearchFootprint> m_SearchFootprints;
        private string m_SelectedItemId;
        private int m_LootId = -1;
        private bool m_DroppedItem;
        private int m_CorpseCharacterIndex = -1;
        private IReadOnlyDictionary<string, string> m_CorpseEquipmentSlots;
        private int m_RenderedVersion = int.MinValue;
        private string m_RenderedLayout, m_DeferredLayout;
        private IVisualElementScheduledItem m_DeferredRender;
        private int m_EscapeHandledFrame = -1;
        public VisualElement Element => m_Root;
        public bool Busy => m_Busy;
        private string m_HoveredItemId;
        public InventoryItem QuickSlotCandidate => m_Graph?.Find(m_HoveredItemId) ?? m_Graph?.Find(m_SelectedItemId);
        public Func<InventoryItem, Vector2, bool> QuickSlotDrop { get; set; }
        public bool OperationAudioEnabled { get; set; } = true;
        public Func<InventoryItem, bool> ShieldRechargeAvailable { get; set; }
        public Action<InventoryItem, Vector2> QuickSlotDragPreview { get; set; }

        public void SetReadOnly(string reason) {
            if (m_ReadOnlyReason == reason) return;
            if (reason != null) ClearQuickTransfers();
            CancelDrag();
            m_ReadOnlyReason = reason;
            MoonkovLocalization.Set(m_Message, reason ?? "");
            RefreshWorkshop();
        }

        public ContainerInventoryView(
            VisualElement parent,
            bool showStash,
            Action<InventoryCommand> send,
            Action<InventoryItem> useMedical = null,
            Action<InventoryItem> useConsumable = null,
            Action<InventoryItem> drop = null) {
            m_Stash = showStash;
            m_Send = send;
            m_UseMedical = useMedical;
            m_UseConsumable = useConsumable;
            m_Drop = drop;
            m_Root = TerminalLayout.Clone("RaidContainerInventory", "containerInventory");
            m_Root.EnableInClassList("inventory-warehouse", showStash);
            m_Root.RemoveFromClassList("moonkov-controls-light");
            m_Root.AddToClassList("archive-ui");
            m_Root.RemoveFromClassList("archive-light");
            m_Root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/ContainerUI"));
            m_Root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/InventoryTheme"));
            parent.Add(m_Root);
            m_Root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/RaidInventory"));
            m_ItemTemplate = Resources.Load<VisualTreeAsset>("Moonkov/UI/Components/InventoryItem");
            m_ContainerTemplate = Resources.Load<VisualTreeAsset>("Moonkov/UI/Components/InventoryContainer");
            m_SlotTemplate = Resources.Load<VisualTreeAsset>("Moonkov/UI/Components/EquipmentSlot");
            m_Body = m_Root.Q<VisualElement>("inventoryBody");
            m_LootPane = m_Root.Q<VisualElement>("lootPane");
            m_CharacterPane = m_Root.Q<VisualElement>("characterPane");
            m_CharacterStage = m_Root.Q<VisualElement>("characterStage");
            string[] slots = { "Helmet", "Shield", "ChestRig", "Pistol", "Backpack", "Primary", "Secondary", "Melee" };
            for (int n = 0; n < slots.Length; n++) {
                string slot = slots[n];
                if (slot == "ChestRig" || slot == "Backpack") continue;
                var button = m_Root.Q<Button>("inventorySlot" + slot) ??
                             (Button)TerminalLayout.Clone(m_SlotTemplate, "equipmentSlot");
                button.name = "inventorySlot" + slot;
                button.AddToClassList("inventory-slot-" + n);
                MoonkovLocalization.Set(button.Q<Label>("slotLabel"), SlotLabel(slot));
                if (button.parent == null) m_Root.Q("slotLayer").Add(button);
                m_Slots.Add((LoadoutSlot)Enum.Parse(typeof(LoadoutSlot), slot), button);
                button.RemoveManipulator(button.clickable);
                button.AddManipulator(new Drag(this, () => m_Graph?.Equipped(slot), () => ChooseSlot(slot), () => {
                    var item = m_Graph?.Equipped(slot);
                    if (item != null && InventoryCatalog.Get(item.Code).Container)
                        OpenContainer(item);
                    else
                        ChooseSlot(slot);
                }));
                m_Grids.Add(new Grid {
                    Parent = "equipment", Region = slot, Element = button,
                    Definition = InventoryCatalog.Get("equipment").Regions.First(r => r.Id == slot)
                });
            }
            m_Weight = m_Root.Q<Label>("inventoryWeight");
            m_ContainerPane = m_Root.Q<ScrollView>("containerPane");
            var stashPlaceholder = m_Root.Q<VisualElement>("stashPane");
            // 仓库模式把右栏留给个人仓储，局内模式才用它画搜刮目标。两者共用中间的随身容器和左边的装备槽。
            m_StashPane = showStash ? m_LootPane : stashPlaceholder;
            stashPlaceholder.RemoveFromHierarchy();
            if (showStash) {
                var workshop = new Button(OpenWorkshop) { name = "inventoryHaloWorkshop" };
                workshop.AddToClassList("inventory-halo-workshop");
                MoonkovLocalization.Set(workshop, "HALO MODIFICATION");
                m_CharacterPane.Insert(1, workshop);
                MoonkovLocalization.Set(m_Root.Q<Label>("lootTitle"), "个人仓库");
                MoonkovLocalization.Set(m_Root.Q<Label>("lootCaption"), "拖动整理 · 双击容器打开 · Ctrl／Shift 点击转移");
                m_Root.Q("inventoryQuickbarDock").style.display = DisplayStyle.None;
            }
            m_Message = m_Root.Q<Label>("inventoryHint");
            m_Ghost = m_Root.Q<VisualElement>("inventoryGhost");
            m_Preview = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Preview.AddToClassList("inventory-drop");
            m_Preview.style.display = DisplayStyle.None;
            m_Root.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            Application.focusChanged += FocusChanged;
            m_Windows = new TerminalWindows(m_Root, null);
            InitializeItemTooltip();
            InitializeRaidPortrait();
            Present(null);
            m_PortraitSettings = GameSettings.Instance;
            if (m_PortraitSettings != null) m_PortraitSettings.PlayerCharacterChanged += PortraitCharacterChanged;
        }

        private static Label Text(VisualElement parent, string text, string css) {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(css);
            parent.Add(label);
            MoonkovLocalization.Set(label, text);
            return label;
        }

        private static string SlotLabel(string slot) {
            if (slot.StartsWith("Special", StringComparison.Ordinal)) return "特殊装备 " + slot.Substring(7);
            return slot switch {
                "SecureContainer" => "保险箱",
                "Helmet" => "COLLAR",
                "Shield" => "RETIRED EQUIPMENT",
                "Melee" => "MELEE",
                "ChestRig" => "CHEST BADGE",
                "Primary" => "PRIMARY",
                "Secondary" => "SECONDARY",
                _ => slot.ToUpperInvariant()
            };
        }

        public static StashArtKind Art(string code) => StashItemArt.KindFor(code);

        // 收到新数据时，先区分是格子布局变了，还是电量、数量等值变了。后者原地更新，拖动中的按钮不会被删掉。
        public void Present(
            InventoryGraph graph,
            string message = null,
            bool operationCompleted = true,
            int lootId = -1,
            bool droppedItem = false,
            int corpseCharacterIndex = -1,
            IReadOnlyDictionary<string, string> corpseEquipmentSlots = null,
            int searchTotal = -1,
            IReadOnlyList<RaidSearchFootprint> searchFootprints = null) {
            if (m_Disposed) return;
            // 更换箱子、库存消失或上一步失败时，取消还没发的连续转移。剩下的物品不能按旧场景继续搬。
            if (graph == null || m_LootId != lootId || (operationCompleted && message != null)) ClearQuickTransfers();
            if (OperationAudioEnabled && m_Busy && operationCompleted) {
                if (message != null)
                    MoonkovAudio.Error();
                else
                    MoonkovAudio.Play(m_OperationSound ? m_OperationSound : MoonkovAudio.Library?.Equipment,
                        Vector3.zero);
            }
            if (operationCompleted) m_OperationSound = null;
            string layout = LayoutKey(graph);
            if (searchFootprints != null)
                layout += "/search/" + string.Join(";",
                    searchFootprints.Select(f => $"{f.Id}:{f.Region}:{f.X}:{f.Y}:{f.Width}:{f.Height}"));
            bool corpseSlotsChanged = !SameCorpseSlots(m_CorpseEquipmentSlots, corpseEquipmentSlots);
            bool changed = m_SearchTotal != searchTotal || m_RenderedVersion == int.MinValue ||
                           m_RenderedLayout != layout || m_LootId != lootId || m_DroppedItem != droppedItem ||
                           m_CorpseCharacterIndex != corpseCharacterIndex || corpseSlotsChanged;
            bool workshopChanged = changed || m_RenderedVersion != (graph?.Version ?? -1) ||
                                   (operationCompleted && m_Busy) || message != null;
            m_SearchTotal = searchTotal;
            m_SearchFootprints = searchFootprints;
            m_Graph = graph;
            m_LootId = lootId;
            m_DroppedItem = droppedItem;
            if (m_Slots.TryGetValue(LoadoutSlot.Shield, out var retiredSlot))
                retiredSlot.style.display = graph?.Equipped("Shield") != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_CorpseCharacterIndex = corpseCharacterIndex;
            m_CorpseEquipmentSlots = corpseEquipmentSlots == null
                ? null
                : corpseEquipmentSlots.ToDictionary(pair => pair.Key, pair => pair.Value);
            // 只有处理方确认本次操作结束，界面才解除忙碌。搜索揭示等快照还可以在等待搬运期间刷新显示。
            if (operationCompleted) m_Busy = false;
            // 搜刮揭示新物品时，手里那件还在原位就保持拖动。等松手再摆好剩下的格子。
            if (changed && m_Drag != null && m_Drag.CanRetain(graph)) {
                // 记住下次要重画的布局。这一帧只刷新数值，让手上被抓住的按钮继续接收松手事件。
                m_DeferredLayout = layout;
                RefreshSnapshotValues();
            } else if (changed) {
                m_DeferredLayout = null;
                CancelDrag();
                Render();
                m_RenderedLayout = layout;
            // 坐标和父容器都一样时，不需要重建视图。即使版本号变化，也可直接更新数量、电量和提示。
            } else {
                m_DeferredLayout = null;
                RefreshSnapshotValues();
            }
            m_RenderedVersion = graph?.Version ?? -1;
            if (m_PortraitShowing) {
                PresentOperatorPortrait();
                PresentCorpsePortrait();
            }
            if (message != null)
                MoonkovLocalization.Set(m_Message, message);
            else if (!m_Busy && graph != null) MoonkovLocalization.Set(m_Message, m_ReadOnlyReason ?? "");
            if (operationCompleted) m_WorkshopMessage = message;
            if (workshopChanged) RefreshWorkshop();
            if (operationCompleted && !m_Busy && m_QuickTransfers.Count > 0) {
                // 等 HUD 处理完当前回执再继续，
                // 因为同一轮更新可能重复展示快照，或关闭搜刮容器。
                m_QuickTransferPump?.Pause();
                // 每次回执过一轮界面更新再发下一件，避免同一份快照重绘两次时连着发两个请求。
                m_QuickTransferPump = m_Root.schedule.Execute(ProcessQuickTransfers);
            }
        }

        public void Show() {
            if (m_Disposed) return;
            m_PortraitShowing = true;
            PresentOperatorPortrait();
            PresentCorpsePortrait();
            m_PortraitTimer?.Resume();
        }

        public void Suspend() {
            HideItemTooltip();
            ClearQuickTransfers();
            m_PortraitShowing = false;
            m_PortraitTimer?.Pause();
            ReleaseWorkshop();
            CancelDrag();
            m_HoveredItemId = m_SelectedItemId = null;
            m_Windows.CloseAll();
            m_Open.Clear();
            ReleaseOperatorPortrait();
            ReleaseCorpsePortrait();
        }

        // 仓库和背包的角色小窗都有自己的预览场景。窗口隐藏后会把它们释放，再打开时根据当前状态重建。
        private void ReleaseOperatorPortrait() {
            if (m_Character == null) return;
            m_Character.Element.RemoveFromHierarchy();
            m_Character.Dispose();
            m_Character = null;
            m_PortraitWeaponItem = null;
        }

        private void Render() {
            HideItemTooltip();
            ReleaseCorpsePortrait();
            m_CorpseStage = null;
            m_HoveredItemId = null;
            m_Grids.RemoveAll(g => !m_Slots.Values.Any(s => ReferenceEquals(s, g.Element)));
            m_Tiles.Clear();
            m_ContainerPane.Clear();
            m_StashPane.Clear();
            m_LootPane.Clear();
            var loot = m_Graph?.Find(LootInventoryExchange.Root);
            m_Root.EnableInClassList("inventory-with-loot", loot != null);
            m_LootPane.style.display = DisplayStyle.Flex;
            m_CharacterPane.style.display = DisplayStyle.Flex;
            m_Rebuilding = true;
            m_Windows.CloseAll();
            m_Rebuilding = false;
            foreach (var pair in m_Slots) {
                var item = m_Graph?.Equipped(pair.Key.ToString());
                var button = pair.Value;
                var art = button.Q<VisualElement>("art");
                art.Clear();
                MoonkovItemPresentation.ApplyCategory(button, item?.Code);
                MoonkovLocalization.Set(button.Q<Label>("item"),
                    m_Graph == null ? "LOADING" : item == null ? "EMPTY" : InventoryCatalog.Get(item.Code).Name);
                if (item != null) {
                    var icon = new StashItemArt(item.Code);
                    icon.AddToClassList("inventory-equipment-icon");
                    art.Add(icon);
                    m_Tiles[item.Id] = button;
                }
                button.tooltip = MoonkovLocalization.Text(SlotLabel(pair.Key.ToString())) + " / " + (item == null
                    ? MoonkovLocalization.Text("EMPTY")
                    : MoonkovLocalization.Text(InventoryCatalog.Get(item.Code).Name) + "\n" +
                      MoonkovItemPresentation.ItemState(item));
                button.EnableInClassList("inventory-equipped", item != null);
                button.EnableInClassList("inventory-selected", item != null && item.Id == m_SelectedItemId);
            }
            // 数据还没回来时先画空容器占位，看到名字不代表玩家真的带了这些装备。
            if (m_Graph == null) {
                MoonkovLocalization.Set(m_Weight, "Waiting for inventory snapshot");
                MoonkovLocalization.Set(m_Message, "Loading carried inventory from the server…");
                AddContainer(m_ContainerPane, null, "POCKETS");
                AddContainer(m_ContainerPane, null, "CHEST BADGE");
                AddContainer(m_ContainerPane, null, "BACKPACK");
                if (m_Stash) AddContainer(m_StashPane, null, "PERSONAL STORAGE");
                if (!m_Stash) RenderRaidLoot(null);
                return;
            }
            RefreshWeight();
            AddContainer(m_ContainerPane, m_Graph.Equipped("ChestRig"), "CHEST BADGE");
            AddContainer(m_ContainerPane, m_Graph.Find("pockets"), "POCKETS");
            AddSpecialEquipment();
            AddContainer(m_ContainerPane, m_Graph.Equipped("Backpack"), "BACKPACK");
            AddContainer(m_ContainerPane, m_Graph.Equipped("SecureContainer"), "保险箱");
            if (!m_Stash) {
                // 只有玩家携带可放入光环的研究物资时，才提供光环收纳形态。若已经装入，优先展开装着它的那个光环。
                var cargo = m_Graph.Items.Find(item =>
                    InventoryCatalog.IsHaloResearchCargo(item.Code) && m_Graph.Carried(item));
                if (cargo != null) {
                    var halo = m_Graph.Children(InventoryCatalog.Equipment)
                        .Where(item => InventoryCatalog.CanStoreHaloCargo(item.Code))
                        .OrderByDescending(item => item.Id == cargo.Parent).FirstOrDefault();
                    if (halo != null) {
                        bool loaded = cargo.Region == InventoryCatalog.HaloCargoRegion.Id;
                        if (loaded || m_HaloCargoOpen) {
                            AddContainer(m_ContainerPane, halo, ResearchQuestCopy.Text("光环收纳形态", "Halo storage form"),
                                onlyRegion: InventoryCatalog.HaloCargoRegion);
                        } else {
                            var transform = new Button(() => {
                                m_HaloCargoOpen = true;
                                Render();
                            });
                            transform.AddToClassList("terminal-button");
                            MoonkovLocalization.Set(transform,
                                ResearchQuestCopy.Text("切换光环收纳形态", "Open Halo storage form"));
                            m_ContainerPane.Add(transform);
                        }
                    }
                } else { m_HaloCargoOpen = false; }
            }
            if (!m_Stash)
                RenderRaidLoot(loot);
            else if (loot != null)
                AddContainer(m_LootPane, loot,
                    RaidFurnitureLoot.IsFurniture(m_LootId) ? RaidFurnitureLoot.NameFor(m_LootId) :
                    m_LootId >= RaidLootContainers.FirstMissionCacheId ? "任务输出槽 / 任务产物" :
                    m_DroppedItem ? "地面丢弃物 / DROPPED SUPPLIES" :
                    m_LootId >= RaidLootContainers.FirstDeathBagId ? "FALLEN EXPEDITION / GEAR" :
                    "SUPPLY CACHE / " + (m_LootId + 1).ToString("00"));
            if (m_Stash) AddContainer(m_StashPane, m_Graph.Find("stash"), "PERSONAL STORAGE");
            // 重建会重新打开仍存在的已展开容器。复制一份编号列表来遍历，原列表可在过程中清掉已经消失的容器。
            foreach (string id in new List<string>(m_Open)) {
                var item = m_Graph.Find(id);
                if (item == null)
                    m_Open.Remove(id);
                else
                    OpenContainer(item, false);
            }
        }

        private void AddContainer(
            VisualElement pane,
            InventoryItem item,
            string title,
            HashSet<string> hidden = null,
            bool compact = false,
            string corpseSlot = null,
            InventoryRegion onlyRegion = null) {
            var group = TerminalLayout.Clone(m_ContainerTemplate, "inventoryContainer");
            pane.Add(group);
            MoonkovLocalization.Set(group.Q<Label>("containerTitle"), title);
            if (item?.Code == "stash" || (item?.Code == "loot" && (!compact || m_SearchTotal >= 0)))
                group.Q("containerTitle").style.display = DisplayStyle.None;
            if (pane == m_ContainerPane && (title == "CHEST BADGE" || title == "BACKPACK" || title == "保险箱"))
                AddContainerEquipmentSlot(group, item,
                    title == "CHEST BADGE" ? "ChestRig" : title == "BACKPACK" ? "Backpack" : "SecureContainer");
            if (corpseSlot != null) AddCorpseContainerSlot(group, item, corpseSlot);
            var caption = group.Q<Label>("containerCaption");
            if (item == null) {
                MoonkovLocalization.Set(caption, m_Graph == null ? "LOADING…" : "NOT EQUIPPED");
                return;
            }
            var def = InventoryCatalog.Get(item.Code);
            caption.style.display = DisplayStyle.None;
            var regionRow = group.Q("containerRegions");
            foreach (var region in onlyRegion == null ? def.Regions : new[] { onlyRegion }) {
                if (region.SlotKind.HasValue) continue;
                var wrapper = new VisualElement();
                wrapper.AddToClassList("inventory-region");
                regionRow.Add(wrapper);
                if (onlyRegion == null) Text(wrapper, region.Id.ToUpperInvariant(), "inventory-region-title");
                var gridParent = wrapper;
                var entries = m_Graph.Children(item.Id, region.Id).Where(e => hidden == null || !hidden.Contains(e.Id))
                    .ToArray();
                // 尸体等容器允许把物品在界面上排得更紧凑。这组坐标只负责画格子，鼠标落位还要按真正的库存规则找到可用位置。
                var displayed = compact ? CompactLoot(entries, region.Width) : entries.ToDictionary(e => e.Id, e => e);
                int rows = compact
                    ? Math.Max(1,
                        displayed.Values
                            .Select(e =>
                                e.Y + (e.Rotated
                                    ? InventoryCatalog.Get(e.Code).Width
                                    : InventoryCatalog.Get(e.Code).Height)).DefaultIfEmpty(1).Max())
                    : m_Graph.Rows(item.Id, region);
                var visual = new StashGridVisual(region.Width, rows)
                    { LineColor = m_Stash ? new Color(.28f, .30f, .36f, .6f) : new Color(.42f, .45f, .50f, .4f) };
                visual.AddToClassList("inventory-grid");
                visual.style.width = region.Width * StorageCellSize;
                visual.style.height = rows * StorageCellSize;
                gridParent.Add(visual);
                visual.name = "inventoryGrid" + item.Id + "-" + region.Id;
                var grid = new Grid {
                    Parent = item.Id, Region = region.Id, Definition = region, Element = visual, AutoPlace = compact
                };
                m_Grids.Add(grid);
                foreach (var entry in entries) {
                    var captured = entry;
                    var tile = (Button)TerminalLayout.Clone(m_ItemTemplate, "inventoryItem");
                    tile.name = "inventoryItem" + entry.Id;
                    MoonkovItemPresentation.ApplyCategory(tile, entry.Code);
                    var icon = new StashItemArt(entry.Code);
                    icon.AddToClassList("inventory-item-art");
                    tile.Insert(1, icon);
                    var itemDef = InventoryCatalog.Get(entry.Code);
                    string itemName = ResearchQuestCopy.ItemName(entry.Code) ?? MoonkovLocalization.Text(itemDef.Name);
                    MoonkovLocalization.Set(tile.Q<Label>("itemLabel"), itemName.Split(' ')[0].ToUpperInvariant());
                    MoonkovLocalization.Set(tile.Q<Label>("quantity"),
                        itemDef.Container ? "OPEN ↗" :
                        BatteryEnergy.IsCell(entry) ? $"{BatteryEnergy.Stored(entry)}E" : entry.Quantity.ToString());
                    tile.EnableInClassList("inventory-selected", entry.Id == m_SelectedItemId);
                    tile.tooltip = itemName + " / " + entry.Quantity +
                                   MoonkovLocalization.Text(entry.FoundInRaid ? " / FOUND IN RAID" : " / BROUGHT IN");
                    tile.tooltip += "\n" + MoonkovItemPresentation.Details(entry.Code);
                    string state = MoonkovItemPresentation.ItemState(entry);
                    if (!string.IsNullOrEmpty(state)) tile.tooltip += "\n" + state;
                    if (HaloModules.IsWeapon(entry.Code)) tile.tooltip += " / " + HaloModules.Summary(m_Graph, entry);
                    if (HaloModules.FromCode(entry.Code) != HaloModule.None)
                        tile.tooltip += " / " + MoonkovLocalization.Text(HaloModules.Description(entry.Code));
                    if (BatteryEnergy.IsCell(entry))
                        tile.tooltip += MoonkovLocalization.Format(
                            $" / {BatteryEnergy.Stored(entry)}/{entry.Quantity * BatteryEnergy.Capacity} ENERGY");
                    tile.RemoveManipulator(tile.clickable);
                    // 拖起时按物品 ID 从最新数据查一遍。建按钮时捕获的对象可能已经被后来的快照替换。
                    tile.AddManipulator(new Drag(this, () => m_Graph.Find(captured.Id), () => Describe(captured),
                        () => {
                            if (HaloModules.IsWeapon(captured.Code) && m_Stash)
                                OpenModification(captured);
                            else if (itemDef.Container)
                                OpenContainer(captured);
                            else
                                Describe(captured);
                        }));
                    visual.Add(tile);
                    m_Tiles[entry.Id] = tile;
                    Position(tile, displayed[entry.Id], grid.Cell);
                }
                // 未知物品只画快照给出的占格，已经揭示的由真实物品按钮表示。第一个还未知的格子高亮为当前搜索项。
                if (item.Id == LootInventoryExchange.Root && m_SearchFootprints != null) {
                    bool current = true;
                    foreach (var footprint in m_SearchFootprints) {
                        if (m_Graph.Find(footprint.Id) != null) continue;
                        var block = new VisualElement
                            { name = "inventoryUnknown" + footprint.Id, pickingMode = PickingMode.Ignore };
                        block.AddToClassList("field-search-hidden");
                        block.EnableInClassList("field-search-current", current);
                        current = false;
                        block.style.left = footprint.X * grid.Cell;
                        block.style.top = footprint.Y * grid.Cell;
                        block.style.width = footprint.Width * grid.Cell;
                        block.style.height = footprint.Height * grid.Cell;
                        visual.Add(block);
                    }
                }
            }
        }

        private static void Position(VisualElement tile, InventoryItem item, float cell) {
            var def = InventoryCatalog.Get(item.Code);
            tile.style.left = item.X * cell;
            tile.style.top = item.Y * cell;
            tile.style.width = (item.Rotated ? def.Height : def.Width) * cell;
            tile.style.height = (item.Rotated ? def.Width : def.Height) * cell;
        }

        internal void RevealStorageItem(string id) {
            var item = m_Graph?.Find(id);
            if (!m_Stash || item?.Parent != InventoryCatalog.Stash || m_Drag != null ||
                !m_Tiles.TryGetValue(id, out var tile))
                return;
            tile.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(tile);
            Describe(item);
        }

        private void Describe(InventoryItem item) {
            m_SelectedItemId = item.Id;
            foreach (var pair in m_Tiles) pair.Value.EnableInClassList("inventory-selected", pair.Key == item.Id);
            var def = InventoryCatalog.Get(item.Code);
            if (BatteryEnergy.IsCell(item))
                MoonkovLocalization.Set(m_Message,
                    "{0} · {1} · {2:0.0} KG · {3} · {4}/{5} ENERGY · ACTIVE CELL {6}/{7}", def.Name, item.Quantity,
                    def.Weight * item.Quantity, Path(item.Parent), BatteryEnergy.Stored(item),
                    item.Quantity * BatteryEnergy.Capacity, BatteryEnergy.Charge(item), BatteryEnergy.Capacity);
            else
                MoonkovLocalization.Set(m_Message, "{0} · ×{1} · {2:0.##} KG TOTAL · {3} · {4}",
                    ResearchQuestCopy.ItemName(item.Code) ?? def.Name, item.Quantity, def.Weight * item.Quantity,
                    Path(item.Parent), MoonkovItemPresentation.Details(item.Code));
        }

        private string Path(string id) {
            var parts = new List<string>();
            var item = m_Graph.Find(id);
            // 沿着 Parent 往外找，就能得到物品所在的多层容器路径。最大深度与库存规则一致。
            for (int n = 0; item != null && n < InventoryCatalog.MaxDepth; n++) {
                parts.Insert(0, InventoryCatalog.Get(item.Code).Name);
                item = m_Graph.Find(item.Parent);
            }
            return string.Join(" / ", parts.ConvertAll(MoonkovLocalization.Text));
        }

        private void OpenContainer(InventoryItem item, bool remember = true) {
            if (!InventoryCatalog.Get(item.Code).Container) return;
            // 每个容器只开一个浮窗。重画时可借用已记录的编号打开，真正关窗后会从记录里移除。
            if (remember && !m_Open.Add(item.Id)) return;
            var window = m_Windows.Open(InventoryCatalog.Get(item.Code).Name.ToUpperInvariant());
            window.AddToClassList("inventory-container-window");
            Text(window, Path(item.Parent), "inventory-caption");
            AddContainer(window, item, "CONTENTS");
            window.RegisterCallback<DetachFromPanelEvent>(_ => {
                if (!m_Rebuilding) m_Open.Remove(item.Id);
                m_Grids.RemoveAll(g => !g.Definition.SlotKind.HasValue && g.Element.panel == null);
            });
        }

        private void ChooseSlot(string slot) {
            if (m_Graph == null || m_Busy) return;
            var window = m_Windows.Open(SlotLabel(slot), null, true);
            var kind = InventoryCatalog.Get("equipment").Regions.First(r => r.Id == slot).SlotKind;
            foreach (var item in m_Graph.Items.Where(i => InventoryCatalog.Get(i.Code).Kind == kind)) {
                var captured = item;
                MoonkovTerminal.ActionButton(window, InventoryCatalog.Get(item.Code).Name, () => {
                    m_Windows.Close(window);
                    Command(new InventoryCommand { ItemId = captured.Id, Parent = "equipment", Region = slot });
                });
            }
            var equipped = m_Graph.Equipped(slot);
            if (equipped != null && HaloModules.IsWeapon(equipped.Code) && m_Stash)
                MoonkovTerminal.ActionButton(window, "MODIFY HALO", () => {
                    m_Windows.Close(window);
                    OpenModification(equipped);
                });
            if (equipped != null)
                MoonkovTerminal.ActionButton(window, "UNEQUIP / STORE", () => {
                    m_Windows.Close(window);
                    QuickTransfer(equipped);
                });
        }

        private void Context(InventoryItem item, Vector2 position) {
            var window = m_Windows.Open(InventoryCatalog.Get(item.Code).Name, m_Root.WorldToLocal(position), true);
            if (!m_Stash && m_UseConsumable != null && BatteryEnergy.IsCell(item)) {
                var recharge = MoonkovTerminal.ActionButton(window, "RECHARGE HALO", () => {
                    if (m_Busy || m_ReadOnlyReason != null || !(ShieldRechargeAvailable?.Invoke(item) ?? false)) return;
                    m_Windows.Close(window);
                    m_Busy = true;
                    m_UseConsumable(item);
                });
                recharge.SetEnabled(!m_Busy && m_ReadOnlyReason == null &&
                                    (ShieldRechargeAvailable?.Invoke(item) ?? false));
                recharge.tooltip = MoonkovLocalization.Text(
                    "Requires halo energy below maximum and a charged battery in pockets or an equipped badge.");
            }
            if (m_UseConsumable != null && m_Graph.ConsumableAccessible(item))
                MoonkovTerminal.ActionButton(window, MoonkovItemPresentation.UseAction(item.Code), () => {
                    if (m_Busy || m_ReadOnlyReason != null) return;
                    m_Windows.Close(window);
                    m_Busy = true;
                    m_UseConsumable(item);
                });
            else if (m_UseConsumable != null && ConsumableCatalog.Get(item.Code)?.Oxygen > 0 && m_Graph.Carried(item))
                Text(window, "Move the oxygen canister to a pocket or equipped chest badge before use.",
                    "inventory-caption");
            if (m_UseMedical != null && m_Graph.MedicalAccessible(item))
                MoonkovTerminal.ActionButton(window, MoonkovItemPresentation.UseAction(item.Code), () => {
                    if (m_Busy || m_ReadOnlyReason != null) return;
                    m_Windows.Close(window);
                    m_Busy = true;
                    m_UseMedical(item);
                });
            if (HaloModules.IsWeapon(item.Code) && m_Stash)
                MoonkovTerminal.ActionButton(window, "MODIFY HALO", () => {
                    m_Windows.Close(window);
                    OpenModification(item);
                });
            if (m_Drop != null && !m_Stash && m_Graph.Carried(item))
                MoonkovTerminal.ActionButton(window, "丢弃到地面 / DROP", () => {
                    if (m_Busy || m_ReadOnlyReason != null) return;
                    m_Windows.Close(window);
                    m_Busy = true;
                    m_Drop(item);
                });
            MoonkovTerminal.ActionButton(window, "INSPECT", () => {
                m_Windows.Close(window);
                OpenInspection(item);
            });
            if (InventoryCatalog.Get(item.Code).Container)
                MoonkovTerminal.ActionButton(window, "OPEN CONTAINER", () => {
                    m_Windows.Close(window);
                    OpenContainer(item);
                });
            MoonkovTerminal.ActionButton(window, "QUICK TRANSFER", () => {
                m_Windows.Close(window);
                QuickTransfer(item);
            });
            if (InventoryCatalog.Get("equipment").Regions.Any(r => r.SlotKind == InventoryCatalog.Get(item.Code).Kind))
                MoonkovTerminal.ActionButton(window, "EQUIP", () => {
                    m_Windows.Close(window);
                    foreach (var region in InventoryCatalog.Get("equipment").Regions)
                        if (region.SlotKind == InventoryCatalog.Get(item.Code).Kind) {
                            Command(new InventoryCommand
                                { ItemId = item.Id, Parent = "equipment", Region = region.Id });
                            break;
                        }
                });
            if (item.Quantity > 1)
                MoonkovTerminal.ActionButton(window, "SPLIT STACK", () => {
                    m_Windows.Close(window);
                    Split(item);
                });
        }

        private void OpenInspection(InventoryItem item) {
            Describe(item);
            var definition = InventoryCatalog.Get(item.Code);
            var window = m_Windows.Open(definition.Name);
            window.AddToClassList("archive-inspection-window");
            var content = TerminalLayout.Clone("Components/ItemInspection", "itemInspection");
            window.Add(content);
            var art = new StashItemArt(item.Code);
            art.style.flexGrow = 1;
            content.Q("inspectionArt").Add(art);
            MoonkovLocalization.Set(content.Q<Label>("inspectionTitle"), definition.Name);
            MoonkovLocalization.Set(content.Q<Label>("inspectionStats"), "{0} · ×{1} · {2:0.##} KG TOTAL · {3} · {4}",
                definition.Name, item.Quantity, definition.Weight * item.Quantity, Path(item.Parent),
                definition.Width + " × " + definition.Height);
            MoonkovLocalization.Set(content.Q<Label>("inspectionDetail"),
                MoonkovItemPresentation.Details(item.Code) + "\n" + MoonkovItemPresentation.ItemState(item));
            var actions = content.Q("inspectionActions");
            var transfer = MoonkovTerminal.ActionButton(actions, "QUICK TRANSFER", () => {
                m_Windows.Close(window);
                QuickTransfer(item);
            });
            transfer.SetEnabled(!m_Busy && m_ReadOnlyReason == null);
            if (definition.Container)
                MoonkovTerminal.ActionButton(actions, "OPEN CONTAINER", () => {
                    m_Windows.Close(window);
                    OpenContainer(item);
                });
            if (m_Stash && HaloModules.IsWeapon(item.Code))
                MoonkovTerminal.ActionButton(actions, "MODIFY HALO", () => {
                    m_Windows.Close(window);
                    OpenModification(item);
                });
        }

        private void Split(InventoryItem item) {
            var window = m_Windows.Open("SPLIT STACK", null, true);
            var amount = new IntegerField("QUANTITY") { value = Mathf.Max(1, item.Quantity / 2), isDelayed = true };
            window.Add(amount);
            MoonkovTerminal.ActionButton(window, "SPLIT", () => {
                // 拆分出来的堆叠将是另一件物品。先在原容器中试找空位，再提交数量和位置。
                var split = item.Clone();
                split.Id = Guid.NewGuid().ToString("D");
                if (!m_Graph.FindSpace(split, item.Parent, out string region, out int x, out int y)) {
                    MoonkovLocalization.Set(m_Message, "No space in this container.");
                    return;
                }
                m_Windows.Close(window);
                Command(new InventoryCommand {
                    Operation = InventoryOperation.Split, ItemId = item.Id, Parent = item.Parent, Region = region,
                    X = x, Y = y, Rotated = item.Rotated, Quantity = amount.value
                });
            });
        }

        private void QuickTransfer(InventoryItem item) {
            if (item == null || m_Graph == null || m_Disposed) return;
            if (!m_Stash && m_Graph.Find(LootInventoryExchange.Root) != null) {
                if (m_ReadOnlyReason != null) {
                    MoonkovLocalization.Set(m_Message, m_ReadOnlyReason);
                    return;
                }
                // 局内同时点多件时，用物品 ID 去重排队。服务端每确认一件后才继续下一件。
                if (m_QuickTransfers.Any(intent => intent.ItemId == item.Id)) return;
                if (m_QuickTransfers.Count >= 32) return;
                m_QuickTransfers.Enqueue(new QuickTransferIntent
                    { ItemId = item.Id, FromCarried = m_Graph.Carried(item) });
                if (m_Busy) MoonkovLocalization.Set(m_Message, "正在收纳 {0} 项", m_QuickTransfers.Count);
                ProcessQuickTransfers();
                return;
            }
            if (m_Graph.Find(LootInventoryExchange.Root) != null && m_Graph.Carried(item)) {
                if (!AutoMove(item, LootInventoryExchange.Root))
                    MoonkovLocalization.Set(m_Message, "No compatible free space in this cache.");
                return;
            }
            if (m_Stash && m_Graph.Carried(item)) {
                if (!AutoMove(item, "stash")) MoonkovLocalization.Set(m_Message, "No free space in storage.");
                return;
            }
            foreach (string parent in m_Graph.QuickTransferContainers())
                if (parent != item.Parent && parent != item.Id && AutoMove(item, parent))
                    return;
            MoonkovLocalization.Set(m_Message, "No compatible free space.");
        }

        private void ClearQuickTransfers() {
            m_QuickTransferPump?.Pause();
            m_QuickTransferPump = null;
            m_QuickTransfers.Clear();
            m_QuickTransferSpaceBlocked = false;
        }

        // 快捷收纳一次处理一件，按最新库存先尝试合并，再找空格。放不下就继续处理队列里后面的物品。
        private void ProcessQuickTransfers() {
            if (m_Busy || m_Disposed) return;
            if (m_Graph == null || m_ReadOnlyReason != null || m_Graph.Find(LootInventoryExchange.Root) == null) {
                ClearQuickTransfers();
                return;
            }
            while (m_QuickTransfers.Count > 0) {
                var intent = m_QuickTransfers.Peek();
                var item = m_Graph.Find(intent.ItemId);
                // 保留原本移动的方向。已经取走的物品要跳过，
                // 即使上一轮只合并了一部分堆叠，也不能又送回去。
                // 重新读取这件东西目前在哪。它已被拿走或换边时，跳过旧意图，防止反方向把它搬回去。
                if (item == null || m_Graph.Carried(item) != intent.FromCarried) {
                    m_QuickTransfers.Dequeue();
                    continue;
                }
                string[] parents = intent.FromCarried
                    ? new[] { LootInventoryExchange.Root }
                    : m_Graph.QuickTransferContainers().ToArray();
                InventoryCommand command = null;
                for (int pass = 0; pass < 2 && command == null; pass++)
                    foreach (string parent in parents) {
                        if (parent == item.Parent || parent == item.Id) continue;
                        command = m_Graph.PlanQuickMove(item.Id, parent, pass == 0, true);
                        if (command != null) break;
                    }
                if (command == null) {
                    m_QuickTransfers.Dequeue();
                    m_QuickTransferSpaceBlocked = true;
                    continue;
                }
                m_ProcessingQuickTransfer = true;
                try { Command(command); } finally { m_ProcessingQuickTransfer = false; }
                return;
            }
            if (m_QuickTransferSpaceBlocked) {
                m_QuickTransferSpaceBlocked = false;
                MoonkovAudio.Error();
                MoonkovLocalization.Set(m_Message, "空间不足，未装下的物品留在原处。");
            }
        }

        private bool AutoMove(InventoryItem item, string parent) {
            var command = m_Graph.PlanQuickMove(item.Id, parent);
            if (command == null) return false;
            Command(command);
            return true;
        }

        private void Command(InventoryCommand command) {
            if (m_Busy || m_Graph == null) return;
            if (!m_ProcessingQuickTransfer) ClearQuickTransfers();
            if (m_ReadOnlyReason != null) {
                MoonkovLocalization.Set(m_Message, m_ReadOnlyReason);
                return;
            }
            // 命令带着玩家实际看到的库存版本。先在副本试放，成功只表示可以发请求，真实库存等处理结果更新。
            command.ExpectedVersion = m_Graph.Version;
            var error = m_Graph.Clone().TryApply(command);
            if (error != InventoryError.None) {
                ClearQuickTransfers();
                MoonkovAudio.Error();
                MoonkovLocalization.Set(m_Message,
                    error == InventoryError.Overweight ? "已达携带重量上限，物品留在原处。" : "Move blocked: {0}", error.ToString());
                return;
            }
            // 先按操作类型确定音效，标记等待并把命令交给外层。最终显示仍由外层处理结果后的快照更新。
            m_OperationSound = MoonkovInventoryAudio.ForMove(m_Graph, command, MoonkovAudio.Library);
            m_Busy = true;
            MoonkovLocalization.Set(m_Message, "UPDATING INVENTORY…");
            RefreshWorkshop();
            m_Send(command);
        }

        private void FlushDeferredLayout() {
            if (m_DeferredLayout == null || m_Disposed || m_Drag != null) return;
            string layout = m_DeferredLayout;
            m_DeferredLayout = null;
            Render();
            m_RenderedLayout = layout;
            if (m_PortraitShowing) {
                PresentOperatorPortrait();
                PresentCorpsePortrait();
            }
        }

        public void CancelDrag() { m_Drag?.Cancel(); }

        private void FocusChanged(bool focused) {
            if (!focused) {
                HideItemTooltip();
                CancelDrag();
            }
        }

        public bool Escape() {
            if (Application.isPlaying && m_EscapeHandledFrame == Time.frameCount) return true;
            if (m_Drag != null) {
                CancelDrag();
                m_EscapeHandledFrame = Time.frameCount;
                return true;
            }
            if (!m_Windows.CloseTop()) return false;
            m_EscapeHandledFrame = Time.frameCount;
            return true;
        }

        public bool HandleKey(KeyDownEvent evt) {
            if (evt.keyCode == KeyCode.Escape && Escape()) {
                evt.StopImmediatePropagation();
                return true;
            }
            if (evt.keyCode == KeyCode.R && m_Drag != null) {
                m_Drag.Rotate();
                evt.StopImmediatePropagation();
                return true;
            }
            return false;
        }

        private void Key(KeyDownEvent evt) { HandleKey(evt); }

        public void Dispose() {
            m_DeferredRender?.Pause();
            m_DeferredLayout = null;
            if (m_Disposed) return;
            if (m_PortraitSettings != null) m_PortraitSettings.PlayerCharacterChanged -= PortraitCharacterChanged;
            Suspend();
            ReleaseItemTooltip();
            m_Disposed = true;
            Application.focusChanged -= FocusChanged;
            m_Root.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            m_Windows.Dispose();
            m_Root.RemoveFromHierarchy();
        }
    }
}
