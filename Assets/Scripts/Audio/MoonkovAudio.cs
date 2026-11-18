// 玩法和界面通常从这里播音效。按需读取 AudioLibrary，再把声音交给 GameManager 的对象池。需要空间感、跟随或裁剪时用 PlayWeaponLayer。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    // 对外提供共用播放入口，底层沿用模板中带对象池的 SoundSystem。
    public static class MoonkovAudio {
        private static MoonkovAudioLibrary s_Library;
        private static bool s_Loaded;
        private static float s_LastHover;

        public static MoonkovAudioLibrary Library {
            get {
                // 所有场景共享同一个音效资源表。第一次真正需要时加载并缓存，后续按钮和世界音效只做字段查找。
                if (!s_Loaded) {
                    s_Loaded = true;
                    s_Library = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
                }
                return s_Library;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() {
            s_Library = null;
            s_Loaded = false;
            s_LastHover = -1f;
            s_BoundRoots.Clear();
        }

        public static SoundSystem.SoundInfo Play(SoundDef sound, Vector3 position, float volume = 1f) {
            var game = GameManager.Instance;
            if (!Application.isPlaying || game == null || game.IsHeadless || sound == null || sound.Clips == null ||
                sound.Clips.Count == 0)
                return null;
            return game.SoundSystem?.CreateEmitter(sound, position, volume);
        }

        // 发声器、音源池和混音器都继续共用。一条声音可以单独设所有者和音高。
        public static SoundSystem.SoundInfo PlayWeaponLayer(
            SoundDef sound,
            Vector3 position,
            float volume,
            float spatialBlend,
            float pitchCents = 0,
            float? startOffsetFraction = null,
            float? stopAfterSeconds = null,
            Transform follow = null) {
            var game = GameManager.Instance;
            if (!Application.isPlaying || game == null || game.IsHeadless || sound == null || sound.Clips == null ||
                sound.Clips.Count == 0)
                return null;
            // 跟随物体时把世界位置换算到它的局部坐标。空间混合、音高和开始结束位置到这一声才覆盖，原 SoundDef 仍能给其他音效复用。
            var info = new SoundSystem.SoundInfo {
                SoundDefinition = sound, SoundTransform = follow,
                Position = follow ? follow.InverseTransformPoint(position) : position,
                SoundDefOverrideInfo = new SoundEmitter.SoundDefOverrideData(sound) {
                    SpatialBlend = Mathf.Clamp01(spatialBlend), BasePitchInCents = sound.BasePitchInCents + pitchCents,
                    StartOffsetFraction = startOffsetFraction, StopAfterSeconds = stopAfterSeconds
                }
            };
            return game.SoundSystem != null && game.SoundSystem.PlayEmitter(info, volume) ? info : null;
        }

        public const float WorldWeaponGain = .57f;

        // 本机枪声贴近听者播放，旁人的枪声使用世界位置和较低的基础音量。远近衰减仍交给声音池。
        public static SoundSystem.SoundInfo PlayWeaponReport(
            SoundDef sound,
            Vector3 position,
            bool owned,
            float gain = 1f) {
            return PlayWeaponLayer(sound, position, gain * (owned ? 1f : WorldWeaponGain), owned ? 0f : 1f);
        }

        public static bool CanHearDetail(Vector3 position, float range) {
            var game = GameManager.Instance;
            return Application.isPlaying && game && !game.IsHeadless && game.SoundSystem is SoundSystem system &&
                   system.IsWithinHearing(position, range);
        }

        public static void Click() { Play(Library?.Click, Vector3.zero); }
        public static void Confirm() { Play(Library?.Confirm, Vector3.zero); }
        public static void AirlockButtonPress() { Play(Library?.AirlockButtonPress, Vector3.zero); }
        public static void Error() { Play(Library?.Error, Vector3.zero); }

        // 控件在启用前就要绑定声音。文档扫描之间动态建出的按钮也要处理。
        // Clickable 会捕获松手事件。导航操作可能在 ClickEvent 传给父元素前就移除按钮，
        // 因此声音挂到 Button.clicked 上，按钮自己负责播放。
        private static readonly HashSet<VisualElement> s_BoundRoots = new();

        public static void BindUI(VisualElement root) {
            if (root == null || !s_BoundRoots.Add(root)) return;
            // 根节点在事件下传时提前找即将激活的按钮。键鼠与键盘确认都能绑定，后来动态加进页面的按钮也会经过根节点。
            root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(OnSubmit, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerOverEvent>(OnHover, TrickleDown.TrickleDown);
        }

        public static void UnbindUI(VisualElement root) {
            if (root == null || !s_BoundRoots.Remove(root)) return;
            root.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationSubmitEvent>(OnSubmit, TrickleDown.TrickleDown);
            root.UnregisterCallback<PointerOverEvent>(OnHover, TrickleDown.TrickleDown);
        }

        /// <summary>清理面板已离开的根节点，避免绑定记录持续增长。</summary>
        public static void PruneBoundRoots() {
            // 窗口切换后没有挂在 Panel 上的根节点要解除监听。遍历副本可以一边查看一边从原集合移除。
            foreach (var root in new List<VisualElement>(s_BoundRoots))
                if (root.panel == null)
                    UnbindUI(root);
        }

        private static Button ButtonFor(EventBase evt) {
            var target = evt.target as VisualElement;
            return target as Button ?? target?.GetFirstAncestorOfType<Button>();
        }

        // 先在按下或键盘确认时给按钮挂上声音，再等按钮实际激活。即使点击后页面马上被拆掉，这个按钮自己的回调也能响。
        private static void BindButton(Button button) {
            if (button == null || !button.enabledInHierarchy) return;
            // 子树和所在 UIDocument 都可能观察到按下动作。重复绑定同一个委托应只生效一次，
            // 页面移除后也无需继续持有原按钮。
            button.clicked -= Click;
            button.clicked += Click;
        }

        private static void OnPointerDown(PointerDownEvent evt) {
            if (evt.button == 0) BindButton(ButtonFor(evt));
        }

        private static void OnSubmit(NavigationSubmitEvent evt) { BindButton(ButtonFor(evt)); }

        private static void OnHover(PointerOverEvent evt) {
            var button = ButtonFor(evt);
            if (button == null || !button.enabledInHierarchy || Time.unscaledTime - s_LastHover < .1f) return;
            // 全界面的悬浮声共用节奏。鼠标快速扫过一排按钮时会限频，避免同一小段时间叠太多提示。
            s_LastHover = Time.unscaledTime;
            Play(Library?.Hover, Vector3.zero);
        }

        public static float MasterVolume => Mathf.Clamp01(PlayerPrefs.GetFloat("Moonkov.Audio.Master", .8f));

        public static float EffectsVolume =>
            Mathf.Clamp(PlayerPrefs.GetFloat("Moonkov.Audio.Effects", .85f), 0f, SoundMixer.MaximumCategoryVolume);

        public static float MusicVolume =>
            Mathf.Clamp(PlayerPrefs.GetFloat("Moonkov.Audio.Music", SoundMixer.soundMusicVol?.FloatValue ?? 1f), 0f,
                SoundMixer.MaximumCategoryVolume);

        public static float InterfaceVolume => Mathf.Clamp01(PlayerPrefs.GetFloat("Moonkov.Audio.Interface", .65f));

        public static float MaximumVolume(string channel) {
            return channel == "Effects" || channel == "Music" ? SoundMixer.MaximumCategoryVolume : 1f;
        }

        public static void ApplyVolumes() {
            // 总音量从听者入口应用，音乐、效果和菜单再按各自偏好送给 AudioMixer。玩家每调一次滑块就重新应用这些值。
            AudioListener.volume = MasterVolume;
            if (SoundMixer.soundSFXVol != null)
                SoundMixer.soundSFXVol.Value =
                    EffectsVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (SoundMixer.soundMusicVol != null)
                SoundMixer.soundMusicVol.Value =
                    MusicVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (SoundMixer.soundMenuVol != null)
                SoundMixer.soundMenuVol.Value =
                    InterfaceVolume.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static void SetVolume(string channel, float value) {
            PlayerPrefs.SetFloat("Moonkov.Audio." + channel, Mathf.Clamp(value, 0f, MaximumVolume(channel)));
            ApplyVolumes();
        }
    }
}