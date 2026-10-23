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
            m_LanRoom = new LanRoomView(m_MainMenu.Q("Canvas"), () => { m_ShowConnectionMenu = false; UpdateAccountPanel(); });
            LanRoomClient.Changed += LanProfileChanged;
            LanProfileChanged();
        }
        private void LanProfileChanged()
        {
            bool active = PlayerProfileClient.IsLan;
            if (m_WasInLan == active && m_LastLanProfile == LanRoomClient.Profile) return;
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
