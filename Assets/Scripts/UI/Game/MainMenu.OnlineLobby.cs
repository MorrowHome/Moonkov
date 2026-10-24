using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.MP_FPS.Online;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public partial class MainMenu
    {
        private VisualElement m_OnlineLobby;
        private CancellationTokenSource m_LobbyStop;
        private IVisualElementScheduledItem m_LobbyPoll;
        private bool m_LobbyBusy;
        private string m_LobbyError;
        private bool m_LobbyOpen;
        private void InitializeOnlineLobby()
        {
            m_LobbyStop = new CancellationTokenSource();
            m_OnlineLobby = TerminalLayout.Clone("OnlineLobby", "onlineLobby");
            m_MainMenu.Q("OnlineLobbyHost").Add(m_OnlineLobby);
            m_OnlineLobby.Q<Button>("onlineCreate").clicked += () => LobbyAction(new LobbyAction { Action = "create" });
            m_OnlineLobby.Q<Button>("onlineJoin").clicked += () => LobbyAction(new LobbyAction { Action = "join", Code = m_OnlineLobby.Q<TextField>("onlineInvite").value });
            m_OnlineLobby.Q<Button>("onlineCopy").clicked += () => UnityEngine.GUIUtility.systemCopyBuffer = OnlineLobbyClient.Snapshot?.Code ?? "";
            m_OnlineLobby.Q<Button>("onlineLeave").clicked += () => LobbyAction(new LobbyAction { Action = "leave" });
            m_OnlineLobby.Q<Button>("onlineCancel").clicked += () => LobbyAction(new LobbyAction { Action = "cancel" });
            m_OnlineLobby.Q<Button>("onlineStart").clicked += () => LobbyAction(new LobbyAction { Action = "start" });
            m_OnlineLobby.Q<Button>("onlineReady").clicked += () => LobbyAction(new LobbyAction { Action = "ready", Ready = !(OnlineLobbyClient.Snapshot?.Members.FirstOrDefault(m => m.PlayerId == OnlineLobbyClient.Snapshot.SelfId)?.Ready ?? false) });
            m_OnlineLobby.Q<Button>("onlineRetry").clicked += () => OnlineLobbyClient.Join(retry: true);
            m_OnlineLobby.Q<Button>("onlineReturn").clicked += () => LobbyAction(new LobbyAction { Action = "return" });
            m_OnlineLobby.Q<RadioButtonGroup>("onlineMode").RegisterValueChangedCallback(_ => LobbySettings());
            m_OnlineLobby.Q<Toggle>("onlinePublic").RegisterValueChangedCallback(_ => LobbySettings());
            m_OnlineLobby.Q<RadioButtonGroup>("onlineTeam").RegisterValueChangedCallback(e => LobbyAction(new LobbyAction { Action = "team", Team = e.newValue }));
            m_LobbyPoll = m_MainMenu.schedule.Execute(PollLobby).Every(2000);
            PresentLobby();
        }
        private void LobbySettings() => LobbyAction(new LobbyAction { Action = "settings",
            Mode = m_OnlineLobby.Q<RadioButtonGroup>("onlineMode").value == 0 ? "Coop" : "Versus",
            PublicQueue = m_OnlineLobby.Q<Toggle>("onlinePublic").value });
        private bool OnlineAtMenu => PlayerProfileClient.IsLoggedIn && !PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan &&
            GameSettings.Instance.GameState == GlobalGameState.MainMenu;
        private async void PollLobby()
        {
            PresentLobby();
            if (!OnlineAtMenu || m_AccountBusy || m_LobbyBusy) return;
            var lifetime = m_LobbyStop;
            // Keep party membership alive even while its members prepare inventory in the stash.
            m_LobbyBusy = true;
            try
            {
                await OnlineLobbyClient.RefreshAsync(lifetime.Token);
                m_LobbyError = null;
                var s = OnlineLobbyClient.Snapshot;
                if (s?.Phase == "Ready" && !SessionReconnect.Available && m_LobbyOpen) OnlineLobbyClient.Join();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { m_LobbyError = ex.Message; }
            finally { if (ReferenceEquals(m_LobbyStop, lifetime)) { m_LobbyBusy = false; if (!lifetime.IsCancellationRequested) PresentLobby(); } }
        }
        private async void LobbyAction(LobbyAction action)
        {
            if (!OnlineAtMenu || m_LobbyBusy || m_AccountBusy) return;
            var lifetime = m_LobbyStop;
            m_LobbyBusy = true; m_LobbyError = null; PresentLobby();
            try
            {
                if (action.Action == "return") await OnlineLobbyClient.RetryReturnAsync(lifetime.Token);
                else await OnlineLobbyClient.ActAsync(action, lifetime.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { m_LobbyError = ex.Message; }
            finally { if (ReferenceEquals(m_LobbyStop, lifetime)) { m_LobbyBusy = false; if (!lifetime.IsCancellationRequested) PresentLobby(); } }
        }
        private void PresentLobby()
        {
            if (m_OnlineLobby == null) return;
            m_OnlineLobby.style.display = OnlineAtMenu ? DisplayStyle.Flex : DisplayStyle.None;
            var s = OnlineLobbyClient.Snapshot;
            bool party = s != null && s.Phase != "None" && !string.IsNullOrEmpty(s.Code);
            bool leader = party && s.SelfId == s.LeaderId;
            bool lobby = party && s.Phase == "Lobby";
            bool busy = m_LobbyBusy || m_AccountBusy;
            var me = party ? s.Members.FirstOrDefault(m => m.PlayerId == s.SelfId) : null;
            m_OnlineLobby.Q("onlineEntry").style.display = party ? DisplayStyle.None : DisplayStyle.Flex;
            m_OnlineLobby.Q("onlineParty").style.display = party ? DisplayStyle.Flex : DisplayStyle.None;
            m_OnlineLobby.Q("onlineEntry").SetEnabled(!busy);
            m_OnlineLobby.Q<Label>("onlineStatus").text = m_LobbyError ?? OnlineLobbyClient.Error ?? ConnectionSettings.Instance.ConnectionError ?? (party ? s.Message : "创建小队后，把队伍码发给好友；单人可直接准备出发。");
            if (!party) return;
            m_OnlineLobby.Q<Button>("onlineCopy").text = "队伍码 " + s.Code + " · 点击复制";
            m_OnlineLobby.Q<Label>("onlineMembers").text = string.Join("\n", s.Members.Select(m =>
                (m.PlayerId == s.LeaderId ? "队长 " : "队员 ") + m.Name + (s.Mode == "Versus" ? " / " + (m.Team == 0 ? "A 队" : "B 队") : "") + (m.Returned ? " · 已返回" : m.Ready ? " · 已准备" : " · 未准备"))) + "\n" + s.Members.Length + "/4 人 · " + PhaseText(s.Phase);
            m_OnlineLobby.Q("onlineSettings").SetEnabled(lobby && leader && !busy);
            m_OnlineLobby.Q<RadioButtonGroup>("onlineMode").SetValueWithoutNotify(s.Mode == "Coop" ? 0 : 1);
            m_OnlineLobby.Q<Toggle>("onlinePublic").SetValueWithoutNotify(s.PublicQueue);
            var team = m_OnlineLobby.Q<RadioButtonGroup>("onlineTeam");
            team.style.display = s.Mode == "Versus" && !s.PublicQueue ? DisplayStyle.Flex : DisplayStyle.None;
            team.SetEnabled(lobby && !busy); team.SetValueWithoutNotify(me?.Team ?? 0);
            var ready = m_OnlineLobby.Q<Button>("onlineReady"); ready.text = me?.Ready == true ? "取消准备" : "准备"; ready.SetEnabled(lobby && !busy);
            var start = m_OnlineLobby.Q<Button>("onlineStart"); start.style.display = leader ? DisplayStyle.Flex : DisplayStyle.None;
            start.text = s.PublicQueue && s.Mode == "Versus" ? "全队准备后寻找对手" : "全员准备后直接进图";
            start.SetEnabled(lobby && !busy && s.Members.All(m => m.Ready));
            m_OnlineLobby.Q<Button>("onlineLeave").SetEnabled(lobby && !busy);
            var cancel = m_OnlineLobby.Q<Button>("onlineCancel"); cancel.style.display = leader && s.Phase == "Queued" ? DisplayStyle.Flex : DisplayStyle.None; cancel.SetEnabled(!busy);
            var retry = m_OnlineLobby.Q<Button>("onlineRetry"); retry.style.display = s.Phase == "Ready" && me?.Returned != true && s.InstanceId == OnlineLobbyClient.LastAttempt && !SessionReconnect.Available ? DisplayStyle.Flex : DisplayStyle.None; retry.SetEnabled(!busy);
            var back = m_OnlineLobby.Q<Button>("onlineReturn"); back.style.display = !lobby && s.InstanceId == OnlineLobbyClient.LastAttempt && me?.Returned != true ? DisplayStyle.Flex : DisplayStyle.None; back.SetEnabled(!busy);
        }
        private static string PhaseText(string phase) => phase == "Queued" ? "等待出发 / 可取消" : phase == "Starting" ? "正在加载本局地图" : phase == "Ready" ? "本局运行中，等待全员结算返回" : "准备中";
        private void DisposeOnlineLobby()
        { m_LobbyPoll?.Pause(); m_LobbyStop?.Cancel(); m_LobbyStop?.Dispose(); m_OnlineLobby?.RemoveFromHierarchy(); }
    }
}
