using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;

// Native UI and observable sound requests. No account, scene save, Play or network access.
public sealed class AccessAudioReview : EditorWindow
{
    private const string Output = "LocalData/AccessAudio/checks.txt";
    private readonly List<string> m_Checks = new List<string>();
    private HalosTerminalBackdrop m_Backdrop;
    private HalosAccessTransition m_Transition;
    private RecordingSystem m_Sounds;
    private VisualElement m_Host;

    public static void Check()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use an idle Editor.");
        var window = CreateInstance<AccessAudioReview>();
        window.titleContent = new GUIContent("Moonkov / Audio sync review");
        window.position = new Rect(40, 50, 1280, 750);
        window.ShowUtility(); window.Run();
    }

    // Uses the same real emitter/source path as the SoundDef Inspector preview.
    // This checks native playback without entering Play or touching account state.
    public static async void CheckPlayback()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use an idle Editor.");
        var lines = new List<string>();
        var go = new GameObject("Access audio playback review") { hideFlags = HideFlags.HideAndDontSave };
        var data = new SoundGameObject.AudioComponentData {
            ASource = go.AddComponent<AudioSource>(), LowPassFilter = go.AddComponent<AudioLowPassFilter>(),
            HighPassFilter = go.AddComponent<AudioHighPassFilter>(), DistortionFilter = go.AddComponent<AudioDistortionFilter>()
        };
        var source = new SoundGameObject(go, data);
        var emitter = new SoundEmitter(null) { Reserved = SoundEmitter.ReservedInfo.ReservedEmitterAndAudioSources };
        emitter.ActiveSoundGameObjects.Add(source);
        try
        {
            var lib = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>("Assets/Resources/Moonkov/AudioLibrary.asset");
            var mixer = new SoundMixer(lib.AccessTerminal.UnityAudioMixer);
            var route = mixer.MixerGroups[(int)SoundMixer.SoundMixerGroup.Menu];
            if (!route) throw new InvalidOperationException("Missing Menu mixer group");
            foreach (var bank in Enumerable.Repeat(lib.AccessTerminal, 33).Concat(new[] { lib.AccessClose, lib.AccessReveal }))
            {
                emitter.SetSoundDef(bank); emitter.CopyOverrideDataFromSoundDef(bank); emitter.Activate();
                source.EnableAudioFilterComponents(bank); source.Enable(emitter); emitter.Volume = 1;
                emitter.Play(null, mixer.MixerGroups);
                var audio = data.ASource;
                if (!audio.isPlaying || audio.clip != bank.Clips[0] || audio.pitch != 1 ||
                    audio.volume <= 0 || audio.outputAudioMixerGroup != route || audio.spatialBlend != 0)
                    throw new InvalidOperationException("Invalid real AudioSource playback: " + bank.name);
                lines.Add("PASS real AudioSource: " + bank.name + ", pitch=" + audio.pitch + ", gain=" + audio.volume);
                await Task.Delay(bank == lib.AccessTerminal ? 90 : Mathf.CeilToInt(audio.clip.length * 1000) + 50);
                audio.Stop();
            }
            lib.AccessTerminal.UnityAudioMixer.GetFloat("MasterVolume", out float master);
            lib.AccessTerminal.UnityAudioMixer.GetFloat("MenuVolume", out float menu);
            lines.Add("Mixer levels at audition: master=" + master + " dB, menu=" + menu + " dB");
        }
        catch (Exception e) { lines.Add("FAIL " + e); Debug.LogException(e); }
        finally
        {
            data.ASource.Stop(); UnityEngine.Object.DestroyImmediate(go);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText("LocalData/AccessAudio/playback-checks.txt", string.Join("\n", lines));
        }
    }

    private void Require(bool pass, string label)
    {
        if (!pass) throw new InvalidOperationException(label);
        m_Checks.Add("PASS " + label);
    }

    private Task Wait(int ms)
    {
        var done = new TaskCompletionSource<bool>();
        rootVisualElement.schedule.Execute(() => done.SetResult(true)).StartingIn(ms);
        return done.Task;
    }

    private async Task Until(Func<bool> predicate, float timeout = 12)
    {
        double end = EditorApplication.timeSinceStartup + timeout;
        while (!predicate() && EditorApplication.timeSinceStartup < end) await Wait(20);
        Require(predicate(), "expected visible/audio phase reached");
    }

    private void Create(MoonkovAudioLibrary lib, bool loading = false)
    {
        m_Backdrop?.Dispose(); rootVisualElement.Clear();
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/MainMenu.uxml").CloneTree();
        tree.style.flexGrow = 1; rootVisualElement.Add(tree);
        m_Host = tree.Q("Canvas"); m_Host.AddToClassList("archive-access-account");
        m_Host.AddToClassList("halos-compact");
        tree.Q("stashScreenHost").style.display = DisplayStyle.None;
        tree.Q("ConnectionPanel").style.display = DisplayStyle.None;
        tree.Q("HidingBackground").style.display = DisplayStyle.None;
        m_Sounds = new RecordingSystem();
        m_Backdrop = new HalosTerminalBackdrop(m_Host, loading, null, new HalosAccessAudio(m_Sounds, lib));
    }

    private async void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        try
        {
            var lib = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>("Assets/Resources/Moonkov/AudioLibrary.asset");
            Require(lib != null, "audio library exists");
            foreach (var sound in new[] { lib.AccessCarrier, lib.AccessTerminal, lib.AccessCut, lib.AccessReveal, lib.AccessClose, lib.AccessRoom, lib.AccessGlitch })
            {
                Require(sound && sound.Clips.Count > 0 && sound.Clips.All(clip => clip), "all access references are wired");
                Require(sound.MixerGroup == SoundMixer.SoundMixerGroup.Menu && sound.DistanceInfo.SpatialBlend == 0 &&
                    !sound.Occlusion && sound.MaxVoices >= 1, "menu volume route, 2D sound and bounded voices: " + sound.name);
                foreach (var clip in sound.Clips)
                {
                    var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
                    Require(clip.channels == 2 && clip.frequency == 48000, "stereo 48 kHz: " + clip.name);
                    bool loop = sound.RepeatInfo.LoopCount == 0;
                    Require(importer.defaultSampleSettings.loadType == (loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad) &&
                        importer.defaultSampleSettings.preloadAudioData, "loop/transient loading and preloading: " + clip.name);
                }
            }
            Require(lib.AccessCarrier.RepeatInfo.LoopCount == 0 && lib.AccessRoom.RepeatInfo.LoopCount == 0 &&
                lib.AccessGlitch.Clips.Count == 3 && lib.AccessTerminal.Clips.Count == 1, "two loops, one consistent pop and glitch variants");
            Require(lib.AccessTerminal.BasePitchInCents == 0 && lib.AccessTerminal.PitchAndVolumeInfo.PitchMin == 0 &&
                lib.AccessTerminal.PitchAndVolumeInfo.PitchMax == 0, "pop asset has no pitch shift or pitch randomization");
            Require(lib.AccessClose.Clips[0].length >= .74f && lib.AccessReveal.Clips[0].length >= 2.5f,
                "reverse rise spans dimming/closing and crash contains a long reverb tail");

            Create(lib); m_Host.style.display = DisplayStyle.None; m_Backdrop.Present(true, false);
            await Wait(300);
            Require(m_Sounds.Events.Count == 0 && m_Backdrop.IntroPending, "hidden startup cannot play audio or consume intro");
            m_Host.style.display = DisplayStyle.Flex;
            await Until(() => m_Sounds.Count(lib.AccessTerminal) > 0);
            Require(m_Sounds.Active(lib.AccessCarrier) && m_Host.Q("halosTerminal0").style.display == DisplayStyle.Flex,
                "first data tick and carrier accompany the first visible window");
            m_Backdrop.Present(false, false);
            int count = m_Sounds.Events.Count;
            Require(m_Sounds.ActiveCount == 0, "hiding stops all owned voices");
            await Wait(250);
            Require(m_Sounds.Events.Count == count, "hidden time produces no delayed cues");
            m_Backdrop.Present(true, false);
            await Until(() => m_Sounds.Count(lib.AccessCut) == 1);
            Require(m_Host.Q(className: "halos-impact").style.opacity.value > 0 &&
                !m_Sounds.Active(lib.AccessCarrier), "signal cut lands on the visible flash and releases the carrier");
            Require(m_Sounds.Count(lib.AccessTerminal) == 33,
                "all thirty-three windows receive one pop, including shared-frame arrivals");
            Require(m_Sounds.Events.Where(e => e.Definition == lib.AccessTerminal).All(e => e.Pitch == 0),
                "all thirty-three pop requests preserve the original pitch");
            await Until(() => m_Host.Q("halosTerminal0").style.display == DisplayStyle.None);
            Require(m_Sounds.ActiveCount == 0, "black hold is silent");
            await Until(() => m_Sounds.Count(lib.AccessReveal) == 1);
            Require(m_Host.Q(className: "halos-shutter-top").style.translate.value.y.value < 0,
                "reveal sweep starts with shutter movement");
            await Until(() => m_Sounds.Count(lib.AccessRoom) == 1);
            Require(m_Host.Q("Container").style.visibility == Visibility.Visible, "room and voices fade in as login content appears");
            await Until(() => !m_Backdrop.IntroPending);
            await Wait(700);
            Require(m_Sounds.Count(lib.AccessCut) == 1 && m_Sounds.Count(lib.AccessReveal) == 1 &&
                m_Sounds.Count(lib.AccessRoom) == 1, "steady updates never replay transitions or duplicate the ambient loop");
            await Until(() => m_Sounds.Count(lib.AccessGlitch) >= 1);
            var firstGlitch = m_Sounds.Events.Last(e => e.Definition == lib.AccessGlitch);
            var firstRoom = m_Sounds.Events.First(e => e.Definition == lib.AccessRoom);
            Require(firstGlitch.At - firstRoom.At >= 3.4, "first login glitch respects the quiet interval");
            await Until(() => m_Sounds.Count(lib.AccessGlitch) == 2);
            var secondGlitch = m_Sounds.Events.Last(e => e.Definition == lib.AccessGlitch);
            Require(secondGlitch.At - firstGlitch.At >= 3.4, "login glitches stay sparse");
            m_Backdrop.Present(true, true);
            count = m_Sounds.Count(lib.AccessGlitch);
            await Wait(3800);
            Require(m_Sounds.Count(lib.AccessGlitch) == count && m_Sounds.Active(lib.AccessRoom),
                "reduced motion keeps the room bed but disables glitch cues");
            var canvasParent = m_Host.parent;
            int canvasIndex = canvasParent.IndexOf(m_Host);
            m_Host.RemoveFromHierarchy();
            Require(m_Sounds.ActiveCount == 0, "panel detach releases every voice");
            canvasParent.Insert(canvasIndex, m_Host); await Wait(150);
            Require(m_Sounds.Active(lib.AccessRoom), "reattached reduced-motion login restores ambience");
            m_Backdrop.Dispose();
            Require(m_Sounds.ActiveCount == 0, "dispose releases every voice");

            Create(lib); m_Backdrop.Present(true, false);
            await Until(() => m_Sounds.Count(lib.AccessTerminal) > 0);
            m_Backdrop.Skip(); await Wait(200);
            Require(!m_Backdrop.IntroPending && !m_Sounds.Active(lib.AccessCarrier) && m_Sounds.Active(lib.AccessRoom) &&
                m_Sounds.Count(lib.AccessCut) == 0 && m_Sounds.Count(lib.AccessReveal) == 0,
                "skip cancels buildup without playing delayed flash or shutter cues");
            Create(lib, true); m_Backdrop.Present(true, false); await Wait(300);
            Require(m_Sounds.Events.Count == 0, "battle loading does not replay login audio");
            m_Backdrop.Dispose();
            m_Transition = new HalosAccessTransition(m_Host, null, new HalosAccessAudio(m_Sounds, lib));
            m_Transition.Present(true, true, true, false);
            await Wait(250);
            Require(m_Sounds.Count(lib.AccessClose) == 1, "rise starts with confirmation dimming and builds into closure");
            await Until(() => m_Sounds.Count(lib.AccessClose) == 1);
            var shutters = m_Host.Q("halosAccessTransition");
            float y = shutters.Q(className: "halos-shutter-top").style.translate.value.y.value;
            Require(y < 0 && !m_Transition.ContentVisible, "reverse crash starts with closing shutters");
            await Until(() => m_Transition.ContentVisible);
            Require(!m_Sounds.Active(lib.AccessClose) && m_Sounds.Count(lib.AccessReveal) == 0,
                "reverse crash stops at closure; covered preparation remains silent");
            await Until(() => m_Sounds.Count(lib.AccessReveal) == 1);
            Require(shutters.Q(className: "halos-shutter-top").style.translate.value.y.value < 0,
                "crash impact starts as the destination shutter opens");
            await Until(() => !m_Transition.IsRunning);
            Require(m_Sounds.Active(lib.AccessReveal), "completed visual reveal preserves the crash reverb tail");
            m_Transition.Present(true, true, true, false); await Wait(120);
            Require(m_Sounds.Count(lib.AccessReveal) == 1, "repeated authentication presentation does not replay crash");
            await Until(() => !m_Sounds.Active(lib.AccessReveal), 4);
            Require(m_Sounds.ActiveCount == 0, "crash tail completes without a retained playing voice");
            m_Transition.Present(false, true, true, false);
            m_Transition.Present(true, true, true, false);
            await Until(() => m_Sounds.Count(lib.AccessClose) == 2);
            m_Transition.Present(false, true, true, false);
            Require(m_Sounds.ActiveCount == 0, "cancel during closing releases reverse crash");
            m_Transition.Present(true, true, true, true); await Wait(120);
            Require(m_Sounds.Count(lib.AccessClose) == 2 && m_Sounds.Count(lib.AccessReveal) == 1,
                "reduced-motion transition produces no decorative crash");
            File.WriteAllText(Output, string.Join("\n", m_Checks));
            Debug.Log("Access audio review: " + m_Checks.Count + " checks passed.");
        }
        catch (Exception e)
        {
            File.WriteAllText(Output, string.Join("\n", m_Checks) + "\nFAIL " + e);
            Debug.LogException(e);
        }
        finally { Close(); }
    }

    private void OnDisable() { m_Backdrop?.Dispose(); m_Transition?.Dispose(); rootVisualElement.Clear(); }

    private sealed class RecordingSystem : ISoundSystem
    {
        public sealed class Event { public SoundDef Definition; public double At; public float Pitch; }
        public readonly List<Event> Events = new List<Event>();
        private readonly List<SoundSystem.SoundInfo> m_Active = new List<SoundSystem.SoundInfo>();
        private readonly Dictionary<SoundSystem.SoundInfo, double> m_Ends = new Dictionary<SoundSystem.SoundInfo, double>();
        public int ActiveCount { get { Expire(); return m_Active.Count; } }
        public bool Active(SoundDef def) { Expire(); return m_Active.Any(i => i.SoundDefinition == def); }
        public int Count(SoundDef def) => Events.Count(e => e.Definition == def);
        public void Init(Transform t, int max, SoundGameObjectPool p, AudioMixer m) { }
        public SoundSystem.SoundInfo CreateEmitter(SoundDef d, Transform t, float v = 1) => CreateEmitter(d, Vector3.zero, v);
        public SoundSystem.SoundInfo CreateEmitter(SoundDef d, Transform t, Vector3 p, float v = 1) => CreateEmitter(d, p, v);
        public SoundSystem.SoundInfo CreateEmitter(SoundDef d, Vector3 p, float v = 1)
        { var i = new SoundSystem.SoundInfo { SoundDefinition = d }; PlayEmitter(i, v); return i; }
        public bool PlayEmitter(SoundSystem.SoundInfo i, float v = 1)
        {
            var e = new SoundEmitter(null) { Allocated = true, SeqId = Events.Count, Volume = v };
            i.Handle = new SoundHandle(e); m_Active.Add(i);
            double now = EditorApplication.timeSinceStartup;
            m_Ends[i] = i.SoundDefinition.RepeatInfo.LoopCount == 0 ? double.PositiveInfinity : now + i.SoundDefinition.Clips[0].length;
            Events.Add(new Event { Definition = i.SoundDefinition, At = now,
                Pitch = i.SoundDefOverrideInfo?.BasePitchInCents ?? i.SoundDefinition.BasePitchInCents });
            return true;
        }
        private void Expire()
        {
            for (int n = m_Active.Count - 1; n >= 0; n--)
                if (EditorApplication.timeSinceStartup >= m_Ends[m_Active[n]]) Stop(m_Active[n]);
        }
        public bool Stop(SoundSystem.SoundInfo i, float f = 0)
        { if (i?.Handle != null) i.Handle.Emitter.Allocated = false; m_Ends.Remove(i); return m_Active.Remove(i); }
        public bool Kill(SoundSystem.SoundInfo i) => Stop(i);
        public void KillAll() { foreach (var i in m_Active) i.Handle.Emitter.Allocated = false; m_Active.Clear(); m_Ends.Clear(); }
        public bool SetListenerTransform(Transform t) => true;
        public void UpdateSoundSystem(bool mute = false) { }
    }
}
