// 主菜单的总入口。这里把界面和 GameSettings、ConnectionSettings 绑在一起，再装上账号、模式和房间页面。
// 从选好模式到开始加载，接着看 MainMenu.Account 的 ShowRaidPreparation 和 GameManager.StartGameAsync。
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    [RequireComponent(typeof(UIDocument))]
    public partial class MainMenu : MonoBehaviour {
        private static class UIElementNames {
            public const string HidingBackground = "HidingBackground";
            public const string NameInputField = "PlayerNameField";
            public const string ConnectionModeOption = "ConnetionModeOption";
            public const string SessionNameLabel = "SessionName";
            public const string SessionInputField = "SessionNameField";
            public const string CreateGame = "CreateJoinGame";
            public const string StartHost = "StartHost";
            public const string ConnectToServer = "ConnectToServer";
            public const string QuitGame = "QuitButton";
        }

        private VisualElement m_MainMenu;
        private RadioButtonGroup m_ConnectionModeGroup;
        private Label m_SessionNameLabel;
        private TextField m_SessionNameField;
        private Button m_CreateGameButton;
        private Button m_StartHostButton;
        private Button m_ConnectToServerButton;
        private Button m_QuitButton;
        private Button m_JoinRelayButton;
        private VisualElement m_RelayJoinControls;
        private Button m_ResumeRaid;
        private Label m_ResumeStatus;
        private IVisualElementScheduledItem m_ResumeRefresh;

        private void OnEnable() {
            m_MainMenu = GetComponent<UIDocument>().rootVisualElement;
            // 菜单是否显示听 GameSettings。连接流程切换全局状态时，UI 绑定会自动跟上，不用每个按钮都手动隐藏页面。
            m_MainMenu.SetBinding("style.display", new DataBinding {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.MainMenuStylePropertyName),
                bindingMode = BindingMode.ToTarget
            });
            // 玩家名和会话名是双向绑定，文本输入和设置使用同一份数据。认证账号启用后由账号页接管玩家名。
            var nameInputField = m_MainMenu.Q<TextField>(UIElementNames.NameInputField);
            nameInputField.SetBinding("value", new DataBinding {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.PlayerName)),
                bindingMode = BindingMode.TwoWay
            });
            m_SessionNameLabel = m_MainMenu.Q<Label>(UIElementNames.SessionNameLabel);
            var connectionMode = m_ConnectionModeGroup =
                m_MainMenu.Q<RadioButtonGroup>(UIElementNames.ConnectionModeOption);
            connectionMode.SetBinding("value", new DataBinding {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.ConnectionMode)),
                bindingMode = BindingMode.TwoWay
            });
            m_ConnectionModeGroup.RegisterValueChangedCallback(OnConnectionModeChanged);
            var sessionInputField = m_SessionNameField = m_MainMenu.Q<TextField>(UIElementNames.SessionInputField);
            sessionInputField.SetBinding("value", new DataBinding {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.SessionName)),
                bindingMode = BindingMode.TwoWay
            });
            m_CreateGameButton = m_MainMenu.Q<Button>(UIElementNames.CreateGame);
            m_CreateGameButton.clicked += OnCreateGamePressed;
            m_RelayJoinControls = m_MainMenu.Q("RelayJoinControls");
            m_MainMenu.Q<TextField>("RelayCodeField").SetBinding("value", new DataBinding {
                dataSource = ConnectionSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(ConnectionSettings.SessionCode)),
                bindingMode = BindingMode.TwoWay
            });
            m_JoinRelayButton = m_MainMenu.Q<Button>("JoinRelayByCode");
            m_JoinRelayButton.clicked += OnJoinRelayPressed;
            // 通过房间码加入时，先让 ConnectionSettings 判断格式。等真正连接后才会知道房间码是否可用。
            m_JoinRelayButton.SetBinding("enabledSelf", new DataBinding {
                dataSource = ConnectionSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(ConnectionSettings.IsSessionCodeFormatValid)),
                bindingMode = BindingMode.ToTarget
            });
            m_StartHostButton = m_MainMenu.Q<Button>(UIElementNames.StartHost);
            m_StartHostButton.clicked += OnStartHostPressed;
            m_StartHostButton.tooltip = m_CreateGameButton.tooltip = "在线房主需要受信服务器的后端配置；普通客户端使用房间码或服务器地址加入。";
            m_ConnectToServerButton = m_MainMenu.Q<Button>(UIElementNames.ConnectToServer);
            m_ConnectToServerButton.clicked += OnConnectToServerPressed;
            m_QuitButton = m_MainMenu.Q<Button>(UIElementNames.QuitGame);
            m_QuitButton.clicked += OnQuitPressed;
            // 主菜单资源未加载完时保留遮挡层。场景完成后通过属性通知撤掉它，避免露出中间画面。
            var hidingBackground = m_MainMenu.Q<VisualElement>(UIElementNames.HidingBackground);
            hidingBackground.SetBinding("style.display", new DataBinding {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.MainMenuSceneLoadedPropertyName),
                bindingMode = BindingMode.ToTarget
            });
            ToggleConnectionModeDisplay();
            InitializeAccountPanel();
            InitializeBountyModeUI();
            InitializeLanUI();
            InitializeOnlineLobby();
            m_ResumeRaid = m_MainMenu.Q<Button>("ResumeRaid");
            m_ResumeStatus = m_MainMenu.Q<Label>("ResumeStatus");
            m_ResumeRaid.clicked += ResumeRaid;
            m_ResumeRefresh = m_MainMenu.schedule.Execute(RefreshReconnect).Every(1000);
            RefreshReconnect();
            MoonkovLocalization.Bind(m_MainMenu);
        }

        // 离开菜单需结束轮询和事件订阅。否则旧按钮还可能调用已经切换状态的 GameManager。
        private void OnDisable() {
            m_ResumeRefresh?.Pause();
            if (m_ResumeRaid != null) m_ResumeRaid.clicked -= ResumeRaid;
            DisposeLanUI();
            DisposeOnlineLobby();
            DisposeBountyModeUI();
            DisposeAccountPanel();
            m_CreateGameButton.clicked -= OnCreateGamePressed;
            m_JoinRelayButton.clicked -= OnJoinRelayPressed;
            m_ConnectionModeGroup.UnregisterValueChangedCallback(OnConnectionModeChanged);
            m_ConnectToServerButton.clicked -= OnConnectToServerPressed;
            m_StartHostButton.clicked -= OnStartHostPressed;
            m_QuitButton.clicked -= OnQuitPressed;
        }

        private void OnConnectionModeChanged(ChangeEvent<int> evt) {
            GameSettings.Instance.ConnectionMode = evt.newValue;
            ToggleConnectionModeDisplay();
        }

        // 切换 Relay 或直连时更换能用的入口。选择连接方式只控制这层界面，出发要点相应按钮。
        private void ToggleConnectionModeDisplay() {
            m_RelayJoinControls.style.display =
                GameSettings.Instance.ConnectionMode == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (GameSettings.Instance.ConnectionMode == 0) {
                m_SessionNameLabel.style.display = m_SessionNameField.style.display = DisplayStyle.Flex;
                m_CreateGameButton.style.display = DisplayStyle.Flex;
                m_StartHostButton.style.display = m_ConnectToServerButton.style.display = DisplayStyle.None;
            } else {
                m_SessionNameLabel.style.display = m_SessionNameField.style.display = DisplayStyle.None;
                m_CreateGameButton.style.display = DisplayStyle.None;
                ;
                m_StartHostButton.style.display = m_ConnectToServerButton.style.display = DisplayStyle.Flex;
            }
        }

        private void Start() { }
        // 这些按钮只告诉 GameManager 要用哪条连接流程。创建 World、等连接或者回到菜单由 GameManager 负责。
        private static void OnCreateGamePressed() { GameManager.Instance.StartGameAsync(CreationType.CreateOrJoin); }
        private static void OnJoinRelayPressed() { GameManager.Instance.StartGameAsync(CreationType.JoinRelay); }
        private static void OnStartHostPressed() { GameManager.Instance.StartGameAsync(CreationType.Host); }

        private static void OnConnectToServerPressed() {
            GameManager.Instance.StartGameAsync(CreationType.ConnectAndJoin);
        }

        private static void OnQuitPressed() { GameManager.Instance.QuitAsync(); }
        private static void ResumeRaid() { GameManager.Instance.ReconnectLastGameAsync(); }

        // 重连入口定时看 SessionReconnect。剩余时间是客户端提示，恢复原角色还要等服务器确认。
        private void RefreshReconnect() {
            bool available = SessionReconnect.Available && PlayerProfileClient.IsLoggedIn;
            m_MainMenu.Q("ReconnectPanel").style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            m_ResumeRaid.SetEnabled(available && !m_AccountBusy &&
                                    GameSettings.Instance.GameState == GlobalGameState.MainMenu);
            m_ResumeStatus.text = $"原战局仍保留，重连窗口约剩 {SessionReconnect.SecondsRemaining} 秒。角色仍可受伤；服务器确认是否可恢复。";
        }
    }
}