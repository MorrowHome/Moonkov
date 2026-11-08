using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class ConsumableAudioSetup
{
    const string Root = "Assets/Audio/Moonkov/";
    [MenuItem("Moonkov/Audio/Install consumable foley")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit mode");
        foreach(var guid in AssetDatabase.FindAssets("t:AudioClip",new[]{Root+"Clips/Consumables"}))
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
        lib.BottleClose=Bank("BottleClose",lib.BottleOpen,-16,false,"BottleClose1","BottleClose2","BottleClose3");
        lib.MedicalHandling=Bank("MedicalHandling",lib.PacketOpen,-14,false,"MedicalHandling1","MedicalHandling2");
        lib.InjectorPress=Bank("InjectorPress",lib.InjectorPress,-13,false,"InjectorPush1","InjectorPush2");
        lib.Medical=Bank("Medical",lib.Medical,-17,false,"MedicalFinish");
        lib.OxygenRefill=Bank("OxygenRefill",lib.OxygenRefill,-16,false,"OxygenRelease");
        lib.OxygenCanisterFlow=Bank("OxygenCanisterFlow",lib.OxygenSupply,-16,true);
        EditorUtility.SetDirty(lib);AssetDatabase.SaveAssetIfDirty(lib);
    }
    static SoundDef Bank(string name,SoundDef template,float db,bool loop,params string[] clips)
    {
        string path=Root+"SoundDefs/"+name+".asset";
        var sound=AssetDatabase.LoadAssetAtPath<SoundDef>(path);bool create=!sound;
        if(create)sound=UnityEngine.Object.Instantiate(template);
        sound.name=name;
        if(clips.Length>0)
        {
            sound.Clips=new List<AudioClip>();
            foreach(var namePart in clips)
            {
                var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Root+"Clips/Consumables/"+namePart+".wav");
                if(!clip)throw new InvalidOperationException("Missing "+namePart);sound.Clips.Add(clip);
            }
        }
        sound.Role=SoundDef.MixRole.Interaction;sound.MixerGroup=SoundMixer.SoundMixerGroup.SFX;
        sound.Priority=100;sound.MaxVoices=6;sound.MinimumInterval=0;sound.Occlusion=true;
        sound.VolumeScale=1;sound.BasePitchInCents=0;sound.PlayCount=1;
        sound.PlaybackType=sound.Clips.Count>1?SoundDef.PlaybackTypes.RandomNotLast:SoundDef.PlaybackTypes.Sequential;
        sound.RepeatInfo=new SoundDef.Repeat {LoopCount=loop?0:1};sound.StartStopInfo=new SoundDef.StartStop();
        sound.PitchAndVolumeInfo=new SoundDef.PitchAndVolume {VolumeMin=db-1,VolumeMax=db};
        sound.DistanceInfo=new SoundDef.Distance {SpatialBlend=1,VolumeDistMin=1,VolumeDistMax=10,
            VolumeRolloffMode=AudioRolloffMode.Linear,LPF_MaxDistance=10,LPF_MinCutoff=1800};
        sound.LowPassFilter=new SoundDef.Filter();sound.HighPassFilter=new SoundDef.Filter();sound.DistortionFilter=new SoundDef.Distortion();
        if(create)AssetDatabase.CreateAsset(sound,path);else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound);return sound;
    }
}
