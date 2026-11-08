using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Unity.MP_FPS;
using UnityEngine;

public static class LiftPresenceReview
{
    public static string RunIsolated()
    {
        if(!Application.isPlaying||GameManager.Instance)throw new InvalidOperationException("Use isolated empty Play scene");
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var property=typeof(GameManager).GetProperty("Instance",BindingFlags.Public|BindingFlags.Static);
        var holder=new GameObject("Lift review manager");holder.SetActive(false);
        var manager=holder.AddComponent<GameManager>();
        var camera=new GameObject("Lift review listener");camera.tag="MainCamera";camera.AddComponent<Camera>();camera.AddComponent<AudioListener>();
        var sources=new GameObject("Lift review pool");
        var pool=(SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system=new SoundSystem();
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder",hidden).SetValue(pool,sources);
            pool.SoundGameObjectList=new List<SoundGameObject>();
            for(int i=0;i<8;i++)pool.SoundGameObjectList.Add(pool.Create());
            for(int i=0;i<8;i++)pool.SoundGameObjectList[i].NextAvailable=pool.SoundGameObjectList[(i+1)%8];
            pool.NextSoundGameObject=pool.SoundGameObjectList[0];pool.AvailableSoundObjectCount=8;
            system.Init(camera.transform,8,pool,MoonkovAudio.Library.LiftMotor.UnityAudioMixer);
            property.SetValue(null,manager);typeof(GameManager).GetField("m_SoundSystem",hidden).SetValue(manager,system);
            return Run();
        }
        finally
        {
            system.KillAll();property.SetValue(null,null);
            UnityEngine.Object.DestroyImmediate(holder);UnityEngine.Object.DestroyImmediate(camera);UnityEngine.Object.DestroyImmediate(sources);
        }
    }
    public static string Run()
    {
        if(!Application.isPlaying||!GameManager.Instance||!Camera.main)throw new InvalidOperationException("Use existing client Play");
        var system=(SoundSystem)GameManager.Instance.SoundSystem;
        var cabin=new GameObject("Lift audio validation cabin");cabin.transform.position=Camera.main.transform.position+Vector3.forward;
        var audio=new MoonkovLiftAudio();var library=MoonkovAudio.Library;
        int first=system.m_SoundEmitterActiveList.Select(e=>e.SeqId).DefaultIfEmpty(-1).Max(),checks=0;
        int Count(SoundDef bank)=>system.m_SoundEmitterActiveList.Count(e=>e.SeqId>first&&e.Active&&e.Definition==bank);
        void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
        try
        {
            float t=Time.unscaledTime;
            audio.Update(cabin.transform,0,1,t);Check(Count(library.LiftMotor)==0&&Count(library.LiftGate)==0,"Idle lift is not silent");
            audio.Update(cabin.transform,0,.5f,t+.4f);Check(Count(library.LiftGate)==1&&Count(library.LiftMotor)==0,"Gate motion did not start its motor");
            audio.Update(cabin.transform,.1f,0,t+.8f);Check(Count(library.LiftMotor)==1&&Count(library.LiftStart)==1,"Travel did not start drive and brake release");
            audio.Update(cabin.transform,.2f,0,t+1.2f);Check(Count(library.LiftMotor)==1&&Count(library.LiftStart)==1,"Continuing travel allocated duplicate motors");
            foreach(var emitter in system.m_SoundEmitterActiveList.Where(e=>e.SeqId>first&&e.Active&&e.Definition==library.LiftMotor))
            {
                var source=emitter.ActiveSoundGameObjects[0].m_AudioSource;
                Check(source.isPlaying&&source.loop&&source.volume>0&&source.outputAudioMixerGroup.name=="SFX","Drive AudioSource did not play through SFX");
                Check(source.transform.IsChildOf(cabin.transform),"Drive did not follow the cabin");
            }
            audio.Update(cabin.transform,.2f,0,t+1.6f);Check(Count(library.LiftStop)==1,"Stopping travel missed docking contact");
            audio.Update(cabin.transform,.2f,0,t+2);Check(Count(library.LiftStop)==1,"Stationary snapshots repeated docking contact");
            audio.Stop();return "PASS "+checks+" actual pooled gate/drive/start/stop, routing and duplicate checks";
        }
        finally
        {
            audio.Stop();
            foreach(var emitter in system.m_SoundEmitterActiveList.Where(e=>e.SeqId>first&&e.Definition.name.StartsWith("Lift")).ToArray())
                system.Kill(new SoundSystem.SoundInfo {SoundDefinition=emitter.Definition,Handle=new SoundHandle(emitter)});
            UnityEngine.Object.DestroyImmediate(cabin);
        }
    }
}
