// 连接过程的等待界面。从 ConnectionSettings 绑定显隐。点取消后交给 GameManager 退回主菜单，由它收掉正在进行的连接流程。
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    /// <summary>In-game connection state screen.</summary>
    [RequireComponent(typeof(UIDocument))]
    public class ConnectionStatusScreen : MonoBehaviour {
        private static class UIElementNames {
            public const string CancelButton = "CancelButton";
        }

        private Button m_CancelButton;

        private void OnEnable() {
            var root = GetComponent<UIDocument>().rootVisualElement;
            // 界面只跟连接状态绑定，所以 GameManager 断开连接时可以统一收起它和其他页面。
            root.SetBinding("style.display", new DataBinding {
                dataSource = ConnectionSettings.Instance,
                dataSourcePath = new PropertyPath(ConnectionSettings.ConnectionStatusStylePropertyName),
                bindingMode = BindingMode.ToTarget
            });
            m_CancelButton = root.Q<Button>(UIElementNames.CancelButton);
            // 取消走通用回菜单流程。连接资源和 World 在那里一起处理。
            m_CancelButton.clicked += OnCancelPressed;
        }

        private void OnDisable() { m_CancelButton.clicked -= OnCancelPressed; }
        private static void OnCancelPressed() { GameManager.Instance.ReturnToMainMenuAsync(); }
    }
}