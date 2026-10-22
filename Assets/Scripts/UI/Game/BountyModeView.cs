using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // A local preference picker. Connection approval belongs to the server handshake.
    public sealed class BountyModeView : IDisposable
    {
        private readonly VisualElement m_Root, m_Container;
        private readonly StyleSheet m_Sheet;
        private readonly Button m_Expedition, m_Clash, m_Close;
        private readonly Label m_Availability;
        private readonly Action<bool> m_Select;
        private readonly List<Button> m_Entries = new List<Button>();
        private Button m_Opener;
        private bool m_ClashSelected, m_Offline;

        public BountyModeView(VisualElement host, Action<bool> select)
        {
            m_Select = select;
            // Keep the CloneTree stylesheet scope. Loading a StyleSheet by the shared
            // UXML/USS resource basename can resolve the UXML's inlineStyle subasset.
            m_Container = Resources.Load<VisualTreeAsset>("Moonkov/UI/BountyModePicker").CloneTree();
            m_Container.pickingMode = PickingMode.Ignore;
            m_Container.style.position = Position.Absolute; m_Container.style.left = m_Container.style.top = 0;
            host.Add(m_Container); m_Root = m_Container.Q("bountyModePicker");
            m_Sheet = m_Container.styleSheets.count > 0 ? m_Container.styleSheets[0] : null;
            MoonkovLocalization.Bind(m_Root);
            BountyUIComposition.ModeCard(m_Root.Q<Button>("modeExpedition")); BountyUIComposition.ModeCard(m_Root.Q<Button>("modeBountyClash"));
            BountyUIComposition.Window(m_Root.Q("modeWindow"));
            m_Expedition = m_Root.Q<Button>("modeExpedition"); m_Clash = m_Root.Q<Button>("modeBountyClash");
            m_Close = m_Root.Q<Button>("modeClose"); m_Availability = m_Root.Q<Label>("modeAvailability");
            m_Expedition.clicked += Expedition; m_Clash.clicked += Clash; m_Close.clicked += Close;
            m_Root.RegisterCallback<KeyDownEvent>(Key);
        }
        public Button CreateEntry(VisualElement host)
        {
            var button = new Button(); button.AddToClassList("bounty-mode-entry");
            if (m_Sheet) button.styleSheets.Add(m_Sheet);
            button.clicked += Open; host.Add(button); m_Entries.Add(button); return button;
        }
        public void Present(bool clash, bool offline)
        {
            RefreshViewport();
            m_ClashSelected = clash; m_Offline = offline;
            foreach (var entry in m_Entries) MoonkovLocalization.Set(entry, clash ? offline ? "MODE / SOLO BOUNTY CLASH" : "MODE / BOUNTY CLASH" : "MODE / EXPEDITION");
            m_Expedition.EnableInClassList("bounty-mode-selected", !clash);
            m_Clash.EnableInClassList("bounty-mode-selected", clash);
            m_Clash.SetEnabled(!offline || PlayerProfileClient.SupportsSinglePlayer);
            MoonkovLocalization.Set(m_Clash.Q<Label>(className: "bounty-mode-card-title"), offline ? "SOLO BOUNTY CLASH" : "BOUNTY CLASH");
            MoonkovLocalization.Set(m_Clash.Q<Label>(className: "bounty-mode-card-copy"), offline
                ? "You against three AI duos. One bounty. Death loses equipment; extraction keeps loot in your offline stash."
                : "15 minutes. One target. One bounty. Teams of 1–3; at least two teams to begin.");
            MoonkovLocalization.Set(m_Availability, offline
                ? "OFFLINE STASH / Equip your loadout before deploying. No online account or teammates required."
                : "Invite your teammates to a room, then get ready together.");
        }
        private void Open()
        {
            RefreshViewport();
            foreach (var entry in m_Entries) if (entry.panel?.focusController.focusedElement == entry) m_Opener = entry;
            m_Root.RemoveFromClassList("bounty-hidden"); m_Container.BringToFront();
            if (m_ClashSelected && m_Clash.enabledSelf) m_Clash.Focus(); else m_Expedition.Focus();
        }
        private void RefreshViewport()
        {
            // UI uses the display viewport; menu preview cameras may render to a larger texture.
            var parent = m_Container.parent;
            float pixels = Screen.width;
            float height = Screen.height;
            if (parent == null || parent.contentRect.width < 1 || pixels < 1 || height < 1) return;
            float width = Mathf.Min(pixels, parent.contentRect.width), scale = parent.contentRect.width / width;
            m_Container.style.right = StyleKeyword.Auto; m_Container.style.bottom = StyleKeyword.Auto;
            m_Container.style.width = width; m_Container.style.height = height * width / pixels;
            m_Container.style.transformOrigin = new TransformOrigin(Length.Percent(0), Length.Percent(0), 0);
            m_Container.style.scale = new Scale(new Vector3(scale, scale, 1));
        }
        private void Close() { m_Root.AddToClassList("bounty-hidden"); m_Opener?.Focus(); }
        private void Expedition() { m_Select(false); Close(); }
        private void Clash() { if (!m_Offline || PlayerProfileClient.SupportsSinglePlayer) { m_Select(true); Close(); } }
        private void Key(KeyDownEvent evt) { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } }
        public void Dispose()
        {
            foreach (var entry in m_Entries) { entry.clicked -= Open; entry.RemoveFromHierarchy(); }
            m_Expedition.clicked -= Expedition; m_Clash.clicked -= Clash; m_Close.clicked -= Close;
            m_Root.UnregisterCallback<KeyDownEvent>(Key); m_Container.RemoveFromHierarchy();
        }
    }
}
