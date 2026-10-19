using System;
using System.IO;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Missions;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Real presenter and native runtime panel; private fixture World has no transport or profile service.
[InitializeOnLoad]
public static class MissionUIReview
{
    private const string Pending = "Moonkov.MissionReview.Pending", Previous = "Moonkov.MissionReview.Previous";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Cases = { "hud", "configuration", "archive-compact", "result" };
    private static GameObject s_Object, s_SiteObject;
    private static RaidHUD s_Hud;
    private static VisualElement s_Root;
    private static World s_World;
    private static PanelSettings s_Settings;
    private static RenderTexture s_Target;
    private static int s_Case;
    private static double s_Ready;
    public static string Status { get; private set; } = "Not started";
    private static string Output => Path.GetFullPath("Library/BuildValidation/Missions/UI");
    static MissionUIReview() { EditorApplication.playModeStateChanged += State; }
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || RaidMissionAuthoring.HasLiveWorlds()) throw new InvalidOperationException("Requires idle Editor.");
        for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve unsaved scenes.");
        SessionState.SetString(Previous, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(Pending, true); Status = "Starting"; EditorApplication.EnterPlaymode();
    }
    private static void State(PlayModeStateChange value)
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (value == PlayModeStateChange.EnteredPlayMode) Setup();
        if (value == PlayModeStateChange.ExitingPlayMode) Cleanup();
        if (value == PlayModeStateChange.EnteredEditMode)
        {
            string previous = SessionState.GetString(Previous, "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
            SessionState.SetBool(Pending, false);
        }
    }
    private static void Set(string name, object value) => typeof(RaidHUD).GetField(name, Private).SetValue(s_Hud, value);
    private static object Call(string name, params object[] values) => typeof(RaidHUD).GetMethod(name, Private).Invoke(s_Hud, values);
    private static void Require(bool value, string description) { if (!value) throw new InvalidOperationException(description); }
    private static void Submit(Button button) { button.Focus(); using (var evt = NavigationSubmitEvent.GetPooled()) button.SendEvent(evt); }
    private static void Setup()
    {
        try
        {
            Status = "Running"; Directory.CreateDirectory(Output);
            s_Object = new GameObject("Mission UI / isolated local review"); s_Object.SetActive(false);
            s_Settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset"));
            s_Settings.scaleMode = PanelScaleMode.ConstantPixelSize; s_Settings.scale = 1;
            s_Target = new RenderTexture(1600, 900, 0, RenderTextureFormat.ARGB32); s_Target.Create();
            s_Settings.targetTexture = s_Target; s_Settings.clearColor = true; s_Settings.colorClearValue = new Color(.04f, .05f, .07f);
            var document = s_Object.AddComponent<UIDocument>(); document.panelSettings = s_Settings;
            s_Hud = s_Object.AddComponent<RaidHUD>(); s_Hud.enabled = false; s_Object.SetActive(true);
            Call("OnEnable"); // presenter stays disabled: no connection lookup or normal raid Update
            s_Root = document.rootVisualElement; s_Root.style.width = 1600; s_Root.style.height = 900;
            s_World = new World("Mission UI fixture / no network", WorldFlags.Game);
            var manager = s_World.EntityManager;
            manager.CreateEntity(typeof(LocalTransform)); manager.CreateEntity(typeof(NetworkId), typeof(NetworkStreamInGame));
            Set("m_World", s_World);
            Set("m_StateQuery", manager.CreateEntityQuery(typeof(RaidClientState)));
            Set("m_PlayerQuery", manager.CreateEntityQuery(typeof(LocalTransform)));
            Set("m_ConnectionQuery", manager.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame)));
            Set("m_InventoryQuery", manager.CreateEntityQuery(typeof(RaidInventoryClientState)));
            s_SiteObject = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RaidMissionAuthoring.Folder + "/MC00MissionSite.prefab"));
            var notice = new Label("NATIVE UNITY UI · LOCAL FIXTURE · NO PROFILE WRITES") { pickingMode = PickingMode.Ignore };
            notice.style.position = Position.Absolute; notice.style.bottom = 4; notice.style.left = 8; notice.style.color = Color.white; s_Root.Add(notice);
            s_Case = 0; Build(); EditorApplication.update += Tick;
        }
        catch (Exception ex) { Finish(ex); }
    }
    private static void Build()
    {
        bool compact = Cases[s_Case].Contains("compact"), archive = compact;
        s_Root.style.width = compact ? 1280 : 1600; s_Root.style.height = compact ? 720 : 900;
        s_Root.EnableInClassList("mission-compact", compact);
        s_Root.Q("raidResult").style.display = DisplayStyle.None;
        s_Root.Q("raidStatus").style.display = DisplayStyle.Flex;
        s_Root.Q("raidLoadout").style.display = DisplayStyle.None;
        s_Root.Q("raidInventory").style.display = DisplayStyle.None;
        var definition = archive ? MissionCatalog.Archive() : MissionCatalog.Mooncore();
        var snapshot = new RaidMissionSnapshotRpc { RaidId = 1, Epoch = 1, OperationId = "ui-review", DefinitionIndex = archive ? 1 : 0,
            Sequence = 1, Phase = MissionPhase.Active, Remaining = 379, BackendSupported = true, CompletedMask = archive ? 3u : 1u };
        Set("m_Snapshot", new RaidSnapshotRpc { RaidId = 1, Phase = RaidPhase.Active }); Set("m_MissionSnapshot", snapshot);
        Call("SetMissionDetails", false); Call("RenderMission", definition, snapshot, true);
        s_Root.Q("missionHud").style.display = DisplayStyle.Flex;
        if (s_Case == 1) { Call("OpenMissionConfiguration", definition.Objectives[0], 0); Call("RenderMission", definition, snapshot, true); }
        if (archive) Call("SetMissionDetails", true);
        if (s_Case == 3)
        {
            s_Root.Q("missionHud").style.display = DisplayStyle.None; s_Root.Q("raidResult").style.display = DisplayStyle.Flex;
            s_Root.Q<Label>("raidResultText").text = "已成功撤离 / 任务物品 2 件";
            Set("m_Snapshot", new RaidSnapshotRpc { RaidId = 1, Phase = RaidPhase.Extracted, SaveState = RaidSaveState.Saved });
            snapshot.RecoveredArtifacts = 2; snapshot.MissionFirstsSaved = 3; snapshot.SaveState = RaidSaveState.Saved;
            Call("RenderMissionResult", snapshot);
        }
        s_Ready = EditorApplication.timeSinceStartup + 2;
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < s_Ready) return;
        try
        {
            if (s_Case == 1)
            {
                Require(s_Root.Q<Label>("missionClue").text.Contains("A 支路烧损"), "Snapshot overwrote configuration clue");
                Require(s_Root.Q("missionControls").childCount == 3, "Circuit choices missing");
                var controls = s_Root.Q("missionControls");
                Require(controls.worldBound.yMax < s_Root.Q("missionClose").worldBound.yMin, "Configuration overlaps footer");
            }
            var texture = new Texture2D(Cases[s_Case].Contains("compact") ? 1280 : 1600, Cases[s_Case].Contains("compact") ? 720 : 900, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try { RenderTexture.active = s_Target; texture.ReadPixels(new Rect(0, 900 - texture.height, texture.width, texture.height), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(Output, Cases[s_Case] + ".png"), texture.EncodeToPNG()); }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
            if (s_Case == 0)
            {
                Submit(s_Root.Q<Button>("missionPin")); Require(s_Root.Q("missionHud").style.display == DisplayStyle.None, "Unpin failed");
                using var q = s_World.EntityManager.CreateEntityQuery(typeof(RaidMissionActionRpc)); Require(q.IsEmptyIgnoreFilter, "Tracking must not send server action");
                Submit(s_Root.Q<Button>("missionPin"));
            }
            if (s_Case == 1)
            {
                Submit(s_Root.Q("missionControls").Q<Button>());
                using var q = s_World.EntityManager.CreateEntityQuery(typeof(RaidMissionActionRpc));
                Require(q.GetSingleton<RaidMissionActionRpc>().Intent == MissionIntent.Configure, "Circuit button did not enqueue real intent");
                Require(s_Root.Q("missionDetails").style.display == DisplayStyle.None, "Circuit submit did not close modal");
            }
            if (++s_Case < Cases.Length) Build(); else Finish(null);
        }
        catch (Exception ex) { Finish(ex); }
    }
    private static void Finish(Exception error)
    {
        Status = error == null ? "PASS: four native UI states; pin local-only; circuit callback; clue retention; result and compact layout." : "FAIL: " + error.GetBaseException().Message;
        Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "review.txt"), Status + "\nPrivate fixture World; no profile writes; not a formal raid movement/Host/client acceptance.");
        if (error != null) Debug.LogException(error); else Debug.Log(Status);
        Cleanup(); EditorApplication.ExitPlaymode();
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (s_Hud) Call("OnDisable");
        if (s_Object) UnityEngine.Object.DestroyImmediate(s_Object); s_Hud = null;
        if (s_SiteObject) UnityEngine.Object.DestroyImmediate(s_SiteObject);
        if (s_World != null && s_World.IsCreated) s_World.Dispose(); s_World = null;
        if (s_Settings) UnityEngine.Object.DestroyImmediate(s_Settings);
        if (s_Target) { s_Target.Release(); UnityEngine.Object.DestroyImmediate(s_Target); }
    }
}
