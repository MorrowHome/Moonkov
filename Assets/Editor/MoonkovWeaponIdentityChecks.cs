using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class MoonkovWeaponIdentityChecks
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Moonkov/Check Weapon Audio Identities")]
    public static void Run()
    {
        var library = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
        var unique = new HashSet<AudioClip>();
        for (uint id = 2; id <= 14; id++)
        {
            var profile = library.ForWeapon(id);
            Require(profile != null && profile.FireBody && profile.FireTail, "Missing weapon " + id);
            Require(profile.FireSnap == null, "Generic snap masks recorded identity " + id);
            Require(profile.FireBody.Clips.Count == 3, "Missing variants " + id);
            float gain = Mathf.Pow(10, profile.FireBody.PitchAndVolumeInfo.VolumeMax / 20) +
                .85f * Mathf.Pow(10, profile.FireTail.PitchAndVolumeInfo.VolumeMax / 20);
            Require(gain < .8f, "Shot exhausts mix headroom " + id);
            foreach (var bank in new[] { profile.FireBody, profile.FireTail })
            {
                Require(bank.MaxVoices > 0 && bank.MixerGroup == SoundMixer.SoundMixerGroup.SFX, "Voice/mix route " + id);
                foreach (var clip in bank.Clips)
                {
                    Require(clip && unique.Add(clip), "Shared recording across weapon identities " + id);
                    Require(clip.channels == 1 && clip.frequency == 48000 && clip.loadType == AudioClipLoadType.DecompressOnLoad,
                        "Invalid import " + clip.name);
                    clip.LoadAudioData(); var samples = new float[clip.samples];
                    Require(clip.GetData(samples, 0), "Unreadable PCM " + clip.name);
                    float peak = 0;
                    foreach (var sample in samples) { Require(float.IsFinite(sample), "Nonfinite PCM"); peak = Mathf.Max(peak, Mathf.Abs(sample)); }
                    Require(peak > .5f && peak < .9f, "Silent/clipped report " + clip.name);
                }
            }
            Require(profile.BodyImpact == library.BaseForWeapon(id).BodyImpact &&
                profile.KillConfirm == library.BaseForWeapon(id).KillConfirm, "Impact feedback changed " + id);
        }
        Require(unique.Count == 52, "Expected 52 distinct clips");
        Debug.Log("Weapon audio: 13 mappings, 52 unique PCM clips, variants, headroom, mixer and original impact references passed.");
    }

    // Isolated pool in the current Editor, without spawning a player or connecting a game.
    public static string CheckPlayback()
    {
        Require(Application.isPlaying, "Use Play mode");
        var listener = new GameObject("Weapon audio validation listener anchor");
        var pool = new SoundGameObjectPool("Weapon audio validation pool", 4);
        var system = new SoundSystem();
        var library = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
        int played = 0;
        try
        {
            system.Init(listener.transform, 4, pool, AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"));
            for (uint id = 2; id <= 14; id++)
            {
                AudioClip previous = null;
                for (int repeat = 0; repeat < 4; repeat++)
                {
                    var profile = library.ForWeapon(id);
                    var info = system.CreateEmitter(profile.FireBody, Vector3.zero);
                    Require(info != null && info.Handle.IsValid(), "No emitter " + id);
                    var source = info.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                    Require(source && source.isPlaying && source.clip != previous, "Playback/variation failed " + id);
                    Require(source.outputAudioMixerGroup && source.outputAudioMixerGroup.name == "SFX", "Wrong bus");
                    previous = source.clip; played++; system.Kill(info);
                }
            }
            Require(pool.AvailableSoundObjectCount == 4, "Pool leak");
            return played + " actual playing sources across 13 weapons; no adjacent repeat; SFX routing; pool fully reclaimed";
        }
        finally { system.KillAll(); pool.Dispose(); UnityEngine.Object.Destroy(listener); }
    }
}
