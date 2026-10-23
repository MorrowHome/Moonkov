using System;
using System.Linq;
using System.Text;
using Unity.MP_FPS.Inventory;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class ContainerInventoryView
    {
        // Protection gear sends live resource snapshots every half second without
        // changing placement. These must never destroy a pressed/captured button.
        private static string LayoutKey(InventoryGraph graph)
        {
            if (graph == null) return null;
            var key = new StringBuilder().Append(graph.StashRows).Append('/').Append(graph.LootRows);
            foreach (var item in graph.Items.OrderBy(i => i.Id, StringComparer.Ordinal))
            {
                foreach (string value in new[] { item.Id, item.Code, item.Parent ?? "", item.Region ?? "" })
                    key.Append('|').Append(value.Length).Append(':').Append(value);
                key.Append('|').Append(item.X).Append(',').Append(item.Y).Append(',').Append(item.Rotated);
            }
            return key.ToString();
        }
        private void RefreshSnapshotValues()
        {
            if (m_Graph == null) return;
            foreach (var pair in m_Tiles)
            {
                var item = m_Graph.Find(pair.Key); if (item == null) continue;
                var def = InventoryCatalog.Get(item.Code); var tile = pair.Value;
                var quantity = tile.Q<Label>("quantity");
                if (quantity != null) MoonkovLocalization.Set(quantity, def.Container ? "OPEN ↗" : BatteryEnergy.IsCell(item) ? $"{BatteryEnergy.Stored(item)}E" : item.Quantity.ToString());
                tile.tooltip = MoonkovLocalization.Text(def.Name) + " ×" + item.Quantity + "\n" + MoonkovItemPresentation.Details(item.Code) + "\n" + MoonkovItemPresentation.ItemState(item);
            }
            MoonkovLocalization.Set(m_Weight, "CARRIED {0:0.0} / {1:0} KG", m_Graph.CarriedWeight, InventoryCatalog.CarryWeightLimit);
            if (m_Drag != null) m_Drag.RefreshItem();
        }
    }
}
