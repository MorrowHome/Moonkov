using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MedicalFoleySetup
{
    const string Root="Assets/Audio/Moonkov/";
    [MenuItem("Moonkov/Audio/Install dry injection and cloth bandages")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit mode");
        foreach(var guid in AssetDatabase.FindAssets("t:AudioClip",new[]{Root+"Clips/Medical"}))
        {
            var importer=(AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat=AudioCompressionFormat.PCM;settings.sampleRateSetting=AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride=48000;settings.preloadAudioData=true;importer.defaultSampleSettings=settings;
            importer.forceToMono=true;importer.loadInBackground=false;
            var serialized=new SerializedObject(importer);serialized.FindProperty("m_Normalize").boolValue=false;
            serialized.ApplyModifiedPropertiesWithoutUndo();importer.SaveAndReimport();
        }
        var lib=Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        lib.InjectorCap=Bank("InjectorCap",lib.InjectorCap,"InjectorDryCap",-16,false);
        lib.InjectorPress=Bank("InjectorPress",lib.InjectorPress,"InjectorDryPress",-14,false);
        lib.MedicalOpen=Bank("MedicalOpen",lib.PacketOpen,"BandageOpen",-17,false);
        lib.MedicalHandling=Bank("MedicalHandling",lib.MedicalHandling,"BandageWrap",-15,true);
        lib.MedicalGel=Bank("MedicalGel",lib.Medical,"MedicalGelRub",-17,false);
        lib.Medical=Bank("Medical",lib.Medical,"BandageTighten",-17,false);
        EditorUtility.SetDirty(lib);AssetDatabase.SaveAssetIfDirty(lib);
    }
    static SoundDef Bank(string name,SoundDef template,string clipName,float db,bool loop)
    {
        string path=Root+"SoundDefs/"+name+".asset";
        var sound=AssetDatabase.LoadAssetAtPath<SoundDef>(path);bool create=!sound;if(create)sound=UnityEngine.Object.Instantiate(template);
        var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"Clips/Medical/"+clipName+".wav");if(!clip)throw new InvalidOperationException(clipName);
        sound.name=name;sound.Clips=new List<AudioClip>{clip};sound.Role=SoundDef.MixRole.Interaction;
        sound.MixerGroup=SoundMixer.SoundMixerGroup.SFX;sound.Priority=100;sound.MaxVoices=6;sound.MinimumInterval=0;sound.Occlusion=true;
        sound.VolumeScale=1;sound.BasePitchInCents=0;sound.PlayCount=1;sound.PlaybackType=SoundDef.PlaybackTypes.Sequential;
        sound.RepeatInfo=new SoundDef.Repeat {LoopCount=loop?0:1};sound.StartStopInfo=new SoundDef.StartStop();
        sound.PitchAndVolumeInfo=new SoundDef.PitchAndVolume {VolumeMin=db-1,VolumeMax=db};
        sound.DistanceInfo=new SoundDef.Distance {SpatialBlend=1,VolumeDistMin=1,VolumeDistMax=10,
            VolumeRolloffMode=AudioRolloffMode.Linear,LPF_MaxDistance=10,LPF_MinCutoff=1800};
        sound.LowPassFilter=new SoundDef.Filter();sound.HighPassFilter=new SoundDef.Filter();sound.DistortionFilter=new SoundDef.Distortion();
        if(create)AssetDatabase.CreateAsset(sound,path);else EditorUtility.SetDirty(sound);AssetDatabase.SaveAssetIfDirty(sound);return sound;
    }
}
