using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client
{
    public sealed class RaidResultCargoView
    {
        private readonly ScrollView m_Host;
        private RaidResultManifest m_Manifest;
        private string m_Locale;
        private bool m_Presented;

        public RaidResultCargoView(ScrollView host)
        {
            m_Host = host; m_Host.focusable = true;
            m_Host.RegisterCallback<NavigationMoveEvent>(evt =>
            {
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

        public void Present(RaidResultManifest manifest)
        {
            string locale = LocalizationSettings.SelectedLocale?.Identifier.Code;
            if (m_Presented && ReferenceEquals(manifest, m_Manifest) && locale == m_Locale) return;
            m_Presented = true; m_Manifest = manifest; m_Locale = locale;
            m_Host.Clear(); m_Host.scrollOffset = UnityEngine.Vector2.zero;
            if (manifest == null) { Message("Waiting for cargo manifest..."); return; }
            if (manifest.Items.Count == 0) { Message("No carried items at the end of this expedition."); return; }
            AddGroup(manifest.Outcome == RaidPhase.Extracted ? "BROUGHT BACK" : "PROTECTED ITEMS", true);
            AddGroup("LOST ITEMS", false);
        }

        private void Message(string text)
        {
            var label = new Label(); label.AddToClassList("raid-cargo-empty");
            m_Host.Add(label); MoonkovLocalization.Set(label, text);
        }

        private void AddGroup(string title, bool returned)
        {
            bool heading = false;
            foreach (var item in m_Manifest.Items)
            {
                int quantity = returned ? item.Returned : item.Lost;
                if (quantity <= 0) continue;
                if (!heading)
                {
                    var label = new Label(); label.AddToClassList("raid-cargo-group");
                    m_Host.Add(label); MoonkovLocalization.Set(label, title); heading = true;
                }
                var row = new VisualElement(); row.AddToClassList("raid-cargo-row");
                var name = new Label(); name.AddToClassList("raid-cargo-name");
                var amount = new Label(); amount.AddToClassList("raid-cargo-quantity");
                row.Add(name); row.Add(amount); m_Host.Add(row);
                MoonkovLocalization.Set(name, InventoryCatalog.Get(item.Code).Name);
                MoonkovLocalization.Set(amount, "{0:N0}", quantity);
            }
        }
    }
}
