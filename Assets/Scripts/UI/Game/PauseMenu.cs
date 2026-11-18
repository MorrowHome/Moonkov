// 局内 Esc 菜单。暂停状态由 GameSettings 控制，设置页沿用船舱终端的 BindSettings。继续、主菜单和退出按钮各自发出对应操作。
using Unity.MP_FPS.Client;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    [RequireComponent(typeof(UIDocument))]
    public class PauseMenu : MonoBehaviour {
        private static class UIElementNames {
            public const string ResumeButton = "ResumeButton";
            public const string SettingsButton = "SettingsButton";
            public const string MainMenuButton = "MainMenuButton";
            public const string QuitButton = "QuitButton";
        }

        private Button m_ResumeButton;
        private Button m_SettingsButton, m_SettingsBackButton;
        private Button m_MainMenuButton;
        private Button m_QuitButton;
        private VisualElement m_Root, m_Menu, m_Settings, m_SettingsContent;
        private IVisualElementScheduledItem m_Sizing;
        private bool m_SettingsVisible;
        private float m_Scale = -1;

        private void OnEnable() {
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
            // 继续与返回等入口只在这个组件活着时订阅。暂停状态留在 GameSettings，其他输入系统也会看它。
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
            m_Sizing = root.schedule.Execute(() => {
                if (GameSettings.Instance.IsPauseMenuOpen) FitLayout();
            }).Every(250);
        }

        private void OnDisable() {
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

        private void TogglePauseMenuVisibility(InputAction.CallbackContext obj) {
            if (!Application.isFocused || GameSettings.Instance.GameState != GlobalGameState.InGame) return;
            if (GameSettings.Instance.IsPauseMenuOpen) {
                // UI Toolkit receives this key after the input action; let its popup consume it first.
                // 语言的下拉层当前可见时，留给 UI Toolkit 处理当次 Esc。下一次 Esc 才退出设置页。
                if (m_SettingsVisible &&
                    m_Root.panel?.visualTree.Q(className: GenericDropdownMenu.ussClassName) != null)
                    return;
                if (m_SettingsVisible)
                    CloseSettings();
                else
                    OnResumePressed();
                return;
            }
            // 先让局内界面关掉最上层的弹窗、背包或地图。本次 Esc 已有界面处理时就到此为止。
            if (RaidHUD.CloseInventory()) return;
            GameSettings.Instance.IsPauseMenuOpen = true;
            FitLayout();
            m_ResumeButton.Focus();
        }

        private void OnSettingsPressed() {
            if (m_SettingsVisible) return;
            // 设置页打开时克隆同一份设置模板。控件经共享绑定即时应用设置，所以退出面板不需要再确认。
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

        private void OnSettingsBackPressed() { CloseSettings(); }

        private void CloseSettings(bool focus = true) {
            m_SettingsVisible = false;
            m_SettingsContent.Clear();
            m_Settings.style.display = DisplayStyle.None;
            m_Menu.style.display = DisplayStyle.Flex;
            if (focus) m_SettingsButton.Focus();
        }

        private void PauseStateChanged(object sender, BindablePropertyChangedEventArgs evt) {
            if (evt.propertyName != GameSettings.PauseMenuStylePropertyName) return;
            m_Root.style.display = GameSettings.Instance.PauseMenuStyle;
            // 暂停状态也可被外部流程关闭。同步清理设置子页，下一次打开时能从主暂停菜单开始。
            if (!GameSettings.Instance.IsPauseMenuOpen && m_SettingsVisible) CloseSettings(false);
        }

        private void FitLayout(bool force = false) {
            if (m_Root.worldBound.width <= 0 || Screen.width <= 0) return;
            float scale = Mathf.Max(1, m_Root.worldBound.width / Screen.width);
            // 面板和屏幕比例没变时沿用原布局，减少定时检查修改控件尺寸的次数。
            if (!force && Mathf.Approximately(scale, m_Scale)) return;
            m_Scale = scale;
            m_Menu.style.width = 360 * scale;
            m_Menu.Query<Button>().ForEach(button => {
                button.style.height = 60 * scale;
                button.style.marginBottom = 10 * scale;
            });
            m_Menu.Query<TextElement>().ForEach(text =>
                text.style.fontSize = (text.ClassListContains("pause-caption") ? 12 : 16) * scale);
            m_Settings.style.width = 680 * scale;
            m_Settings.style.paddingTop = m_Settings.style.paddingBottom =
                m_Settings.style.paddingLeft = m_Settings.style.paddingRight = 16 * scale;
            m_Settings.Query<TextElement>().ForEach(text =>
                text.style.fontSize =
                    (text.ClassListContains("terminal-heading") || text.ClassListContains("pause-settings-title")
                        ? 16
                        : 14) * scale);
            m_Settings.Query(className: "archive-settings-row").ForEach(row => row.style.minHeight = 44 * scale);
            m_Settings.Query(className: "unity-base-field__label").ForEach(label => label.style.minWidth = 164 * scale);
            m_Settings.Query<Button>().ForEach(button => button.style.minHeight = 36 * scale);
        }

        private static void OnResumePressed() { GameSettings.Instance.IsPauseMenuOpen = false; }

        private static void OnMainMenuPressed() {
            GameManager.Instance.ReturnToMainMenuAsync();
            Utils.SetCursorVisible(true);
        }

        private static void OnQuitPressed() { GameManager.Instance.QuitAsync(); }
    }
}