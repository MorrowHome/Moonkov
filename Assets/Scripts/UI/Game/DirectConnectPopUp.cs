// 按 IP 和端口直连的弹窗。输入写入 ConnectionSettings，确认或取消用于结束 GameManager 正等待的弹窗任务。
using Unity.Properties;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    namespace Unity.StarterKit.FPS {
        [RequireComponent(typeof(UIDocument))]
        public class DirectConnectPopUp : MonoBehaviour {
            private static class UIElementNames {
                public const string IPAddressInputField = "IPAddressField";
                public const string PortInputField = "PortField";
                public const string ConfirmButton = "ConfirmButton";
                public const string CancelButton = "CancelButton";
            }

            private VisualElement m_DirectConnectPopUp;
            private Button m_ConfirmButton;
            private Button m_CancelButton;

            private void OnEnable() {
                m_DirectConnectPopUp = GetComponent<UIDocument>().rootVisualElement;
                m_DirectConnectPopUp.SetBinding("style.display", new DataBinding {
                    dataSource = GameSettings.Instance,
                    dataSourcePath = new PropertyPath(GameSettings.DirectConnectStylePropertyName),
                    bindingMode = BindingMode.ToTarget
                });
                // 地址输入和端口都直接绑定连接设置。等确认通过后，创建连接的代码会从同一处读取两个值。
                var ipAddressInputField = m_DirectConnectPopUp.Q<TextField>(UIElementNames.IPAddressInputField);
                ipAddressInputField.SetBinding("value", new DataBinding {
                    dataSource = ConnectionSettings.Instance,
                    dataSourcePath = new PropertyPath(nameof(ConnectionSettings.IPAddress)),
                    bindingMode = BindingMode.TwoWay
                });
                var portInputField = m_DirectConnectPopUp.Q<TextField>(UIElementNames.PortInputField);
                portInputField.SetBinding("value", new DataBinding {
                    dataSource = ConnectionSettings.Instance,
                    dataSourcePath = new PropertyPath(nameof(ConnectionSettings.Port)),
                    bindingMode = BindingMode.TwoWay
                });
                m_ConfirmButton = m_DirectConnectPopUp.Q<Button>(UIElementNames.ConfirmButton);
                m_ConfirmButton.clicked += OnConfirmPressed;
                // 地址和端口组成的网络端点要合法才允许提交。这里负责格式门槛，连接是否成功要等网络结果。
                m_ConfirmButton.SetBinding("enabledSelf", new DataBinding {
                    dataSource = ConnectionSettings.Instance,
                    dataSourcePath = new PropertyPath(nameof(ConnectionSettings.IsNetworkEndpointValid)),
                    bindingMode = BindingMode.ToTarget
                });
                m_CancelButton = m_DirectConnectPopUp.Q<Button>(UIElementNames.CancelButton);
                m_CancelButton.clicked += OnCancelPressed;
                GameInput.Actions.UI.TogglePauseMenu.performed += CancelFromKeyboard;
            }

            private void OnDisable() {
                m_ConfirmButton.clicked -= OnConfirmPressed;
                m_CancelButton.clicked -= OnCancelPressed;
                GameInput.Actions.UI.TogglePauseMenu.performed -= CancelFromKeyboard;
            }

            private static void OnConfirmPressed() { GameSettings.Instance.CancellableUserInputPopUp.SetResult(); }

            private static void CancelFromKeyboard(InputAction.CallbackContext context) {
                // 只有窗口有焦点且当前确实是直连弹窗，Esc 才按取消处理。其他菜单自己负责它的返回键。
                if (Application.isFocused && GameSettings.Instance.GameState == GlobalGameState.MainMenu &&
                    GameSettings.Instance.MainMenuState == MainMenuState.DirectConnectPopUp)
                    OnCancelPressed();
            }

            private static void OnCancelPressed() {
                // 取消唤醒等待中的连接流程，并把本次没确认的输入还原为已配置的地址和端口。
                GameSettings.Instance.CancellableUserInputPopUp.SetCanceled();
                ConnectionSettings.Instance.IPAddress = ConnectionSettings.ConfiguredAddress;
                ConnectionSettings.Instance.Port = ConnectionSettings.ConfiguredPort;
            }
        }
    }
}