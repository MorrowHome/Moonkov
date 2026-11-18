// 存主菜单、加载、游戏内和暂停等状态，并通过属性变更通知 UI Toolkit。入局时选的模式和角色也由流程代码从这里读取。
using System;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    public enum GlobalGameState {
        MainMenu,
        InGame,
        Loading
    }

    public enum MainMenuState {
        MainMenuScreen,
        StartHostPopup,
        DirectConnectPopUp,
        JoinCodePopUp
    }

    public enum PlayerState {
        IsPlaying,
        IsDead
    }

    public class GameSettings : INotifyBindablePropertyChanged {
        // Keep the existing network identity so old snapshots and corpse visuals stay compatible.
        public const int DollSingerCharacterIndex = 2;
        public const int HatsuneMikuCharacterIndex = 3;
        public const int ShianCharacterIndex = 4;

        public static bool IsPlayableCharacter(int characterIndex) {
            return characterIndex == DollSingerCharacterIndex || characterIndex == HatsuneMikuCharacterIndex ||
                   characterIndex == ShianCharacterIndex;
        }

        public static int NormalizeCharacterIndex(int characterIndex) {
            return IsPlayableCharacter(characterIndex) ? characterIndex : DollSingerCharacterIndex;
        }

        public static string CharacterName(int characterIndex) {
            return characterIndex == ShianCharacterIndex ? "SHIAN" :
                characterIndex == HatsuneMikuCharacterIndex ? "HATSUNE MIKU" : "DOLLSINGER";
        }

        public static string MenuCharacterSettingsPath(int characterIndex) {
            return characterIndex switch {
                HatsuneMikuCharacterIndex => "Moonkov/HatsuneMikuMenuCharacterSettings",
                ShianCharacterIndex => "Moonkov/ShianMenuCharacterSettings",
                _ => "Moonkov/MenuCharacterSettings"
            };
        }

        public static GameSettings Instance { get; private set; } = null!;

        /// <summary>
        /// This INitialOnloadMethod instantiate the singletone instance 
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RuntimeInitializeOnLoad() { Instance = new GameSettings(); }

        private const string k_PlayerNameKey = "PlayerName";
        private const string k_ConnectionModeKey = "ConnectionMode";
        private const string k_SessionNameKey = "SessionName";
        private const string k_RaidModeKey = "Moonkov.RaidMode";
        private const string k_PlayerCharacterKey = "Moonkov.PlayerCharacter";
        private const string k_ExpeditionCompanionKey = "Moonkov.ExpeditionCompanions";

        public int ExpeditionCompanionCount {
            get => Mathf.Clamp(PlayerPrefs.GetInt(k_ExpeditionCompanionKey, 0), 0, RaidRules.MaxCompanions);
            set => PlayerPrefs.SetInt(k_ExpeditionCompanionKey, Mathf.Clamp(value, 0, RaidRules.MaxCompanions));
        }

        private RaidGameMode m_RequestedMode;

        public RaidGameMode RequestedMode {
            get => m_RequestedMode;
            set {
                if (!Enum.IsDefined(typeof(RaidGameMode), value)) return;
                if (m_RequestedMode == value) return;
                m_RequestedMode = value;
                PlayerPrefs.SetInt(k_RaidModeKey, (int)value);
                Notify();
            }
        }

        public int RequestedRulesVersion =>
            RequestedMode switch {
                RaidGameMode.BountyClash => BountyClashRules.Version,
                RaidGameMode.Arena => ArenaMatch.Version,
                RaidGameMode.Classic => ClassicMatch.Version,
                _ => 0
            };

        private GameSettings() {
            m_PlayerName = PlayerPrefs.GetString(k_PlayerNameKey, Environment.UserName);
            m_PlayerCharacter =
                NormalizeCharacterIndex(PlayerPrefs.GetInt(k_PlayerCharacterKey, DollSingerCharacterIndex));
            // The shipped settings file decides which connection mode a fresh install opens in, so
            // players pointed at a dedicated server land straight on the host/join buttons.
            m_ConnectionMode = PlayerPrefs.GetInt(k_ConnectionModeKey,
                ClientSettings.ConnectionMode >= 0 ? ClientSettings.ConnectionMode : 0);
            m_SessionName = PlayerPrefs.GetString(k_SessionNameKey, "default-session");
            m_RequestedMode = GhostBridgeBootstrap.IsServerOnly ? RaidGameMode.Expedition :
                PlayerPrefs.GetInt(k_RaidModeKey, 0) == 1 ? RaidGameMode.BountyClash : RaidGameMode.Expedition;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-raid-mode")
                    m_RequestedMode = args[i + 1].Equals("bounty-clash", StringComparison.OrdinalIgnoreCase)
                        ? RaidGameMode.BountyClash
                        : RaidGameMode.Expedition;
        }

        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        private void Notify([CallerMemberName] string property = "") {
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
        }

        public AwaitableCompletionSource CancellableUserInputPopUp;
        private GlobalGameState m_GameState;

        public GlobalGameState GameState {
            get => m_GameState;
            set {
                if (m_GameState == value) return;
                m_GameState = value;
                Notify(MainMenuStylePropertyName);
                Notify(LoadingScreenStylePropertyName);
                Notify(InGameUIPropertyName);
            }
        }

        private MainMenuState m_MainMenuState;

        public MainMenuState MainMenuState {
            get => m_MainMenuState;
            set {
                if (m_MainMenuState == value) return;
                m_MainMenuState = value;
                Notify(MainMenuStylePropertyName);
                Notify(JoinSessionStylePropertyName);
                Notify(StartHostStylePropertyName);
                Notify(DirectConnectStylePropertyName);
            }
        }

        private PlayerState m_PlayerState;

        public PlayerState PlayerState {
            get => m_PlayerState;
            set {
                if (m_PlayerState == value) return;
                m_PlayerState = value;
                //Notify(RespawnScreenStylePropertyName); //TODO: Define the respawn screen style
            }
        }

        public static readonly string MainMenuStylePropertyName = nameof(MainMenuStyle);

        [CreateProperty]
        private DisplayStyle MainMenuStyle =>
            m_GameState == GlobalGameState.MainMenu && MainMenuState == MainMenuState.MainMenuScreen
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        public static readonly string JoinSessionStylePropertyName = nameof(JoinSessionStyle);

        [CreateProperty]
        private DisplayStyle JoinSessionStyle =>
            m_GameState == GlobalGameState.MainMenu && MainMenuState == MainMenuState.JoinCodePopUp
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        public static readonly string StartHostStylePropertyName = nameof(StartHostStyle);

        [CreateProperty]
        private DisplayStyle StartHostStyle =>
            m_GameState == GlobalGameState.MainMenu && MainMenuState == MainMenuState.StartHostPopup
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        public static readonly string DirectConnectStylePropertyName = nameof(DirectConnectStyle);

        [CreateProperty]
        private DisplayStyle DirectConnectStyle =>
            m_GameState == GlobalGameState.MainMenu && MainMenuState == MainMenuState.DirectConnectPopUp
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        public static readonly string LoadingScreenStylePropertyName = nameof(LoadingScreenStyle);

        [CreateProperty]
        private DisplayStyle LoadingScreenStyle {
            get {
                if (m_GameState == GlobalGameState.Loading) return DisplayStyle.Flex;
                LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.NotLoading, 0.0f);
                return DisplayStyle.None;
            }
        }

        public static readonly string InGameUIPropertyName = nameof(InGameUI);

        [CreateProperty]
        private DisplayStyle InGameUI => m_GameState == GlobalGameState.InGame ? DisplayStyle.Flex : DisplayStyle.None;

        private bool m_IsPauseMenuOpen = false;

        public bool IsPauseMenuOpen {
            get => m_IsPauseMenuOpen;
            set {
                if (m_IsPauseMenuOpen == value) return;
                m_IsPauseMenuOpen = value;
                Utils.SetCursorVisible(value || RaidHUD.PointerRequested);
                Notify(PauseMenuStylePropertyName);
                Notify(MobileControlsOpacityPropertyName);
            }
        }

        public static readonly string PauseMenuStylePropertyName = nameof(PauseMenuStyle);
        [CreateProperty] public DisplayStyle PauseMenuStyle => IsPauseMenuOpen ? DisplayStyle.Flex : DisplayStyle.None;
        public static readonly string MobileControlsOpacityPropertyName = nameof(MobileControlsPauseMenuOpacity);
        [CreateProperty] public StyleFloat MobileControlsPauseMenuOpacity => IsPauseMenuOpen ? 0.5f : 1f;
        private string m_PlayerName;

        [CreateProperty]
        public string PlayerName {
            get => m_PlayerName;
            set {
                if (m_PlayerName == value) return;
                m_PlayerName = value;
                PlayerPrefs.SetString(k_PlayerNameKey, value);
            }
        }

        private int m_PlayerCharacter;
        public event Action<int> PlayerCharacterChanged;

        [CreateProperty]
        public int PlayerCharacter {
            get => m_PlayerCharacter;
            set {
                int characterIndex = NormalizeCharacterIndex(value);
                if (m_PlayerCharacter == characterIndex) return;
                m_PlayerCharacter = characterIndex;
                PlayerPrefs.SetInt(k_PlayerCharacterKey, characterIndex);
                PlayerPrefs.Save();
                Notify();
                PlayerCharacterChanged?.Invoke(characterIndex);
            }
        }

        private int m_ConnectionMode = 0;

        [CreateProperty]
        public int ConnectionMode {
            get => m_ConnectionMode;
            set {
                if (m_ConnectionMode == value) return;
                m_ConnectionMode = value;
                PlayerPrefs.SetInt(k_ConnectionModeKey, value);
            }
        }

        private string m_SessionName = "default-session";

        [CreateProperty]
        public string SessionName {
            get => m_SessionName;
            set {
                if (m_SessionName == value) return;
                m_SessionName = value;
                PlayerPrefs.SetString(k_SessionNameKey, value);
            }
        }

        public static readonly string MainMenuSceneLoadedPropertyName = nameof(MainMenuSceneLoadedStyle);

        [CreateProperty]
        private DisplayStyle MainMenuSceneLoadedStyle => m_MainMenuSceneLoaded ? DisplayStyle.None : DisplayStyle.Flex;

        private bool m_MainMenuSceneLoaded;

        public bool MainMenuSceneLoaded {
            get => m_MainMenuSceneLoaded;
            set {
                if (m_MainMenuSceneLoaded == value) return;
                m_MainMenuSceneLoaded = value;
                Notify(MainMenuSceneLoadedPropertyName);
            }
        }
    }
}