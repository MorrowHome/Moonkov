using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class MoonkovInteractionAudioChecks
{
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Moonkov/Check Interaction Audio")]
    public static void Run()
    {
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        var action = new MoonkovActionAudio();
        Require(action.Observe(1, 1, false, false, lib.Error) == null, "Late join replayed an action");
        action.Track(1, 5, lib.Medical, lib.Consume);
        Require(action.Observe(2, 5, false, false, lib.Error) == null, "Foreign raid acknowledged request");
        Require(action.Observe(1, 4, false, false, lib.Error) == null, "Stale response acknowledged request");
        Require(action.Observe(1, 5, true, false, lib.Error) == lib.Consume, "No timed action start");
        for (int i = 0; i < 50; i++)
            Require(action.Observe(1, 5, true, false, lib.Error) == null, "Progress replayed audio");
        Require(action.Observe(1, 5, false, false, lib.Error) == lib.Medical, "Medical completion missing");
        Require(action.Observe(1, 5, false, false, lib.Error) == null, "Completion replayed");
        action.Track(1, 6, lib.Pickup);
        Require(action.Observe(1, 6, false, true, lib.Error) == lib.Error, "Rejected pickup sounded successful");
        Require(action.Observe(1, 6, false, false, lib.Error) == null, "Rejected action later replayed");
        action.Track(1, 7, lib.Medical, lib.Consume);
        action.Track(1, 8, lib.Equipment); // Cancel replaces the treatment request.
        Require(action.Observe(1, 7, false, false, lib.Error) == null, "Cancelled treatment completed");
        Require(action.Observe(1, 8, false, false, lib.Error) == lib.Equipment, "Cancellation missing");
        action.Track(1, 9, lib.OxygenRefill);
        Require(action.Observe(1, 9, false, false, lib.Error) == lib.OxygenRefill, "Small refill missing");
        action.Track(1, 10, lib.Pickup); action.Reset();
        Require(action.Observe(1, 10, false, false, lib.Error) == null, "Timed out request replayed");

        var reload = new MoonkovReloadAudio();
        Require(reload.Observe(10, 2, 3, false, true) == ReloadAudioEvent.None, "Historical reload replay");
        Require(reload.Observe(11, 2, 3, true, true) == (ReloadAudioEvent.Start | ReloadAudioEvent.Stop), "Reload start missing");
        Require(reload.Observe(11, 2, 3, true, true) == ReloadAudioEvent.None, "Reload repeated");
        Require(reload.Observe(11, 2, 10, false, true) == (ReloadAudioEvent.Stop | ReloadAudioEvent.Ready), "Reload completion missing");
        Require(reload.Observe(11, 2, 10, false, true) == ReloadAudioEvent.None, "Ready repeated");
        reload.Observe(11, 2, 2, false, true);
        Require(reload.Observe(12, 2, 10, false, true) == (ReloadAudioEvent.Stop | ReloadAudioEvent.Ready), "Skipped short reload lost ready");
        reload.Observe(13, 2, 2, true, true);
        Require(reload.Observe(13, 3, 20, false, true) == ReloadAudioEvent.Stop, "Weapon switch sounded complete");
        reload.Observe(14, 3, 2, true, true);
        Require(reload.Observe(14, 3, 2, false, true) == ReloadAudioEvent.Stop, "Cancellation did not stop reload");
        reload.Observe(15, 3, 2, true, true);
        Require(reload.Observe(15, 3, 10, false, false) == ReloadAudioEvent.Stop, "Death sounded ready");
        Require(reload.Observe(15, 3, 10, false, true) == ReloadAudioEvent.None, "Respawn replayed reload");
        foreach (var sound in Banks())
        {
            Require(sound && sound.Clips.Count > 0 && sound.MaxVoices > 0, "Unwired/unbudgeted interaction");
            foreach (var clip in sound.Clips) Require(clip && clip.length > 0, "Missing interaction clip");
            Require(sound.UnityAudioMixer && sound.MixerGroup == SoundMixer.SoundMixerGroup.SFX, "Interaction outside SFX bus: " + sound.name);
        }
        Debug.Log("Interaction audio: request ownership, 50 progress updates, success/failure/cancel/timeout, small refill; reload lifecycle/skipped snapshots/switch/death and sound banks passed.");
    }

    private static SoundDef[] Banks()
    {
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        return new[] { lib.Pickup, lib.Medical, lib.Consume, lib.OxygenRefill, lib.ShieldRestore,
            lib.Equipment, lib.Container, lib.Light, lib.ReloadReady,
            AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Moonkov/SoundDefs/HaloRecharge.asset"),
            AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Moonkov/SoundDefs/RevolverRecharge.asset") };
    }

    public static string CheckPlayback()
    {
        Require(Application.isPlaying, "Use existing Editor Play mode");
        var anchor = new GameObject("Interaction audio check anchor");
        var pool = new SoundGameObjectPool("Interaction audio check pool", 4);
        var system = new SoundSystem();
        int played = 0;
        try
        {
            system.Init(anchor.transform, 4, pool, AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"));
            foreach (var bank in Banks())
            {
                var info = system.CreateEmitter(bank, Vector3.zero);
                Require(info != null && info.Handle.IsValid(), "No emitter: " + bank.name);
                var source = info.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                Require(source.isPlaying && source.volume > 0 && source.outputAudioMixerGroup.name == "SFX", "Silent/misrouted: " + bank.name);
                played++; system.Kill(info);
            }
            Require(pool.AvailableSoundObjectCount == 4, "Leaked interaction voice");
            return played + " interaction/reload banks actually playing through SFX; pool reclaimed";
        }
        finally { system.KillAll(); pool.Dispose(); UnityEngine.Object.Destroy(anchor); }
    }
}
