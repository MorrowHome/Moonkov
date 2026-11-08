using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

// Runs actual single-player commands with a disposable profile; never opens a remote connection.
public static class GameplayPresenceReview
{
    static readonly Dictionary<string,int> counts=new Dictionary<string,int>();
    static readonly HashSet<int> sequences=new HashSet<int>();
    static ClientInputReaderSystem reader;
    static bool readerEnabled, background;
    static World world;
    static EntityQuery inputs, movements;
    static DollSingerNetworkPresentation player;
    static double deadline;
    static float began,yaw,distance,capturedSeconds;
    static Vector3 previous;
    static int frame=-1,maxSources;
    static bool driving;
    static string result="not started";
    public static string Begin()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.isDirty||scene.name!="MainMenu")throw new InvalidOperationException("Use clean MainMenu Edit scene");
        SessionState.SetBool("PresenceReview.Mute",EditorUtility.audioMasterMute);
        SessionState.SetBool("PresenceReview.HadName",PlayerPrefs.HasKey("PlayerName"));
        SessionState.SetString("PresenceReview.Name",PlayerPrefs.GetString("PlayerName"));
        EditorApplication.isPlaying=true;return "Entering MainMenu Play for isolated single-player profile";
    }
    public static string Start()
    {
        if(!Application.isPlaying||!GameManager.Instance||PlayerProfileClient.IsOffline||PlayerProfileClient.IsLan)
            throw new InvalidOperationException("Use fresh MainMenu Play without an open profile");
        background=Application.runInBackground;Application.runInBackground=true;EditorUtility.audioMasterMute=false;
        Directory.CreateDirectory("LocalData/RaidPresenceReview");
        string path=Path.GetFullPath("LocalData/RaidPresenceReview/profile-"+Guid.NewGuid().ToString("N")+".json");
        var store=new OfflineProfileStore(path);
        typeof(PlayerProfileClient).GetField("s_Offline",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,store);
        var refresh=typeof(PlayerProfileClient).GetMethod("RefreshOffline",BindingFlags.Static|BindingFlags.NonPublic);
        refresh.Invoke(null,null);
        foreach(var spec in new[]{new[]{"rifle","equipment","Primary"},new[]{"cells","pockets","1"},new[]{"medkit","pockets","2"}})
        {
            var graph=PlayerProfileClient.Inventory;var item=graph.Items.FirstOrDefault(x=>x.Code==spec[0]);
            if(item==null||item.Parent==spec[1]&&item.Region==spec[2])continue;
            store.Move(new Unity.MP_FPS.Inventory.InventoryCommand {Operation=Unity.MP_FPS.Inventory.InventoryOperation.Move,
                ExpectedVersion=graph.Version,ItemId=item.Id,Parent=spec[1],Region=spec[2]});refresh.Invoke(null,null);
        }
        store.UpdateTutorial(skip:true);refresh.Invoke(null,null);
        typeof(GameSettings).GetField("m_RequestedMode",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(GameSettings.Instance,RaidGameMode.Expedition);
        counts.Clear();sequences.Clear();driving=false;frame=-1;distance=0;capturedSeconds=0;maxSources=0;result="loading";
        deadline=EditorApplication.timeSinceStartup+180;
        EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;AssemblyReloadEvents.beforeAssemblyReload+=Stop;
        GameManager.Instance.StartGameAsync(CreationType.Offline);
        return "Started temporary single-player raid";
    }
    static void Tick()
    {
        if(!Application.isPlaying)return;
        try
        {
            if(EditorApplication.timeSinceStartup>deadline)throw new InvalidOperationException("Gameplay capture timed out");
            if(!driving)
            {
                player=UnityEngine.Object.FindObjectsByType<DollSingerNetworkPresentation>().FirstOrDefault(p=>p.OwnedInput);
                if(!player||!player.GhostGameObject.GhostEntityExists()||!Camera.main||Camera.main.name!="DollSinger Camera")return;
                world=player.World;reader=world.GetExistingSystemManaged<ClientInputReaderSystem>();readerEnabled=reader.Enabled;reader.Enabled=false;
                inputs=world.EntityManager.CreateEntityQuery(typeof(ClientInput));movements=world.EntityManager.CreateEntityQuery(typeof(ClientMovementInput));
                began=Time.unscaledTime;yaw=Camera.main.transform.eulerAngles.y;previous=player.transform.position;driving=true;result="recording";
                if(SoundMixer.soundMute!=null)SoundMixer.soundMute.Value="0";
            }
            if(frame==Time.frameCount)return;frame=Time.frameCount;
            var sounds=GameManager.Instance.SoundSystem as SoundSystem;
            maxSources=Math.Max(maxSources,sounds.m_SoundEmitterActiveList.Count(x=>x.Active));
            foreach(var emitter in sounds.m_SoundEmitterActiveList)
                if(emitter.Active&&sequences.Add(emitter.SeqId))
                {string name=emitter.Definition.name;counts[name]=counts.TryGetValue(name,out int n)?n+1:1;}
            distance+=Vector3.Distance(player.transform.position,previous);previous=player.transform.position;
            float t=Time.unscaledTime-began;
            if(t>=32){result="captured";Stop();return;}
            var look=new float2(yaw+Mathf.Sin(t*.25f)*40,0);player.UpdateOwnedLook(ref look,out _,true);
            var input=new PlayerInput {MoveInput=new float2(0,t<28?.8f:0),LookYawPitchDegrees=look,HaloLightingSettings=player.OwnedInput.LightingSettings};
            input.AimPoint=player.CaptureAimPoint(look,100);
            input.SetFlag(PlayerInput.InputFlag.Sprint,t>=8&&t<17);
            input.SetFlag(PlayerInput.InputFlag.Jump,t>=5&&t<5.15f||t>=14&&t<14.15f);
            input.SetFlag(PlayerInput.InputFlag.FireHeld,t>=20&&t<21);
            input.SetFlag(PlayerInput.InputFlag.Shoot,t>=20&&t<21);
            input.SetFlag(PlayerInput.InputFlag.Reload,t>=23&&t<23.15f);
            using(var entities=inputs.ToEntityArray(Unity.Collections.Allocator.Temp))foreach(var e in entities){var value=new ClientInput();value.SetInput(0,input);world.EntityManager.SetComponentData(e,value);}
            using(var entities=movements.ToEntityArray(Unity.Collections.Allocator.Temp))foreach(var e in entities){var value=new ClientMovementInput();value.SetInput(0,input);world.EntityManager.SetComponentData(e,value);}
        }
        catch(Exception error){result="FAIL "+error.Message;Stop();}
    }
    static void Changed(PlayModeStateChange change){if(change==PlayModeStateChange.ExitingPlayMode)Stop();}
    public static object Status()=>new{result,seconds=driving?Time.unscaledTime-began:capturedSeconds,distance,maxSources,counts};
    public static void Stop()
    {
        if(driving)capturedSeconds=Time.unscaledTime-began;driving=false;
        EditorApplication.update-=Tick;EditorApplication.playModeStateChanged-=Changed;AssemblyReloadEvents.beforeAssemblyReload-=Stop;
        if(reader!=null&&world!=null&&world.IsCreated){reader.Enabled=readerEnabled;inputs.Dispose();movements.Dispose();}reader=null;
        Application.runInBackground=background;
        var lines=new List<string>{result,"distance="+distance,"maxSources="+maxSources};
        foreach(var pair in counts)lines.Add(pair.Key+"="+pair.Value);
        Directory.CreateDirectory("LocalData/RaidPresenceReview");File.WriteAllLines("LocalData/RaidPresenceReview/gameplay.txt",lines);
    }
    public static string Finish()
    {
        Stop();GameManager.Instance.ReturnToMainMenuAsync();return "Returning from temporary raid";
    }
    public static string Restore()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play first");
        EditorUtility.audioMasterMute=SessionState.GetBool("PresenceReview.Mute",true);
        if(SessionState.GetBool("PresenceReview.HadName",false))PlayerPrefs.SetString("PlayerName",SessionState.GetString("PresenceReview.Name",""));
        else PlayerPrefs.DeleteKey("PlayerName");
        SessionState.EraseString("PresenceReview.Name");return "Restored editor audio and player-name preference";
    }
}
