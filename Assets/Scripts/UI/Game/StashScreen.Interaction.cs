using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class StashScreen
    {
        private StashLayout m_Layout;
        private MoonkovTerminal m_Terminal;
        private VisualElement m_DropPreview;
        private GridDrag m_ActiveDrag;
        public void NavigatePage(int page) => m_Terminal.Navigate(page);
        private void PrepareTerminal() => m_Terminal.Prepare();
        private void ShipTerminal() => m_Terminal.Navigate(0);

        private void InitializeTerminal()
        {
            m_Layout = new StashLayout(Columns, Rows);
            m_Loadout = new StashLoadout(m_Layout);
            foreach (var stack in m_Stacks)
            {
                m_Layout.Add(stack.Name, stack.Width, stack.Height, -100, -100);
                m_Loadout.Register(stack.Name, stack.Kind);
                if (m_Layout.FindSpace(stack.Name, out var space)) m_Layout.TryMove(stack.Name, space.x, space.y, false);
                var drag = new GridDrag(this, stack); stack.Tile.AddManipulator(drag);
                stack.Tile.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 1) { Context(stack, m_Root.WorldToLocal(evt.position)); evt.StopPropagation(); }
                    else if (evt.button == 2 || evt.clickCount == 2) { Inspect(stack); evt.StopPropagation(); }
                    else if (evt.ctrlKey || evt.altKey)
                    {
                        QuickEquip(stack); evt.StopImmediatePropagation();
                    }
                }, TrickleDown.TrickleDown);
            }
            m_DropPreview = new VisualElement { pickingMode = PickingMode.Ignore };
            m_DropPreview.AddToClassList("stash-drop-preview"); m_DropPreview.style.display = DisplayStyle.None;
            m_Grid.Add(m_DropPreview);
            m_DragGhost = new VisualElement { pickingMode = PickingMode.Ignore };
            m_DragGhost.AddToClassList("stash-drag-ghost"); m_DragGhost.style.display = DisplayStyle.None; m_Root.Add(m_DragGhost);
            m_Root.RegisterCallback<KeyDownEvent>(ItemKey, TrickleDown.TrickleDown);
            m_Terminal = new MoonkovTerminal(m_Root, m_OnPrepare);
            m_Terminal.Navigating += CancelDrag;
            m_Root.Q<Label>(className: "stash-footer-hint").text = "DRAG / MOVE     R / ROTATE     CTRL + CLICK / EQUIP     ESC / BACK";
            m_Root.Q<Label>(className: "stash-inspect-help").text = "Drag to arrange · R to rotate\nDouble click to inspect · Right click for actions";
            m_Sort.clicked += Arrange;
            var inspect = new Button(() => { if (m_Selected != null) Inspect(m_Selected); }) { text = "OPEN INSPECTION" };
            inspect.AddToClassList("terminal-button"); m_Root.Q<VisualElement>(className: "stash-inspector").Add(inspect);
            var medical = new Button(Health) { text = "HEALTH / SUIT STATUS" }; medical.AddToClassList("terminal-button");
            m_Root.Q<VisualElement>(className: "stash-character-pane").Add(medical);
        }
        private void Arrange()
        {
            var previous = new System.Collections.Generic.Dictionary<string, Vector2Int>();
            foreach (var stack in m_Visible)
            {
                var item = m_Layout.Items[stack.Name]; if (item.Locked) continue;
                previous.Add(stack.Name, new Vector2Int(item.X, item.Y)); item.X = item.Y = -100;
            }
            foreach (var stack in m_Visible)
            {
                var placement = m_Layout.Items[stack.Name]; if (placement.Locked) continue;
                bool placed = false;
                for (int y = 0; y < Rows; y++)
                {
                    for (int x = 0; x < Columns; x++) if (m_Layout.TryMove(stack.Name, x, y, placement.Rotated)) { placed = true; break; }
                    if (placed) break;
                }
                if (!placed)
                {
                    foreach (var pair in previous) { m_Layout.Items[pair.Key].X = pair.Value.x; m_Layout.Items[pair.Key].Y = pair.Value.y; }
                    m_Terminal.Windows.Toast("Storage cannot be rearranged around locked positions."); break;
                }
            }
            Layout();
        }
        private void ItemKey(KeyDownEvent evt)
        {
            if (evt.target is VisualElement focused && focused.GetFirstAncestorOfType<TextField>() != null) return;
            if (evt.keyCode == KeyCode.Escape && m_ActiveDrag != null)
            { CancelDrag(); evt.StopImmediatePropagation(); return; }
            if (evt.keyCode == KeyCode.R)
            {
                if (m_ActiveDrag != null) m_ActiveDrag.Rotate();
                else if (m_Selected != null)
                {
                    var item = m_Layout.Items[m_Selected.Name];
                    if (!m_Layout.TryMove(m_Selected.Name, item.X, item.Y, !item.Rotated)) m_Terminal.Windows.Toast("Rotation blocked: locked item, collision or storage boundary.");
                    Layout();
                }
                evt.StopImmediatePropagation();
            }
            if (evt.keyCode == KeyCode.Delete && m_Selected != null)
            { m_Terminal.Windows.Toast("Discard is unavailable for account supply stacks."); evt.StopPropagation(); }
        }
        private void Inspect(Stack stack)
        {
            Select(stack);
            var window = m_Terminal.Windows.Open("ITEM INSPECTION");
            var art = new StashItemArt(stack.Art); art.AddToClassList("terminal-inspect-art"); window.Add(art);
            MoonkovTerminal.Text(window, stack.Name.ToUpperInvariant(), "terminal-title");
            MoonkovTerminal.Text(window, stack.Category, "terminal-eyebrow");
            MoonkovTerminal.Text(window, stack.Description, "terminal-copy");
            MoonkovTerminal.Stat(window, "OWNED", Number(stack.Quantity));
            var item = m_Layout.Items[stack.Name]; MoonkovTerminal.Stat(window, "FOOTPRINT", item.Width + " × " + item.Height);
            var location = m_Loadout.Location(stack.Name);
            MoonkovTerminal.Stat(window, "LOCATION", location == LoadoutSlot.None ? "PERSONAL STASH" : SlotName(location));
            if (stack.Kind == EquipmentKind.None) MoonkovTerminal.Text(window, "Supply stacks are stored as account totals. Placement and rotation only arrange this display.", "terminal-copy");
        }
        private void Context(Stack stack, Vector2 position)
        {
            Select(stack);
            position.x = Mathf.Min(position.x, Mathf.Max(0, m_Root.resolvedStyle.width - 270));
            position.y = Mathf.Min(position.y, Mathf.Max(0, m_Root.resolvedStyle.height - 360));
            var window = m_Terminal.Windows.Open(stack.ShortName, position, true);
            MoonkovTerminal.ActionButton(window, "INSPECT", () => { m_Terminal.Windows.Close(window); Inspect(stack); });
            var item = m_Layout.Items[stack.Name];
            MoonkovTerminal.ActionButton(window, item.Locked ? "UNLOCK POSITION" : "LOCK POSITION", () =>
            {
                item.Locked = !item.Locked; stack.Tile.EnableInClassList("stash-item-locked", item.Locked);
                m_Terminal.Windows.Close(window); m_Terminal.Windows.Toast(item.Locked ? "Position locked." : "Position unlocked.");
            });
            MoonkovTerminal.ActionButton(window, item.Favorite ? "REMOVE FAVORITE" : "FAVORITE", () =>
            {
                item.Favorite = !item.Favorite;
                stack.Tile.tooltip = stack.Name + (item.Favorite ? " / FAVORITE" : "");
                stack.Tile.EnableInClassList("stash-item-favorite", item.Favorite); m_Terminal.Windows.Close(window);
            });
            if (stack.Kind != EquipmentKind.None)
                MoonkovTerminal.ActionButton(window, "EQUIP", () => { m_Terminal.Windows.Close(window); QuickEquip(stack); });
            else MoonkovTerminal.Disabled(window, "TRANSFER / EQUIP", "Account supplies cannot be transferred to equipment.");
            MoonkovTerminal.Disabled(window, "SELL / SALVAGE", "No trading contract is available.");
            MoonkovTerminal.Disabled(window, "DISCARD", "Discarding account totals is unavailable.");
        }
        private void Health()
        {
            var window = m_Terminal.Windows.Open("MEDICAL / SUIT DIAGNOSTICS");
            MoonkovTerminal.Text(window, "AWAITING BODY TELEMETRY", "terminal-title");
            foreach (var part in new[] { "HEAD", "THORAX", "ABDOMEN", "LEFT ARM", "RIGHT ARM", "LEFT LEG", "RIGHT LEG" })
                MoonkovTerminal.Stat(window, part, "—");
            MoonkovTerminal.Stat(window, "OXYGEN / PRESSURE / RADIATION", "—");
            MoonkovTerminal.Text(window, "Part-specific health and suit sensors are not available. Your current overall health is shown during the raid.", "terminal-copy");
            MoonkovTerminal.Disabled(window, "TREAT ALL", "Medical treatment is unavailable.");
        }
        private void DisposeTerminal()
        {
            m_ActiveDrag?.Cancel(); m_ActiveDrag = null;
            m_Sort.clicked -= Arrange; m_Root.UnregisterCallback<KeyDownEvent>(ItemKey, TrickleDown.TrickleDown);
            m_Terminal.Navigating -= CancelDrag; m_Terminal.Dispose(); m_DragGhost.RemoveFromHierarchy();
        }

        private void QuickEquip(Stack stack)
        {
            foreach (var slot in new[] { LoadoutSlot.Helmet, LoadoutSlot.Primary, LoadoutSlot.Secondary, LoadoutSlot.Pistol, LoadoutSlot.ChestRig, LoadoutSlot.Backpack })
                if (m_Loadout.Accepts(stack.Name, slot) && m_Loadout.At(slot) == null)
                { if (m_Loadout.TryEquip(stack.Name, slot)) Rebuild(); else m_Terminal.Windows.Toast("This item is locked."); return; }
            if (stack.Kind == EquipmentKind.None) m_Terminal.Windows.Toast("Supplies cannot be equipped.");
            else foreach (var slot in s_SlotOrder) if (m_Loadout.Accepts(stack.Name, slot)) { ChooseEquipment(slot); return; }
        }

        private sealed class GridDrag : PointerManipulator
        {
            private readonly StashScreen m_Owner;
            private readonly Func<Stack> m_Source;
            private Stack m_Stack;
            private int m_Pointer = -1, m_X, m_Y;
            private bool m_Dragging, m_Rotated;
            private Vector2 m_Start, m_Last, m_Grab;
            private IVisualElementScheduledItem m_ScrollTick;
            public GridDrag(StashScreen owner, Stack stack) : this(owner, () => stack) { }
            public GridDrag(StashScreen owner, Func<Stack> source) { m_Owner = owner; m_Source = source; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown); target.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown); target.RegisterCallback<PointerCaptureOutEvent>(Lost);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel();
                target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown); target.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown); target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            }
            private void Down(PointerDownEvent evt)
            {
                if (evt.button != 0 || evt.ctrlKey || evt.altKey || evt.clickCount > 1) return;
                m_Stack = m_Source(); if (m_Stack == null) return;
                m_Owner.Select(m_Stack); if (m_Owner.m_Layout.Items[m_Stack.Name].Locked) return;
                m_Owner.CancelDrag();
                m_Pointer = evt.pointerId; m_Start = m_Last = evt.position;
                m_Rotated = m_Owner.m_Layout.Items[m_Stack.Name].Rotated;
                var bounds = target.worldBound;
                m_Grab = new Vector2(Mathf.Clamp01((m_Start.x - bounds.x) / bounds.width), Mathf.Clamp01((m_Start.y - bounds.y) / bounds.height));
                m_Owner.m_ActiveDrag = this;
                target.CapturePointer(m_Pointer);
            }
            private void Move(PointerMoveEvent evt)
            {
                if (m_Pointer != evt.pointerId) return;
                m_Last = evt.position;
                if (!m_Dragging && Vector2.Distance(m_Start, m_Last) < 5) return;
                if (!m_Dragging)
                {
                    m_Dragging = true; target.AddToClassList("stash-item-dragging");
                    var ghost = m_Owner.m_DragGhost; ghost.Clear();
                    var art = new StashItemArt(m_Stack.Art); art.AddToClassList("stash-item-art"); ghost.Add(art);
                    var label = new Label(m_Stack.ShortName) { pickingMode = PickingMode.Ignore }; label.AddToClassList("stash-item-name"); ghost.Add(label);
                    ghost.style.display = DisplayStyle.Flex; ghost.BringToFront();
                    m_ScrollTick = target.schedule.Execute(Scroll).Every(16);
                }
                Preview(); evt.StopImmediatePropagation();
            }
            private void Preview()
            {
                float cell = m_Owner.m_CellSize; if (cell <= 0) return;
                var item = m_Owner.m_Layout.Items[m_Stack.Name];
                int w = m_Rotated ? item.BaseHeight : item.BaseWidth, h = m_Rotated ? item.BaseWidth : item.BaseHeight;
                Vector2 offset = new Vector2(m_Grab.x * w * cell, m_Grab.y * h * cell);
                var local = m_Owner.m_Grid.WorldToLocal(m_Last) - offset;
                m_X = Mathf.RoundToInt(local.x / cell); m_Y = Mathf.RoundToInt(local.y / cell);
                var point = m_Owner.m_Root.WorldToLocal(m_Last) - offset;
                var ghost = m_Owner.m_DragGhost;
                ghost.style.left = point.x; ghost.style.top = point.y; ghost.style.width = w * cell; ghost.style.height = h * cell;
                m_Owner.HoverSlot(m_Owner.SlotAt(m_Last), m_Stack);
                bool within = m_Owner.m_Scroll.contentViewport.worldBound.Contains(m_Last);
                var preview = m_Owner.m_DropPreview; preview.style.display = within ? DisplayStyle.Flex : DisplayStyle.None;
                preview.style.left = m_X * cell; preview.style.top = m_Y * cell; preview.style.width = w * cell; preview.style.height = h * cell;
                bool valid = within && m_Owner.m_Layout.CanPlace(m_Stack.Name, m_X, m_Y, m_Rotated);
                preview.EnableInClassList("stash-drop-valid", valid); preview.EnableInClassList("stash-drop-invalid", !valid); preview.BringToFront();
            }
            public void Rotate() { if (!m_Dragging) return; m_Rotated = !m_Rotated; Preview(); }
            private void Scroll()
            {
                var viewport = m_Owner.m_Scroll.contentViewport.worldBound;
                if (m_Last.x < viewport.xMin || m_Last.x > viewport.xMax) return;
                float speed = m_Last.y < viewport.yMin + 30 && m_Last.y > viewport.yMin - 12 ? -7
                    : m_Last.y > viewport.yMax - 30 && m_Last.y < viewport.yMax + 12 ? 7 : 0;
                if (speed == 0) return;
                var scroll = m_Owner.m_Scroll;
                float next = Mathf.Clamp(scroll.scrollOffset.y + speed, 0, Mathf.Max(0, m_Owner.m_Grid.resolvedStyle.height - viewport.height));
                scroll.scrollOffset = new Vector2(0, next); Preview();
            }
            private void Up(PointerUpEvent evt)
            {
                if (m_Pointer != evt.pointerId) return;
                if (!m_Dragging)
                {
                    // Let Button's Clickable finish the click and release its capture.
                    m_Pointer = -1; m_Owner.m_ActiveDrag = null; return;
                }
                if (m_Dragging)
                {
                    m_Last = evt.position; Preview();
                    bool within = m_Owner.m_Scroll.contentViewport.worldBound.Contains(m_Last);
                    bool placed = m_Owner.m_HoverSlot != LoadoutSlot.None ? m_Owner.m_Loadout.TryEquip(m_Stack.Name, m_Owner.m_HoverSlot)
                        : within && m_Owner.m_Loadout.TryStore(m_Stack.Name, m_X, m_Y, m_Rotated);
                    if (!placed) m_Owner.m_Terminal.Windows.Toast("Placement blocked. Item returned to its previous position.");
                    evt.StopImmediatePropagation();
                }
                Cancel(); m_Owner.Rebuild();
            }
            private void Lost(PointerCaptureOutEvent evt) { Cancel(); }
            public void Cancel()
            {
                m_ScrollTick?.Pause(); m_ScrollTick = null;
                int pointer = m_Pointer; m_Pointer = -1;
                if (pointer >= 0 && target.HasPointerCapture(pointer)) target.ReleasePointer(pointer);
                target.RemoveFromClassList("stash-item-dragging");
                m_Dragging = false; m_Owner.m_ActiveDrag = null;
                if (m_Owner.m_DropPreview != null) m_Owner.m_DropPreview.style.display = DisplayStyle.None;
                if (m_Owner.m_DragGhost != null) m_Owner.m_DragGhost.style.display = DisplayStyle.None;
                m_Owner.ClearSlotHover();
                m_Owner.Layout();
            }
        }
    }
}
