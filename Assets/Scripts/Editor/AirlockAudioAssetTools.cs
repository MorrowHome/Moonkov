using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;

public static class AirlockAudioAssetTools
{
    public const string Folder = "Assets/Audio/Moonkov/Airlock";
    private static SoundDef Definition(string name, bool loop)
    {
        string path = Folder + "/SoundDefs/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<SoundDef>(path); if (existing) return existing;
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/Clips/" + name + ".wav"); if (!clip) return null;
        var sound = Object.Instantiate(AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Moonkov/SoundDefs/Equipment.asset"));
        sound.name = name + "_CC0_CANDIDATE_REPLACEABLE"; sound.Clips = new List<AudioClip> { clip };
        sound.RepeatInfo.LoopCount = loop ? 0 : 1; sound.RepeatInfo.RepeatMin = sound.RepeatInfo.RepeatMax = 1;
        sound.PitchAndVolumeInfo.PitchMin = sound.PitchAndVolumeInfo.PitchMax = 0;
        sound.PitchAndVolumeInfo.VolumeMin = sound.PitchAndVolumeInfo.VolumeMax = -12;
        sound.DistanceInfo.SpatialBlend = 1; sound.DistanceInfo.VolumeDistMin = 1; sound.DistanceInfo.VolumeDistMax = 14;
        sound.DistanceInfo.SpatialBlend_MaxDistance = 14;
        sound.LowPassFilter.EnableComponent = true; sound.LowPassFilter.CutoffMin = sound.LowPassFilter.CutoffMax = 22000;
        TuneDoor(sound,name); AssetDatabase.CreateAsset(sound,path); return sound;
    }
    [MenuItem("Tools/Moonkov/Airlock/Tune door audibility")]
    public static void TuneDoors()
    {
        foreach (string name in new[] { "DoorMotor_Loop", "LatchLock", "LatchUnlock" })
        {
            var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(Folder + "/SoundDefs/" + name + ".asset");
            if (!sound) throw new System.InvalidOperationException("Missing airlock cue " + name);
            TuneDoor(sound, name); EditorUtility.SetDirty(sound); AssetDatabase.SaveAssetIfDirty(sound);
        }
    }
    private static void TuneDoor(SoundDef sound, string name)
    {
        if (name != "DoorMotor_Loop" && name != "LatchLock" && name != "LatchUnlock") return;
        // Physical air/contact propagation remains owned by AirlockAudio.
        sound.PitchAndVolumeInfo.VolumeMin = sound.PitchAndVolumeInfo.VolumeMax = name == "DoorMotor_Loop" ? -5 : -3;
        sound.DistanceInfo.VolumeDistMin = 3.5f; sound.DistanceInfo.VolumeDistMax = name == "DoorMotor_Loop" ? 28 : 22;
        sound.DistanceInfo.SpatialBlend_MaxDistance = sound.DistanceInfo.VolumeDistMax;
        sound.Priority = 70; sound.MaxVoices = 8; sound.Role = SoundDef.MixRole.Interaction;
    }
    [MenuItem("Tools/Moonkov/Airlock/Bind replaceable CC0 audio candidates to prototype only")]
    public static string Bind()
    {
        Directory.CreateDirectory(Folder + "/SoundDefs"); AssetDatabase.Refresh();
        var valve = Definition("ValveAir_Loop",true); var pump = Definition("Pump_Loop",true); var door = Definition("DoorMotor_Loop",true);
        var latch = Definition("LatchLock",false); var unlock = Definition("LatchUnlock",false);
        var root = PrefabUtility.LoadPrefabContents(AirlockPrototypeTools.PrefabPath);
        try
        {
            var audio = root.GetComponent<AirlockAudio>();
            audio.Configure(root.GetComponent<AirlockController>(),null,root.transform.Find("Valve anchor"),root.transform.Find("Pump anchor"),
                root.transform.Find("Outer motor anchor"),root.transform.Find("Inner motor anchor"),valve,pump,door,latch,unlock);
            PrefabUtility.SaveAsPrefabAsset(root,AirlockPrototypeTools.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(AirlockPrototypeTools.ScenePath,UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            foreach (var r in scene.GetRootGameObjects())
            {
                var owner = r.GetComponent<AirlockController>(); if (!owner) continue;
                r.GetComponent<AirlockAudio>().Configure(owner,null,r.transform.Find("Valve anchor"),r.transform.Find("Pump anchor"),
                    r.transform.Find("Outer motor anchor"),r.transform.Find("Inner motor anchor"),valve,pump,door,latch,unlock);
                r.GetComponent<AirlockPrototypeDemo>()?.ConfigureAudioMixer(AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"));
            }
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true); UnityEngine.SceneManagement.SceneManager.SetActiveScene(original); }
        return "CC0 everyday-device candidate sound definitions created and wired on new placeholder prefab only; provenance in Sources.json; subjective audition remains pending.";
    }
}
