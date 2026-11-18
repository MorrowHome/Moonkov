// 战后物资清单。根据 RaidResultManifest 展示带回、受保护和丢失的物品数量。界面按已经结算的清单绘制，切语言时会重新翻译。
using UnityEngine.UIElements;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client {
    public sealed class RaidResultCargoView {
        private readonly ScrollView m_Host;
        private RaidResultManifest m_Manifest;
        private string m_Locale;
        private bool m_Presented;

        public RaidResultCargoView(ScrollView host) {
            m_Host = host;
            m_Host.focusable = true;
            // 手柄和键盘焦点在清单上时，上下移动可以滚动列表。到顶部或底部再让普通导航继续处理。
            m_Host.RegisterCallback<NavigationMoveEvent>(evt => {
                if (m_Host.panel?.focusController.focusedElement != m_Host) return;
                float direction = evt.direction == NavigationMoveEvent.Direction.Down ? 1 :
                    evt.direction == NavigationMoveEvent.Direction.Up ? -1 : 0;
                if (direction == 0) return;
                var offset = m_Host.scrollOffset;
                float next = UnityEngine.Mathf.Clamp(offset.y + direction * 36, 0, m_Host.verticalScroller.highValue);
                if (UnityEngine.Mathf.Approximately(next, offset.y)) return;
                m_Host.scrollOffset = new UnityEngine.Vector2(offset.x, next);
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }

        public void Present(RaidResultManifest manifest) {
            string locale = MoonkovLocalization.CurrentLocaleCode;
            // 同一份清单且语言不变时保留当前滚动位置。换清单或译文后才从头排出来。
            if (m_Presented && ReferenceEquals(manifest, m_Manifest) && locale == m_Locale) return;
            m_Presented = true;
            m_Manifest = manifest;
            m_Locale = locale;
            m_Host.Clear();
            m_Host.scrollOffset = UnityEngine.Vector2.zero;
            // 结果快照可能先于清单到。还在等待用加载提示，已收到且无物资时才显示没有物资。
            if (manifest == null) {
                Message("Waiting for cargo manifest...");
                return;
            }
            if (manifest.Items.Count == 0) {
                Message("No carried items at the end of this expedition.");
                return;
            }
            AddGroup(manifest.Outcome == RaidPhase.Extracted ? "BROUGHT BACK" : "PROTECTED ITEMS", true);
            AddGroup("LOST ITEMS", false);
        }

        private void Message(string text) {
            var label = new Label();
            label.AddToClassList("raid-cargo-empty");
            m_Host.Add(label);
            MoonkovLocalization.Set(label, text);
        }

        private void AddGroup(string title, bool returned) {
            bool heading = false;
            foreach (var item in m_Manifest.Items) {
                // 同种物资可以有带回和丢失两种数量。每组只画其中有数量的内容，空组不用多占一行标题。
                int quantity = returned ? item.Returned : item.Lost;
                if (quantity <= 0) continue;
                if (!heading) {
                    var label = new Label();
                    label.AddToClassList("raid-cargo-group");
                    m_Host.Add(label);
                    MoonkovLocalization.Set(label, title);
                    heading = true;
                }
                var row = new VisualElement();
                row.AddToClassList("raid-cargo-row");
                var name = new Label();
                name.AddToClassList("raid-cargo-name");
                var amount = new Label();
                amount.AddToClassList("raid-cargo-quantity");
                row.Add(name);
                row.Add(amount);
                m_Host.Add(row);
                MoonkovLocalization.Set(name, InventoryCatalog.Get(item.Code).Name);
                MoonkovLocalization.Set(amount, "{0:N0}", quantity);
            }
        }
    }
}