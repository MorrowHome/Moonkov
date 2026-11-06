using System.Text.RegularExpressions;
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
        private VisualElement m_TooltipStats;
        private readonly VisualElement[] m_TooltipStatRows = new VisualElement[7];
        private readonly Label[] m_TooltipStatLabels = new Label[7], m_TooltipStatValues = new Label[7];
        private static readonly Regex s_TooltipNumbers = new Regex(@"(?<![A-Za-z0-9])[+-]?\d+(?:[.,]\d+)?(?:\s*(?:%|°|秒|分钟|米|s\b|m\b))?");

        private void InitializeItemTooltip()
        {
            m_ItemTooltip = m_Root.Q("inventoryItemTooltip");
            m_TooltipTitle = m_ItemTooltip.Q<Label>("tooltipTitle");
            m_TooltipSummary = m_ItemTooltip.Q<Label>("tooltipSummary");
            m_TooltipDetail = m_ItemTooltip.Q<Label>("tooltipDetail");
            m_TooltipState = m_ItemTooltip.Q<Label>("tooltipState");
            m_TooltipStats = m_ItemTooltip.Q("tooltipStats");
            for (int i = 0; i < m_TooltipStatRows.Length; i++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("inventory-tooltip-stat"); m_TooltipStats.Add(row); m_TooltipStatRows[i] = row;
                m_TooltipStatLabels[i] = new Label { pickingMode = PickingMode.Ignore };
                m_TooltipStatLabels[i].AddToClassList("inventory-tooltip-stat-label"); row.Add(m_TooltipStatLabels[i]);
                m_TooltipStatValues[i] = new Label { name = "tooltipStatValue" + i, pickingMode = PickingMode.Ignore };
                m_TooltipStatValues[i].AddToClassList("inventory-tooltip-stat-value"); row.Add(m_TooltipStatValues[i]);
            }
            m_TooltipStatValues[0].AddToClassList("inventory-tooltip-damage");
            m_TooltipStatValues[2].AddToClassList("inventory-tooltip-energy");
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
            bool module = HaloModules.FromCode(item.Code) != HaloModule.None;
            string detail = module ? MoonkovLocalization.Text(WorkshopPurpose(item.Code)) : MoonkovItemPresentation.Purpose(item.Code);
            m_TooltipDetail.text = TooltipNumbers(detail);
            m_TooltipDetail.style.display = string.IsNullOrEmpty(detail) ? DisplayStyle.None : DisplayStyle.Flex;
            string effect = module ? MoonkovLocalization.Text(WorkshopBenefit(item.Code)) : MoonkovItemPresentation.Effect(item.Code);
            m_TooltipSummary.text = module ? effect : TooltipNumbers(effect, "#3dd68c");
            m_TooltipSummary.style.display = string.IsNullOrEmpty(effect) ? DisplayStyle.None : DisplayStyle.Flex;
            m_TooltipSummary.RemoveFromClassList("inventory-tooltip-mode");
            RefreshTooltipWeapon(item);
            string state = MoonkovItemPresentation.ItemState(item);
            if (BatteryEnergy.IsCell(item))
                state = MoonkovLocalization.Format($"{BatteryEnergy.Stored(item)}/{item.Quantity * BatteryEnergy.Capacity} ENERGY / VALUE SCALES WITH CHARGE");
            if (module) state = MoonkovLocalization.Text(WorkshopTradeoff(item.Code));
            bool warning = module || (item.Code == "helmet" || item.Code == "energy_head_dome") && item.SuitOxygen >= 0 && item.SuitOxygen <= 20 ||
                BatteryEnergy.IsCell(item) && BatteryEnergy.Stored(item) <= 0;
            m_TooltipState.EnableInClassList("inventory-tooltip-warning", warning);
            m_TooltipState.text = TooltipNumbers(state, warning ? "#ffca16" : "#7de2d1");
            m_TooltipState.style.display = string.IsNullOrEmpty(state) ? DisplayStyle.None : DisplayStyle.Flex;
            m_ItemTooltip.style.display = DisplayStyle.Flex;
            m_ItemTooltip.BringToFront();
        }

        private static string TooltipNumbers(string text, string color = "#7de2d1") =>
            s_TooltipNumbers.Replace(text ?? "", match => "<color=" + (match.Value.StartsWith("-") ? "#ff9592" : color) + ">" + match.Value + "</color>");

        private void TooltipStat(int index, string label, string value)
        {
            m_TooltipStatRows[index].style.display = DisplayStyle.Flex;
            MoonkovLocalization.Set(m_TooltipStatLabels[index], label);
            m_TooltipStatValues[index].text = value;
        }

        private void RefreshTooltipWeapon(InventoryItem item)
        {
            m_TooltipStats.style.display = DisplayStyle.None;
            uint id = DollSingerWeapons.WeaponId(item.Code);
            if (!DollSingerWeapons.IsHalo(id)) return;
            if (!WorkshopWeaponRegistry)
                WorkshopWeaponRegistry = WeaponManager.Instance ? WeaponManager.Instance.WeaponRegistry :
                    Resources.Load<MenuCharacterSettings>("Moonkov/MenuCharacterSettings")?.WeaponRegistry;
            var mask = HaloModules.Mask(m_Graph, item);
            var weapon = WorkshopWeaponRegistry ? WorkshopWeaponRegistry.GetWeaponData(id, (ushort)mask) : null;
            if (!weapon)
            {
                m_TooltipSummary.text = MoonkovLocalization.Text("Weapon data is unavailable.");
                m_TooltipSummary.style.display = DisplayStyle.Flex;
                return;
            }
            bool burst = weapon.BurstCount > 1 || id == DollSingerWeapons.Halo && (mask & HaloModule.PulseController) != 0;
            int burstCount = weapon.BurstCount > 1 ? weapon.BurstCount : 3;
            m_TooltipSummary.text = burst ? MoonkovLocalization.Format($"{burstCount}-ROUND BURST") :
                MoonkovLocalization.Text(weapon.Automatic ? "AUTOMATIC" : "SEMI-AUTOMATIC");
            m_TooltipSummary.style.display = DisplayStyle.Flex;
            m_TooltipSummary.AddToClassList("inventory-tooltip-mode");
            foreach (var row in m_TooltipStatRows) row.style.display = DisplayStyle.None;
            int pellets = Mathf.Max(1, weapon.PelletCount);
            TooltipStat(0, "DAMAGE PER SHOT", pellets > 1 ? $"{weapon.Damage * pellets:0.##} <color=#b0b4ba>({weapon.Damage:0.##} × {pellets})</color>" : $"{weapon.Damage:0.##}");
            TooltipStat(1, "SHOT INTERVAL", $"{(weapon.BurstCount > 1 ? weapon.BurstInterval : weapon.CooldownInMs):0.###} s");
            TooltipStat(2, "ENERGY PER ROUND", Mathf.Max(1, weapon.EnergyPerRound).ToString());
            TooltipStat(3, weapon.Type == WeaponType.Projectile ? "PROJECTILE SPEED" : "RANGE",
                weapon.Type == WeaponType.Projectile ? $"{weapon.ProjectileSpeed:0.##} m/s" : $"{weapon.HitscanRange:0.##} m");
            if (weapon.Behavior == ProjectileBehavior.AreaOfEffect) TooltipStat(4, "BLAST RADIUS", $"{weapon.AoeRadius:0.##} m");
            else TooltipStat(4, "DAMAGE FALLOFF", weapon.DamageFalloffEnd > weapon.DamageFalloffStart
                ? $"{weapon.DamageFalloffStart:0.#}–{weapon.DamageFalloffEnd:0.#} m" : MoonkovLocalization.Text("No falloff"));
            TooltipStat(5, "AIMED SPREAD", $"{weapon.SpreadDegrees:0.##}°");
            if (burst) TooltipStat(6, "BURST PAUSE", $"{(weapon.BurstCount > 1 ? weapon.BurstCooldown : Mathf.Max(.42f, weapon.CooldownInMs)):0.###} s");
            m_TooltipStats.style.display = DisplayStyle.Flex;
        }

        private void PlaceItemTooltip()
        {
            if (m_TooltipTarget == null) return;
            const float margin = 8, offset = 18;
            var bounds = m_Root.contentRect;
            if (bounds.width <= margin * 2 || bounds.height <= margin * 2) return;
            m_ItemTooltip.style.width = Mathf.Min(400, bounds.width - margin * 2);
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
