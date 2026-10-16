using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class MoonkovRaidAudioChecks
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Moonkov/Check Raid Audio Assets")]
    public static void Run()
    {
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        Require(lib, "Missing audio library");
        foreach (var name in new[] { "RegolithFootsteps", "MetalFootsteps", "HardFootsteps", "AlienContact", "AlienWarning",
            "AlienStrike", "AlienHurt", "AlienDeath", "AlienIdle", "Aim", "Light", "DryFire", "ReloadReady", "Medical",
            "Pickup", "OxygenRefill", "SuitBreath", "Heartbeat", "SuitWarning", "ExtractStart", "ExtractCancel", "ExtractTick" })
        {
            var sound = (SoundDef)typeof(MoonkovAudioLibrary).GetField(name).GetValue(lib);
            Require(sound && sound.Clips.Count > 0, "Unwired cue: " + name);
            Require(sound.MaxVoices > 0 && sound.Priority < 200, "Missing playback budget: " + name);
            Require(sound.DistanceInfo.SpatialBlend == (sound.Occlusion ? 1 : 0), "Wrong spatial route: " + name);
            foreach (var clip in sound.Clips)
            {
                Require(clip && clip.channels == 1 && clip.frequency == 48000, "Bad import: " + name);
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
                Require(importer.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad &&
                    importer.defaultSampleSettings.compressionFormat == AudioCompressionFormat.PCM, "Transient codec: " + clip.name);
                var samples = new float[clip.samples]; clip.GetData(samples, 0); float peak = 0, square = 0;
                foreach (float sample in samples) { peak = Mathf.Max(peak, Mathf.Abs(sample)); square += sample * sample; }
                Require(peak < .8f && peak > .35f && square > .001f, "Silent/clipped asset: " + clip.name);
            }
        }
        var surface = new GameObject("Audio surface fixture");
        try
        {
            var collider = surface.AddComponent<BoxCollider>();
            Require(MoonkovPlayerAudio.ClassifySurface(collider, lib) == lib.HardFootsteps, "Plain collider falsely classified as metal");
            surface.name = "MoonRock";
            Require(MoonkovPlayerAudio.ClassifySurface(collider, lib) == lib.RegolithFootsteps, "Rock boot mapping");
            var marker = surface.AddComponent<RifleImpactSurface>(); marker.Kind = RifleImpactKind.Metal;
            Require(MoonkovPlayerAudio.ClassifySurface(collider, lib) == lib.MetalFootsteps, "Explicit metal mapping");
        }
        finally { UnityEngine.Object.DestroyImmediate(surface); }
        Debug.Log("Raid audio asset/import/surface checks passed.");
    }

    // Run with the menu initialized in Play mode. A separate tiny pool cannot touch live player handles.
    public static string CheckPlayback()
    {
        Require(EditorApplication.isPlaying && GameManager.Instance, "Use initialized Play mode");
        var listener = new GameObject("Audio pool check listener");
        var pool = new SoundGameObjectPool("Audio check pool", 4);
        var system = new SoundSystem();
        var source = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary").MetalFootsteps;
        var sound = UnityEngine.Object.Instantiate(source);
        try
        {
            sound.DistanceInfo = new SoundDef.Distance { SpatialBlend = 0, VolumeDistMin = 1, VolumeDistMax = 10 };
            sound.MaxVoices = 0; sound.MinimumInterval = 0; sound.Occlusion = false;
            system.Init(listener.transform, 2, pool, AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"));
            AudioClip previous = null;
            SoundSystem.SoundInfo first = null;
            for (int i = 0; i < 24; i++)
            {
                var info = system.CreateEmitter(sound, Vector3.zero, .1f);
                Require(info != null && info.Handle.IsValid(), "Allocation failed");
                var audio = info.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                Require(audio.clip != previous, "Repeated adjacent clip across emitter reuse");
                Require(audio.volume <= .1f, "First DSP block ignored emitter gain");
                previous = audio.clip;
                if (i == 0) first = info;
                else Require(!system.Stop(first), "Expired handle stopped a reused emitter");
                system.Kill(info);
            }
            sound.MaxVoices = 1;
            var held = system.CreateEmitter(sound, Vector3.zero);
            Require(held != null && system.CreateEmitter(sound, Vector3.zero) == null, "Voice cap not enforced");
            system.Kill(held);
            sound.MaxVoices = 0; sound.MinimumInterval = 1;
            Require(system.CreateEmitter(sound, Vector3.zero) == null, "Retrigger interval not enforced");
            sound.MinimumInterval = 0; sound.DistanceInfo.SpatialBlend = 1;
            Require(system.CreateEmitter(sound, Vector3.forward * 20) == null, "Inaudible source consumed a voice");
            Require(pool.AvailableSoundObjectCount == 4, "Playback rejection leaked pooled source");
            sound.DistanceInfo.SpatialBlend = 0;
            for (int i = 0; i < 4; i++) Require(system.CreateEmitter(sound, Vector3.zero) != null, "Cannot fill fixture pool");
            var important = UnityEngine.Object.Instantiate(sound);
            try
            {
                important.Priority = 40;
                Require(system.CreateEmitter(important, Vector3.zero) != null && pool.AvailableSoundObjectCount == 0,
                    "Low-priority foley swallowed important cue in full pool");
            }
            finally { system.KillAll(); UnityEngine.Object.Destroy(important); }
            Require(pool.AvailableSoundObjectCount == 4, "Voice preemption leaked a source");
            return "24 nonrepeating recycled playbacks; initial gain; stale handle; voice/interval/range budgets; priority preemption; no pool leak: passed";
        }
        finally { system.KillAll(); pool.Dispose(); UnityEngine.Object.Destroy(listener); UnityEngine.Object.Destroy(sound); }
    }
}
