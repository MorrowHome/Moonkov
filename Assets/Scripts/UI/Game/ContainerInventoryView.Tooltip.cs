// 背包物品的悬修信息卡。记录当前指向的物品编号，从最新库存取内容，交给 MoonkovItemInformation 渲染。
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    public sealed partial class ContainerInventoryView {
        private VisualElement m_ItemTooltip, m_TooltipTarget;
        private string m_TooltipItemId;
        private Vector2 m_TooltipPointer;
        private MoonkovItemInformation m_ItemInformation;

        private void InitializeItemTooltip() {
            m_ItemTooltip = m_Root.Q("inventoryItemTooltip");
            m_ItemInformation = new MoonkovItemInformation(m_ItemTooltip);
            m_ItemTooltip.RegisterCallback<GeometryChangedEvent>(TooltipGeometryChanged);
            m_Root.RegisterCallback<GeometryChangedEvent>(TooltipGeometryChanged);
            m_Root.RegisterCallback<WheelEvent>(TooltipScrolled, TrickleDown.TrickleDown);
            m_Root.RegisterCallback<DetachFromPanelEvent>(TooltipDetached);
        }

        private void ShowItemTooltip(VisualElement target, InventoryItem item, Vector2 position) {
            // 打开右键菜单时可能收到一次新的鼠标进入事件。这里依然挡住信息卡，避免它抢在菜单上面。
            if (m_Disposed || m_Drag != null || m_Windows.HasContext || item == null || target.panel == null) return;
            // 鼠标在同一格内移动只计算位置。换了物品或目标元素才重新填详情。
            bool changed = m_TooltipTarget != target || m_TooltipItemId != item.Id;
            m_TooltipTarget = target;
            m_TooltipItemId = item.Id;
            m_TooltipPointer = m_Root.WorldToLocal(position);
            if (changed) RefreshItemTooltip();
            PlaceItemTooltip();
        }

        private void RefreshItemTooltip() {
            if (m_TooltipTarget == null) return;
            // 鼠标仍悬停时，物品可能已被移走。每次刷新按 ID 查，查不到就关闭信息卡。
            var item = m_Graph?.Find(m_TooltipItemId);
            if (item == null || m_TooltipTarget.panel == null || m_Drag != null) {
                HideItemTooltip();
                return;
            }
            m_ItemInformation.Present(item, m_Graph, WorkshopWeaponRegistry);
            if (!WorkshopWeaponRegistry) WorkshopWeaponRegistry = m_ItemInformation.WeaponRegistry;
            m_ItemTooltip.style.display = DisplayStyle.Flex;
            m_ItemTooltip.BringToFront();
        }

        private void PlaceItemTooltip() {
            if (m_TooltipTarget == null) return;
            const float margin = 8, offset = 18;
            var bounds = m_Root.contentRect;
            if (bounds.width <= margin * 2 || bounds.height <= margin * 2) return;
            m_ItemTooltip.style.width = Mathf.Min(400, bounds.width - margin * 2);
            m_ItemTooltip.style.maxHeight = bounds.height - margin * 2;
            float width = m_ItemTooltip.resolvedStyle.width, height = m_ItemTooltip.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height)) return;
            float x = m_TooltipPointer.x + offset, y = m_TooltipPointer.y + offset;
            // 通常把卡片放在指针右下方。靠近右边或底边时翻到另一侧，再限制在界面以内。
            if (x + width > bounds.xMax - margin) x = m_TooltipPointer.x - width - offset;
            if (y + height > bounds.yMax - margin) y = m_TooltipPointer.y - height - offset;
            m_ItemTooltip.style.left = Mathf.Clamp(x, bounds.xMin + margin,
                Mathf.Max(bounds.xMin + margin, bounds.xMax - width - margin));
            m_ItemTooltip.style.top = Mathf.Clamp(y, bounds.yMin + margin,
                Mathf.Max(bounds.yMin + margin, bounds.yMax - height - margin));
        }

        private void HideItemTooltip(VisualElement target = null) {
            // 旧元素的离开事件可能晚于新元素的进入事件。只关闭它自己的卡片，免得把新卡盖掉。
            if (target != null && target != m_TooltipTarget) return;
            m_TooltipTarget = null;
            m_TooltipItemId = null;
            m_ItemTooltip.style.display = DisplayStyle.None;
        }

        private void TooltipGeometryChanged(GeometryChangedEvent evt) { PlaceItemTooltip(); }
        private void TooltipScrolled(WheelEvent evt) { HideItemTooltip(); }
        private void TooltipDetached(DetachFromPanelEvent evt) { HideItemTooltip(); }

        // 收掉父节点大小变化、滚动和离开面板的监听。销毁后的视图才能停止收到悬停事件。
        private void ReleaseItemTooltip() {
            HideItemTooltip();
            m_ItemTooltip.UnregisterCallback<GeometryChangedEvent>(TooltipGeometryChanged);
            m_Root.UnregisterCallback<GeometryChangedEvent>(TooltipGeometryChanged);
            m_Root.UnregisterCallback<WheelEvent>(TooltipScrolled, TrickleDown.TrickleDown);
            m_Root.UnregisterCallback<DetachFromPanelEvent>(TooltipDetached);
        }
    }
}