// 把好友房界面接到主菜单。LanRoomClient 通知档案变化时，这里更新仓库和账号栏用哪份资料。
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    public partial class MainMenu {
        private LanRoomView m_LanRoom;
        private LanProfileSnapshot m_LastLanProfile;
        private bool m_WasInLan;

        private void InitializeLanUI() {
            m_LanRoom = new LanRoomView(m_MainMenu.Q("Canvas"), () => {
                m_ShowConnectionMenu = false;
                UpdateAccountPanel();
            }, () => {
                m_LobbyOpen = false;
                PresentLobby();
            });
            // 房间状态变化是仓库重新取档的触发器。创建房间面板后也立即跑一次，覆盖重开菜单的情况。
            LanRoomClient.Changed += LanProfileChanged;
            LanProfileChanged();
            UpdateAccountPanel();
        }

        private void LanProfileChanged() {
            bool active = PlayerProfileClient.IsLan;
            // 仅当档案或是否在房间的状态变化才重画账号页。普通的成员列表变化不需要重复恢复登录。
            if (m_WasInLan == active && m_LastLanProfile == LanRoomClient.Profile) return;
            // 进入房间档案时取消手上还在跑的账号请求，避免旧线上档案回调覆盖刚切过来的状态。
            if (active && !m_WasInLan) {
                // 切进局域网档案后取消未完成的线上恢复请求，避免晚到的结果覆盖新档案。
                m_AccountStop?.Cancel();
                m_AccountStop?.Dispose();
                m_AccountStop = new System.Threading.CancellationTokenSource();
                m_AccountBusy = false;
            }
            m_WasInLan = active;
            m_LastLanProfile = LanRoomClient.Profile;
            // 退出好友房后重新判断现有线上或离线档案。界面随后按新档案选择仓库或登录页。
            m_AccountVerified = active || PlayerProfileClient.IsOffline || AccountClient.IsLoggedIn;
            m_AccountError = null;
            m_ShowConnectionMenu = false;
            UpdateAccountPanel();
        }

        private void DisposeLanUI() {
            LanRoomClient.Changed -= LanProfileChanged;
            m_LanRoom?.Dispose();
            m_LanRoom = null;
        }
    }
}