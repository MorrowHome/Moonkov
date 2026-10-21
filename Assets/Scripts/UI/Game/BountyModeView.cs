using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // A local preference picker. Connection approval belongs to the server handshake.
    public sealed class BountyModeView : IDisposable
    {
        private readonly VisualElement m_Root;
        private readonly Button m_Expedition, m_Clash, m_Close;
        private readonly Label m_Availability;
        private readonly Action<bool> m_Select;
        private readonly List<Button> m_Entries = new List<Button>();
        private Button m_Opener;
        private bool m_ClashSelected, m_Offline;

        public BountyModeView(VisualElement host, Action<bool> select)
        {
            m_Select = select;
            var sheet = Resources.Load<StyleSheet>("Moonkov/UI/BountyModePicker");
            host.styleSheets.Add(sheet);
            m_Root = TerminalLayout.Clone("BountyModePicker", "bountyModePicker"); host.Add(m_Root);
            m_Expedition = m_Root.Q<Button>("modeExpedition"); m_Clash = m_Root.Q<Button>("modeBountyClash");
            m_Close = m_Root.Q<Button>("modeClose"); m_Availability = m_Root.Q<Label>("modeAvailability");
            m_Expedition.clicked += Expedition; m_Clash.clicked += Clash; m_Close.clicked += Close;
            m_Root.RegisterCallback<KeyDownEvent>(Key);
        }
        public Button CreateEntry(VisualElement host)
        {
            var button = new Button(); button.AddToClassList("bounty-mode-entry");
            button.clicked += Open; host.Add(button); m_Entries.Add(button); return button;
        }
        public void Present(bool clash, bool offline)
        {
            m_ClashSelected = clash; m_Offline = offline;
            foreach (var entry in m_Entries) MoonkovLocalization.Set(entry, clash ? "MODE / BOUNTY CLASH" : "MODE / EXPEDITION");
            m_Expedition.EnableInClassList("bounty-mode-selected", !clash);
            m_Clash.EnableInClassList("bounty-mode-selected", clash);
            m_Clash.SetEnabled(!offline);
            MoonkovLocalization.Set(m_Availability, offline
                ? "Bounty Clash needs online accounts and at least two teams. Single player remains available in Expedition."
                : "A server with the same mode and rules version is required. Your team is assigned by the server.");
        }
        private void Open()
        {
            foreach (var entry in m_Entries) if (entry.panel?.focusController.focusedElement == entry) m_Opener = entry;
            m_Root.RemoveFromClassList("bounty-hidden"); m_Root.BringToFront();
            if (m_ClashSelected && !m_Offline) m_Clash.Focus(); else m_Expedition.Focus();
        }
        private void Close() { m_Root.AddToClassList("bounty-hidden"); m_Opener?.Focus(); }
        private void Expedition() { m_Select(false); Close(); }
        private void Clash() { if (!m_Offline) { m_Select(true); Close(); } }
        private void Key(KeyDownEvent evt) { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } }
        public void Dispose()
        {
            foreach (var entry in m_Entries) { entry.clicked -= Open; entry.RemoveFromHierarchy(); }
            m_Expedition.clicked -= Expedition; m_Clash.clicked -= Clash; m_Close.clicked -= Close;
            m_Root.UnregisterCallback<KeyDownEvent>(Key); m_Root.RemoveFromHierarchy();
        }
    }
}
