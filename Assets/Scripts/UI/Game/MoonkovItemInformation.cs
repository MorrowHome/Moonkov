using System.Text.RegularExpressions;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // The same item facts serve inventory hover, shop hover and the selected product.
    public sealed class MoonkovItemInformation
    {
        public VisualElement Element { get; }
        public WeaponRegistry WeaponRegistry { get; private set; }
        private readonly Label m_Title, m_Summary, m_Detail, m_State;
        private readonly VisualElement m_Stats;
        private readonly VisualElement[] m_Rows = new VisualElement[7];
        private readonly Label[] m_Labels = new Label[7], m_Values = new Label[7];
        private static readonly Regex s_Numbers = new Regex(@"(?<![A-Za-z0-9])[+-]?\d+(?:[.,]\d+)?(?:\s*(?:%|°|秒|分钟|米|s\b|m\b))?");

        public MoonkovItemInformation(VisualElement parent)
        {
            Element = TerminalLayout.Clone("Components/ItemInformation", "itemInformation");
            Element.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/ItemInformation")); parent.Add(Element);
            m_Title = Element.Q<Label>("tooltipTitle"); m_Summary = Element.Q<Label>("tooltipSummary");
            m_Detail = Element.Q<Label>("tooltipDetail"); m_State = Element.Q<Label>("tooltipState"); m_Stats = Element.Q("tooltipStats");
            for (int i = 0; i < m_Rows.Length; i++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore }; row.AddToClassList("inventory-tooltip-stat"); m_Stats.Add(row); m_Rows[i] = row;
                m_Labels[i] = new Label { pickingMode = PickingMode.Ignore }; m_Labels[i].AddToClassList("inventory-tooltip-stat-label"); row.Add(m_Labels[i]);
                m_Values[i] = new Label { name = "tooltipStatValue" + i, pickingMode = PickingMode.Ignore }; m_Values[i].AddToClassList("inventory-tooltip-stat-value"); row.Add(m_Values[i]);
            }
            m_Values[0].AddToClassList("inventory-tooltip-damage"); m_Values[2].AddToClassList("inventory-tooltip-energy");
        }

        public void Present(InventoryItem item, InventoryGraph graph, WeaponRegistry registry = null)
        {
            var definition = InventoryCatalog.Get(item?.Code);
            Element.style.display = definition == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (definition == null) return;
            if (registry) WeaponRegistry = registry;
            MoonkovLocalization.Set(m_Title, definition.Name);
            bool module = HaloModules.FromCode(item.Code) != HaloModule.None;
            string detail = module ? MoonkovLocalization.Text(MoonkovItemPresentation.ModulePurpose(item.Code)) : MoonkovItemPresentation.Purpose(item.Code);
            m_Detail.text = Numbers(detail); m_Detail.style.display = string.IsNullOrEmpty(detail) ? DisplayStyle.None : DisplayStyle.Flex;
            string effect = module ? MoonkovLocalization.Text(MoonkovItemPresentation.ModuleBenefit(item.Code)) : MoonkovItemPresentation.Effect(item.Code);
            m_Summary.text = module ? effect : Numbers(effect, "#3dd68c");
            m_Summary.style.display = string.IsNullOrEmpty(effect) ? DisplayStyle.None : DisplayStyle.Flex;
            m_Summary.RemoveFromClassList("inventory-tooltip-mode");
            PresentWeapon(item, graph);
            string state = MoonkovItemPresentation.ItemState(item);
            if (BatteryEnergy.IsCell(item)) state = MoonkovLocalization.Format($"{BatteryEnergy.Stored(item)}/{item.Quantity * BatteryEnergy.Capacity} ENERGY / VALUE SCALES WITH CHARGE");
            if (module) state = MoonkovLocalization.Text(MoonkovItemPresentation.ModuleTradeoff(item.Code));
            bool warning = module || (item.Code == "helmet" || item.Code == "energy_head_dome") && item.SuitOxygen >= 0 && item.SuitOxygen <= 20 || BatteryEnergy.IsCell(item) && BatteryEnergy.Stored(item) <= 0;
            m_State.EnableInClassList("inventory-tooltip-warning", warning);
            m_State.text = Numbers(state, warning ? "#ffca16" : "#7de2d1");
            m_State.style.display = string.IsNullOrEmpty(state) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static string Numbers(string text, string color = "#7de2d1") => s_Numbers.Replace(text ?? "", match => "<color=" + (match.Value.StartsWith("-") ? "#ff9592" : color) + ">" + match.Value + "</color>");
        private void Stat(int index, string label, string value)
        { m_Rows[index].style.display = DisplayStyle.Flex; MoonkovLocalization.Set(m_Labels[index], label); m_Values[index].text = value; }

        private void PresentWeapon(InventoryItem item, InventoryGraph graph)
        {
            m_Stats.style.display = DisplayStyle.None;
            uint id = DollSingerWeapons.WeaponId(item.Code); if (!DollSingerWeapons.IsHalo(id)) return;
            if (!WeaponRegistry) WeaponRegistry = WeaponManager.Instance ? WeaponManager.Instance.WeaponRegistry : Resources.Load<MenuCharacterSettings>("Moonkov/MenuCharacterSettings")?.WeaponRegistry;
            var mask = HaloModules.Mask(graph, item);
            var weapon = WeaponRegistry ? WeaponRegistry.GetWeaponData(id, (ushort)mask) : null;
            if (!weapon) { m_Summary.text = MoonkovLocalization.Text("Weapon data is unavailable."); m_Summary.style.display = DisplayStyle.Flex; return; }
            bool burst = weapon.BurstCount > 1 || id == DollSingerWeapons.Halo && (mask & HaloModule.PulseController) != 0;
            int count = weapon.BurstCount > 1 ? weapon.BurstCount : 3;
            m_Summary.text = burst ? MoonkovLocalization.Format($"{count}-ROUND BURST") : MoonkovLocalization.Text(weapon.Automatic ? "AUTOMATIC" : "SEMI-AUTOMATIC");
            m_Summary.style.display = DisplayStyle.Flex; m_Summary.AddToClassList("inventory-tooltip-mode");
            foreach (var row in m_Rows) row.style.display = DisplayStyle.None;
            int pellets = Mathf.Max(1, weapon.PelletCount);
            Stat(0, "DAMAGE PER SHOT", pellets > 1 ? $"{weapon.Damage * pellets:0.##} <color=#b0b4ba>({weapon.Damage:0.##} × {pellets})</color>" : $"{weapon.Damage:0.##}");
            Stat(1, "SHOT INTERVAL", $"{(weapon.BurstCount > 1 ? weapon.BurstInterval : weapon.CooldownInMs):0.###} s");
            Stat(2, "ENERGY PER ROUND", Mathf.Max(1, weapon.EnergyPerRound).ToString());
            Stat(3, weapon.Type == WeaponType.Projectile ? "PROJECTILE SPEED" : "RANGE", weapon.Type == WeaponType.Projectile ? $"{weapon.ProjectileSpeed:0.##} m/s" : $"{weapon.HitscanRange:0.##} m");
            if (weapon.Behavior == ProjectileBehavior.AreaOfEffect) Stat(4, "BLAST RADIUS", $"{weapon.AoeRadius:0.##} m");
            else Stat(4, "DAMAGE FALLOFF", weapon.DamageFalloffEnd > weapon.DamageFalloffStart ? $"{weapon.DamageFalloffStart:0.#}–{weapon.DamageFalloffEnd:0.#} m" : MoonkovLocalization.Text("No falloff"));
            Stat(5, "AIMED SPREAD", $"{weapon.SpreadDegrees:0.##}°");
            if (burst) Stat(6, "BURST PAUSE", $"{(weapon.BurstCount > 1 ? weapon.BurstCooldown : Mathf.Max(.42f, weapon.CooldownInMs)):0.###} s");
            m_Stats.style.display = DisplayStyle.Flex;
        }
    }
}
