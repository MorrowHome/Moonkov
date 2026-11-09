using Unity.MP_FPS.Client;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    [RequireComponent(typeof(UIDocument))]
    public class PauseMenu : MonoBehaviour
    {
        static class UIElementNames
        {
            public const string ResumeButton = "ResumeButton";
            public const string SettingsButton = "SettingsButton";
            public const string MainMenuButton = "MainMenuButton";
            public const string QuitButton = "QuitButton";
        }

        Button m_ResumeButton;
        Button m_SettingsButton, m_SettingsBackButton;
        Button m_MainMenuButton;
        Button m_QuitButton;
        VisualElement m_Root, m_Menu, m_Settings, m_SettingsContent;
        IVisualElementScheduledItem m_Sizing;
        bool m_SettingsVisible;
        float m_Scale = -1;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            m_Root = root;
            m_Menu = root.Q("Container");
            m_Settings = root.Q("PauseSettings");
            m_SettingsContent = root.Q("PauseSettingsContent");
            root.Q("ResumeCard").Q<Button>().name = UIElementNames.ResumeButton;
            root.Q("SettingsCard").Q<Button>().name = UIElementNames.SettingsButton;
            root.Q("MainMenuCard").Q<Button>().name = UIElementNames.MainMenuButton;
            root.Q("QuitCard").Q<Button>().name = UIElementNames.QuitButton;
            MoonkovLocalization.Bind(root);
            GameInput.Actions.UI.TogglePauseMenu.performed += TogglePauseMenuVisibility;
            GameSettings.Instance.propertyChanged += PauseStateChanged;
            root.style.display = GameSettings.Instance.PauseMenuStyle;
          
            m_ResumeButton = root.Q<Button>(UIElementNames.ResumeButton);
            m_ResumeButton.clicked += OnResumePressed;

            m_SettingsButton = root.Q<Button>(UIElementNames.SettingsButton);
            m_SettingsButton.clicked += OnSettingsPressed;
            m_SettingsBackButton = root.Q<Button>("SettingsBackButton");
            m_SettingsBackButton.clicked += OnSettingsBackPressed;

            m_MainMenuButton = root.Q<Button>(UIElementNames.MainMenuButton);
            m_MainMenuButton.clicked += OnMainMenuPressed;
            m_MainMenuButton.SetEnabled(GameManager.CanUseMainMenu);

            m_QuitButton = root.Q<Button>(UIElementNames.QuitButton);
            m_QuitButton.clicked += OnQuitPressed;
            CloseSettings(false);
            m_Scale = -1;
            m_Sizing = root.schedule.Execute(() => { if (GameSettings.Instance.IsPauseMenuOpen) FitLayout(); }).Every(250);
        }

        void OnDisable()
        {
            GameInput.Actions.UI.TogglePauseMenu.performed -= TogglePauseMenuVisibility;
            GameSettings.Instance.propertyChanged -= PauseStateChanged;
            m_Sizing?.Pause();
            m_SettingsContent.Clear();
            m_ResumeButton.clicked -= OnResumePressed;
            m_SettingsButton.clicked -= OnSettingsPressed;
            m_SettingsBackButton.clicked -= OnSettingsBackPressed;
            m_MainMenuButton.clicked -= OnMainMenuPressed;
            m_QuitButton.clicked -= OnQuitPressed;
        }

        void TogglePauseMenuVisibility(InputAction.CallbackContext obj)
        {
            if (!Application.isFocused || GameSettings.Instance.GameState != GlobalGameState.InGame) return;
            if (GameSettings.Instance.IsPauseMenuOpen)
            {
                // UI Toolkit receives this key after the input action; let its popup consume it first.
                if (m_SettingsVisible && m_Root.panel?.visualTree.Q(className: GenericDropdownMenu.ussClassName) != null) return;
                if (m_SettingsVisible) CloseSettings(); else OnResumePressed();
                return;
            }
            if (RaidHUD.CloseInventory()) return;
            GameSettings.Instance.IsPauseMenuOpen = true;
            FitLayout();
            m_ResumeButton.Focus();
        }

        void OnSettingsPressed()
        {
            if (m_SettingsVisible) return;
            TerminalLayout.Populate(m_SettingsContent, "Settings");
            MoonkovTerminal.BindSettings(m_SettingsContent);
            // The pause page shows live options; stash shortcuts and the retired HUD toggle do not belong here.
            m_SettingsContent.Q("alwaysShowHUD").parent.style.display = DisplayStyle.None;
            m_SettingsVisible = true;
            m_Menu.style.display = DisplayStyle.None;
            m_Settings.style.display = DisplayStyle.Flex;
            FitLayout(true);
            m_SettingsContent.Q<DropdownField>("language").Focus();
        }

        void OnSettingsBackPressed() => CloseSettings();

        void CloseSettings(bool focus = true)
        {
            m_SettingsVisible = false;
            m_SettingsContent.Clear();
            m_Settings.style.display = DisplayStyle.None;
            m_Menu.style.display = DisplayStyle.Flex;
            if (focus) m_SettingsButton.Focus();
        }

        void PauseStateChanged(object sender, BindablePropertyChangedEventArgs evt)
        {
            if (evt.propertyName != GameSettings.PauseMenuStylePropertyName) return;

            m_Root.style.display = GameSettings.Instance.PauseMenuStyle;
            if (!GameSettings.Instance.IsPauseMenuOpen && m_SettingsVisible) CloseSettings(false);
        }

        void FitLayout(bool force = false)
        {
            if (m_Root.worldBound.width <= 0 || Screen.width <= 0) return;
            float scale = Mathf.Max(1, m_Root.worldBound.width / Screen.width);
            if (!force && Mathf.Approximately(scale, m_Scale)) return;
            m_Scale = scale;
            m_Menu.style.width = 360 * scale;
            m_Menu.Query<Button>().ForEach(button => { button.style.height = 60 * scale; button.style.marginBottom = 10 * scale; });
            m_Menu.Query<TextElement>().ForEach(text => text.style.fontSize = (text.ClassListContains("pause-caption") ? 12 : 16) * scale);
            m_Settings.style.width = 680 * scale;
            m_Settings.style.paddingTop = m_Settings.style.paddingBottom = m_Settings.style.paddingLeft = m_Settings.style.paddingRight = 16 * scale;
            m_Settings.Query<TextElement>().ForEach(text => text.style.fontSize = (text.ClassListContains("terminal-heading") || text.ClassListContains("pause-settings-title") ? 16 : 14) * scale);
            m_Settings.Query(className: "archive-settings-row").ForEach(row => row.style.minHeight = 44 * scale);
            m_Settings.Query(className: "unity-base-field__label").ForEach(label => label.style.minWidth = 164 * scale);
            m_Settings.Query<Button>().ForEach(button => button.style.minHeight = 36 * scale);
        }

        static void OnResumePressed() => GameSettings.Instance.IsPauseMenuOpen = false;

        static void OnMainMenuPressed()
        {
            GameManager.Instance.ReturnToMainMenuAsync();
            Utils.SetCursorVisible(true);            
        }

        static void OnQuitPressed() => GameManager.Instance.QuitAsync();
    }
}
