using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client
{
    // Presents authoritative snapshots. Dragging sends an intent; it never edits account quantities.
    public sealed class ContainerInventoryView : IDisposable
    {
        private sealed class Grid
        {
            public string Parent, Region; public InventoryRegion Definition;
            public VisualElement Element; public float Cell = 48;
        }
        private readonly VisualElement m_Root, m_Body, m_CharacterStage, m_CharacterPane, m_LootPane, m_ContainerPane, m_StashPane, m_Ghost, m_Preview;
        private readonly Label m_Weight, m_Message;
        private readonly Action<InventoryCommand> m_Send;
        private readonly Action<InventoryItem> m_UseMedical;
        private readonly Action<InventoryItem> m_UseConsumable;
        private readonly TerminalWindows m_Windows;
        private readonly bool m_Stash;
        private readonly List<Grid> m_Grids = new List<Grid>();
        private readonly Dictionary<LoadoutSlot, Button> m_Slots = new Dictionary<LoadoutSlot, Button>();
        private readonly Dictionary<string, VisualElement> m_Tiles = new Dictionary<string, VisualElement>();
        private readonly HashSet<string> m_Open = new HashSet<string>();
        private MenuCharacterView m_Character;
        private InventoryGraph m_Graph;
        private Drag m_Drag;
        private bool m_Busy, m_Disposed, m_Rebuilding;
        private string m_ReadOnlyReason;
        private int m_LootId=-1;
        private int m_RenderedVersion = int.MinValue;
        private int m_EscapeHandledFrame = -1;
        public VisualElement Element => m_Root;
        public bool Busy => m_Busy;
        public void SetReadOnly(string reason)
        {
            if (m_ReadOnlyReason == reason) return;
            CancelDrag(); m_ReadOnlyReason = reason;
            MoonkovLocalization.Set(m_Message, reason ?? "Drag to move · R rotate · Double click containers · Ctrl click transfer");
        }
        public ContainerInventoryView(VisualElement parent, bool showStash, Action<InventoryCommand> send,
            Action<InventoryItem> useMedical = null, Action<InventoryItem> useConsumable = null)
        {
            m_Stash = showStash; m_Send = send; m_UseMedical = useMedical;
            m_UseConsumable = useConsumable;
            m_Root = TerminalLayout.Clone("ContainerInventory", "containerInventory");
            m_Root.EnableInClassList("inventory-with-stash", showStash);
            m_Root.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/ContainerUI")); parent.Add(m_Root);
            m_Body = m_Root.Q<VisualElement>("inventoryBody");
            m_LootPane = m_Root.Q<VisualElement>("lootPane");
            m_CharacterPane = m_Root.Q<VisualElement>("characterPane");
            m_CharacterStage = m_Root.Q<VisualElement>("characterStage");
            var guides = new StashEquipmentGuides(() => m_Character, m_Slots); m_CharacterStage.Insert(0, guides);
            string[] slots = { "Helmet", "ChestRig", "Pistol", "Backpack", "Primary", "Secondary" };
            for (int n = 0; n < slots.Length; n++)
            {
                string slot = slots[n];
                var button = m_Root.Q<Button>("inventorySlot" + slot);
                m_Slots.Add((LoadoutSlot)Enum.Parse(typeof(LoadoutSlot), slot), button);
                button.RemoveManipulator(button.clickable);
                button.AddManipulator(new Drag(this, () => m_Graph?.Equipped(slot), () => ChooseSlot(slot), () =>
                {
                    var item=m_Graph?.Equipped(slot);
                    if (item!=null && InventoryCatalog.Get(item.Code).Container) OpenContainer(item); else ChooseSlot(slot);
                }));
                m_Grids.Add(new Grid { Parent="equipment", Region=slot, Element=button, Definition=InventoryCatalog.Get("equipment").Regions.First(r => r.Id == slot) });
            }
            m_Weight = m_Root.Q<Label>("inventoryWeight");
            m_ContainerPane = m_Root.Q<ScrollView>("containerPane");
            m_StashPane = m_Root.Q<VisualElement>("stashPane");
            if (!showStash) m_StashPane.RemoveFromHierarchy();
            m_Message = m_Root.Q<Label>("inventoryHint");
            m_Ghost = m_Root.Q<VisualElement>("inventoryGhost");
            m_Preview = new VisualElement { pickingMode=PickingMode.Ignore }; m_Preview.AddToClassList("inventory-drop"); m_Preview.style.display=DisplayStyle.None;
            m_Root.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            m_Windows = new TerminalWindows(m_Root, () => { });
            Present(null);
        }
        private static Label Text(VisualElement parent, string text, string css)
        { var label = new Label { pickingMode=PickingMode.Ignore }; label.AddToClassList(css); parent.Add(label); MoonkovLocalization.Set(label, text); return label; }
        private static string SlotLabel(string slot) => slot == "ChestRig" ? "CHEST RIG" : slot == "Primary" ? "PRIMARY" : slot == "Secondary" ? "SECONDARY" : slot.ToUpperInvariant();
        public static StashArtKind Art(string code)
        {
            switch (code) { case "dust":return StashArtKind.Dust; case "alloy":return StashArtKind.Alloy; case "cells":return StashArtKind.Cell; case "medkit":return StashArtKind.Medical;
                case "helmet":return StashArtKind.Helmet; case "rifle":case "compact":return StashArtKind.HaloRifle; case "pistol":return StashArtKind.HaloRevolver;
                case "ration":return StashArtKind.Ration; case "water":return StashArtKind.Water;
                case "shotgun":return StashArtKind.HaloShotgun;
                case "sniper":return StashArtKind.HaloSniper;
                case "rig":return StashArtKind.ChestRig; case "backpack":case "small_pack":return StashArtKind.Backpack; default:return StashArtKind.None; }
        }
        public void Present(InventoryGraph graph, string message = null, bool operationCompleted = true, int lootId=-1)
        {
            if (m_Disposed) return;
            if (m_Busy && operationCompleted)
            {
                if (message != null) MoonkovAudio.Error();
                else MoonkovAudio.Play(MoonkovAudio.Library?.Equipment, Vector3.zero);
            }
            bool changed = m_Graph != graph || m_RenderedVersion != (graph?.Version ?? -1) || m_LootId!=lootId; m_Graph = graph;m_LootId=lootId;
            if (operationCompleted) m_Busy = false;
            if (changed) { CancelDrag(); Render(); m_RenderedVersion=graph?.Version ?? -1; }
            if (message != null) MoonkovLocalization.Set(m_Message, message);
            else if (!m_Busy && graph != null) MoonkovLocalization.Set(m_Message, m_ReadOnlyReason ?? "Drag to move · R rotate · Double click containers · Ctrl click transfer");
        }
        public void Show()
        {
            if (m_Character != null || m_Disposed || m_Graph?.Find(LootInventoryExchange.Root)!=null) return;
            m_Character = new MenuCharacterView(idleOnly: true);
            m_Character.Element.RemoveFromClassList("terminal-character-art"); m_Character.Element.AddToClassList("stash-character-render");
            m_CharacterStage.Insert(0, m_Character.Element);
            m_Character.FrameRendered += m_CharacterStage.Q<StashEquipmentGuides>().MarkDirtyRepaint;
        }
        public void Suspend()
        {
            CancelDrag(); m_Windows.CloseAll(); m_Open.Clear();
            if (m_Character == null) return;
            m_Character.Element.RemoveFromHierarchy(); m_Character.Dispose(); m_Character = null;
        }
        private void Render()
        {
            m_Grids.RemoveAll(g => !g.Definition.SlotKind.HasValue); m_Tiles.Clear(); m_ContainerPane.Clear(); m_StashPane.Clear();m_LootPane.Clear();
            var loot=m_Graph?.Find(LootInventoryExchange.Root);
            m_Root.EnableInClassList("inventory-with-loot",loot!=null);
            m_LootPane.style.display=loot==null ? DisplayStyle.None : DisplayStyle.Flex;
            m_CharacterPane.style.display=loot==null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Rebuilding=true; m_Windows.CloseAll(); m_Rebuilding=false;
            foreach (var pair in m_Slots)
            {
                var item = m_Graph?.Equipped(pair.Key.ToString()); var button = pair.Value; var art = button.Q<VisualElement>("art"); art.Clear();
                MoonkovLocalization.Set(button.Q<Label>("item"), m_Graph == null ? "LOADING" : item == null ? "EMPTY" : InventoryCatalog.Get(item.Code).Name);
                if (item != null) { var icon = new StashItemArt(Art(item.Code)); icon.AddToClassList("inventory-equipment-icon"); art.Add(icon); m_Tiles[item.Id] = button; }
                button.EnableInClassList("inventory-equipped", item != null);
            }
            if (m_Graph == null)
            {
                MoonkovLocalization.Set(m_Weight, "Waiting for inventory snapshot");
                MoonkovLocalization.Set(m_Message, "Loading carried inventory from the server…");
                AddContainer(m_ContainerPane, null, "POCKETS");
                AddContainer(m_ContainerPane, null, "CHEST RIG");
                AddContainer(m_ContainerPane, null, "BACKPACK");
                if (m_Stash) AddContainer(m_StashPane, null, "PERSONAL STORAGE");
                return;
            }
            MoonkovLocalization.Set(m_Weight, "CARRIED {0:0.0} / {1:0} KG" , m_Graph.CarriedWeight, InventoryCatalog.CarryWeightLimit);
            AddContainer(m_ContainerPane, m_Graph.Find("pockets"), "POCKETS");
            AddContainer(m_ContainerPane, m_Graph.Equipped("ChestRig"), "CHEST RIG");
            AddContainer(m_ContainerPane, m_Graph.Equipped("Backpack"), "BACKPACK");
            if(loot!=null)AddContainer(m_LootPane,loot,m_LootId>=RaidLootContainers.FirstDeathBagId ? "FALLEN EXPEDITION / GEAR" : "SUPPLY CACHE / "+(m_LootId+1).ToString("00"));
            if (m_Stash) AddContainer(m_StashPane, m_Graph.Find("stash"), "PERSONAL STORAGE");
            foreach (var id in new List<string>(m_Open)) { var item = m_Graph.Find(id); if (item == null) m_Open.Remove(id); else OpenContainer(item, false); }
        }
        private void AddContainer(VisualElement pane, InventoryItem item, string title)
        {
            var group = new VisualElement(); group.AddToClassList("inventory-container"); pane.Add(group);
            Text(group, title, "inventory-section-title");
            if (item == null) { Text(group, m_Graph == null ? "LOADING…" : "NOT EQUIPPED", "inventory-caption"); return; }
            var def = InventoryCatalog.Get(item.Code);
            if (def.Kind != ItemKind.Root) Text(group, def.Name, "inventory-caption");
            var regionRow = new VisualElement(); regionRow.AddToClassList("inventory-regions"); group.Add(regionRow);
            foreach (var region in def.Regions)
            {
                if (region.SlotKind.HasValue) continue;
                var wrapper = new VisualElement(); wrapper.AddToClassList("inventory-region"); regionRow.Add(wrapper);
                Text(wrapper, region.Id.ToUpperInvariant(), "inventory-region-title");
                VisualElement gridParent = wrapper;
                if (item.Code == "stash" || item.Code=="loot") { var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("inventory-warehouse-scroll"); wrapper.Add(scroll); gridParent = scroll; }
                int rows = m_Graph.Rows(item.Id,region);
                var visual = new StashGridVisual(region.Width, rows); visual.AddToClassList("inventory-grid"); visual.style.width=region.Width*48; visual.style.height=rows*48; gridParent.Add(visual);
                var grid = new Grid { Parent=item.Id, Region=region.Id, Definition=region, Element=visual }; m_Grids.Add(grid);
                foreach (var entry in m_Graph.Children(item.Id, region.Id))
                {
                    var captured = entry;
                    var tile = new Button(() => Describe(captured)); tile.AddToClassList("inventory-item"); tile.name="inventoryItem"+entry.Id;
                    var icon = new StashItemArt(Art(entry.Code)); icon.AddToClassList("inventory-item-art"); tile.Add(icon);
                    var itemDef = InventoryCatalog.Get(entry.Code); Text(tile, MoonkovLocalization.Text(itemDef.Name).Split(' ')[0].ToUpperInvariant(), "inventory-item-label");
                    Text(tile, itemDef.Container ? "OPEN ↗" : BatteryEnergy.IsCell(entry) ? $"{BatteryEnergy.Stored(entry)}E" : entry.Quantity.ToString(), "inventory-item-quantity");
                    tile.tooltip=MoonkovLocalization.Text(itemDef.Name) + " / " + entry.Quantity + MoonkovLocalization.Text(entry.FoundInRaid ? " / FOUND IN RAID" : " / BROUGHT IN");
                    if (BatteryEnergy.IsCell(entry)) tile.tooltip += MoonkovLocalization.Format($" / {BatteryEnergy.Stored(entry)}/{entry.Quantity * BatteryEnergy.Capacity} ENERGY");
                    tile.RemoveManipulator(tile.clickable);
                    tile.AddManipulator(new Drag(this, () => m_Graph.Find(captured.Id), () => Describe(captured),
                        () => { if (itemDef.Container) OpenContainer(captured); else Describe(captured); }));
                    visual.Add(tile); m_Tiles[entry.Id]=tile; Position(tile, entry, grid.Cell);
                }
                if (item.Code == "stash")
                {
                    wrapper.style.flexGrow=1; wrapper.style.minWidth=0; regionRow.style.flexGrow=1; group.style.flexGrow=1;
                    wrapper.RegisterCallback<GeometryChangedEvent>(evt =>
                    {
                        float cell = Mathf.Clamp((evt.newRect.width-16)/region.Width, 20, 44);
                        if (Mathf.Abs(grid.Cell-cell)<.1f) return; grid.Cell=cell; visual.style.width=region.Width*cell; visual.style.height=rows*cell;
                        foreach (var entry in m_Graph.Children(item.Id, region.Id)) if (m_Tiles.TryGetValue(entry.Id, out var tile)) Position(tile, entry, cell);
                    });
                }
            }
        }
        private static void Position(VisualElement tile, InventoryItem item, float cell)
        {
            var def=InventoryCatalog.Get(item.Code); tile.style.left=item.X*cell; tile.style.top=item.Y*cell;
            tile.style.width=(item.Rotated ? def.Height : def.Width)*cell; tile.style.height=(item.Rotated ? def.Width : def.Height)*cell;
        }
        private void Describe(InventoryItem item)
        {
            var def=InventoryCatalog.Get(item.Code);
            if (BatteryEnergy.IsCell(item)) MoonkovLocalization.Set(m_Message,
                "{0} · {1} · {2:0.0} KG · {3} · {4}/{5} ENERGY · ACTIVE CELL {6}/{7}",
                def.Name, item.Quantity, def.Weight*item.Quantity, Path(item.Parent),
                BatteryEnergy.Stored(item), item.Quantity * BatteryEnergy.Capacity, BatteryEnergy.Charge(item), BatteryEnergy.Capacity);
            else MoonkovLocalization.Set(m_Message, "{0} · {1} · {2:0.0} KG · {3}", def.Name, item.Quantity, def.Weight*item.Quantity, Path(item.Parent));
        }
        private string Path(string id)
        {
            var parts=new List<string>(); var item=m_Graph.Find(id);
            for (int n=0; item!=null && n<InventoryCatalog.MaxDepth; n++) { parts.Insert(0, InventoryCatalog.Get(item.Code).Name); item=m_Graph.Find(item.Parent); }
            return string.Join(" / ", parts.ConvertAll(MoonkovLocalization.Text));
        }
        private void OpenContainer(InventoryItem item, bool remember=true)
        {
            if (!InventoryCatalog.Get(item.Code).Container) return;
            if (remember && !m_Open.Add(item.Id)) return;
            var window=m_Windows.Open(InventoryCatalog.Get(item.Code).Name.ToUpperInvariant()); window.AddToClassList("inventory-container-window");
            Text(window, Path(item.Parent), "inventory-caption"); AddContainer(window, item, "CONTENTS");
            window.RegisterCallback<DetachFromPanelEvent>(_ => { if (!m_Rebuilding) m_Open.Remove(item.Id); m_Grids.RemoveAll(g=>!g.Definition.SlotKind.HasValue && g.Element.panel==null); });
        }
        private void ChooseSlot(string slot)
        {
            if (m_Graph==null || m_Busy) return;
            var window=m_Windows.Open(SlotLabel(slot), null, true);
            var kind=InventoryCatalog.Get("equipment").Regions.First(r=>r.Id==slot).SlotKind;
            foreach (var item in m_Graph.Items.Where(i=>InventoryCatalog.Get(i.Code).Kind==kind))
            { var captured=item; MoonkovTerminal.ActionButton(window, InventoryCatalog.Get(item.Code).Name, ()=> { m_Windows.Close(window); Command(new InventoryCommand { ItemId=captured.Id, Parent="equipment", Region=slot }); }); }
            var equipped=m_Graph.Equipped(slot);
            if (equipped!=null) MoonkovTerminal.ActionButton(window, "UNEQUIP / STORE", ()=> { m_Windows.Close(window); QuickTransfer(equipped); });
        }
        private void Context(InventoryItem item, Vector2 position)
        {
            var window=m_Windows.Open(InventoryCatalog.Get(item.Code).Name, m_Root.WorldToLocal(position), true);
            if (m_UseConsumable != null && m_Graph.ConsumableAccessible(item))
                MoonkovTerminal.ActionButton(window, ConsumableCatalog.Get(item.Code).Food ? "EAT / +45 ENERGY" : "DRINK / +50 HYDRATION", () =>
                {
                    if (m_Busy || m_ReadOnlyReason != null) return;
                    m_Windows.Close(window); m_Busy = true; m_UseConsumable(item);
                });
            if (m_UseMedical != null && m_Graph.MedicalAccessible(item))
                MoonkovTerminal.ActionButton(window, "USE / RESTORE 40 HP", () =>
                {
                    if (m_Busy || m_ReadOnlyReason != null) return;
                    m_Windows.Close(window); m_Busy = true; m_UseMedical(item);
                });
            MoonkovTerminal.ActionButton(window, "INSPECT", ()=> { m_Windows.Close(window); Describe(item); });
            if (InventoryCatalog.Get(item.Code).Container) MoonkovTerminal.ActionButton(window, "OPEN CONTAINER", ()=> { m_Windows.Close(window); OpenContainer(item); });
            MoonkovTerminal.ActionButton(window, "QUICK TRANSFER", ()=> { m_Windows.Close(window); QuickTransfer(item); });
            if (InventoryCatalog.Get("equipment").Regions.Any(r => r.SlotKind == InventoryCatalog.Get(item.Code).Kind))
                MoonkovTerminal.ActionButton(window, "EQUIP", ()=> { m_Windows.Close(window); foreach (var region in InventoryCatalog.Get("equipment").Regions) if (region.SlotKind==InventoryCatalog.Get(item.Code).Kind) { Command(new InventoryCommand { ItemId=item.Id, Parent="equipment", Region=region.Id }); break; } });
            if (item.Quantity>1) MoonkovTerminal.ActionButton(window, "SPLIT STACK", ()=> { m_Windows.Close(window); Split(item); });
        }
        private void Split(InventoryItem item)
        {
            var window=m_Windows.Open("SPLIT STACK", null, true); var amount=new IntegerField("QUANTITY") { value=Mathf.Max(1,item.Quantity/2), isDelayed=true }; window.Add(amount);
            MoonkovTerminal.ActionButton(window, "SPLIT", ()=>
            {
                var split=item.Clone(); split.Id=Guid.NewGuid().ToString("D");
                if (!m_Graph.FindSpace(split, item.Parent, out var region, out var x, out var y)) { MoonkovLocalization.Set(m_Message, "No space in this container."); return; }
                m_Windows.Close(window); Command(new InventoryCommand { Operation=InventoryOperation.Split, ItemId=item.Id, Parent=item.Parent, Region=region, X=x, Y=y, Rotated=item.Rotated, Quantity=amount.value });
            });
        }
        private void QuickTransfer(InventoryItem item)
        {
            if(m_Graph.Find(LootInventoryExchange.Root)!=null && m_Graph.Carried(item))
            {if(!AutoMove(item,LootInventoryExchange.Root))MoonkovLocalization.Set(m_Message, "No compatible free space in this cache.");return;}
            if (m_Stash && m_Graph.Carried(item)) { if (!AutoMove(item,"stash")) MoonkovLocalization.Set(m_Message, "No free space in storage."); return; }
            if (!m_Graph.Carried(item)) foreach (var region in InventoryCatalog.Get("equipment").Regions)
                if (region.SlotKind==InventoryCatalog.Get(item.Code).Kind && m_Graph.Equipped(region.Id)==null) { Command(new InventoryCommand { ItemId=item.Id, Parent="equipment", Region=region.Id }); return; }
            foreach (string parent in m_Graph.CarryContainers()) if (parent!=item.Parent && parent!=item.Id && AutoMove(item,parent)) return;
            MoonkovLocalization.Set(m_Message, "No compatible free space.");
        }
        private bool AutoMove(InventoryItem item, string parent)
        {
            var definition=InventoryCatalog.Get(item.Code);
            if(!definition.Container && definition.MaxStack>1)
                foreach(var stack in m_Graph.Children(parent))
                    if(stack.Id!=item.Id && stack.Code==item.Code && stack.FoundInRaid==item.FoundInRaid && stack.Quantity+item.Quantity<=definition.MaxStack)
                    {Command(new InventoryCommand {Operation=InventoryOperation.Merge,ItemId=item.Id,TargetId=stack.Id});return true;}
            if (!m_Graph.FindSpace(item,parent,out var region,out var x,out var y)) return false;
            Command(new InventoryCommand { ItemId=item.Id, Parent=parent, Region=region, X=x, Y=y, Rotated=item.Rotated }); return true;
        }
        private void Command(InventoryCommand command)
        {
            if (m_Busy || m_Graph==null) return;
            if (m_ReadOnlyReason != null) { MoonkovLocalization.Set(m_Message, m_ReadOnlyReason); return; }
            command.ExpectedVersion=m_Graph.Version;
            var error=m_Graph.Clone().TryApply(command);
            if (error!=InventoryError.None) { MoonkovAudio.Error(); MoonkovLocalization.Set(m_Message, "Move blocked: {0}", error.ToString()); return; }
            m_Busy=true; MoonkovLocalization.Set(m_Message, "UPDATING INVENTORY…"); m_Send(command);
        }
        public void CancelDrag() => m_Drag?.Cancel();
        public bool Escape()
        {
            if (Application.isPlaying && m_EscapeHandledFrame==Time.frameCount) return true;
            if (m_Drag!=null) { CancelDrag(); m_EscapeHandledFrame=Time.frameCount; return true; }
            if (!m_Windows.CloseTop()) return false;
            m_EscapeHandledFrame=Time.frameCount; return true;
        }
        public bool HandleKey(KeyDownEvent evt)
        {
            if (evt.keyCode==KeyCode.Escape && Escape()) { evt.StopImmediatePropagation(); return true; }
            if (evt.keyCode==KeyCode.R && m_Drag!=null) { m_Drag.Rotate(); evt.StopImmediatePropagation(); return true; }
            return false;
        }
        private void Key(KeyDownEvent evt) => HandleKey(evt);
        public void Dispose()
        {
            if (m_Disposed) return; Suspend(); m_Disposed=true; m_Root.UnregisterCallback<KeyDownEvent>(Key,TrickleDown.TrickleDown); m_Windows.Dispose(); m_Root.RemoveFromHierarchy();
        }

        private sealed class Drag : PointerManipulator
        {
            private readonly ContainerInventoryView m_View; private readonly Func<InventoryItem> m_Source;
            private readonly Action m_Click, m_DoubleClick;
            private InventoryItem m_Item; private int m_Pointer=-1; private bool m_Active,m_Rotated; private Vector2 m_Start,m_Last,m_Grab;
            private InventoryCommand m_Command;
            private InventoryCommand m_ValidatedCommand;
            private InventoryError m_PlacementError;
            private int m_ClickCount;
            private IVisualElementScheduledItem m_Scroll;
            public Drag(ContainerInventoryView view, Func<InventoryItem> source, Action click, Action doubleClick)
            { m_View=view; m_Source=source; m_Click=click; m_DoubleClick=doubleClick; }
            protected override void RegisterCallbacksOnTarget()
            { target.RegisterCallback<PointerDownEvent>(Down,TrickleDown.TrickleDown); target.RegisterCallback<PointerMoveEvent>(Move,TrickleDown.TrickleDown); target.RegisterCallback<PointerUpEvent>(Up,TrickleDown.TrickleDown); target.RegisterCallback<PointerCaptureOutEvent>(Lost); }
            protected override void UnregisterCallbacksFromTarget()
            { Cancel(); target.UnregisterCallback<PointerDownEvent>(Down,TrickleDown.TrickleDown); target.UnregisterCallback<PointerMoveEvent>(Move,TrickleDown.TrickleDown); target.UnregisterCallback<PointerUpEvent>(Up,TrickleDown.TrickleDown); target.UnregisterCallback<PointerCaptureOutEvent>(Lost); }
            private void Down(PointerDownEvent evt)
            {
                if (m_View.m_Graph==null || m_View.m_Busy || m_View.m_Drag?.m_Active==true) { evt.StopImmediatePropagation(); return; }
                m_Item=m_Source();
                if (evt.button==1) { if (m_Item!=null) m_View.Context(m_Item,evt.position); evt.StopImmediatePropagation(); return; }
                if (evt.button!=0) return;
                if (evt.ctrlKey) { if (m_Item!=null) m_View.QuickTransfer(m_Item); evt.StopImmediatePropagation(); return; }
                m_View.CancelDrag(); m_View.m_Drag=this; m_ClickCount=evt.clickCount; m_ValidatedCommand=null;
                m_Pointer=evt.pointerId; m_Start=m_Last=evt.position; m_Rotated=m_Item?.Rotated ?? false;
                var rect=target.worldBound; m_Grab=new Vector2(Mathf.Clamp01((m_Start.x-rect.x)/rect.width),Mathf.Clamp01((m_Start.y-rect.y)/rect.height)); target.CapturePointer(m_Pointer);
                target.Focus(); evt.StopImmediatePropagation();
            }
            private void Move(PointerMoveEvent evt)
            {
                if (evt.pointerId!=m_Pointer) return; m_Last=evt.position;
                evt.StopImmediatePropagation();
                if (m_View.m_ReadOnlyReason != null) return;
                if (m_Item==null || !m_Active && Vector2.Distance(m_Start,m_Last)<4) return;
                if (!m_Active)
                {
                    m_Active=true; target.style.opacity=.4f; var ghost=m_View.m_Ghost; ghost.Clear();
                    var art=new StashItemArt(Art(m_Item.Code)); art.AddToClassList("inventory-item-art"); ghost.Add(art); ghost.style.display=DisplayStyle.Flex; ghost.BringToFront();
                    m_Scroll=target.schedule.Execute(Scroll).Every(16);
                }
                Preview(); evt.StopImmediatePropagation();
            }
            private void Preview()
            {
                var def=InventoryCatalog.Get(m_Item.Code); float cell=40;
                var point=m_View.m_Root.WorldToLocal(m_Last); var ghost=m_View.m_Ghost;
                m_View.m_Preview.style.display=DisplayStyle.None; m_Command=null;
                foreach (var slot in m_View.m_Slots.Values) { slot.RemoveFromClassList("inventory-drop-valid"); slot.RemoveFromClassList("inventory-drop-invalid"); }
                // Later grids are open windows and visually above the underlying container.
                for (int n=m_View.m_Grids.Count-1; n>=0; n--)
                {
                    var grid=m_View.m_Grids[n]; if (grid.Element.panel==null || !grid.Element.worldBound.Contains(m_Last)) continue;
                    if(grid.Definition.SlotKind.HasValue && m_View.m_Graph.Find(LootInventoryExchange.Root)!=null)continue;
                    var scroll=grid.Element.GetFirstAncestorOfType<ScrollView>(); if (scroll!=null && !scroll.contentViewport.worldBound.Contains(m_Last)) continue;
                    bool slot=grid.Definition.SlotKind.HasValue; var local=grid.Element.WorldToLocal(m_Last);
                    cell=grid.Cell;
                    int x=slot?0:Mathf.FloorToInt(local.x/grid.Cell)-Mathf.FloorToInt(m_Grab.x*(m_Rotated?def.Height:def.Width));
                    int y=slot?0:Mathf.FloorToInt(local.y/grid.Cell)-Mathf.FloorToInt(m_Grab.y*(m_Rotated?def.Width:def.Height));
                    m_Command=new InventoryCommand { ExpectedVersion=m_View.m_Graph.Version, ItemId=m_Item.Id, Parent=grid.Parent, Region=grid.Region, X=x,Y=y,Rotated=m_Rotated };
                    if (!slot) foreach (var entry in m_View.m_Graph.Children(grid.Parent,grid.Region))
                        if (entry.Id!=m_Item.Id && entry.Code==m_Item.Code && !def.Container && m_View.m_Tiles.TryGetValue(entry.Id,out var tile) && tile.worldBound.Contains(m_Last))
                        { m_Command.Operation=InventoryOperation.Merge; m_Command.TargetId=entry.Id; break; }
                    if (!SamePlacement(m_ValidatedCommand,m_Command))
                    { m_PlacementError=m_View.m_Graph.Clone().TryApply(m_Command); m_ValidatedCommand=m_Command; }
                    bool valid=m_PlacementError==InventoryError.None;
                    if (slot) grid.Element.AddToClassList(valid?"inventory-drop-valid":"inventory-drop-invalid");
                    else
                    {
                        var preview=m_View.m_Preview; grid.Element.Add(preview); preview.style.display=DisplayStyle.Flex;
                        preview.style.left=x*grid.Cell; preview.style.top=y*grid.Cell; preview.style.width=(m_Rotated?def.Height:def.Width)*grid.Cell; preview.style.height=(m_Rotated?def.Width:def.Height)*grid.Cell;
                        preview.EnableInClassList("inventory-drop-valid",valid); preview.EnableInClassList("inventory-drop-invalid",!valid);
                    }
                    break;
                }
                float w=(m_Rotated?def.Height:def.Width)*cell, h=(m_Rotated?def.Width:def.Height)*cell;
                ghost.style.left=point.x-m_Grab.x*w; ghost.style.top=point.y-m_Grab.y*h; ghost.style.width=w; ghost.style.height=h;
            }
            private static bool SamePlacement(InventoryCommand a, InventoryCommand b) => a!=null && b!=null &&
                a.ExpectedVersion==b.ExpectedVersion && a.Parent==b.Parent && a.Region==b.Region && a.X==b.X && a.Y==b.Y &&
                a.Rotated==b.Rotated && a.Operation==b.Operation && a.TargetId==b.TargetId;
            private void Scroll()
            {
                foreach (var grid in m_View.m_Grids)
                {
                    var scroll=grid.Element.GetFirstAncestorOfType<ScrollView>(); if (scroll==null) continue; var rect=scroll.contentViewport.worldBound;
                    if (!rect.Contains(m_Last)) continue; float speed=m_Last.y<rect.yMin+24?-7:m_Last.y>rect.yMax-24?7:0;
                    if (speed==0) continue; scroll.scrollOffset=new Vector2(0,Mathf.Clamp(scroll.scrollOffset.y+speed,0,Mathf.Max(0,scroll.contentContainer.resolvedStyle.height-rect.height))); Preview(); break;
                }
            }
            public void Rotate() { if (m_Active) { m_Rotated=!m_Rotated; Preview(); } }
            private void Up(PointerUpEvent evt)
            {
                if (evt.pointerId!=m_Pointer) return;
                if (!m_Active)
                {
                    bool click=Vector2.Distance(m_Start,evt.position)<4 && target.worldBound.Contains(evt.position);
                    Cancel(); evt.StopImmediatePropagation();
                    if (click) { if (m_ClickCount>1) m_DoubleClick?.Invoke(); else m_Click?.Invoke(); }
                    return;
                }
                m_Last=evt.position; Preview(); var command=m_Command; Cancel(); evt.StopImmediatePropagation();
                if (command!=null) m_View.Command(command); else MoonkovLocalization.Set(m_View.m_Message, "Move cancelled. Item remains in its container.");
            }
            private void Lost(PointerCaptureOutEvent evt) { if (evt.target==target && evt.pointerId==m_Pointer) Cancel(); }
            public void Cancel()
            {
                m_Scroll?.Pause(); m_Scroll=null; int pointer=m_Pointer; m_Pointer=-1;
                if (pointer>=0 && target.HasPointerCapture(pointer)) target.ReleasePointer(pointer); target.style.opacity=1;
                m_Active=false; if (m_View.m_Drag==this) m_View.m_Drag=null;
                m_View.m_Ghost.style.display=DisplayStyle.None; m_View.m_Preview.style.display=DisplayStyle.None;
                foreach (var slot in m_View.m_Slots.Values) { slot.RemoveFromClassList("inventory-drop-valid"); slot.RemoveFromClassList("inventory-drop-invalid"); }
            }
        }
    }
}
