using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public partial class MainMenu
    {
        private BountyModeView m_ModePicker;
        private Label m_ModeStatus;
        private Label m_ModeHint;
        private IVisualElementScheduledItem m_ModeRefresh;
        private void InitializeBountyModeUI()
        {
            m_ModePicker = new BountyModeView(m_MainMenu.Q("Canvas"), clash =>
            {
                if (PlayerProfileClient.IsLan) return;
                GameSettings.Instance.RequestedMode = clash ? RaidGameMode.BountyClash : RaidGameMode.Expedition;
                ConnectionSettings.Instance.ConnectionError = null;
                RefreshModePicker();
            });
            m_ModePicker.CreateEntry(m_MainMenu.Q(className: "stash-footer"));
            m_ModeHint = m_MainMenu.Q<Label>(className: "stash-footer-hint");
            m_ModePicker.CreateEntry(m_ConnectionPanel);
            m_ModeStatus = new Label { pickingMode = PickingMode.Ignore }; m_ModeStatus.AddToClassList("bounty-mode-message");
            m_ConnectionPanel.Add(m_ModeStatus);
            m_ModeRefresh = m_MainMenu.schedule.Execute(RefreshModePicker).Every(250);
            RefreshModePicker();
        }
        private void RefreshModePicker()
        {
            bool clash = GameSettings.Instance.RequestedMode == RaidGameMode.BountyClash;
            bool offline = PlayerProfileClient.IsOffline;
            m_ModePicker.Present(clash, offline);
            if (m_ModeHint != null) MoonkovLocalization.Set(m_ModeHint, clash && offline
                ? "SOLO BOUNTY CLASH / Offline stash. Death loses equipment; extraction keeps loot."
                : "Choose the game mode, then choose how to connect.");
            MoonkovLocalization.Set(m_ModeStatus, ConnectionSettings.Instance.ConnectionError ?? (clash
                ? offline ? "SOLO BOUNTY CLASH / You against three AI duos. Prepare your offline loadout, then deploy."
                    : "Online Bounty Clash / waiting for server confirmation. At least two teams must be ready."
                : "Original expedition rules. Connection method does not change the game mode."));
        }
        private void DisposeBountyModeUI()
        {
            m_ModeRefresh?.Pause(); m_ModePicker?.Dispose(); m_ModePicker = null;
            m_ModeStatus?.RemoveFromHierarchy(); m_ModeStatus = null;
        }
    }
}
