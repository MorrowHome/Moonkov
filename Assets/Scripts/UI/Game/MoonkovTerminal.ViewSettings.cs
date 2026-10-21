using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class MoonkovTerminal
    {
        private void BindViewSettings()
        {
            m_Content.Q<VisualElement>(className: "archive-settings-page").styleSheets.Add(
                Resources.Load<StyleSheet>("Moonkov/UI/ViewSettings"));
            var mouse = m_Content.Q<Slider>("lookMouse");
            var aim = m_Content.Q<Slider>("lookAim");
            var fov = m_Content.Q<Slider>("lookFov");
            var reset = m_Content.Q<Button>("lookReset");
            void Present()
            {
                mouse.SetValueWithoutNotify(DollSingerViewSettings.MouseSensitivity);
                aim.SetValueWithoutNotify(DollSingerViewSettings.AimSensitivity);
                fov.SetValueWithoutNotify(DollSingerViewSettings.WorldFieldOfView);
                m_Content.Q<Label>("lookMouseValue").text = mouse.value.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture);
                m_Content.Q<Label>("lookAimValue").text = aim.value.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + "×";
                m_Content.Q<Label>("lookFovValue").text = fov.value.ToString("0", System.Globalization.CultureInfo.CurrentCulture) + "°";
            }
            var save = reset.schedule.Execute(DollSingerViewSettings.Save);
            save.Pause();
            void Changed(ChangeEvent<float> evt)
            {
                DollSingerViewSettings.Set(mouse.value, aim.value, Mathf.Round(fov.value));
                Present();
                save.ExecuteLater(250);
            }
            mouse.RegisterValueChangedCallback(Changed);
            aim.RegisterValueChangedCallback(Changed);
            fov.RegisterValueChangedCallback(Changed);
            reset.clicked += () => { DollSingerViewSettings.RestoreDefaults(); Present(); };
            // Leaving the page must persist a keyboard/drag edit even if it precedes
            // the debounce. Rebuilding controls never resets the player's choices.
            reset.RegisterCallback<DetachFromPanelEvent>(_ => DollSingerViewSettings.Save());
            Present();
        }
    }
}
