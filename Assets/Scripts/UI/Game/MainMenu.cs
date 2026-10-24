using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    [RequireComponent(typeof(UIDocument))]
    public partial class MainMenu : MonoBehaviour
    {
        static class UIElementNames
        {
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

        VisualElement m_MainMenu;
        RadioButtonGroup m_ConnectionModeGroup;
        Label m_SessionNameLabel;
        TextField m_SessionNameField;
        Button m_CreateGameButton;
        Button m_StartHostButton;
        Button m_ConnectToServerButton;
        Button m_QuitButton;
        Button m_JoinRelayButton;
        VisualElement m_RelayJoinControls;
        Button m_ResumeRaid;
        Label m_ResumeStatus;
        IVisualElementScheduledItem m_ResumeRefresh;

        void OnEnable()
        {
            m_MainMenu = GetComponent<UIDocument>().rootVisualElement;

            m_MainMenu.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.MainMenuStylePropertyName),
                bindingMode = BindingMode.ToTarget,
            });

            var nameInputField = m_MainMenu.Q<TextField>(UIElementNames.NameInputField);
            nameInputField.SetBinding("value", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.PlayerName)),
                bindingMode = BindingMode.TwoWay,
            });

            m_SessionNameLabel = m_MainMenu.Q<Label>(UIElementNames.SessionNameLabel);
            var connectionMode = m_ConnectionModeGroup = m_MainMenu.Q<RadioButtonGroup>(UIElementNames.ConnectionModeOption);
            connectionMode.SetBinding("value", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.ConnectionMode)),
                bindingMode = BindingMode.TwoWay,
            });
            m_ConnectionModeGroup.RegisterValueChangedCallback(OnConnectionModeChanged);

            var sessionInputField = m_SessionNameField = m_MainMenu.Q<TextField>(UIElementNames.SessionInputField);
            sessionInputField.SetBinding("value", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.SessionName)),
                bindingMode = BindingMode.TwoWay,
            });

            m_CreateGameButton = m_MainMenu.Q<Button>(UIElementNames.CreateGame);
            m_CreateGameButton.clicked += OnCreateGamePressed;
            m_RelayJoinControls = m_MainMenu.Q("RelayJoinControls");
            m_MainMenu.Q<TextField>("RelayCodeField").SetBinding("value", new DataBinding
            {
                dataSource = ConnectionSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(ConnectionSettings.SessionCode)),
                bindingMode = BindingMode.TwoWay,
            });
            m_JoinRelayButton = m_MainMenu.Q<Button>("JoinRelayByCode");
            m_JoinRelayButton.clicked += OnJoinRelayPressed;
            m_JoinRelayButton.SetBinding("enabledSelf", new DataBinding
            {
                dataSource = ConnectionSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(ConnectionSettings.IsSessionCodeFormatValid)),
                bindingMode = BindingMode.ToTarget,
            });

            m_StartHostButton = m_MainMenu.Q<Button>(UIElementNames.StartHost);
            m_StartHostButton.clicked += OnStartHostPressed;
            m_StartHostButton.tooltip = m_CreateGameButton.tooltip = "在线房主需要受信服务器的后端配置；普通客户端使用房间码或服务器地址加入。";

            m_ConnectToServerButton = m_MainMenu.Q<Button>(UIElementNames.ConnectToServer);
            m_ConnectToServerButton.clicked += OnConnectToServerPressed;

            m_QuitButton = m_MainMenu.Q<Button>(UIElementNames.QuitGame);
            m_QuitButton.clicked += OnQuitPressed;

            var hidingBackground = m_MainMenu.Q<VisualElement>(UIElementNames.HidingBackground);
            hidingBackground.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.MainMenuSceneLoadedPropertyName),
                bindingMode = BindingMode.ToTarget,
            });

            ToggleConnectionModeDisplay();
            InitializeAccountPanel();
            InitializeBountyModeUI();
            InitializeLanUI();
            m_ResumeRaid = m_MainMenu.Q<Button>("ResumeRaid");
            m_ResumeStatus = m_MainMenu.Q<Label>("ResumeStatus");
            m_ResumeRaid.clicked += ResumeRaid;
            m_ResumeRefresh = m_MainMenu.schedule.Execute(RefreshReconnect).Every(1000);
            RefreshReconnect();
            MoonkovLocalization.Bind(m_MainMenu);
        }

        void OnDisable()
        {
            m_ResumeRefresh?.Pause();
            if (m_ResumeRaid != null) m_ResumeRaid.clicked -= ResumeRaid;
            DisposeLanUI();
            DisposeBountyModeUI();
            DisposeAccountPanel();
            m_CreateGameButton.clicked -= OnCreateGamePressed;
            m_JoinRelayButton.clicked -= OnJoinRelayPressed;
            m_ConnectionModeGroup.UnregisterValueChangedCallback(OnConnectionModeChanged);
            m_ConnectToServerButton.clicked -= OnConnectToServerPressed;
            m_StartHostButton.clicked -= OnStartHostPressed;
            m_QuitButton.clicked -= OnQuitPressed;
        }

        void OnConnectionModeChanged(ChangeEvent<int> evt)
        {
            GameSettings.Instance.ConnectionMode = evt.newValue;
            ToggleConnectionModeDisplay();
        }

        void ToggleConnectionModeDisplay()
        {
            m_RelayJoinControls.style.display = GameSettings.Instance.ConnectionMode == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (GameSettings.Instance.ConnectionMode == 0)
            {
                m_SessionNameLabel.style.display = m_SessionNameField.style.display = DisplayStyle.Flex;
                m_CreateGameButton.style.display = DisplayStyle.Flex;
                m_StartHostButton.style.display = m_ConnectToServerButton.style.display = DisplayStyle.None;
            }
            else
            {
                m_SessionNameLabel.style.display = m_SessionNameField.style.display = DisplayStyle.None;
                m_CreateGameButton.style.display = DisplayStyle.None;;
                m_StartHostButton.style.display = m_ConnectToServerButton.style.display = DisplayStyle.Flex;
            }
        }

        void Start()
        {

        }

        static void OnCreateGamePressed() => GameManager.Instance.StartGameAsync(CreationType.CreateOrJoin);
        static void OnJoinRelayPressed() => GameManager.Instance.StartGameAsync(CreationType.JoinRelay);

        static void OnStartHostPressed() => GameManager.Instance.StartGameAsync(CreationType.Host);

        static void OnConnectToServerPressed() => GameManager.Instance.StartGameAsync(CreationType.ConnectAndJoin);

        static void OnQuitPressed() => GameManager.Instance.QuitAsync();
        static void ResumeRaid() => GameManager.Instance.ReconnectLastGameAsync();
        void RefreshReconnect()
        {
            bool available = SessionReconnect.Available && PlayerProfileClient.IsLoggedIn;
            m_MainMenu.Q("ReconnectPanel").style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            m_ResumeRaid.SetEnabled(available && !m_AccountBusy && GameSettings.Instance.GameState == GlobalGameState.MainMenu);
            m_ResumeStatus.text = $"原战局仍保留，重连窗口约剩 {SessionReconnect.SecondsRemaining} 秒。角色仍可受伤；服务器确认是否可恢复。";
        }
    }
}
