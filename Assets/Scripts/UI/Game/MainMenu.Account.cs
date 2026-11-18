// 登录页和仓库之间的调度。在这里切换线上账号、本地单人档和好友房档案，具体鉴权或读档由 PlayerProfileClient 完成。
using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    public partial class MainMenu {
        [SerializeField] private string m_AccountServiceUrl = "http://127.0.0.1:5080/";
        private VisualElement m_AccountPanel, m_ConnectionPanel;
        private TextField m_Username, m_Password;
        private Label m_AccountMessage, m_LoggedInLabel;
        private Button m_Login, m_Register, m_Logout;
        private CancellationTokenSource m_AccountStop;
        private bool m_AccountBusy, m_AccountVerified;
        private bool m_AccountConfigurationValid;
        private StashScreen m_StashScreen;
        private bool m_ShowConnectionMenu;
        private Button m_BackToStash;
        private IntegerField m_CarryCells;
        private Label m_CarryNote;
        private string m_AccountError;
        private GlobalGameState m_LastStashGameState;
        private Button m_ProfileModeButton;
        private HalosTerminalBackdrop m_LoginAnimation;
        private HalosAccessTransition m_AccessTransition;
        private Toggle m_AccessReduceMotion;
        private VisualElement m_AccountCanvas;

        private void InitializeAccountPanel() {
            var canvas = m_AccountCanvas = m_MainMenu.Q<VisualElement>("Canvas");
            m_AccountStop = new CancellationTokenSource();
            m_ProfileModeButton = m_MainMenu.Q<Button>("ProfileMode");
            m_ProfileModeButton.clicked += SwitchProfileMode;
            m_ConnectionPanel = m_MainMenu.Q<VisualElement>("ConnectionPanel");
            m_AccountPanel = TerminalLayout.Clone("AccountAccess", "AccountPanel");
            m_AccountPanel.RegisterCallback<KeyDownEvent>(AccountKeyboardSubmit);
            canvas.RegisterCallback<GeometryChangedEvent>(AccountGeometryChanged);
            m_AccessReduceMotion = m_AccountPanel.Q<Toggle>("accessReduceMotion");
            m_AccessReduceMotion.SetValueWithoutNotify(HalosTerminalBackdrop.PrefersReducedMotion);
            m_AccessReduceMotion.RegisterValueChangedCallback(AccessMotionChanged);
            m_Username = m_AccountPanel.Q<TextField>("accountUsername");
            m_Username.SetValueWithoutNotify(AccountClient.DisplayName ?? "");
            m_Password = m_AccountPanel.Q<TextField>("accountPassword");
            m_Login = m_AccountPanel.Q<Button>("accountLogin");
            m_Login.clicked += () => RunAccountAction(false);
            m_Register = m_AccountPanel.Q<Button>("accountRegister");
            m_Register.clicked += () => RunAccountAction(true);
            m_AccountPanel.Q<Button>("accountQuit").clicked += OnQuitPressed;
            m_AccountMessage = m_AccountPanel.Q<Label>("accountMessage");
            m_ConnectionPanel.parent.Insert(0, m_AccountPanel);
            m_LoginAnimation = new HalosTerminalBackdrop(canvas, false, AccessIntroFinished);
            m_AccessTransition = new HalosAccessTransition(canvas, AccessTransitionChanged);
            m_LoggedInLabel = new Label();
            m_Logout = new Button(Logout) { text = "LOG OUT" };
            m_ConnectionPanel.Insert(0, m_LoggedInLabel);
            m_ConnectionPanel.Insert(1, m_Logout);
            m_MainMenu.Q<TextField>(UIElementNames.NameInputField).SetEnabled(false);
            m_MainMenu.Q<VisualElement>("InputPlayerName").style.display = DisplayStyle.None;
            m_ShowConnectionMenu = false;
            // 已在单人档或好友房时可直接用本地资料。保存过的线上登录要再跟账号服务验证一次。
            m_AccountVerified = PlayerProfileClient.IsOffline || PlayerProfileClient.IsLan;
            m_BackToStash = new Button(ShowStash) { text = "BACK TO STASH" };
            m_ConnectionPanel.Insert(0, m_BackToStash);
            m_CarryCells = new IntegerField("ENERGY CELLS TO CARRY") { name = "carryCells", isDelayed = true };
            m_CarryCells.RegisterValueChangedCallback(CarryCellsChanged);
            m_CarryNote = new Label();
            m_CarryNote.style.whiteSpace = WhiteSpace.Normal;
            m_ConnectionPanel.Insert(2, m_CarryCells);
            m_ConnectionPanel.Insert(3, m_CarryNote);
            m_StashScreen = new StashScreen(m_MainMenu.Q<VisualElement>("stashScreenHost"), ShowRaidPreparation, Logout,
                RefreshStash);
            m_LastStashGameState = GameSettings.Instance.GameState;
            GameSettings.Instance.propertyChanged += StashSettingsChanged;
            m_AccountConfigurationValid = false;
            // 服务地址无法配置时先记录错误，关闭线上登录入口。切换单人档仍可用，不需要等服务修复。
            try { m_AccountServiceUrl = AccountServiceSettings.Configure(m_AccountServiceUrl); } catch (Exception ex) {
                MoonkovLocalization.Set(m_AccountMessage, m_AccountError = ex.Message);
                UpdateAccountPanel();
                return;
            }
            m_AccountConfigurationValid = true;
            UpdateAccountPanel();
            if (PlayerProfileClient.IsLoggedIn) RestoreLogin();
        }

        // 线上登录恢复先经过验证，动画也要走到可以显示内容的阶段，仓库才会出现。帘幕还在动时暂时停用两边的输入。
        private void UpdateAccountPanel() {
            bool authenticated = PlayerProfileClient.IsLoggedIn && m_AccountVerified;
            // 只在首屏真正就绪且停留在主菜单的时刻推进登录过渡。进入加载界面时账号页不再接管屏幕。
            bool accessVisible = GameSettings.Instance.MainMenuSceneLoaded &&
                                 GameSettings.Instance.GameState == GlobalGameState.MainMenu &&
                                 GameSettings.Instance.MainMenuState == MainMenuState.MainMenuScreen;
            bool loggedIn = m_AccessTransition.Present(authenticated, !m_LoginAnimation.IntroPending, accessVisible,
                m_AccessReduceMotion.value);
            bool transitioning = m_AccessTransition.IsRunning;
            GameManager.Instance?.GetComponent<MoonkovAudioEnvironment>()?.SetAccessVisible(!loggedIn);
            if (!loggedIn || PlayerProfileClient.IsOffline || PlayerProfileClient.IsLan) m_LobbyOpen = false;
            MoonkovLocalization.Set(m_ProfileModeButton,
                PlayerProfileClient.IsOffline ? "ONLINE ACCOUNTS" : "SINGLEPLAYER");
            m_ProfileModeButton.SetEnabled(!transitioning && !PlayerProfileClient.IsLan &&
                                           (!m_AccountBusy || !PlayerProfileClient.IsOffline) &&
                                           PlayerProfileClient.SupportsSinglePlayer);
            MoonkovLocalization.Set(m_Logout,
                PlayerProfileClient.IsLan ? "离开好友房间" :
                PlayerProfileClient.IsOffline ? "CLOSE SINGLEPLAYER" : "LOG OUT");
            m_ProfileModeButton.tooltip = MoonkovLocalization.Text(PlayerProfileClient.SupportsSinglePlayer
                ? "Open the independent local stash and single-player raids."
                : "Single player requires a Client+Server build.");
            bool showStash = loggedIn && !m_ShowConnectionMenu;
            m_MainMenu.Q("Canvas").EnableInClassList("archive-access-stash", showStash);
            var canvas = m_MainMenu.Q("Canvas");
            canvas.EnableInClassList("archive-access-account", !loggedIn);
            canvas.AddToClassList("archive-login-plates");
            m_LoginAnimation.Present(!loggedIn && accessVisible, m_AccessReduceMotion.value);
            // 同一个账号模式按钮在登录页、仓库和连接区之间移动，事件只绑一次。好友房入口也跟随当前区域。
            var modeHost = loggedIn
                ? showStash ? m_MainMenu.Q("stashMenuActions") : m_ConnectionPanel
                : m_AccountPanel.Q("accountSecondaryActions");
            m_ProfileModeButton.EnableInClassList("menu-entry-docked", loggedIn);
            if (m_ProfileModeButton.parent != modeHost) {
                m_ProfileModeButton.RemoveFromHierarchy();
                modeHost.Insert(0, m_ProfileModeButton);
            }
            m_LanRoom?.SetEntryHost(loggedIn ? modeHost : null);
            m_AccountPanel.style.display = loggedIn ? DisplayStyle.None : DisplayStyle.Flex;
            m_AccountPanel.SetEnabled(!transitioning);
            m_MainMenu.Q("stashScreenHost").SetEnabled(!transitioning);
            m_ConnectionPanel.SetEnabled(!transitioning);
            m_ConnectionPanel.style.display = loggedIn && m_ShowConnectionMenu ? DisplayStyle.Flex : DisplayStyle.None;
            m_MainMenu.Q<VisualElement>("Container").style.display = showStash ? DisplayStyle.None : DisplayStyle.Flex;
            // 账号、人物、资源数字和错误统一从当前档案与连接状态传下去。仓库页本身无需知道账号如何验证。
            m_StashScreen.Present(PlayerProfileClient.DisplayName, GameSettings.Instance.PlayerCharacter,
                PlayerProfileClient.StashDust, PlayerProfileClient.StashAlloy, PlayerProfileClient.StashCells,
                showStash, m_AccountBusy || transitioning,
                m_AccountError ?? ConnectionSettings.Instance.ConnectionError);
            m_CreateGameButton.SetEnabled(!m_AccountBusy);
            m_StartHostButton.SetEnabled(!m_AccountBusy);
            m_ConnectToServerButton.SetEnabled(!m_AccountBusy);
            MoonkovLocalization.Set(m_LoggedInLabel, "Signed in as {0}\nSTASH: Dust {1}   Alloy {2}   Cells {3}",
                PlayerProfileClient.DisplayName, PlayerProfileClient.StashDust, PlayerProfileClient.StashAlloy,
                PlayerProfileClient.StashCells);
            m_LoggedInLabel.style.whiteSpace = WhiteSpace.Normal;
            m_CarryCells.SetValueWithoutNotify(PlayerProfileClient.CarryCells);
            m_CarryCells.SetEnabled(!m_AccountBusy);
            MoonkovLocalization.Set(m_CarryNote,
                "Carry {0}/{1} cells / Stash {2}.\nEach full cell stores {3} energy. Reloading spends energy per round. Partial charge is retained. Lost on death.",
                PlayerProfileClient.CarryCells, RaidRules.BagCapacity, PlayerProfileClient.StashCells,
                BatteryEnergy.Capacity);
            m_Login.SetEnabled(!m_AccountBusy && m_AccountConfigurationValid);
            m_Register.SetEnabled(!m_AccountBusy && m_AccountConfigurationValid);
            m_Logout.SetEnabled(!m_AccountBusy);
            m_Username.SetEnabled(!m_AccountBusy);
            m_Password.SetEnabled(!m_AccountBusy);
            m_AccountPanel.EnableInClassList("is-busy", m_AccountBusy);
            m_AccountMessage.EnableInClassList("is-error", !string.IsNullOrEmpty(m_AccountError));
            PresentLobby();
        }

        private async void RestoreLogin() {
            await AccountAction(async ct => {
                await PlayerProfileClient.ValidateAsync(m_AccountServiceUrl, ct);
                m_AccountVerified = PlayerProfileClient.IsLoggedIn;
                MoonkovLocalization.Set(m_AccountMessage,
                    m_AccountVerified ? "Signed in." : "Login expired. Please sign in again.");
            });
        }

        private async void RunAccountAction(bool register) {
            await AccountAction(async ct => {
                MoonkovLocalization.Set(m_AccountMessage, register ? "Creating account..." : "Signing in...");
                // 注册和登录共用一个入口。认证成功后把输入框里的密码清空，并通知转场可以进入仓库。
                await PlayerProfileClient.AuthenticateAsync(m_AccountServiceUrl, m_Username.value, m_Password.value,
                    register, ct);
                m_Password.value = "";
                m_AccountVerified = true;
                MoonkovLocalization.Set(m_AccountMessage, "Signed in.");
            });
        }

        private async void Logout() {
            await AccountAction(async ct => {
                await PlayerProfileClient.LogoutAsync(m_AccountServiceUrl, ct);
                m_AccountVerified = false;
                m_ShowConnectionMenu = false;
                m_Password.value = "";
                MoonkovLocalization.Set(m_AccountMessage, "Signed out.");
            });
        }

        // 同一个出发按钮有三条路径。好友房先开房间页，单人直接启动本地战局，线上先打开小队页等大家准备。
        private void ShowRaidPreparation() {
            // 打开好友房只进入准备。单人出发则立即请求建立本地战局，线上小队留在菜单等待服务器给入场信息。
            if (PlayerProfileClient.IsLan) { m_LanRoom.Open(); } else if (PlayerProfileClient.IsOffline) {
                GameManager.Instance.StartGameAsync(CreationType.Offline);
            } else {
                m_LobbyOpen = true;
                m_ShowConnectionMenu = true;
                UpdateAccountPanel();
                PollLobby();
            }
        }

        private async void SwitchProfileMode() {
            if (GameSettings.Instance.GameState != GlobalGameState.MainMenu || PlayerProfileClient.IsLan ||
                m_AccessTransition.IsRunning)
                return;
            if (m_AccountBusy && !PlayerProfileClient.IsOffline) {
                // 切到单人档时取消仍在等待的线上请求，避免旧回调晚到后改写新界面。
                var previous = m_AccountStop;
                previous.Cancel();
                previous.Dispose();
                m_AccountStop = new CancellationTokenSource();
                m_AccountBusy = false;
            }
            await AccountAction(async ct => {
                m_ShowConnectionMenu = false;
                if (PlayerProfileClient.IsOffline) {
                    // 离开单人档前先把排队的库存修改刷完。完成后才清除本地会话并切回线上账号入口。
                    await PlayerProfileClient.FlushMenuInventoryAsync(ct);
                    PlayerProfileClient.LeaveOffline();
                    m_AccountVerified = false;
                    MoonkovLocalization.Set(m_AccountMessage, "Sign in to your online account.");
                } else {
                    await PlayerProfileClient.EnterOfflineAsync(ct);
                    m_AccountVerified = true;
                    MoonkovLocalization.Set(m_AccountMessage, "Single-player save opened.");
                }
            }, true);
        }

        private void CarryCellsChanged(ChangeEvent<int> evt) {
            PlayerProfileClient.SelectCarryCells(evt.newValue);
            UpdateAccountPanel();
        }

        private void ShowStash() {
            m_LobbyOpen = false;
            m_ShowConnectionMenu = false;
            UpdateAccountPanel();
        }

        private void StashSettingsChanged(object sender, BindablePropertyChangedEventArgs evt) {
            var gameState = GameSettings.Instance.GameState;
            if (gameState != GlobalGameState.MainMenu) {
                m_LoginAnimation?.Present(false, m_AccessReduceMotion.value);
                m_AccessTransition?.Present(PlayerProfileClient.IsLoggedIn && m_AccountVerified,
                    !m_LoginAnimation.IntroPending, false, m_AccessReduceMotion.value);
            }
            // 只在从战局回到菜单的那次状态变化重新验证档案。还在小队里的玩家会回到小队页。
            bool returnedToMenu = m_LastStashGameState != GlobalGameState.MainMenu &&
                                  gameState == GlobalGameState.MainMenu;
            m_LastStashGameState = gameState;
            if (!returnedToMenu) {
                if (evt.propertyName == GameSettings.MainMenuSceneLoadedPropertyName ||
                    evt.propertyName == GameSettings.MainMenuStylePropertyName)
                    UpdateAccountPanel();
                return;
            }
            // 回到菜单时，用房间快照判断是否仍属于原小队。有房间就显示小队页，否则回自己的仓库。
            m_ShowConnectionMenu = OnlineLobbyClient.Snapshot?.Code != null;
            m_LobbyOpen = m_ShowConnectionMenu;
            if (PlayerProfileClient.IsLoggedIn)
                RefreshStash();
            else
                UpdateAccountPanel();
        }

        private async void RefreshStash() {
            await AccountAction(async ct => {
                await PlayerProfileClient.ValidateAsync(m_AccountServiceUrl, ct);
                m_AccountVerified = PlayerProfileClient.IsLoggedIn;
            });
        }

        private async Task AccountAction(Func<CancellationToken, Task> action, bool allowOffline = false) {
            if (m_AccountBusy || m_AccessTransition.IsRunning || (!m_AccountConfigurationValid &&
                                                                  !PlayerProfileClient.IsOffline &&
                                                                  !PlayerProfileClient.IsLan && !allowOffline))
                return;
            // 请求记住开始时的取消令牌。离开页面或者切档后，旧回调不会再解除新页面的忙碌状态。
            var lifetime = m_AccountStop;
            m_AccountError = null;
            m_AccountBusy = true;
            UpdateAccountPanel();
            // 请求执行期间禁用重复操作。异常变成界面提示，结束后同一批请求才可恢复按钮。
            try { await action(lifetime.Token); } catch (OperationCanceledException) {
                if (!lifetime.IsCancellationRequested)
                    MoonkovLocalization.Set(m_AccountMessage,
                        m_AccountError = "Account service timed out. Please retry.");
            } catch (System.Net.WebException ex) {
                if (!lifetime.IsCancellationRequested)
                    MoonkovLocalization.Set(m_AccountMessage,
                        m_AccountError =
                            ex.Status == System.Net.WebExceptionStatus.Timeout ||
                            ex.Status == System.Net.WebExceptionStatus.RequestCanceled
                                ? "Account service timed out. Please retry."
                                : "Cannot reach account service. Please retry.");
            } catch (System.Net.Http.HttpRequestException) {
                if (!lifetime.IsCancellationRequested)
                    MoonkovLocalization.Set(m_AccountMessage,
                        m_AccountError = "Cannot reach account service. Please retry.");
            } catch (Exception ex) {
                if (!lifetime.IsCancellationRequested)
                    MoonkovLocalization.Set(m_AccountMessage, m_AccountError = ex.Message);
            } finally {
                if (!lifetime.IsCancellationRequested) {
                    m_AccountBusy = false;
                    UpdateAccountPanel();
                }
            }
        }

        private void DisposeAccountPanel() {
            m_LoginAnimation?.Dispose();
            m_LoginAnimation = null;
            m_AccessTransition?.Dispose();
            m_AccessTransition = null;
            m_AccountPanel?.UnregisterCallback<KeyDownEvent>(AccountKeyboardSubmit);
            m_AccountCanvas?.UnregisterCallback<GeometryChangedEvent>(AccountGeometryChanged);
            m_AccessReduceMotion?.UnregisterValueChangedCallback(AccessMotionChanged);
            m_AccountStop?.Cancel();
            if (m_ProfileModeButton != null) m_ProfileModeButton.clicked -= SwitchProfileMode;
            GameSettings.Instance.propertyChanged -= StashSettingsChanged;
            m_StashScreen?.Dispose();
            m_StashScreen = null;
            m_BackToStash?.RemoveFromHierarchy();
            m_CarryCells?.UnregisterValueChangedCallback(CarryCellsChanged);
            m_CarryCells?.RemoveFromHierarchy();
            m_CarryNote?.RemoveFromHierarchy();
            m_AccountPanel?.RemoveFromHierarchy();
            m_LoggedInLabel?.RemoveFromHierarchy();
            m_Logout?.RemoveFromHierarchy();
            m_AccountBusy = m_AccountVerified = false;
            m_AccountCanvas = null;
        }

        private void AccountKeyboardSubmit(KeyDownEvent evt) {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
            var target = evt.target as VisualElement;
            if (target == null || (!m_Username.Contains(target) && !m_Password.Contains(target) &&
                                   target != m_Username && target != m_Password))
                return;
            if (m_Login.enabledInHierarchy) RunAccountAction(false);
            evt.StopPropagation();
        }

        private void AccountGeometryChanged(GeometryChangedEvent evt) {
            m_AccountCanvas?.EnableInClassList("halos-compact", evt.newRect.height < 800);
        }

        private void AccessMotionChanged(ChangeEvent<bool> evt) {
            PlayerPrefs.SetInt(HalosTerminalBackdrop.ReducedMotionPreference, evt.newValue ? 1 : 0);
            UpdateAccountPanel();
        }

        private void AccessIntroFinished() {
            UpdateAccountPanel();
            FocusAccountInput();
        }

        private void AccessTransitionChanged() {
            UpdateAccountPanel();
            if (!m_AccessTransition.IsRunning && m_AccessTransition.ContentVisible) {
                var home = m_MainMenu.Q("halosOriginalHome");
                if (home != null) {
                    home.focusable = true;
                    home.Focus();
                }
            }
        }

        private void FocusAccountInput() {
            if (m_AccountBusy || m_AccessTransition.IsRunning || m_AccountPanel.style.display == DisplayStyle.None)
                return;
            if (string.IsNullOrEmpty(m_Username.value))
                m_Username.Focus();
            else
                m_Password.Focus();
        }
    }
}