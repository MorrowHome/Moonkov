using Unity.MP_FPS.DollSinger;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    [RequireComponent(typeof(UIDocument))]
    public class LoadingScreen : MonoBehaviour
    {
        private HalosTerminalBackdrop m_Backdrop;
        private VisualElement m_Root;
        private Label m_Hint;
        private float m_HintScale = -1;
        static class UIElementNames
        {
            public const string LoadingStatus = "LoadingStatus";
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            m_Root = root; m_Hint = root.Q<Label>("LoadingHint"); m_HintScale = -1;
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
        private void Update()
        {
            if (m_Hint == null || !(m_Root.resolvedStyle.width > 0)) return;
            float scale = Mathf.Max(1, m_Root.resolvedStyle.width / Mathf.Max(1, Screen.width));
            if (Mathf.Approximately(scale, m_HintScale)) return;
            m_HintScale = scale;
            m_Hint.style.fontSize = 14 * scale;
        }
        private void PresentAnimation()
        {
            bool loading = GameSettings.Instance.GameState == GlobalGameState.Loading;
            if (loading)
                MoonkovLocalization.Set(m_Hint, "Hold {0} to defend. Press {1} to deploy halo. Hold {2} to scan.",
                    RaidKeyBindings.DisplayName(RaidKey.Defense), RaidKeyBindings.DisplayName(RaidKey.DeployHalo),
                    RaidKeyBindings.DisplayName(RaidKey.Scan));
            m_Backdrop.Present(loading, HalosTerminalBackdrop.PrefersReducedMotion);
        }

        private void OnDisable()
        {
            GameSettings.Instance.propertyChanged -= LoadingSettingsChanged;
            m_Backdrop?.Dispose(); m_Backdrop = null;
            m_Root = null; m_Hint = null;
        }
    }
}
