// 把好友房界面接到主菜单。LanRoomClient 通知档案变化时，这里更新仓库和账号栏用哪份资料。
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public partial class MainMenu
    {
        private LanRoomView m_LanRoom;
        private LanProfileSnapshot m_LastLanProfile;
        private bool m_WasInLan;
        private void InitializeLanUI()
        {
            m_LanRoom = new LanRoomView(m_MainMenu.Q("Canvas"), () => { m_ShowConnectionMenu = false; UpdateAccountPanel(); },
                () => { m_LobbyOpen = false; PresentLobby(); });
            LanRoomClient.Changed += LanProfileChanged;
            LanProfileChanged();
            UpdateAccountPanel();
        }
        private void LanProfileChanged()
        {
            bool active = PlayerProfileClient.IsLan;
            if (m_WasInLan == active && m_LastLanProfile == LanRoomClient.Profile) return;
            // 进入房间档案时取消手上还在跑的账号请求，避免旧线上档案回调覆盖刚切过来的状态。
            if (active && !m_WasInLan)
            {
                // An online restore request must not overwrite the newly selected LAN account view.
                m_AccountStop?.Cancel(); m_AccountStop?.Dispose();
                m_AccountStop = new System.Threading.CancellationTokenSource(); m_AccountBusy = false;
            }
            m_WasInLan = active; m_LastLanProfile = LanRoomClient.Profile;
            m_AccountVerified = active || PlayerProfileClient.IsOffline || AccountClient.IsLoggedIn;
            m_AccountError = null; m_ShowConnectionMenu = false;
            UpdateAccountPanel();
        }
        private void DisposeLanUI()
        { LanRoomClient.Changed -= LanProfileChanged; m_LanRoom?.Dispose(); m_LanRoom = null; }
    }
}
