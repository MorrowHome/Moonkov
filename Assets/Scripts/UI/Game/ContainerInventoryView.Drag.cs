using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Inventory;
using PointerType = UnityEngine.UIElements.PointerType;

namespace Unity.MP_FPS.Client {
    public sealed partial class ContainerInventoryView {
        private sealed class Drag : PointerManipulator {
            private readonly ContainerInventoryView m_View;
            private readonly Func<InventoryItem> m_Source;
            private readonly Action m_Click, m_DoubleClick;
            private InventoryItem m_Item;
            private int m_Pointer = -1;
            private bool m_Active, m_Rotated;
            private Vector2 m_Start, m_Last, m_Grab;
            private InventoryCommand m_Command;
            private InventoryCommand m_ValidatedCommand;
            private InventoryError m_PlacementError;
            private int m_ClickCount;
            private IVisualElementScheduledItem m_Scroll, m_ReleaseRecovery;

            public Drag(ContainerInventoryView view, Func<InventoryItem> source, Action click, Action doubleClick) {
                m_View = view;
                m_Source = source;
                m_Click = click;
                m_DoubleClick = doubleClick;
            }

            public bool CanRetain(InventoryGraph graph) {
                // 只有抓着的物品仍在同一容器、同一格且朝向相同，才保留拖动。数量等变化可刷新，位置变化需要重画。
                var current = m_Item == null ? null : graph?.Find(m_Item.Id);
                return current != null && current.Parent == m_Item.Parent && current.Region == m_Item.Region &&
                       current.X == m_Item.X && current.Y == m_Item.Y && current.Rotated == m_Item.Rotated;
            }

            public void RefreshItem() {
                if (m_Item != null) m_Item = m_View.m_Graph.Find(m_Item.Id);
            }

            protected override void RegisterCallbacksOnTarget() {
                target.RegisterCallback<PointerEnterEvent>(Enter);
                target.RegisterCallback<PointerLeaveEvent>(Leave);
                target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Cancelled);
                target.RegisterCallback<DetachFromPanelEvent>(Detached);
            }

            protected override void UnregisterCallbacksFromTarget() {
                Cancel();
                target.UnregisterCallback<PointerEnterEvent>(Enter);
                target.UnregisterCallback<PointerLeaveEvent>(Leave);
                target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Cancelled);
                target.UnregisterCallback<DetachFromPanelEvent>(Detached);
            }

            private void Enter(PointerEnterEvent evt) {
                var item = m_Source();
                m_View.m_HoveredItemId = item?.Id;
                if (evt.pointerType == PointerType.mouse && evt.pressedButtons == 0)
                    m_View.ShowItemTooltip(target, item, evt.position);
            }

            private void Leave(PointerLeaveEvent evt) {
                m_View.HideItemTooltip(target);
                if (m_View.m_HoveredItemId == m_Source()?.Id) m_View.m_HoveredItemId = null;
            }

            private void Down(PointerDownEvent evt) {
                m_View.HideItemTooltip();
                if (m_View.m_Graph == null) {
                    evt.StopImmediatePropagation();
                    return;
                }
                if (evt.button == 1 && m_View.m_Drag != null) {
                    m_View.CancelDrag();
                    evt.StopImmediatePropagation();
                    return;
                }
                if (evt.button == 0 && m_ReleaseRecovery != null) Cancel();
                // 同一次按住收到多次按下通知时，保留正在处理的拖动。
                if (evt.button == 0 && m_View.m_Drag == this && m_Pointer == evt.pointerId) {
                    evt.StopImmediatePropagation();
                    return;
                }
                m_Item = m_Source();
                if (evt.button == 1) {
                    if (m_Item != null) m_View.Context(m_Item, evt.position);
                    evt.StopImmediatePropagation();
                    return;
                }
                if (evt.button != 0) return;
                // 带修饰键的左键走连续收纳，普通左键进入可能的点击或拖动。右键显示菜单已经在上面单独处理。
                if (evt.ctrlKey || evt.shiftKey) {
                    m_View.CancelDrag();
                    if (m_Item != null) m_View.QuickTransfer(m_Item);
                    evt.StopImmediatePropagation();
                    return;
                }
                m_View.CancelDrag();
                m_View.m_Drag = this;
                m_ClickCount = evt.clickCount;
                m_ValidatedCommand = null;
                m_Pointer = evt.pointerId;
                m_Start = m_Last = evt.position;
                m_Rotated = m_Item?.Rotated ?? false;
                // 记住抓住图标的相对位置。旋转和跨格子拖动时，鼠标仍能抓在图标对应位置。
                var rect = target.worldBound;
                m_Grab = new Vector2(Mathf.Clamp01((m_Start.x - rect.x) / rect.width),
                    Mathf.Clamp01((m_Start.y - rect.y) / rect.height));
                target.CapturePointer(m_Pointer);
                target.Focus();
                evt.StopImmediatePropagation();
            }

