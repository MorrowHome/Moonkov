using System.Collections.Generic;
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
            s_BoundRoots.Clear();
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

        // Bind before activation, including buttons created dynamically between document scans.
        // Clickable captures pointer-up, and navigation can detach the button before ClickEvent
        // reaches an ancestor. The sound therefore belongs to Button.clicked, not a root event.
        private static readonly HashSet<VisualElement> s_BoundRoots = new HashSet<VisualElement>();

        public static void BindUI(VisualElement root)
        {
            if (root == null || !s_BoundRoots.Add(root)) return;
            root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(OnSubmit, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerOverEvent>(OnHover, TrickleDown.TrickleDown);
        }

        public static void UnbindUI(VisualElement root)
        {
            if (root == null || !s_BoundRoots.Remove(root)) return;
            root.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationSubmitEvent>(OnSubmit, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerOverEvent>(OnHover, TrickleDown.TrickleDown);
        }

        /// <summary>Forget roots whose panel is gone so the bound set does not grow forever.</summary>
        public static void PruneBoundRoots()
        {
            foreach (var root in new List<VisualElement>(s_BoundRoots))
                if (root.panel == null) UnbindUI(root);
        }

        private static Button ButtonFor(EventBase evt)
        {
            var target = evt.target as VisualElement;
            return target as Button ?? target?.GetFirstAncestorOfType<Button>();
        }
        private static void BindButton(Button button)
        {
            if (button == null || !button.enabledInHierarchy) return;
            // A subtree and its UIDocument may both observe the press. Rebinding the same
            // delegate is idempotent, without retaining buttons after their page is discarded.
            button.clicked -= Click;
            button.clicked += Click;
        }
        private static void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button == 0) BindButton(ButtonFor(evt));
        }
        private static void OnSubmit(NavigationSubmitEvent evt)
        {
            BindButton(ButtonFor(evt));
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
