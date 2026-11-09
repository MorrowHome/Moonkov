using System;
using System.Collections.Generic;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class MoonkovTerminal
    {
        // The lobby and pause menu edit the same live settings and persistence path.
        internal static void BindSettings(VisualElement content, Action languageChanged = null)
        {
            BindViewSettings(content);
            new RaidKeyBindingView(content);
            BindAudioSlider(content, "audioMaster", "Master", MoonkovAudio.MasterVolume);
            BindAudioSlider(content, "audioEffects", "Effects", MoonkovAudio.EffectsVolume);
            BindAudioSlider(content, "audioMusic", "Music", MoonkovAudio.MusicVolume);
            BindAudioSlider(content, "audioInterface", "Interface", MoonkovAudio.InterfaceVolume);
            var hud = content.Q<Toggle>("alwaysShowHUD");
            hud.SetValueWithoutNotify(PlayerPrefs.GetInt("Moonkov.AlwaysShowHUD", 0) != 0);
            hud.RegisterValueChangedCallback(e => PlayerPrefs.SetInt("Moonkov.AlwaysShowHUD", e.newValue ? 1 : 0));
            var vitalMotion = content.Q<Toggle>("animateVitalSignal");
            vitalMotion.SetValueWithoutNotify(PlayerPrefs.GetInt(RaidVitalsMonitor.AnimatePreference, 1) != 0);
            vitalMotion.RegisterValueChangedCallback(e => PlayerPrefs.SetInt(RaidVitalsMonitor.AnimatePreference, e.newValue ? 1 : 0));
            var language = content.Q<DropdownField>("language");
            language.choices = new List<string> { "简体中文", "English" };
            language.SetValueWithoutNotify(MoonkovLocalization.CurrentLocaleCode == "zh-Hans" ? "简体中文" : "English");
            language.RegisterValueChangedCallback(e =>
            {
                MoonkovLocalization.Select(e.newValue == "简体中文" ? "zh-Hans" : "en");
                languageChanged?.Invoke();
            });
        }

        private static void BindAudioSlider(VisualElement content, string name, string channel, float value)
        {
            var slider = content.Q<Slider>(name);
            slider.highValue = MoonkovAudio.MaximumVolume(channel);
            slider.SetValueWithoutNotify(value);
            var readout = content.Q<Label>(name + "Value");
            var tracker = slider.Q(className: "unity-base-slider__tracker");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore }; fill.AddToClassList("archive-slider-fill"); tracker?.Add(fill);
            void Present(float v)
            {
                MoonkovLocalization.Set(readout, "{0:0}%", v * 100);
                fill.style.width = Length.Percent(Mathf.InverseLerp(slider.lowValue, slider.highValue, v) * 100);
            }
            var save = slider.schedule.Execute(PlayerPrefs.Save);
            save.Pause();
            Present(value);
            slider.RegisterValueChangedCallback(e =>
            {
                float volume = Mathf.Round(e.newValue * 100) / 100;
                slider.SetValueWithoutNotify(volume);
                MoonkovAudio.SetVolume(channel, volume);
                Present(volume);
                save.ExecuteLater(250);
            });
            slider.RegisterCallback<DetachFromPanelEvent>(_ => PlayerPrefs.Save());
        }

        private static void BindViewSettings(VisualElement content)
        {
            content.Q<VisualElement>(className: "archive-settings-page").styleSheets.Add(
                Resources.Load<StyleSheet>("Moonkov/UI/ViewSettings"));
            var mouse = content.Q<Slider>("lookMouse");
            var aim = content.Q<Slider>("lookAim");
            var fov = content.Q<Slider>("lookFov");
            var reset = content.Q<Button>("lookReset");
            void Present()
            {
                mouse.SetValueWithoutNotify(DollSingerViewSettings.MouseSensitivity);
                aim.SetValueWithoutNotify(DollSingerViewSettings.AimSensitivity);
                fov.SetValueWithoutNotify(DollSingerViewSettings.WorldFieldOfView);
                content.Q<Label>("lookMouseValue").text = mouse.value.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture);
                content.Q<Label>("lookAimValue").text = aim.value.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + "×";
                content.Q<Label>("lookFovValue").text = fov.value.ToString("0", System.Globalization.CultureInfo.CurrentCulture) + "°";
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
