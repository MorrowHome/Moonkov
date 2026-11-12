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
            Require(profile != null && profile.FireBody && !profile.FireSnap && !profile.FireTail, "Missing full report " + id);
            var bank = profile.FireBody;
            Require(bank.name == MoonkovWeaponIdentitySetup.Names[id - 2] + "HuntReport" && bank.Clips.Count == 3, "Missing variants " + id);
            Require(bank.MaxVoices == 24 && bank.PlayCount == 1 && bank.Occlusion &&
                bank.Role == SoundDef.MixRole.Weapon && bank.MixerGroup == SoundMixer.SoundMixerGroup.SFX, "Voice/mix route " + id);
            Require(bank.PitchAndVolumeInfo.PitchMin == 0 && bank.PitchAndVolumeInfo.PitchMax == 0 &&
                bank.BasePitchInCents == 0, "Original pitch was lost " + id);
            foreach (var clip in bank.Clips)
            {
                Require(clip && unique.Add(clip), "Shared recording across weapon identities " + id);
                Require(clip.channels == 1 && clip.frequency == 48000 && clip.loadType == AudioClipLoadType.DecompressOnLoad,
                    "Invalid import " + clip.name);
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
                using (var serialized = new SerializedObject(importer))
                    Require(serialized.FindProperty("m_Normalize")?.boolValue == false, "Importer normalizes " + clip.name);
                clip.LoadAudioData();
                var samples = new float[clip.samples];
                Require(clip.GetData(samples, 0), "Unreadable PCM " + clip.name);
                float peak = 0;
                foreach (var sample in samples) { Require(float.IsFinite(sample), "Nonfinite PCM"); peak = Mathf.Max(peak, Mathf.Abs(sample)); }
                Require(peak > .63f && peak < .85f, "Unexpected report peak " + clip.name);
                Require(peak * Mathf.Pow(10, bank.PitchAndVolumeInfo.VolumeMax / 20) < .75f,
                    "A single report exhausts headroom " + clip.name);
            }
            Require(profile.BodyImpact == library.BaseForWeapon(id).BodyImpact &&
                profile.KillConfirm == library.BaseForWeapon(id).KillConfirm, "Impact feedback changed " + id);
        }
        Require(unique.Count == 39, "Expected 39 distinct clips");
        Debug.Log("Weapon audio: 13 mappings, 39 PCM reports, original pitch, mixer, headroom and impact references passed.");
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
