using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public partial class MainMenu
    {
        [SerializeField] private string m_AccountServiceUrl = "http://127.0.0.1:5080/";
        private VisualElement m_AccountPanel, m_ConnectionPanel;
        private TextField m_Username, m_Password;
        private Label m_AccountMessage, m_LoggedInLabel;
        private Button m_Login, m_Register, m_Logout;
        private CancellationTokenSource m_AccountStop;
        private bool m_AccountBusy, m_AccountVerified;

        private void InitializeAccountPanel()
        {
            m_AccountStop = new CancellationTokenSource();
            m_ConnectionPanel = m_MainMenu.Q<VisualElement>("ConnectionPanel");
            m_AccountPanel = new VisualElement { name = "AccountPanel" };
            m_AccountPanel.Add(new Label("MOON RAID ACCOUNT"));
            m_Username = new TextField("Username") { maxLength = 32, value = AccountClient.DisplayName ?? "" };
            m_Password = new TextField("Password") { isPasswordField = true, maxLength = 128 };
            m_AccountPanel.Add(m_Username); m_AccountPanel.Add(m_Password);
            m_Login = new Button(() => RunAccountAction(false)) { text = "LOG IN" };
            m_Register = new Button(() => RunAccountAction(true)) { text = "REGISTER" };
            m_AccountPanel.Add(m_Login); m_AccountPanel.Add(m_Register);
            m_AccountPanel.Add(new Button(OnQuitPressed) { text = "QUIT" });
            m_AccountMessage = new Label("Username: 3-32 letters, digits or underscores.\nPassword: at least 8 characters.");
            m_AccountMessage.style.whiteSpace = WhiteSpace.Normal;
            m_AccountPanel.Add(m_AccountMessage);
            m_ConnectionPanel.parent.Insert(0, m_AccountPanel);
            m_LoggedInLabel = new Label();
            m_Logout = new Button(Logout) { text = "LOG OUT" };
            m_ConnectionPanel.Insert(0, m_LoggedInLabel); m_ConnectionPanel.Insert(1, m_Logout);
            m_MainMenu.Q<TextField>(UIElementNames.NameInputField).SetEnabled(false);
            m_MainMenu.Q<VisualElement>("InputPlayerName").style.display = DisplayStyle.None;
            UpdateAccountPanel();
            if (AccountClient.IsLoggedIn) RestoreLogin();
        }

        private void UpdateAccountPanel()
        {
            bool loggedIn = AccountClient.IsLoggedIn && m_AccountVerified;
            m_AccountPanel.style.display = loggedIn ? DisplayStyle.None : DisplayStyle.Flex;
            m_ConnectionPanel.style.display = loggedIn ? DisplayStyle.Flex : DisplayStyle.None;
            m_LoggedInLabel.text = $"Signed in as {AccountClient.DisplayName}\nSTASH: Dust {AccountClient.StashDust}   Alloy {AccountClient.StashAlloy}   Cells {AccountClient.StashCells}";
            m_LoggedInLabel.style.whiteSpace = WhiteSpace.Normal;
            m_Login.SetEnabled(!m_AccountBusy); m_Register.SetEnabled(!m_AccountBusy); m_Logout.SetEnabled(!m_AccountBusy);
            m_Username.SetEnabled(!m_AccountBusy); m_Password.SetEnabled(!m_AccountBusy);
        }

        private async void RestoreLogin()
        {
            await AccountAction(async ct =>
            {
                await AccountClient.ValidateAsync(m_AccountServiceUrl, ct);
                m_AccountVerified = AccountClient.IsLoggedIn;
                m_AccountMessage.text = m_AccountVerified ? "Signed in." : "Login expired. Please sign in again.";
            });
        }

        private async void RunAccountAction(bool register)
        {
            await AccountAction(async ct =>
            {
                await AccountClient.AuthenticateAsync(m_AccountServiceUrl, m_Username.value, m_Password.value, register, ct);
                m_Password.value = "";
                m_AccountVerified = true;
                m_AccountMessage.text = "Signed in.";
            });
        }

        private async void Logout()
        {
            await AccountAction(async ct =>
            {
                await AccountClient.LogoutAsync(m_AccountServiceUrl, ct);
                m_AccountVerified = false;
                m_Password.value = "";
                m_AccountMessage.text = "Signed out.";
            });
        }

        private async Task AccountAction(Func<CancellationToken, Task> action)
        {
            if (m_AccountBusy) return;
            var lifetime = m_AccountStop;
            m_AccountBusy = true;
            UpdateAccountPanel();
            try { await action(lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (System.Net.Http.HttpRequestException) { if (!lifetime.IsCancellationRequested) m_AccountMessage.text = "Cannot reach account service. Please retry."; }
            catch (Exception ex) { if (!lifetime.IsCancellationRequested) m_AccountMessage.text = ex.Message; }
            finally
            {
                if (!lifetime.IsCancellationRequested) { m_AccountBusy = false; UpdateAccountPanel(); }
            }
        }

        private void DisposeAccountPanel()
        {
            m_AccountStop?.Cancel();
            m_AccountPanel?.RemoveFromHierarchy(); m_LoggedInLabel?.RemoveFromHierarchy(); m_Logout?.RemoveFromHierarchy();
            m_AccountBusy = m_AccountVerified = false;
        }
    }
}
