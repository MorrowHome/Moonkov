using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.UI;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Cursor = UnityEngine.Cursor;

// One local character and one minimal ClientWorld with only the input reader. There is no driver,
// NetworkStreamConnection, account, profile, server or simulated consumption.
[InitializeOnLoad]
public static class BountyClashUIFixture
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Pending = "Moonkov.ClashUIFixture.Pending", OldScene = "Moonkov.ClashUIFixture.StartScene";
    private static World s_World;
    private static Entity s_Player, s_StateEntity;
    private static RaidHUD s_Hud;
    private static DollSingerNetworkPresentation s_Presentation;
    private static DollSingerInput s_Input;
    private static BountyClashClientState s_Clash;
    private static RaidInventoryClientState s_Inventory;
    private static ClientInputReaderSystem s_InputReader;
    private static MoonRaidMap s_OldMap;
    private static PanelSettings s_Panel;
    private static UIDocument s_PlayerHud;
    private static uint s_Sequence, s_SenseSequence;
    private static BountyModeView s_ModePicker;
    public static bool PickerChoice { get; private set; }
    private static int s_Frame;
    private static GlobalGameState s_OldState;
    private static TacticalItemOwnerRpc s_OldOwner;
    private static WeaponRegistry s_OldRegistry;
    private static WeaponManager s_RegistryOwner;
    private static TacticalItemsFeedback s_OldFeedback;
    private static bool s_ShowCloud, s_Running;
    private static readonly List<Renderer> HiddenRenderers=new List<Renderer>();
    private static readonly List<GameObject> Owned = new List<GameObject>(), Disabled = new List<GameObject>();
    public static RaidHUD Hud => s_Hud;
    public static DollSingerInput Input => s_Input;
    public static World ClientWorld => s_World;
    static BountyClashUIFixture() { EditorApplication.playModeStateChanged += Changed; }
    private static void Write(object obj, string field, object value) => obj.GetType().GetField(field, Hidden).SetValue(obj, value);
    private static T Read<T>(object obj, string field) => (T)obj.GetType().GetField(field, Hidden).GetValue(obj);
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Requires idle Editor; never stops another Play session.");
        foreach (var w in World.All) if (w.IsCreated && (w.IsClient() || w.IsServer())) throw new InvalidOperationException("An existing game World must be left untouched.");
        foreach (var scene in EnumerableScenes()) if (scene.isDirty) throw new InvalidOperationException("Scene is dirty; fixture will not save it.");
        SessionState.SetInt("Moonkov.ClashUIFixture.CursorLock",(int)Cursor.lockState);SessionState.SetBool("Moonkov.ClashUIFixture.CursorVisible",Cursor.visible);
        SessionState.SetString(OldScene, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/DollSinger/Scenes/WeaponGestureLab.unity");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    private static IEnumerable<UnityEngine.SceneManagement.Scene> EnumerableScenes()
    { for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++) yield return UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); }
    private static void Changed(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode) { try { Setup(); } catch (Exception e) { Debug.LogException(e); End(); } }
        if (change == PlayModeStateChange.ExitingPlayMode) End();
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            var path = SessionState.GetString(OldScene, "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            SessionState.SetBool(Pending, false);
            Cursor.lockState=(CursorLockMode)SessionState.GetInt("Moonkov.ClashUIFixture.CursorLock",0);Cursor.visible=SessionState.GetBool("Moonkov.ClashUIFixture.CursorVisible",true);
        }
    }
    private static GameObject Own(GameObject go) { go.hideFlags = HideFlags.DontSave; Owned.Add(go); return go; }
    private static void Disable(GameObject go) { if (go.activeSelf && !Disabled.Contains(go)) { Disabled.Add(go); go.SetActive(false); } }
    private static void Setup()
    {
        foreach (var w in World.All) if (w.IsCreated && (w.IsClient() || w.IsServer())) throw new InvalidOperationException("Only one disconnected fixture ClientWorld is permitted.");
        s_OldState=GameSettings.Instance.GameState;s_OldOwner=TacticalItemsFeedback.LocalOwner;s_OldFeedback=Object.FindAnyObjectByType<TacticalItemsFeedback>();
        QuickSlotPlayChecks.InstallMemoryPreferences();
        foreach(var scene in EnumerableScenes())foreach(var root in scene.GetRootGameObjects())Disable(root);
        foreach (var input in Object.FindObjectsByType<DollSingerInput>(FindObjectsSortMode.None)) Disable(input.transform.root.gameObject);
        foreach (var camera in Camera.allCameras) Disable(camera.gameObject);
        foreach(var doc in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))Disable(doc.gameObject);
        var emptyDefault=World.DefaultGameObjectInjectionWorld;
        if(emptyDefault!=null&&emptyDefault.IsCreated&&emptyDefault.Flags==WorldFlags.Game&&emptyDefault.EntityManager.UniversalQuery.CalculateEntityCount()==0&&emptyDefault.Systems.Count==0)
        {World.DefaultGameObjectInjectionWorld=null;emptyDefault.Dispose();}
        s_World = new World("UI fixture / no transport", WorldFlags.GameClient); ClientServerBootstrap.ClientWorlds.Add(s_World); var em = s_World.EntityManager;
        s_InputReader = s_World.GetOrCreateSystemManaged<ClientInputReaderSystem>();
        em.CreateEntity(typeof(ClientInput), typeof(ClientMovementInput));
        var connection = em.CreateEntity(typeof(NetworkId), typeof(NetworkStreamInGame)); em.SetComponentData(connection, new NetworkId { Value=1 });
        var mode = em.CreateEntity(typeof(RaidModeState)); em.SetComponentData(mode, new RaidModeState { Mode=RaidGameMode.BountyClash, RulesVersion=BountyClashRules.Version });
        var graph=InventoryGraph.Create(false); var rig=graph.Equipped("ChestRig"); rig.Code="field_rig";
        foreach (string code in new[]{"medkit","stamina_shot","antidote_shot","frag_bomb","choke_bomb","ammo_box","energy_grenade","cells"})
            if(graph.AddSupply(code, code=="ammo_box"?1:2,rig.Id)!=InventoryError.None) throw new InvalidOperationException("Fixture cannot legally place "+code);
        if(graph.Validate()!=InventoryError.None)throw new InvalidOperationException("Invalid real inventory graph");
        s_Inventory=new RaidInventoryClientState { Graph=graph,RaidId=901,Sequence=1 };
        s_StateEntity=em.CreateEntity(typeof(RaidClientState),typeof(RaidMissionClientState));
        em.SetComponentData(s_StateEntity,new RaidClientState{Snapshot=new RaidSnapshotRpc{RaidId=901,Sequence=1,Phase=RaidPhase.Active,TimeLeft=900,ExtractionRemaining=-1,SaveState=RaidSaveState.Saved}});
        em.AddComponentObject(s_StateEntity,s_Inventory);em.AddComponentObject(s_StateEntity,new RaidDeathBagClientState());
        s_Clash=new BountyClashClientState {Approved=true,ApprovalReceived=true};var clashEntity=em.CreateEntity();em.AddComponentObject(clashEntity,s_Clash);
        s_Player=em.CreateEntity(typeof(PredictedPlayerGhost),typeof(LocalTransform),typeof(GhostOwner),typeof(GhostOwnerIsLocal),typeof(GhostGameObjectGuid));
        em.SetComponentData(s_Player,LocalTransform.FromPosition(float3.zero));em.SetComponentData(s_Player,new GhostOwner{NetworkId=1});
        var weaponManager=WeaponManager.Instance;
        if (!weaponManager) weaponManager=Own(new GameObject("UI fixture weapon registry")).AddComponent<WeaponManager>();
        s_RegistryOwner=weaponManager;s_OldRegistry=weaponManager.WeaponRegistry;
        weaponManager.WeaponRegistry=AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        var ghost=new PredictedPlayerGhost {ClashEnabled=true,ClashCanAct=true,ClashLife=ClashLife.Alive,ClashMaxHealthFraction=1,Oxygen=100,TacticalSprintStamina=100};
        RaidHealth.Initialize(ref ghost);RaidNutrition.Initialize(ref ghost);DollSingerWeapons.SyncEquipment(ref ghost,graph,weaponManager.WeaponRegistry);
        ghost.ControllerState=new FirstPersonController.ControllerState{CurrentRotation=quaternion.identity,MovementType=FirstPersonController.MovementType.Standing,Stance=DollSingerStance.Standing};
        em.SetComponentData(s_Player,ghost);
        var actor=Own(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab")));
        actor.name="Single UI fixture character";actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        var bridge=actor.GetComponent<GhostGameObject>();Write(bridge,"m_World",s_World);Write(bridge,"m_LinkedEntity",s_Player);Write(bridge,"m_Role",MultiplayerRole.ClientOwned);
        foreach(var behaviour in actor.GetComponentsInChildren<GhostMonoBehaviour>(true))behaviour.SetGhostGameObject(bridge);
        em.AddComponentObject(s_Player,new GhostGameObjectLink {LinkedInstance=bridge});
        var playerView=actor.GetComponent<PlayerGhost>();
        var ownedVisuals=Read<GameObject>(playerView,"m_OwnerVisuals");var observerVisuals=Read<GameObject>(playerView,"m_OtherPlayerVisuals");
        if(ownedVisuals)ownedVisuals.SetActive(true);if(observerVisuals)observerVisuals.SetActive(false);
        s_Presentation=actor.GetComponent<DollSingerNetworkPresentation>();s_Presentation.OnGhostLinked();s_Input=s_Presentation.OwnedInput;
        if(!s_Presentation.OwnedCamera.GetComponent<AudioListener>())s_Presentation.OwnedCamera.gameObject.AddComponent<AudioListener>();
        var mapHolder=Own(new GameObject("Inactive UI fixture map data"));mapHolder.SetActive(false);var map=mapHolder.AddComponent<MoonRaidMap>();
        s_OldMap=MoonRaidMap.Active;typeof(MoonRaidMap).GetProperty("Active").SetValue(null,map);
        var floor=Own(GameObject.CreatePrimitive(PrimitiveType.Plane));floor.name="UI fixture neutral floor";floor.transform.localScale=new Vector3(3,1,3);
        var wall=Own(GameObject.CreatePrimitive(PrimitiveType.Cube));wall.name="UI fixture cloud occlusion wall";wall.transform.position=new Vector3(0,1.5f,12);wall.transform.localScale=new Vector3(16,3,.3f);
        var light=Own(new GameObject("UI fixture light")).AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(50,-30,0);
        var hudObject=Own(new GameObject("Real RaidHUD / local UI fixture"));hudObject.SetActive(false);
        s_Panel=Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset"));
        var document=hudObject.AddComponent<UIDocument>();document.panelSettings=s_Panel;document.sortingOrder=20;
        s_Hud=hudObject.AddComponent<RaidHUD>();
        var playerHudObject=Own(new GameObject("Original PlayerHUD / presentation fixture"));
        s_PlayerHud=playerHudObject.AddComponent<UIDocument>();s_PlayerHud.panelSettings=s_Panel;
        s_PlayerHud.visualTreeAsset=AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/PlayerHUD.uxml");
        s_PlayerHud.sortingOrder=0;
        var spectator=Own(new GameObject("Real RespawnScreen fixture camera")).AddComponent<Camera>();spectator.depth=-1;
        var respawnObject=Own(new GameObject("Real RespawnScreen fixture"));respawnObject.SetActive(false);
        var respawnDocument=respawnObject.AddComponent<UIDocument>();respawnDocument.panelSettings=s_Panel;respawnDocument.visualTreeAsset=AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/GameUI/RespawnScreen.uxml");
        var respawn=respawnObject.AddComponent<RespawnScreen>();respawn.RespawnCamera=spectator;respawnObject.SetActive(true);
        GameSettings.Instance.GameState=GlobalGameState.InGame;hudObject.SetActive(true);
        var banner=new Label("UI FIXTURE / LOCAL SNAPSHOTS / NO SERVER"){pickingMode=PickingMode.Ignore};banner.style.position=Position.Absolute;banner.style.left=12;banner.style.bottom=4;banner.style.fontSize=11;banner.style.color=Color.yellow;document.rootVisualElement.Add(banner);
        s_Running=true;s_Frame=-1;State("active");EditorApplication.update+=Tick;
        Debug.Log("Single UI fixture ready: one disconnected ClientWorld, one character, memory-only scope. Call QuickSlotPlayChecks.BeginIsolated(BountyClashUIFixture.Hud,BountyClashUIFixture.Input).");
    }
    private static void Tick()
    {
        if(!s_Running||!EditorApplication.isPlaying||s_World==null||!s_World.IsCreated)return;
        if(s_Frame==Time.frameCount)return;s_Frame=Time.frameCount;
        s_World.SetTime(new TimeData(Time.realtimeSinceStartupAsDouble,Time.deltaTime));if(s_World.EntityManager.IsComponentEnabled<GhostOwnerIsLocal>(s_Player))s_InputReader.Update();
        var em=s_World.EntityManager;
        using(var inputQuery=em.CreateEntityQuery(typeof(ClientInput)))
        {
            var input=inputQuery.GetSingleton<ClientInput>().PlayerInput;var ghost=em.GetComponentData<PredictedPlayerGhost>(s_Player);
            HaloModuleFire.AdvanceTimers(ref ghost,Time.deltaTime);
            if(DollSingerWeapons.TryEquip(ref ghost,input,WeaponManager.Instance.WeaponRegistry))DollSingerWeapons.SyncEquipment(ref ghost,s_Inventory.Graph,WeaponManager.Instance.WeaponRegistry);
            ghost.ControllerState.YawDegrees=input.LookYawPitchDegrees.x;ghost.ControllerState.PitchDegrees=input.LookYawPitchDegrees.y;
            ghost.ControllerState.CurrentRotation=quaternion.EulerXYZ(0,math.radians(input.LookYawPitchDegrees.x),0);
            ghost.ControllerState.Aiming=input.Aim;ghost.AimPoint=input.AimPoint;ghost.ClashDarkSight=RaidHUD.DarkSightRequested;
            em.SetComponentData(s_Player,ghost);
            var local=em.GetComponentData<LocalTransform>(s_Player);local.Rotation=ghost.ControllerState.CurrentRotation;em.SetComponentData(s_Player,local);s_Presentation.transform.rotation=ghost.ControllerState.CurrentRotation;
        }
        if(s_PlayerHud)
        {
            var visual=s_PlayerHud.rootVisualElement;var g=em.GetComponentData<PredictedPlayerGhost>(s_Player);
            visual.style.display=RaidHUD.InventoryOpen?DisplayStyle.None:DisplayStyle.Flex;
            visual.Q<ProgressBar>("player-health-bar").highValue=g.MaxHealth>0?g.MaxHealth:100;
            visual.Q<ProgressBar>("player-health-bar").value=g.CurrentHealth;
            visual.Q<Label>("health-state").text=MoonkovLocalization.Text("HEALTH");
            visual.Q<Label>("health-value").text=g.CurrentHealth.ToString("0")+"/"+(g.MaxHealth>0?g.MaxHealth:100).ToString("0");
            visual.Q<Label>("ammo-label").text=g.CurrentAmmo+" / "+(WeaponManager.Instance.WeaponRegistry.GetWeaponData(g.EquippedWeaponID,g.EquippedModules)?.MagazineSize??0);
            visual.Q<Label>("reloading-label").style.display=DisplayStyle.None;
        }
        if(RaidHUD.DarkSightRequested){bool boost=s_ShowCloud&&UnityEngine.InputSystem.Mouse.current!=null&&UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;var sense=new ClashSenseRpc{MatchId="local-ui-fixture",Epoch=1,Sequence=s_SenseSequence=System.Math.Max(s_SenseSequence,s_Clash.Snapshot.Sequence)+1,Active=boost,Presence=s_Clash.Snapshot.IsCarrier,Remaining=s_Clash.Snapshot.SenseRemaining,ContactCount=boost?1:0};s_Clash.Receive(sense,Time.realtimeSinceStartupAsDouble);if(boost)s_Clash.Receive(new ClashSenseContactRpc{MatchId=sense.MatchId,Epoch=1,Sequence=sense.Sequence,Index=0,Count=1,Position=new float3(3,1.3f,20),UncertaintyRadius=3,ValidFor=.35f},Time.realtimeSinceStartupAsDouble);}
    }
    public static void State(string name)
    {
        if(!s_Running)throw new InvalidOperationException("Fixture is not running");
        s_World.EntityManager.SetComponentEnabled<GhostOwnerIsLocal>(s_Player,true);
        foreach(var renderer in HiddenRenderers)if(renderer)renderer.enabled=true;HiddenRenderers.Clear();
        s_Presentation.enabled=true;s_Input.enabled=true;if(s_Presentation.OwnedCamera)s_Presentation.OwnedCamera.enabled=true;
        var em=s_World.EntityManager;var ghost=em.GetComponentData<PredictedPlayerGhost>(s_Player);
        ghost.ClashLife=name=="finished"?ClashLife.Extracted:name=="downed"?ClashLife.Downed:ClashLife.Alive;ghost.ClashCanAct=ghost.ClashLife==ClashLife.Alive;ghost.ClashDarkSight=false;
        ghost.ClashMaxHealthFraction=name=="revived"?.75f:1;RaidHealth.Initialize(ref ghost);if(ghost.ClashLife==ClashLife.Downed)ghost.CurrentHealth=0;
        ghost.ControllerState.Stance=ghost.ClashLife==ClashLife.Downed?DollSingerStance.Prone:DollSingerStance.Standing;em.SetComponentData(s_Player,ghost);
        s_ShowCloud=name=="cloud";
        var snapshot=new ClashSnapshotRpc{MatchId="local-ui-fixture",Epoch=1,Sequence=++s_Sequence,RulesVersion=1,LocalParticipantId=1,LocalTeamId=0,TeamCapacity=3,PlayerCount=3,TeamCount=2,Connected=true,IsLeader=true,InviteCode="UI-TEST",CanStartNext=name=="finished",Phase=name=="finished"?ClashPhase.Finished:name=="lobby"?ClashPhase.Lobby:name=="preparing"?ClashPhase.Preparing:ClashPhase.Active,Life=ghost.ClashLife,Remaining=865,PrepareRemaining=9,BoundaryRemaining=name=="boundary"?17:-1,SenseRemaining=name=="empty"?0:3,IsCarrier=name=="cloud"||name=="empty",Bounty=name=="cloud"||name=="empty"?ClashBountyState.Carried:ClashBountyState.Ground,BanishProgress=1,ArenaRadius=130,TargetPosition=new float3(0,0,20),BountyPosition=new float3(3,0,20),ExitPositionA=new float3(-30,0,90),ExitPositionB=new float3(30,0,-90),ExitRemainingA=name=="blocked"?17:-1,ExitRemainingB=-1,ExitBlockA=name=="blocked"?ClashExitBlock.Enemy:ClashExitBlock.None};
        s_Clash.Receive(new ClashParticipantRpc{MatchId=snapshot.MatchId,Epoch=1,Sequence=snapshot.Sequence,ParticipantId=1,TeamId=0,Name="UI Operator",IsLocal=true,OwnTeam=true,Connected=true,Life=ghost.ClashLife,MaxHealthFraction=ghost.ClashMaxHealthFraction});
        s_Clash.Receive(new ClashParticipantRpc{MatchId=snapshot.MatchId,Epoch=1,Sequence=snapshot.Sequence,ParticipantId=2,TeamId=0,Name="Wingmate A",OwnTeam=true,Connected=true,Life=ClashLife.Alive,Position=new float3(5,0,10),MaxHealthFraction=1});
        s_Clash.Receive(new ClashParticipantRpc{MatchId=snapshot.MatchId,Epoch=1,Sequence=snapshot.Sequence,ParticipantId=3,TeamId=0,Name="Wingmate B",OwnTeam=true,Connected=true,Life=ClashLife.Downed,Position=new float3(-6,0,15),MaxHealthFraction=1});
        if(name=="banishing"){snapshot.Bounty=ClashBountyState.Banishing;snapshot.BanishProgress=.47f;}
        s_Clash.Receive(snapshot,Time.realtimeSinceStartupAsDouble);
    }
    public static void OpenPicker(bool offline)
    {
        Inventory(true);
        if(s_ModePicker==null)s_ModePicker=new BountyModeView(s_Hud.GetComponent<UIDocument>().rootVisualElement,value=>PickerChoice=value);
        s_ModePicker.Present(false,offline);
        typeof(BountyModeView).GetMethod("Open",Hidden).Invoke(s_ModePicker,null);
    }
    public static void ClosePicker(){s_ModePicker?.Dispose();s_ModePicker=null;Inventory(false);}
    public static void MissingCharacter()
    {
        Inventory(false);s_ShowCloud=false;
        var camera=s_Presentation.OwnedCamera;if(camera)camera.enabled=false;s_Input.enabled=false;s_Presentation.enabled=false;
        foreach(var renderer in s_Presentation.GetComponentsInChildren<Renderer>())if(renderer.enabled){HiddenRenderers.Add(renderer);renderer.enabled=false;}
        s_World.EntityManager.SetComponentEnabled<GhostOwnerIsLocal>(s_Player,false);
        var value=s_Clash.Snapshot;value.Sequence=++s_Sequence;value.Phase=ClashPhase.Finished;value.Life=ClashLife.Extracted;value.CanStartNext=true;s_Clash.Receive(value,Time.realtimeSinceStartupAsDouble);
    }
    public static void Inventory(bool open)=>typeof(RaidHUD).GetMethod("SetInventory",Hidden).Invoke(s_Hud,new object[]{open});
    public static string ItemId(string code)=>s_Inventory.Graph.Items.Find(i=>i.Code==code)?.Id;
    public static void AcknowledgeFixture()
    {s_Inventory.RequestId=Read<uint>(s_Hud,"m_InventoryRequestId");s_Inventory.Sequence++;s_Inventory.MedicalItemId=null;s_Inventory.MedicalRemaining=0;TacticalItemsFeedback.SetOwner(new TacticalItemOwnerRpc{RaidId=901,RequestId=s_Inventory.RequestId});}
    public static void Fatigue(float stamina,bool exhausted,float boost=0)
    {
        TacticalItemsFeedback.SetOwner(new TacticalItemOwnerRpc{RaidId=901,SprintTracked=true,SprintStamina=stamina,SprintExhausted=exhausted,StaminaSeconds=boost});
        var ghost=s_World.EntityManager.GetComponentData<PredictedPlayerGhost>(s_Player);ghost.TacticalSprintStamina=stamina;ghost.TacticalSprintExhausted=exhausted;s_World.EntityManager.SetComponentData(s_Player,ghost);
    }
    public static void ClearIntents()
    {using var q=s_World.EntityManager.CreateEntityQuery(typeof(SendRpcCommandRequest));s_World.EntityManager.DestroyEntity(q);}
    public static string Status()=>"fixture="+s_Running+"; worlds="+(s_World?.IsCreated==true?1:0)+"; frame="+Time.frameCount+"; input="+(s_Input?s_Input.CanReadPlayerInput:false)+"; camera="+(s_Presentation?.OwnedCamera?s_Presentation.OwnedCamera.name:"none")+"; phase="+s_Clash?.Snapshot.Phase+"; life="+s_Clash?.Snapshot.Life;
    public static void End()
    {
        s_Running=false;EditorApplication.update-=Tick;s_ModePicker?.Dispose();s_ModePicker=null;
        if(s_Hud)s_Hud.enabled=false;QuickSlotPlayChecks.End();
        if(!s_OldFeedback){var feedback=Object.FindAnyObjectByType<TacticalItemsFeedback>();if(feedback)Own(feedback.gameObject);}if(s_Presentation)s_Presentation.OnGhostPreDestroy();
        foreach(var go in Owned)if(go){var bridge=go.GetComponent<GhostGameObject>();if(bridge)bridge.GhostPreDestroyCalled=true;Object.DestroyImmediate(go);}Owned.Clear();
        if(s_World!=null){ClientServerBootstrap.ClientWorlds.Remove(s_World);if(s_World.IsCreated)s_World.Dispose();}s_World=null;HiddenRenderers.Clear();
        TacticalItemsFeedback.SetOwner(s_OldOwner);if(GameSettings.Instance!=null)GameSettings.Instance.GameState=s_OldState;
        if(s_RegistryOwner)s_RegistryOwner.WeaponRegistry=s_OldRegistry;s_RegistryOwner=null;s_OldRegistry=null;
        typeof(MoonRaidMap).GetProperty("Active").SetValue(null,s_OldMap);s_OldMap=null;
        foreach(var go in Disabled)if(go)go.SetActive(true);Disabled.Clear();if(s_Panel)Object.DestroyImmediate(s_Panel);s_Panel=null;
        s_PlayerHud=null;s_Hud=null;s_Input=null;s_Presentation=null;s_Inventory=null;s_Clash=null;
    }
    public static void Finish(){End();EditorApplication.ExitPlaymode();}
}
