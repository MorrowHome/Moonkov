using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovPresenceAudioSetup
{
    const string Root = "Assets/Audio/Moonkov/";
    [MenuItem("Moonkov/Audio/Install gameplay presence sounds")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip",new[]{Root+"Clips/Presence"}))
        {
            var importer=(AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            var sample=importer.defaultSampleSettings; sample.loadType=AudioClipLoadType.DecompressOnLoad;
            sample.compressionFormat=AudioCompressionFormat.PCM;sample.preloadAudioData=true;
            sample.sampleRateSetting=AudioSampleRateSetting.OverrideSampleRate;sample.sampleRateOverride=48000;
            importer.defaultSampleSettings=sample;importer.forceToMono=true;importer.loadInBackground=false;
            var serialized=new SerializedObject(importer);serialized.FindProperty("m_Normalize").boolValue=false;
            serialized.ApplyModifiedPropertiesWithoutUndo();importer.SaveAndReimport();
        }
        var lib=Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        lib.JumpEffort=Bank("JumpEffort",lib.MeleeEffort,-4,18,3,75,SoundDef.MixRole.Suit,false,"JumpEffort1","JumpEffort2","JumpEffort3");
        lib.InteriorVent=Bank("InteriorVent",lib.SuitBreath,-22,0,2,205,SoundDef.MixRole.Ambience,true,"InteriorVent");
        lib.SuitVent=Bank("SuitVent",lib.SuitBreath,-29,0,2,205,SoundDef.MixRole.Ambience,true,"SuitVent");
        var motor=AssetDatabase.LoadAssetAtPath<SoundDef>(Root+"Airlock/SoundDefs/Pump_Loop.asset");
        var gate=AssetDatabase.LoadAssetAtPath<SoundDef>(Root+"Airlock/SoundDefs/DoorMotor_Loop.asset");
        var latch=AssetDatabase.LoadAssetAtPath<SoundDef>(Root+"Airlock/SoundDefs/LatchLock.asset");
        lib.LiftMotor=Bank("LiftMotor",motor,-13,36,4,95,SoundDef.MixRole.Interaction,true);
        lib.LiftGate=Bank("LiftGate",gate,-12,24,4,100,SoundDef.MixRole.Interaction,true);
        lib.LiftStart=Bank("LiftStart",lib.Equipment,-14,22,4,100,SoundDef.MixRole.Interaction,false);
        lib.LiftStop=Bank("LiftStop",latch,-14,28,4,90,SoundDef.MixRole.Interaction,false);
        Level(lib.GearMotion,-19);Level(lib.StanceChange,-19);Level(lib.Crawl,-17);Level(lib.Jump,-11);Level(lib.Land,-11);
        EditorUtility.SetDirty(lib);AssetDatabase.SaveAssetIfDirty(lib);
        MovementFoleySetup.Install();
    }
    static void Level(SoundDef sound,float db)
    { sound.PitchAndVolumeInfo.VolumeMin=db-2;sound.PitchAndVolumeInfo.VolumeMax=db;EditorUtility.SetDirty(sound);AssetDatabase.SaveAssetIfDirty(sound); }
    static SoundDef Bank(string name,SoundDef template,float db,float range,int voices,int priority,SoundDef.MixRole role,bool loop,params string[] clips)
    {
        string path=Root+"SoundDefs/"+name+".asset";
        var bank=AssetDatabase.LoadAssetAtPath<SoundDef>(path);bool create=!bank;
        if(create)bank=UnityEngine.Object.Instantiate(template);else EditorUtility.CopySerialized(template,bank);
        bank.name=name;
        if(clips.Length>0){bank.Clips=new List<AudioClip>();foreach(var clip in clips){var asset=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"Clips/Presence/"+clip+".wav");if(!asset)throw new InvalidOperationException(clip);bank.Clips.Add(asset);}}
        bank.MixerGroup=SoundMixer.SoundMixerGroup.SFX;bank.Role=role;bank.Priority=priority;bank.MaxVoices=voices;
        bank.MinimumInterval=0;bank.Occlusion=range>0;bank.VolumeScale=1;bank.BasePitchInCents=0;
        bank.PlaybackType=bank.Clips.Count>1?SoundDef.PlaybackTypes.RandomNotLast:SoundDef.PlaybackTypes.Sequential;
        bank.PlayCount=1;bank.RepeatInfo=new SoundDef.Repeat {LoopCount=loop?0:1};bank.StartStopInfo=new SoundDef.StartStop();
        bank.PitchAndVolumeInfo=new SoundDef.PitchAndVolume {VolumeMin=db-1,VolumeMax=db};
        bank.DistanceInfo=new SoundDef.Distance {SpatialBlend=range>0?1:0,VolumeDistMin=2,VolumeDistMax=Mathf.Max(2,range),
            VolumeRolloffMode=AudioRolloffMode.Linear,LPF_MaxDistance=Mathf.Max(2,range),LPF_MinCutoff=1800};
        bank.LowPassFilter=new SoundDef.Filter();bank.HighPassFilter=new SoundDef.Filter();bank.DistortionFilter=new SoundDef.Distortion();
        if(create)AssetDatabase.CreateAsset(bank,path);else EditorUtility.SetDirty(bank);AssetDatabase.SaveAssetIfDirty(bank);return bank;
    }
}
