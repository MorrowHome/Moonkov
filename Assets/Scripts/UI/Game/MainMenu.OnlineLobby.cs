using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.MP_FPS.Online;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public partial class MainMenu
    {
        private VisualElement m_OnlineLobby;
        private OnlineSquadPanel m_SquadPanel;
        private VisualElement m_AdvancedHome, m_ReconnectHome;
        private int m_AdvancedIndex, m_ReconnectIndex;
        private CancellationTokenSource m_LobbyStop;
        private IVisualElementScheduledItem m_LobbyPoll;
        private bool m_LobbyBusy;
        private bool m_LobbyPolling;
        private Task m_LobbyRefreshTask = Task.CompletedTask;
        private string m_LobbyError;
        private bool m_LobbyOpen;
        private bool m_SquadVisible;
        private void InitializeOnlineLobby()
        {
            m_LobbyStop = new CancellationTokenSource();
            m_LobbyBusy = m_LobbyPolling = false;
            m_SquadVisible = false;
            m_LobbyRefreshTask = Task.CompletedTask;
            m_SquadPanel = new OnlineSquadPanel(LobbyAction, ShowStash, () => LobbyAction(new LobbyAction { Action = "retry" }), (character, pose) =>
            {
                GameSettings.Instance.PlayerCharacter = character;
                OnlineLobbyClient.SelectedPose = pose;
                PollLobby();
            });
            m_OnlineLobby = m_SquadPanel.Element;
            m_MainMenu.Add(m_OnlineLobby);
            var advanced = m_MainMenu.Q("AdvancedConnection");
            m_AdvancedHome = advanced.parent; m_AdvancedIndex = m_AdvancedHome.IndexOf(advanced);
            var advancedHost = m_OnlineLobby.Q("onlineAdvancedHost");
            advancedHost.Add(advanced);
            advancedHost.style.display = DisplayStyle.Flex;
            var reconnect = m_MainMenu.Q("ReconnectPanel");
            m_ReconnectHome = reconnect.parent; m_ReconnectIndex = m_ReconnectHome.IndexOf(reconnect);
            m_OnlineLobby.Q("onlineReconnectHost").Add(reconnect);
            m_LobbyPoll = m_MainMenu.schedule.Execute(PollLobby).Every(2000);
            PresentLobby();
        }
        private bool OnlineAtMenu => PlayerProfileClient.IsLoggedIn && !PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan &&
            GameSettings.Instance.GameState == GlobalGameState.MainMenu;
        private async void PollLobby()
        {
            PresentLobby();
            if (!OnlineAtMenu || m_AccountBusy || m_LobbyBusy || m_LobbyPolling) return;
            var lifetime = m_LobbyStop;
            // Keep party membership alive even while its members prepare inventory in the stash.
            m_LobbyPolling = true;
            try
            {
                m_LobbyRefreshTask = OnlineLobbyClient.RefreshAsync(lifetime.Token);
                await m_LobbyRefreshTask;
                m_LobbyError = null;
                var s = OnlineLobbyClient.Snapshot;
                if (s?.Phase == "Ready" && !SessionReconnect.Available && m_LobbyOpen && !m_LobbyBusy) OnlineLobbyClient.Join();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { m_LobbyError = ex.Message; }
            finally { if (ReferenceEquals(m_LobbyStop, lifetime)) { m_LobbyPolling = false; if (!lifetime.IsCancellationRequested) PresentLobby(); } }
        }
        private async void LobbyAction(LobbyAction action)
        {
            if (!OnlineAtMenu || m_LobbyBusy || m_AccountBusy) return;
            var lifetime = m_LobbyStop;
            m_LobbyBusy = true; m_LobbyError = null; PresentLobby();
            try
            {
                // A player click waits for the current poll instead of being discarded.
                if (m_LobbyPolling)
                {
                    try { await m_LobbyRefreshTask; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* The explicit action may recover a failed refresh. */ }
                }
                lifetime.Token.ThrowIfCancellationRequested();
                if (action.Action == "return") await OnlineLobbyClient.RetryReturnAsync(lifetime.Token);
                else await OnlineLobbyClient.ActAsync(action, lifetime.Token);
                if (action.Action == "retry")
                {
                    if (SessionReconnect.Available) GameManager.Instance.ReconnectLastGameAsync();
                    else OnlineLobbyClient.Join(retry: true);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { m_LobbyError = ex.Message; }
            finally { if (ReferenceEquals(m_LobbyStop, lifetime)) { m_LobbyBusy = false; if (!lifetime.IsCancellationRequested) PresentLobby(); } }
        }
        private void PresentLobby()
        {
            if (m_SquadPanel == null) return;
            bool visible = OnlineAtMenu && m_LobbyOpen;
            bool opening = visible && !m_SquadVisible; m_SquadVisible = visible;
            m_MainMenu.Q("Canvas").SetEnabled(!visible);
            // The existing reconnect button stays reachable from either screen.
            var reconnect = m_MainMenu.Q("ReconnectPanel");
            var reconnectHost = visible ? m_OnlineLobby.Q("onlineReconnectHost") : m_ReconnectHome;
            if (reconnect != null && reconnectHost != null && reconnect.parent != reconnectHost)
                reconnectHost.Insert(visible ? 0 : Math.Min(m_ReconnectIndex, reconnectHost.childCount), reconnect);
            m_SquadPanel.SetVisible(visible);
            if (!visible) return;
            var self = LobbyAppearanceInventory.FromInventory(PlayerProfileClient.Inventory, GameSettings.Instance.PlayerCharacter, OnlineLobbyClient.SelectedPose);
            m_SquadPanel.Present(OnlineLobbyClient.Snapshot, self, PlayerProfileClient.DisplayName,
                m_LobbyError ?? OnlineLobbyClient.Error ?? ConnectionSettings.Instance.ConnectionError,
                m_LobbyBusy || m_AccountBusy, SessionReconnect.Available);
            if (opening) m_SquadPanel.FocusPrimary();
        }
        private void DisposeOnlineLobby()
        {
            m_LobbyPoll?.Pause(); m_LobbyStop?.Cancel(); m_LobbyStop?.Dispose();
            m_MainMenu.Q("Canvas")?.SetEnabled(true);
            var advanced = m_OnlineLobby?.Q("AdvancedConnection");
            if (advanced != null && m_AdvancedHome != null) m_AdvancedHome.Insert(Math.Min(m_AdvancedIndex, m_AdvancedHome.childCount), advanced);
            var reconnect = m_OnlineLobby?.Q("ReconnectPanel");
            if (reconnect != null && m_ReconnectHome != null) m_ReconnectHome.Insert(Math.Min(m_ReconnectIndex, m_ReconnectHome.childCount), reconnect);
            m_SquadPanel?.Dispose(); m_SquadPanel = null; m_OnlineLobby = null;
        }
    }
}
