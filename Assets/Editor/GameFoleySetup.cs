using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

// Final consumable overrides. Keep the accepted Hunt syringe in MedicalFoleySetup.
public static class GameFoleySetup
{
    const string Root = "Assets/Audio/Moonkov/";

    [MenuItem("Moonkov/Audio/Install original game consumable foley")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "Clips/GameFoley" }))
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 48000; settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = true; importer.loadInBackground = false;
            var serialized = new SerializedObject(importer);
            serialized.FindProperty("m_Normalize").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
        }
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        if (!lib) throw new InvalidOperationException("Missing audio library");
        lib.EatBite = Bank("EatBite", -8, false, "TarkovEat1", "TarkovEat2", "TarkovEat3");
        lib.DrinkSip = Bank("DrinkSip", -8, false, "TarkovDrink1", "TarkovDrink2", "TarkovDrink3");
        lib.PacketOpen = Bank("PacketOpen", -10, false, "TarkovPacketOpen");
        lib.BottleOpen = Bank("BottleOpen", -10, false, "TarkovBottleOpen");
        lib.BottleClose = Bank("BottleClose", -11, false, "TarkovBottleClose");
        lib.MedicalOpen = Bank("MedicalOpen", -10, false, "TarkovBandageOpen");
        lib.MedicalHandling = Bank("MedicalHandling", -11, true, "TarkovBandageWrap");
        lib.Medical = Bank("Medical", -10, false, "TarkovBandageTighten");
        EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
    }

    static SoundDef Bank(string name, float db, bool loop, params string[] clips)
    {
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(Root + "SoundDefs/" + name + ".asset");
        if (!sound) throw new InvalidOperationException("Install base action foley first: " + name);
        var loaded = new List<AudioClip>();
        foreach (var clipName in clips)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Clips/GameFoley/" + clipName + ".wav");
            if (!clip) throw new InvalidOperationException("Missing " + clipName);
            loaded.Add(clip);
        }
        sound.Clips = loaded;
        sound.UnityAudioMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer");
        sound.Role = SoundDef.MixRole.Interaction; sound.MixerGroup = SoundMixer.SoundMixerGroup.SFX;
        sound.Priority = 100; sound.MaxVoices = 6; sound.MinimumInterval = 0; sound.Occlusion = true;
        sound.VolumeScale = 1; sound.BasePitchInCents = 0; sound.PlayCount = 1;
        sound.PlaybackType = loaded.Count > 1 ? SoundDef.PlaybackTypes.RandomNotLast : SoundDef.PlaybackTypes.Sequential;
        sound.RepeatInfo = new SoundDef.Repeat { LoopCount = loop ? 0 : 1 };
        sound.StartStopInfo = new SoundDef.StartStop();
        sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume { VolumeMin = db - 1, VolumeMax = db };
        // Preserve the existing action's spatial range and occlusion distance.
        sound.LowPassFilter = new SoundDef.Filter(); sound.HighPassFilter = new SoundDef.Filter();
        sound.DistortionFilter = new SoundDef.Distortion();
        EditorUtility.SetDirty(sound); AssetDatabase.SaveAssetIfDirty(sound);
        return sound;
    }
}