            private void Move(PointerMoveEvent evt) {
                if (m_Pointer < 0 && evt.pointerType == PointerType.mouse && evt.pressedButtons == 0)
                    m_View.ShowItemTooltip(target, m_Source(), evt.position);
                if (m_Pointer < 0 || evt.pointerId != m_Pointer) return;
                // Input System 可能先发未按住鼠标的移动，再发排队中的 PointerUp。
                // 先让松手事件正常落下物品，再处理真正漏收的松手。
                // 同一批输入里，没按键的移动事件可能比松手先到。延后做兜底取消，给正常的松手事件机会提交落位。
                if ((evt.pressedButtons & 1) == 0) {
                    m_ReleaseRecovery ??= target.schedule.Execute(Cancel);
                    evt.StopImmediatePropagation();
                    return;
                }
                m_ReleaseRecovery?.Pause();
                m_ReleaseRecovery = null;
                m_Last = evt.position;
                evt.StopImmediatePropagation();
                if (m_View.m_ReadOnlyReason != null || m_View.m_Busy) return;
                // 超过很小的位移才算拖动。少量手抖仍视为点击。
                if (m_Item == null || (!m_Active && Vector2.Distance(m_Start, m_Last) < 4)) return;
                if (!m_Active) {
                    m_Active = true;
                    target.style.opacity = .4f;
                    var ghost = m_View.m_Ghost;
                    ghost.Clear();
                    var art = new StashItemArt(m_Item.Code);
                    art.AddToClassList("inventory-item-art");
                    ghost.Add(art);
                    ghost.style.display = DisplayStyle.Flex;
                    ghost.BringToFront();
                    m_Scroll = target.schedule.Execute(Scroll).Every(16);
                }
                Preview();
                evt.StopImmediatePropagation();
            }

            // 从上往下找鼠标下可用的格子。预览同时记住请求版本和落位，变化时才重新用库存副本试放。
            private void Preview() {
                m_View.QuickSlotDragPreview?.Invoke(m_Item, m_Last);
                var def = InventoryCatalog.Get(m_Item.Code);
                float cell = 40;
                var point = m_View.m_Root.WorldToLocal(m_Last);
                var ghost = m_View.m_Ghost;
                m_View.m_Preview.style.display = DisplayStyle.None;
                m_Command = null;
                foreach (var targetGrid in m_View.m_Grids) {
                    targetGrid.Element.RemoveFromClassList("inventory-drop-valid");
                    targetGrid.Element.RemoveFromClassList("inventory-drop-invalid");
                }
                // 较晚加入的格子来自浮窗，显示在原有容器上方。
                // 后加入的格子一般是更上层的浮窗。先试它们，免得在浮窗里松手却命中背后的背包。
                for (int n = m_View.m_Grids.Count - 1; n >= 0; n--) {
                    var grid = m_View.m_Grids[n];
                    if (!VisibleDropTarget(grid.Element, m_Last)) continue;
                    bool slot = grid.Definition.SlotKind.HasValue;
                    var local = grid.Element.WorldToLocal(m_Last);
                    cell = grid.Cell;
                    int x = slot
                        ? 0
                        : Mathf.FloorToInt(local.x / grid.Cell) -
                          Mathf.FloorToInt(m_Grab.x * (m_Rotated ? def.Height : def.Width));
                    int y = slot
                        ? 0
                        : Mathf.FloorToInt(local.y / grid.Cell) -
                          Mathf.FloorToInt(m_Grab.y * (m_Rotated ? def.Width : def.Height));
                    // 紧凑排列的视图坐标只用于显示。拖进去时向库存查询实际可用空位。
                    if (grid.AutoPlace) {
                        var placement = m_Item.Clone();
                        placement.Rotated = m_Rotated;
                        m_View.m_Graph.FindSpace(placement, grid.Parent, out _, out x, out y);
                    }
                    m_Command = new InventoryCommand {
                        ExpectedVersion = m_View.m_Graph.Version, ItemId = m_Item.Id, Parent = grid.Parent,
                        Region = grid.Region, X = x, Y = y, Rotated = m_Rotated
                    };
                    if (!slot)
                        foreach (var entry in m_View.m_Graph.Children(grid.Parent, grid.Region))
                            if (entry.Id != m_Item.Id && entry.Code == m_Item.Code && !def.Container &&
                                m_View.m_Tiles.TryGetValue(entry.Id, out var tile) &&
                                tile.worldBound.Contains(m_Last)) {
                                m_Command.Operation = InventoryOperation.Merge;
                                m_Command.TargetId = entry.Id;
                                break;
                            }
                    // 同一个落点在同一库存版本里只需试一次。变了格位、方向、目标或库存，再用副本检查并调整绿框。
                    if (!SamePlacement(m_ValidatedCommand, m_Command)) {
                        m_PlacementError = m_View.m_Graph.Clone().TryApply(m_Command);
                        m_ValidatedCommand = m_Command;
                    }
                    bool valid = m_PlacementError == InventoryError.None;
                    if (slot || grid.AutoPlace) {
                        grid.Element.AddToClassList(valid ? "inventory-drop-valid" : "inventory-drop-invalid");
                    } else {
                        var preview = m_View.m_Preview;
                        grid.Element.Add(preview);
                        preview.style.display = DisplayStyle.Flex;
                        preview.style.left = x * grid.Cell;
                        preview.style.top = y * grid.Cell;
                        preview.style.width = (m_Rotated ? def.Height : def.Width) * grid.Cell;
                        preview.style.height = (m_Rotated ? def.Width : def.Height) * grid.Cell;
                        preview.EnableInClassList("inventory-drop-valid", valid);
                        preview.EnableInClassList("inventory-drop-invalid", !valid);
                    }
                    break;
                }
                float w = (m_Rotated ? def.Height : def.Width) * cell, h = (m_Rotated ? def.Width : def.Height) * cell;
                ghost.style.left = point.x - m_Grab.x * w;
                ghost.style.top = point.y - m_Grab.y * h;
                ghost.style.width = w;
                ghost.style.height = h;
            }

