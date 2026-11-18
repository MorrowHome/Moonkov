// 本机开服前填写端口的弹窗。端口是否合法由 ConnectionSettings 判断。确定后唤醒等待中的开服流程，取消则恢复原配置。
using Unity.Properties;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    [RequireComponent(typeof(UIDocument))]
    public class StartHostPopUp : MonoBehaviour {
        private static class UIElementNames {
            public const string PortInputField = "PortField";
            public const string StartButton = "StartButton";
            public const string CancelButton = "CancelButton";
        }

        private VisualElement m_StartHostPopUp;
        private Button m_StartButton;
        private Button m_CancelButton;

        private void OnEnable() {
            m_StartHostPopUp = GetComponent<UIDocument>().rootVisualElement;
            m_StartHostPopUp.SetBinding("style.display", new DataBinding {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.StartHostStylePropertyName),
                bindingMode = BindingMode.ToTarget
            });
            var portInputField = m_StartHostPopUp.Q<TextField>(UIElementNames.PortInputField);
            portInputField.SetBinding("value", new DataBinding {
                dataSource = ConnectionSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(ConnectionSettings.Port)),
                bindingMode = BindingMode.TwoWay
            });
            m_StartButton = m_StartHostPopUp.Q<Button>(UIElementNames.StartButton);
            m_StartButton.clicked += OnStartPressed;
            m_StartButton.SetBinding("enabledSelf", new DataBinding {
                dataSource = ConnectionSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(ConnectionSettings.IsNetworkEndpointValid)),
                bindingMode = BindingMode.ToTarget
            });
            m_CancelButton = m_StartHostPopUp.Q<Button>(UIElementNames.CancelButton);
            m_CancelButton.clicked += OnCancelPressed;
            GameInput.Actions.UI.TogglePauseMenu.performed += CancelFromKeyboard;
        }

        private void OnDisable() {
            m_StartButton.clicked -= OnStartPressed;
            m_CancelButton.clicked -= OnCancelPressed;
            GameInput.Actions.UI.TogglePauseMenu.performed -= CancelFromKeyboard;
        }

        private static void OnStartPressed() { GameSettings.Instance.CancellableUserInputPopUp.SetResult(); }

        private static void CancelFromKeyboard(InputAction.CallbackContext context) {
            if (Application.isFocused && GameSettings.Instance.GameState == GlobalGameState.MainMenu &&
                GameSettings.Instance.MainMenuState == MainMenuState.StartHostPopup)
                OnCancelPressed();
        }

        private static void OnCancelPressed() {
            GameSettings.Instance.CancellableUserInputPopUp.SetCanceled();
            ConnectionSettings.Instance.IPAddress = ConnectionSettings.ConfiguredAddress;
            ConnectionSettings.Instance.Port = ConnectionSettings.ConfiguredPort;
        }
    }
}