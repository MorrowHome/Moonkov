using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    // Presentation facade over the template's pooled SoundSystem; never creates audio sources.
    public static class MoonkovAudio
    {
        private static MoonkovAudioLibrary s_Library;
        private static bool s_Loaded;
        private static float s_LastHover;
        public static MoonkovAudioLibrary Library
        {
            get
            {
                if (!s_Loaded)
                {
                    s_Loaded = true;
                    s_Library = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
                }
                return s_Library;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_Library = null;
            s_Loaded = false;
            s_LastHover = -1f;
        }

        public static SoundSystem.SoundInfo Play(SoundDef sound, Vector3 position, float volume = 1f)
        {
            var game = GameManager.Instance;
            if (!Application.isPlaying || game == null || game.IsHeadless || sound == null ||
                sound.Clips == null || sound.Clips.Count == 0) return null;
            return game.SoundSystem?.CreateEmitter(sound, position, volume);
        }

        public static void Click() => Play(Library?.Click, Vector3.zero);
        public static void Confirm() => Play(Library?.Confirm, Vector3.zero);
        public static void Error() => Play(Library?.Error, Vector3.zero);

        // Delegated callbacks cover dynamically built buttons and keyboard activation as well.
        public static void BindUI(VisualElement root)
        {
            root.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(OnSubmit, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerOverEvent>(OnHover);
        }

        public static void UnbindUI(VisualElement root)
        {
            root.UnregisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationSubmitEvent>(OnSubmit, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerOverEvent>(OnHover);
        }

        private static Button ButtonFor(EventBase evt)
        {
            var target = evt.target as VisualElement;
            return target as Button ?? target?.GetFirstAncestorOfType<Button>();
        }
        private static void OnClick(ClickEvent evt)
        {
            if (evt.button == 0 && ButtonFor(evt)?.enabledInHierarchy == true) Click();
        }
        private static void OnSubmit(NavigationSubmitEvent evt)
        {
            if (ButtonFor(evt)?.enabledInHierarchy == true) Click();
        }
        private static void OnHover(PointerOverEvent evt)
        {
            var button = ButtonFor(evt);
            if (button == null || !button.enabledInHierarchy ||
                Time.unscaledTime - s_LastHover < .1f) return;
            s_LastHover = Time.unscaledTime;
            Play(Library?.Hover, Vector3.zero);
        }

        public static float MasterVolume => PlayerPrefs.GetFloat("Moonkov.Audio.Master", .8f);
        public static float EffectsVolume => PlayerPrefs.GetFloat("Moonkov.Audio.Effects", .85f);
        public static float InterfaceVolume => PlayerPrefs.GetFloat("Moonkov.Audio.Interface", .65f);
        public static void ApplyVolumes()
        {
            AudioListener.volume = MasterVolume;
            if (SoundMixer.soundSFXVol != null) SoundMixer.soundSFXVol.Value = EffectsVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (SoundMixer.soundMenuVol != null) SoundMixer.soundMenuVol.Value = InterfaceVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        public static void SetVolume(string channel, float value)
        {
            PlayerPrefs.SetFloat("Moonkov.Audio." + channel, Mathf.Clamp01(value));
            ApplyVolumes();
        }
    }
}