            private static bool SamePlacement(InventoryCommand a, InventoryCommand b) {
                return a != null && b != null && a.ExpectedVersion == b.ExpectedVersion && a.Parent == b.Parent &&
                       a.Region == b.Region && a.X == b.X && a.Y == b.Y && a.Rotated == b.Rotated &&
                       a.Operation == b.Operation && a.TargetId == b.TargetId;
            }

            private void Scroll() {
                foreach (var grid in m_View.m_Grids) {
                    var scroll = grid.Element.GetFirstAncestorOfType<ScrollView>();
                    if (scroll == null) continue;
                    var rect = scroll.contentViewport.worldBound;
                    if (!rect.Contains(m_Last)) continue;
                    float speed = m_Last.y < rect.yMin + 24 ? -7 : m_Last.y > rect.yMax - 24 ? 7 : 0;
                    if (speed == 0) continue;
                    scroll.scrollOffset = new Vector2(0,
                        Mathf.Clamp(scroll.scrollOffset.y + speed, 0,
                            Mathf.Max(0, scroll.contentContainer.resolvedStyle.height - rect.height)));
                    Preview();
                    break;
                }
            }

            public void Rotate() {
                if (m_Active) {
                    m_Rotated = !m_Rotated;
                    Preview();
                }
            }

            private void Up(PointerUpEvent evt) {
                if (m_Pointer < 0 || evt.pointerId != m_Pointer || evt.button != 0) return;
                if (!m_Active) {
                    bool click = Vector2.Distance(m_Start, evt.position) < 4 &&
                                 target.worldBound.Contains(evt.position);
                    Cancel();
                    evt.StopImmediatePropagation();
                    if (click) {
                        if (m_ClickCount > 1)
                            m_DoubleClick?.Invoke();
                        else
                            m_Click?.Invoke();
                    }
                    return;
                }
                m_Last = evt.position;
                // 先判断松手处是不是快捷栏，只有没被快捷栏处理才尝试发搬运命令。先重算落点，避免使用上一帧的绿色预览。
                bool quickDrop = m_View.QuickSlotDrop?.Invoke(m_Item, m_Last) == true;
                Preview();
                var command = m_Command;
                Cancel();
                evt.StopImmediatePropagation();
                if (quickDrop) return;
                if (command != null)
                    m_View.Command(command);
                else
                    MoonkovLocalization.Set(m_View.m_Message, "Move cancelled. Item remains in its container.");
            }

            private void Lost(PointerCaptureOutEvent evt) {
                if (evt.target == target && evt.pointerId == m_Pointer) Cancel();
            }

            private void Cancelled(PointerCancelEvent evt) {
                if (evt.pointerId == m_Pointer) Cancel();
            }

            private void Detached(DetachFromPanelEvent evt) {
                m_View.HideItemTooltip(target);
                if (m_View.m_Drag == this) Cancel();
            }

            public void Cancel() {
                // 当前拖动结束时一起释放指针、定时滚动、备用松手处理和视觉状态，后续事件才不会引用旧格子。
                if (m_View.m_Drag != this) return;
                m_View.QuickSlotDragPreview?.Invoke(null, default);
                m_ReleaseRecovery?.Pause();
                m_ReleaseRecovery = null;
                m_Scroll?.Pause();
                m_Scroll = null;
                int pointer = m_Pointer;
                m_Pointer = -1;
                if (pointer >= 0 && target.HasPointerCapture(pointer)) target.ReleasePointer(pointer);
                target.style.opacity = 1;
                m_Active = false;
                if (m_View.m_Drag == this) m_View.m_Drag = null;
                m_View.m_Ghost.style.display = DisplayStyle.None;
                m_View.m_Preview.style.display = DisplayStyle.None;
                foreach (var targetGrid in m_View.m_Grids) {
                    targetGrid.Element.RemoveFromClassList("inventory-drop-valid");
                    targetGrid.Element.RemoveFromClassList("inventory-drop-invalid");
                }
                if (m_View.m_DeferredLayout != null) {
                    m_View.m_DeferredRender?.Pause();
                    m_View.m_DeferredRender = m_View.m_Root.schedule.Execute(m_View.FlushDeferredLayout);
                }
            }
        }
    }
}
