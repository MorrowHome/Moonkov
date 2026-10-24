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
            MoonkovLocalization.Bind(m_MainMenu);
        }

        void OnDisable()
        {
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
    }
}
