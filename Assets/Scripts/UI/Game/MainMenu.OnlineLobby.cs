// 线上小队的界面控制器。OnlineSquadPanel 收按钮操作，OnlineLobbyClient 去请求房间数据。这里安排何时轮询、提交动作和进局。
using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.MP_FPS.Online;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    public partial class MainMenu {
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

        private void InitializeOnlineLobby() {
            m_LobbyStop = new CancellationTokenSource();
            m_LobbyBusy = m_LobbyPolling = false;
            m_SquadVisible = false;
            m_LobbyRefreshTask = Task.CompletedTask;
            // 这里给视图接了四类回调。房间动作、回仓库、重试和预览外观变更，各自交给现有流程。
            m_SquadPanel = new OnlineSquadPanel(LobbyAction, ShowStash,
                () => LobbyAction(new LobbyAction { Action = "retry" }), (character, pose) => {
                    GameSettings.Instance.PlayerCharacter = character;
                    OnlineLobbyClient.SelectedPose = pose;
                    PollLobby();
                }, PollLobby);
            m_OnlineLobby = m_SquadPanel.Element;
            m_MainMenu.Add(m_OnlineLobby);
            // 高级连接与重连控件原本在主菜单模板。移动的是同一组元素，先记住父节点与排序，关页时能放回去。
            var advanced = m_MainMenu.Q("AdvancedConnection");
            m_AdvancedHome = advanced.parent;
            m_AdvancedIndex = m_AdvancedHome.IndexOf(advanced);
            var advancedHost = m_OnlineLobby.Q("onlineAdvancedHost");
            advancedHost.Add(advanced);
            advancedHost.style.display = DisplayStyle.Flex;
            var reconnect = m_MainMenu.Q("ReconnectPanel");
            m_ReconnectHome = reconnect.parent;
            m_ReconnectIndex = m_ReconnectHome.IndexOf(reconnect);
            m_OnlineLobby.Q("onlineReconnectHost").Add(reconnect);
            m_LobbyPoll = m_MainMenu.schedule.Execute(PollLobby).Every(2000);
            PresentLobby();
        }

        private bool OnlineAtMenu =>
            PlayerProfileClient.IsLoggedIn && !PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan &&
            GameSettings.Instance.GameState == GlobalGameState.MainMenu;

        private bool BrowseLobbyRooms => m_LobbyOpen && m_SquadPanel?.BrowsingRooms == true;

        private async void PollLobby() {
            PresentLobby();
            // 在已登录的线上菜单里才轮询。账号或房间操作正在处理时先等它完成，避免两个快照互相覆盖。
            if (!OnlineAtMenu || m_AccountBusy || m_LobbyBusy || m_LobbyPolling) return;
            var lifetime = m_LobbyStop;
            // Keep party membership alive even while its members prepare inventory in the stash.
            // 玩家切回仓库整理装备时也继续轮询，维持小队状态。只有小队页打开时才会根据 Ready 状态自动入场。
            m_LobbyPolling = true;
            try {
                // 处在房间目录才额外更新可浏览房间。停在个人仓库时继续更新自己的小队，省掉无关列表请求。
                m_LobbyRefreshTask = OnlineLobbyClient.RefreshAsync(lifetime.Token, BrowseLobbyRooms);
                await m_LobbyRefreshTask;
                m_LobbyError = null;
                var s = OnlineLobbyClient.Snapshot;
                // Ready 表示服务器已经准备好进图。还保留重连窗口时，优先交由玩家继续原来的战局。
                if (s?.Phase == "Ready" && !SessionReconnect.Available && m_LobbyOpen && !m_LobbyBusy)
                    OnlineLobbyClient.Join();
            } catch (OperationCanceledException) { } catch (Exception ex) { m_LobbyError = ex.Message; } finally {
                if (ReferenceEquals(m_LobbyStop, lifetime)) {
                    m_LobbyPolling = false;
                    if (!lifetime.IsCancellationRequested) PresentLobby();
                }
            }
        }

        private async void LobbyAction(LobbyAction action) {
            if (!OnlineAtMenu || m_LobbyBusy || m_AccountBusy) return;
            var lifetime = m_LobbyStop;
            m_LobbyBusy = true;
            m_LobbyError = null;
            PresentLobby();
            try {
                // A player click waits for the current poll instead of being discarded.
                // 点击操作等正在跑的刷新结束再发请求。标记忙碌后，新的定时轮询会先让路，避免两边同时更新房间状态。
                if (m_LobbyPolling)
                    try { await m_LobbyRefreshTask; } catch (OperationCanceledException) { throw; } catch (Exception) {
                        /* The explicit action may recover a failed refresh. */
                    }
                lifetime.Token.ThrowIfCancellationRequested();
                // 战后返回失败重试使用独立的返程流程。普通房间按钮则发送自己的动作和目前是否在浏览目录。
                if (action.Action == "return")
                    await OnlineLobbyClient.RetryReturnAsync(lifetime.Token);
                else
                    await OnlineLobbyClient.ActAsync(action, lifetime.Token, BrowseLobbyRooms);
                if (action.Action == "retry") {
                    // 还有可用的旧局时走续接。否则用现有房间信息重新尝试加入。
                    if (SessionReconnect.Available)
                        GameManager.Instance.ReconnectLastGameAsync();
                    else
                        OnlineLobbyClient.Join(true);
                }
            } catch (OperationCanceledException) { } catch (Exception ex) { m_LobbyError = ex.Message; } finally {
                if (ReferenceEquals(m_LobbyStop, lifetime)) {
                    m_LobbyBusy = false;
                    if (!lifetime.IsCancellationRequested) PresentLobby();
                }
            }
        }

        private void PresentLobby() {
            if (m_SquadPanel == null) return;
            bool visible = OnlineAtMenu && m_LobbyOpen;
            bool opening = visible && !m_SquadVisible;
            m_SquadVisible = visible;
            m_MainMenu.Q("Canvas").SetEnabled(!visible);
            // The existing reconnect button stays reachable from either screen.
            var reconnect = m_MainMenu.Q("ReconnectPanel");
            // 重连在小队页和主菜单都可显示。把同一个面板移动到当前区域，两个地方不会各自重复创建。
            var reconnectHost = visible ? m_OnlineLobby.Q("onlineReconnectHost") : m_ReconnectHome;
            if (reconnect != null && reconnectHost != null && reconnect.parent != reconnectHost)
                reconnectHost.Insert(visible ? 0 : Math.Min(m_ReconnectIndex, reconnectHost.childCount), reconnect);
            m_SquadPanel.SetVisible(visible);
            if (!visible) return;
            // 将本地角色外观和装备汇成预览数据，再结合服务端小队快照绘制其他人。仓库待确认状态也一起告诉页面。
            var self = LobbyAppearanceInventory.FromInventory(PlayerProfileClient.Inventory,
                GameSettings.Instance.PlayerCharacter, OnlineLobbyClient.SelectedPose);
            m_SquadPanel.Present(OnlineLobbyClient.Snapshot, self, PlayerProfileClient.DisplayName,
                m_LobbyError ?? OnlineLobbyClient.Error ?? ConnectionSettings.Instance.ConnectionError,
                m_LobbyBusy || m_AccountBusy, SessionReconnect.Available);
            if (opening) m_SquadPanel.FocusPrimary();
        }

        // 离开菜单时停止定时轮询，取消请求，然后把复用控件移回去。这样回菜单后仍能使用原本的入口。
        private void DisposeOnlineLobby() {
            m_LobbyPoll?.Pause();
            m_LobbyStop?.Cancel();
            m_LobbyStop?.Dispose();
            m_MainMenu.Q("Canvas")?.SetEnabled(true);
            var advanced = m_OnlineLobby?.Q("AdvancedConnection");
            if (advanced != null && m_AdvancedHome != null)
                m_AdvancedHome.Insert(Math.Min(m_AdvancedIndex, m_AdvancedHome.childCount), advanced);
            var reconnect = m_OnlineLobby?.Q("ReconnectPanel");
            if (reconnect != null && m_ReconnectHome != null)
                m_ReconnectHome.Insert(Math.Min(m_ReconnectIndex, m_ReconnectHome.childCount), reconnect);
            m_SquadPanel?.Dispose();
            m_SquadPanel = null;
            m_OnlineLobby = null;
        }
    }
}