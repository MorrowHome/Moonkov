using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

// Final interaction bindings; repeated installs preserve every SoundDef GUID.
public static class InteractionFoleySetup
{
    const string Root = "Assets/Audio/Moonkov/";
    [MenuItem("Moonkov/Audio/Install detailed interaction foley")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        var library = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        if (!library) throw new InvalidOperationException("Missing audio library");
        var imported = new HashSet<string>();
        Bind(nameof(library.InventoryOpen), -10, 0, "TarkovPouchOpen");
        Bind(nameof(library.InventoryClose), -12, 0, "TarkovPouchClose");
        Bind(nameof(library.InventoryCloth), -13, 0, "TarkovPouchHandle1", "TarkovPouchHandle2");
        Bind(nameof(library.InventoryMetal), -12, 0, "HL2ItemMetal");
        Bind(nameof(library.InventoryPlastic), -13, 0, "HL2ItemPlastic1", "HL2ItemPlastic2");
        Bind(nameof(library.InventoryFood), -13, 0, "HL2ItemSoft1", "HL2ItemSoft2");
        Bind(nameof(library.InventoryEquip), -11, 0, "HL2EquipmentLatch");
        Bind(nameof(library.InventoryDrop), -11, 0, "HL2ItemDrop1", "HL2ItemDrop2");
        Bind(nameof(library.BatteryInsert), -10, 0, "HL2CellInsert");
        // Keep the original container cue for successful loot discovery.
        Bind(nameof(library.Container), -12, 0, "Interaction/Container");
        Bind(nameof(library.ContainerClose), -13, 0, "HL2ContainerClose");
        Bind(nameof(library.LootSearchWorld), -14, 10, "TarkovPouchSearch1", "TarkovPouchSearch2");
        Bind(nameof(library.WeaponDraw), -12, 10, "CSSWeaponDraw");
        Bind(nameof(library.WeaponDrawPistol), -12, 10, "CSSPistolDraw");
        Bind(nameof(library.MeleeDraw), -13, 10, "CSSKnifeDraw");
        Bind(nameof(library.WeaponStow), -14, 10, "HL2WeaponStow");
        Bind(nameof(library.WeaponAimIn), -18, 0, "HL2AimIn");
        Bind(nameof(library.WeaponAimOut), -20, 0, "HL2AimOut");
        Bind(nameof(library.GrenadeDraw), -13, 10, "HL2GrenadeDraw");
        Bind(nameof(library.GrenadePin), -10, 10, "CSSGrenadePin");
        Bind(nameof(library.GrenadeThrow), -13, 10, "HL2GrenadeThrow");
        Bind(nameof(library.OxygenConnect), -12, 8, "HL2OxygenConnect");
        Bind(nameof(library.OxygenDisconnect), -13, 8, "HL2OxygenDisconnect");
        Bind(nameof(library.Light), -12, 0, "HL2Flashlight");
        Bind(nameof(library.RoomSwitch), -12, 0, "HL2RoomSwitch");
        Bind(nameof(library.DryFire), -14, 0, "HL2DryFire");
        Bind(nameof(library.TerminalStart), -15, 0, "HL2TerminalStart");
        Bind(nameof(library.TerminalComplete), -14, 0, "HL2TerminalComplete");
        EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);

        void Bind(string field, float db, float range, params string[] names)
        {
            var clips = new List<AudioClip>();
            foreach (var name in names)
            {
                string path = Root + "Clips/" + (name.Contains("/") ? name : "GameFoley/" + name) + ".wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (!clip) throw new InvalidOperationException("Missing " + path);
                if (imported.Add(path))
                {
                    var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                    var settings = importer.defaultSampleSettings;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                    settings.sampleRateOverride = 48000; settings.preloadAudioData = true;
                    importer.defaultSampleSettings = settings; importer.forceToMono = true; importer.loadInBackground = false;
                    var serialized = new SerializedObject(importer);
                    serialized.FindProperty("m_Normalize").boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
                }
                clips.Add(clip);
            }
            string bankPath = Root + "SoundDefs/" + field + ".asset";
            var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(bankPath);
            bool create = !sound; if (create) sound = ScriptableObject.CreateInstance<SoundDef>();
            sound.name = field; sound.Clips = clips;
            sound.UnityAudioMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer");
            sound.MixerGroup = SoundMixer.SoundMixerGroup.SFX; sound.Role = SoundDef.MixRole.Interaction;
            sound.Priority = 100; sound.MaxVoices = 6; sound.MinimumInterval = range > 0 ? 0 : .035f; sound.Occlusion = range > 0;
            sound.VolumeScale = 1; sound.BasePitchInCents = 0; sound.PlayCount = 1;
            sound.PlaybackType = clips.Count > 1 ? SoundDef.PlaybackTypes.RandomNotLast : SoundDef.PlaybackTypes.Sequential;
            sound.RepeatInfo = new SoundDef.Repeat { LoopCount = 1 }; sound.StartStopInfo = new SoundDef.StartStop();
            sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume { VolumeMin = db - 1, VolumeMax = db };
            sound.DistanceInfo = new SoundDef.Distance { SpatialBlend = range > 0 ? 1 : 0, DopplerScale = 0,
                VolumeDistMin = 1, VolumeDistMax = Mathf.Max(range, 1), VolumeRolloffMode = AudioRolloffMode.Linear };
            sound.LowPassFilter = new SoundDef.Filter(); sound.HighPassFilter = new SoundDef.Filter();
            sound.DistortionFilter = new SoundDef.Distortion();
            if (create) AssetDatabase.CreateAsset(sound, bankPath); else EditorUtility.SetDirty(sound);
            AssetDatabase.SaveAssetIfDirty(sound);
            typeof(MoonkovAudioLibrary).GetField(field).SetValue(library, sound);
        }
    }
}
