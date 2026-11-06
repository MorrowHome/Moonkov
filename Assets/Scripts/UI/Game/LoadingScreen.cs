using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    [RequireComponent(typeof(UIDocument))]
    public class LoadingScreen : MonoBehaviour
    {
        private HalosTerminalBackdrop m_Backdrop;
        static class UIElementNames
        {
            public const string LoadingStatus = "LoadingStatus";
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            MoonkovLocalization.Bind(root);
            m_Backdrop = new HalosTerminalBackdrop(root.Q("Canvas"), true);
            GameSettings.Instance.propertyChanged += LoadingSettingsChanged;
            PresentAnimation();

            root.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.LoadingScreenStylePropertyName),
                bindingMode = BindingMode.ToTarget,
            });

            root.Q<ProgressBar>().SetBinding("value", new DataBinding
            {
                dataSource = LoadingData.Instance,
                dataSourcePath = new PropertyPath(LoadingData.LoadingProgressPropertyName),
                bindingMode = BindingMode.ToTarget,
            });

            var label = root.Q<Label>(UIElementNames.LoadingStatus);
            label.SetBinding("text", new DataBinding
            {
                dataSource = LoadingData.Instance,
                dataSourcePath = new PropertyPath(LoadingData.LoadingStatusTextPropertyName),
                bindingMode = BindingMode.ToTarget,
            });
        }

        private void LoadingSettingsChanged(object sender, BindablePropertyChangedEventArgs evt) => PresentAnimation();
        private void PresentAnimation() => m_Backdrop.Present(
            GameSettings.Instance.GameState == GlobalGameState.Loading, HalosTerminalBackdrop.PrefersReducedMotion);

        private void OnDisable()
        {
            GameSettings.Instance.propertyChanged -= LoadingSettingsChanged;
            m_Backdrop?.Dispose(); m_Backdrop = null;
        }
    }
}
