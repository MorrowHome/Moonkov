using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class TacticalFoleySetup
{
    const string Root = "Assets/Audio/Moonkov/";
    [MenuItem("Moonkov/Audio/Install tactical world and shield foley")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        if (!lib) throw new InvalidOperationException("Missing audio library");
        var imported = new HashSet<string>();
        Bind(nameof(lib.TacticalDeploy), -15, 16, 110, false, "HL2GrenadeDraw");
        Bind(nameof(lib.TacticalTrapArm), -12, 16, 100, false, "HL2TrapArm");
        Bind(nameof(lib.TacticalSupplyDeploy), -12, 12, 100, false, "HL2ContainerOpen");
        Bind(nameof(lib.TacticalWireRelease), -12, 22, 90, false, "HL2WireRelease");
        Bind(nameof(lib.TacticalFlash), -8, 60, 55, false, "CSSFlashBurst1", "CSSFlashBurst2");
        Bind(nameof(lib.TacticalExplosion), -7, 80, 45, false, "CSSFragBlast1", "CSSFragBlast2");
        Bind(nameof(lib.TacticalGas), -14, 22, 110, false, "CSSSmokeBurst");
        Bind(nameof(lib.TacticalFireStart), -13, 26, 90, false, "HL2FireIgnite");
        Bind(nameof(lib.TacticalFireLoop), -22, 22, 145, true, "HL2FireLoop");
        Bind(nameof(lib.TacticalSmokeLoop), -23, 14, 150, true, "HL2SmokeLoop");
        Bind(nameof(lib.TacticalChokeLoop), -22, 18, 150, true, "HL2ChokeLoop");
        Bind(nameof(lib.TacticalPoisonLoop), -18, 16, 150, true, "HL2PoisonLoop");
        Bind(nameof(lib.TacticalAlarm), -12, 55, 60, true, "HL2TrapAlarm");
        Bind(nameof(lib.ShieldImpact), -13, 24, 65, false, "HL2ShieldImpact1", "HL2ShieldImpact2");
        Bind(nameof(lib.ShieldBreak), -10, 30, 55, false, "HL2ShieldBreak");
        Bind(nameof(lib.ShieldRestore), -16, 16, 90, false, "HL2ShieldRestore");
        Bind(nameof(lib.ShieldRaise), -16, 12, 100, false, "HL2ShieldRaise");
        Bind(nameof(lib.ShieldLower), -18, 10, 100, false, "HL2ShieldLower");
        EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);

        void Bind(string field, float db, float range, int priority, bool loop, params string[] names)
        {
            var clips = new List<AudioClip>();
            foreach (var name in names)
            {
                string path = Root + "Clips/GameFoley/" + name + ".wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (!clip) throw new InvalidOperationException("Missing " + path);
                if (imported.Add(path))
                {
                    var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                    var settings = importer.defaultSampleSettings;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
                    settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; settings.sampleRateOverride = 48000;
                    settings.preloadAudioData = true; importer.defaultSampleSettings = settings;
                    importer.forceToMono = true; importer.loadInBackground = false;
                    var serialized = new SerializedObject(importer); serialized.FindProperty("m_Normalize").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
                }
                clips.Add(clip);
            }
            string pathBank = Root + "SoundDefs/" + field + ".asset";
            var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(pathBank); bool create = !sound;
            if (create) sound = ScriptableObject.CreateInstance<SoundDef>();
            sound.name = field; sound.Clips = clips;
            sound.UnityAudioMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer");
            sound.MixerGroup = SoundMixer.SoundMixerGroup.SFX; sound.Role = SoundDef.MixRole.Interaction;
            sound.Priority = priority; sound.MaxVoices = loop ? 4 : 6; sound.MinimumInterval = loop ? 0 : .035f;
            sound.Occlusion = true; sound.VolumeScale = 1; sound.BasePitchInCents = 0; sound.PlayCount = 1;
            sound.PlaybackType = clips.Count > 1 ? SoundDef.PlaybackTypes.RandomNotLast : SoundDef.PlaybackTypes.Sequential;
            sound.RepeatInfo = new SoundDef.Repeat { LoopCount = loop ? 0 : 1 }; sound.StartStopInfo = new SoundDef.StartStop();
            sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume { VolumeMin = db - 1, VolumeMax = db };
            sound.DistanceInfo = new SoundDef.Distance { SpatialBlend = 1, DopplerScale = 0, VolumeDistMin = 1.5f,
                VolumeDistMax = range, VolumeRolloffMode = AudioRolloffMode.Linear };
            sound.LowPassFilter = new SoundDef.Filter(); sound.HighPassFilter = new SoundDef.Filter();
            sound.DistortionFilter = new SoundDef.Distortion();
            if (create) AssetDatabase.CreateAsset(sound, pathBank); else EditorUtility.SetDirty(sound);
            AssetDatabase.SaveAssetIfDirty(sound); typeof(MoonkovAudioLibrary).GetField(field).SetValue(lib, sound);
        }
    }
}
