using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovDistressAudioSetup
{
    [MenuItem("Moonkov/Audio/Install low health voice")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode");
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        if (!lib.PlayerPain) throw new InvalidOperationException("Install player pain voices first");
        const string path = "Assets/Audio/Moonkov/SoundDefs/PlayerGroan.asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path); bool create = !sound;
        if (create) sound = UnityEngine.Object.Instantiate(lib.PlayerPain);
        else EditorUtility.CopySerialized(lib.PlayerPain, sound);
        sound.name = "PlayerGroan";
        sound.PitchAndVolumeInfo.VolumeMin = -16; sound.PitchAndVolumeInfo.VolumeMax = -14;
        sound.Priority = 115; sound.MaxVoices = 4;
        sound.DistanceInfo.VolumeDistMax = sound.DistanceInfo.LPF_MaxDistance = 12;
        if (create) AssetDatabase.CreateAsset(sound, path); else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound);
        lib.PlayerGroan = sound; EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
    }
}
