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
        private StashScreen m_StashScreen;
        private bool m_ShowConnectionMenu;
        private Button m_BackToStash;
        private IntegerField m_CarryCells;
        private Label m_CarryNote;
        private string m_AccountError;
        private GlobalGameState m_LastStashGameState;
        private LunarBackdrop m_LoginMoon;

        private void InitializeAccountPanel()
        {
            var canvas = m_MainMenu.Q<VisualElement>("Canvas");
            m_LoginMoon = new LunarBackdrop(BackdropMode.Login, canvas);
            m_LoginMoon.AddToClassList("terminal-scene-shade"); canvas.Insert(0, m_LoginMoon);
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
            m_ShowConnectionMenu = false;
            m_BackToStash = new Button(ShowStash) { text = "BACK TO STASH" };
            m_ConnectionPanel.Insert(0, m_BackToStash);
            m_CarryCells = new IntegerField("ENERGY CELLS TO CARRY") { name = "carryCells", isDelayed = true };
            m_CarryCells.RegisterValueChangedCallback(CarryCellsChanged);
            m_CarryNote = new Label();
            m_CarryNote.style.whiteSpace = WhiteSpace.Normal;
            m_ConnectionPanel.Insert(2, m_CarryCells); m_ConnectionPanel.Insert(3, m_CarryNote);
            m_StashScreen = new StashScreen(m_MainMenu.Q<VisualElement>("stashScreenHost"), ShowRaidPreparation, Logout, RefreshStash);
            m_LastStashGameState = GameSettings.Instance.GameState;
            GameSettings.Instance.propertyChanged += StashSettingsChanged;
            UpdateAccountPanel();
            if (AccountClient.IsLoggedIn) RestoreLogin();
        }

        private void UpdateAccountPanel()
        {
            bool loggedIn = AccountClient.IsLoggedIn && m_AccountVerified;
            bool showStash = loggedIn && !m_ShowConnectionMenu;
            m_LoginMoon.style.display = showStash ? DisplayStyle.None : DisplayStyle.Flex;
            m_AccountPanel.style.display = loggedIn ? DisplayStyle.None : DisplayStyle.Flex;
            m_ConnectionPanel.style.display = loggedIn && m_ShowConnectionMenu ? DisplayStyle.Flex : DisplayStyle.None;
            m_MainMenu.Q<VisualElement>("Container").style.display = showStash ? DisplayStyle.None : DisplayStyle.Flex;
            m_StashScreen.Present(AccountClient.DisplayName, GameSettings.Instance.PlayerCharacter,
                AccountClient.StashDust, AccountClient.StashAlloy, AccountClient.StashCells, showStash, m_AccountBusy, m_AccountError);
            m_CreateGameButton.SetEnabled(!m_AccountBusy); m_StartHostButton.SetEnabled(!m_AccountBusy); m_ConnectToServerButton.SetEnabled(!m_AccountBusy);
            m_LoggedInLabel.text = $"Signed in as {AccountClient.DisplayName}\nSTASH: Dust {AccountClient.StashDust}   Alloy {AccountClient.StashAlloy}   Cells {AccountClient.StashCells}";
            m_LoggedInLabel.style.whiteSpace = WhiteSpace.Normal;
            m_CarryCells.SetValueWithoutNotify(AccountClient.CarryCells);
            m_CarryCells.SetEnabled(!m_AccountBusy);
            m_CarryNote.text = $"Carry {AccountClient.CarryCells}/{RaidRules.BagCapacity} cells / Stash {AccountClient.StashCells}.\nCharged when the server accepts deployment. [R] uses one cell to recharge. Lost on death.";
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
                m_ShowConnectionMenu = false;
                m_Password.value = "";
                m_AccountMessage.text = "Signed out.";
            });
        }

        private void ShowRaidPreparation() { m_ShowConnectionMenu = true; UpdateAccountPanel(); }
        private void CarryCellsChanged(ChangeEvent<int> evt)
        {
            AccountClient.SelectCarryCells(evt.newValue);
            UpdateAccountPanel();
        }
        private void ShowStash() { m_ShowConnectionMenu = false; UpdateAccountPanel(); }
        private void StashSettingsChanged(object sender, BindablePropertyChangedEventArgs evt)
        {
            var gameState = GameSettings.Instance.GameState;
            bool returnedToMenu = m_LastStashGameState != GlobalGameState.MainMenu && gameState == GlobalGameState.MainMenu;
            m_LastStashGameState = gameState;
            if (!returnedToMenu) return;
            m_ShowConnectionMenu = false;
            if (AccountClient.IsLoggedIn) RefreshStash();
            else UpdateAccountPanel();
        }
        private async void RefreshStash()
        {
            await AccountAction(async ct =>
            {
                await AccountClient.ValidateAsync(m_AccountServiceUrl, ct);
                m_AccountVerified = AccountClient.IsLoggedIn;
            });
        }

        private async Task AccountAction(Func<CancellationToken, Task> action)
        {
            if (m_AccountBusy) return;
            var lifetime = m_AccountStop;
            m_AccountError = null;
            m_AccountBusy = true;
            UpdateAccountPanel();
            try { await action(lifetime.Token); }
            catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) m_AccountMessage.text = m_AccountError = "Account service timed out. Please retry."; }
            catch (System.Net.Http.HttpRequestException) { if (!lifetime.IsCancellationRequested) m_AccountMessage.text = m_AccountError = "Cannot reach account service. Please retry."; }
            catch (Exception ex) { if (!lifetime.IsCancellationRequested) m_AccountMessage.text = m_AccountError = ex.Message; }
            finally
            {
                if (!lifetime.IsCancellationRequested) { m_AccountBusy = false; UpdateAccountPanel(); }
            }
        }

        private void DisposeAccountPanel()
        {
            m_LoginMoon?.Dispose(); m_LoginMoon?.RemoveFromHierarchy(); m_LoginMoon = null;
            m_AccountStop?.Cancel();
            GameSettings.Instance.propertyChanged -= StashSettingsChanged;
            m_StashScreen?.Dispose(); m_StashScreen = null;
            m_BackToStash?.RemoveFromHierarchy();
            m_CarryCells?.UnregisterValueChangedCallback(CarryCellsChanged);
            m_CarryCells?.RemoveFromHierarchy(); m_CarryNote?.RemoveFromHierarchy();
            m_AccountPanel?.RemoveFromHierarchy(); m_LoggedInLabel?.RemoveFromHierarchy(); m_Logout?.RemoveFromHierarchy();
            m_AccountBusy = m_AccountVerified = false;
        }
    }
}
