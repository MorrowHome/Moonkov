// 区分背包布局和背包数值。LayoutKey 看物品编号、父容器和落位。位置没变时沿用现有格子，直接刷新数量、电量、重量和悬浮信息。
using System;
using System.Linq;
using System.Text;
using Unity.MP_FPS.Inventory;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    public sealed partial class ContainerInventoryView {
        // 防护装备的资源值每半秒可以同步，但摆放位置没变。
        // 这种更新应保留已经按下或抓住指针的按钮。
        private static string LayoutKey(InventoryGraph graph) {
            if (graph == null) return null;
            var key = new StringBuilder().Append(graph.StashRows).Append('/').Append(graph.LootRows);
            // 按 ID 排序，让同一批物品换了遍历顺序也得到同一个布局标记。长度前缀还能区分字符串中的分隔符。
            foreach (var item in graph.Items.OrderBy(i => i.Id, StringComparer.Ordinal)) {
                foreach (string value in new[] { item.Id, item.Code, item.Parent ?? "", item.Region ?? "" })
                    key.Append('|').Append(value.Length).Append(':').Append(value);
                key.Append('|').Append(item.X).Append(',').Append(item.Y).Append(',').Append(item.Rotated);
            }
            return key.ToString();
        }

        private void RefreshSnapshotValues() {
            if (m_Graph == null) return;
            // 使用格子缓存的 ID 去新快照找物品。保留原按钮，只改数量、剩余电量和文字。
            foreach (var pair in m_Tiles) {
                var item = m_Graph.Find(pair.Key);
                if (item == null) continue;
                var def = InventoryCatalog.Get(item.Code);
                var tile = pair.Value;
                var quantity = tile.Q<Label>("quantity");
                if (quantity != null)
                    MoonkovLocalization.Set(quantity,
                        def.Container ? "OPEN ↗" :
                        BatteryEnergy.IsCell(item) ? $"{BatteryEnergy.Stored(item)}E" : item.Quantity.ToString());
                tile.tooltip = MoonkovLocalization.Text(def.Name) + " ×" + item.Quantity + "\n" +
                               MoonkovItemPresentation.Details(item.Code) + "\n" +
                               MoonkovItemPresentation.ItemState(item);
            }
            RefreshWeight();
            RefreshItemTooltip();
            // 被拖动的物品对象也换成快照里的最新对象，防止之后用过期数量或属性作判断。
            if (m_Drag != null) m_Drag.RefreshItem();
        }

        private void RefreshWeight() {
            float weight = m_Graph.CarriedWeight;
            MoonkovLocalization.Set(m_Weight,
                RaidEncumbrance.Burden(weight) > 0
                    ? "CARRIED {0:0.0} / {1:0} KG / HEAVY"
                    : "CARRIED {0:0.0} / {1:0} KG", weight, InventoryCatalog.CarryWeightLimit);
        }
    }
}