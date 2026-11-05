using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class ContainerInventoryView
    {
        private VisualElement m_ItemTooltip, m_TooltipTarget;
        private Label m_TooltipTitle, m_TooltipSummary, m_TooltipDetail, m_TooltipState;
        private string m_TooltipItemId;
        private Vector2 m_TooltipPointer;

        private void InitializeItemTooltip()
        {
            m_ItemTooltip = m_Root.Q("inventoryItemTooltip");
            m_TooltipTitle = m_ItemTooltip.Q<Label>("tooltipTitle");
            m_TooltipSummary = m_ItemTooltip.Q<Label>("tooltipSummary");
            m_TooltipDetail = m_ItemTooltip.Q<Label>("tooltipDetail");
            m_TooltipState = m_ItemTooltip.Q<Label>("tooltipState");
            m_ItemTooltip.RegisterCallback<GeometryChangedEvent>(TooltipGeometryChanged);
            m_Root.RegisterCallback<GeometryChangedEvent>(TooltipGeometryChanged);
            m_Root.RegisterCallback<WheelEvent>(TooltipScrolled, TrickleDown.TrickleDown);
            m_Root.RegisterCallback<DetachFromPanelEvent>(TooltipDetached);
        }

        private void ShowItemTooltip(VisualElement target, InventoryItem item, Vector2 position)
        {
            if (m_Disposed || m_Drag != null || m_Windows.HasContext || item == null || target.panel == null) return;
            bool changed = m_TooltipTarget != target || m_TooltipItemId != item.Id;
            m_TooltipTarget = target;
            m_TooltipItemId = item.Id;
            m_TooltipPointer = m_Root.WorldToLocal(position);
            if (changed) RefreshItemTooltip();
            PlaceItemTooltip();
        }

        private void RefreshItemTooltip()
        {
            if (m_TooltipTarget == null) return;
            var item = m_Graph?.Find(m_TooltipItemId);
            if (item == null || m_TooltipTarget.panel == null || m_Drag != null)
            {
                HideItemTooltip();
                return;
            }
            var definition = InventoryCatalog.Get(item.Code);
            MoonkovLocalization.Set(m_TooltipTitle, definition.Name);
            m_TooltipSummary.text = $"×{item.Quantity} / {definition.Weight * item.Quantity:0.##} kg";
            string detail = MoonkovItemPresentation.Details(item.Code);
            if (HaloModules.IsWeapon(item.Code)) detail += "\n" + HaloModules.Summary(m_Graph, item);
            if (HaloModules.FromCode(item.Code) != HaloModule.None)
                detail += "\n" + MoonkovLocalization.Text(HaloModules.Description(item.Code));
            m_TooltipDetail.text = detail.Replace(" · ", "\n");
            string state = MoonkovItemPresentation.ItemState(item);
            if (BatteryEnergy.IsCell(item))
                state = MoonkovLocalization.Format($"{BatteryEnergy.Stored(item)}/{item.Quantity * BatteryEnergy.Capacity} ENERGY / VALUE SCALES WITH CHARGE");
            m_TooltipState.text = state;
            m_TooltipState.style.display = string.IsNullOrEmpty(state) ? DisplayStyle.None : DisplayStyle.Flex;
            m_ItemTooltip.style.display = DisplayStyle.Flex;
            m_ItemTooltip.BringToFront();
        }

        private void PlaceItemTooltip()
        {
            if (m_TooltipTarget == null) return;
            const float margin = 8, offset = 18;
            var bounds = m_Root.contentRect;
            if (bounds.width <= margin * 2 || bounds.height <= margin * 2) return;
            m_ItemTooltip.style.width = Mathf.Min(360, bounds.width - margin * 2);
            m_ItemTooltip.style.maxHeight = bounds.height - margin * 2;
            float width = m_ItemTooltip.resolvedStyle.width, height = m_ItemTooltip.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height)) return;
            float x = m_TooltipPointer.x + offset, y = m_TooltipPointer.y + offset;
            if (x + width > bounds.xMax - margin) x = m_TooltipPointer.x - width - offset;
            if (y + height > bounds.yMax - margin) y = m_TooltipPointer.y - height - offset;
            m_ItemTooltip.style.left = Mathf.Clamp(x, bounds.xMin + margin, Mathf.Max(bounds.xMin + margin, bounds.xMax - width - margin));
            m_ItemTooltip.style.top = Mathf.Clamp(y, bounds.yMin + margin, Mathf.Max(bounds.yMin + margin, bounds.yMax - height - margin));
        }

        private void HideItemTooltip(VisualElement target = null)
        {
            if (target != null && target != m_TooltipTarget) return;
            m_TooltipTarget = null;
            m_TooltipItemId = null;
            m_ItemTooltip.style.display = DisplayStyle.None;
        }

        private void TooltipGeometryChanged(GeometryChangedEvent evt) => PlaceItemTooltip();
        private void TooltipScrolled(WheelEvent evt) => HideItemTooltip();
        private void TooltipDetached(DetachFromPanelEvent evt) => HideItemTooltip();

        private void ReleaseItemTooltip()
        {
            HideItemTooltip();
            m_ItemTooltip.UnregisterCallback<GeometryChangedEvent>(TooltipGeometryChanged);
            m_Root.UnregisterCallback<GeometryChangedEvent>(TooltipGeometryChanged);
            m_Root.UnregisterCallback<WheelEvent>(TooltipScrolled, TrickleDown.TrickleDown);
            m_Root.UnregisterCallback<DetachFromPanelEvent>(TooltipDetached);
        }
    }
}
