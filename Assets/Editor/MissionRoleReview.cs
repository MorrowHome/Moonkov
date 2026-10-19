using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Missions;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Operator aid for actual Play movement. Never edits a transform, clock, objective or inventory.
[InitializeOnLoad]
public static class MissionRoleReview
{
    static MissionRoleReview()
    {
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) Release(); };
    }
    private static Keyboard s_Keyboard;
    private static Mouse s_Mouse;
    private static Vector3[] s_Path;
    private static int s_Corner;
    private static Vector3? s_Aim;
    private static Key s_Hold;
    private static double s_Until, s_Started, s_ProgressAt;
    private static Vector3 s_LastProgress;
    private static bool s_WaitMenu, s_HadName;
    private static string s_Name, s_File;
    private static uint s_Seed;
    public static string Status { get; private set; } = "Not started";
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private static string Output => Path.GetFullPath("Library/BuildValidation/Missions/Role");
    public static void Begin(uint seed = 37)
    {
        if (!EditorApplication.isPlaying || s_Keyboard != null || PlayerProfileClient.IsOffline) throw new InvalidOperationException("Begin in fresh MainMenu Play, no open profile.");
        Directory.CreateDirectory(Output); s_File = Path.Combine(Output, "profile-" + Guid.NewGuid().ToString("N") + ".json");
        s_HadName = PlayerPrefs.HasKey("PlayerName"); s_Name = PlayerPrefs.GetString("PlayerName"); s_Seed = seed;
        typeof(PlayerProfileClient).GetField("s_Offline", StaticPrivate).SetValue(null, new OfflineProfileStore(s_File));
        PlayerProfileClient.ValidateAsync("", CancellationToken.None).GetAwaiter().GetResult();
        s_Keyboard = InputSystem.AddDevice<Keyboard>("Mission review keyboard"); s_Mouse = InputSystem.AddDevice<Mouse>("Mission review mouse");
        s_Keyboard.MakeCurrent(); s_Mouse.MakeCurrent();
        SceneManager.sceneLoaded += Loaded; InputSystem.onBeforeUpdate += Input;
        EditorApplication.update += Tick; s_WaitMenu = true; s_Started = Time.realtimeSinceStartupAsDouble;
        Status = "Waiting for normal menu startup"; Record(Status);
    }
    private static void Loaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != GameManager.GameSceneName) return;
        var site = RaidMissionSite.Active;
        if (site) typeof(RaidMissionSite).GetField("m_Seed", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(site, (int)s_Seed);
        Record("Formal scene loaded; isolated author seed=" + s_Seed + "; all gameplay timers unchanged.");
    }
    private static void Tick()
    {
        if (!s_WaitMenu || !GameSettings.Instance.MainMenuSceneLoaded) return;
        s_WaitMenu = false; GameManager.Instance.StartGameAsync(CreationType.Offline);
        Status = "Loading normal single-player raid (real local server/client)"; Record(Status);
    }
    public static World Client => ClientServerBootstrap.ClientWorld;
    private static bool Player(out Vector3 position, out PredictedPlayerGhost ghost)
    {
        position = default; ghost = default; var world = Client;
        if (world == null || !world.IsCreated) return false;
        using var query = world.EntityManager.CreateEntityQuery(typeof(LocalTransform), typeof(PredictedPlayerGhost), typeof(GhostOwnerIsLocal));
        query.CompleteDependency(); if (!query.HasSingleton<PredictedPlayerGhost>()) return false;
        ghost = query.GetSingleton<PredictedPlayerGhost>(); position = query.GetSingleton<LocalTransform>().Position; return true;
    }
    public static void Walk(string anchor)
    {
        if (!Player(out var p, out _)) throw new InvalidOperationException("No actual owned player.");
        Vector3 target = anchor == "exit" ? MoonRaidMap.Active.ExtractionPosition : RaidMissionSite.Active.Anchor(anchor).Approach;
        if (!NavMesh.SamplePosition(p, out var start, 3, NavMesh.AllAreas) || !NavMesh.SamplePosition(target, out var end, 3, NavMesh.AllAreas)) throw new InvalidOperationException("No navigation at actual player or destination.");
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) throw new InvalidOperationException("Actual player route incomplete.");
        s_Path = path.corners; s_Corner = 0; s_Aim = null; s_ProgressAt = Time.realtimeSinceStartupAsDouble; s_LastProgress = p;
        Status = "Walking " + anchor + " / " + s_Path.Length + " corners"; Record(Status + " from=" + p.ToString("F2") + " target=" + target.ToString("F2"));
        Hold(Key.F1, .15f); // normal input capture action, no direct controller state mutation
    }
    public static void Aim(string anchor) { s_Path = null; s_Aim = RaidMissionSite.Active.Anchor(anchor).Position; Status = "Aiming " + anchor; Hold(Key.F1, .15f); }
    public static void Hold(Key key, float seconds) { s_Hold = key; s_Until = Time.realtimeSinceStartupAsDouble + seconds; }
    public static void Stop() { s_Path = null; s_Aim = null; s_Until = 0; Status = "Input stopped"; }
    private static void Input()
    {
        if (s_Keyboard == null) return;
        bool forward = false; Vector2 delta = default; double now = Time.realtimeSinceStartupAsDouble;
        if (Player(out var p, out var ghost) && ghost.CurrentHealth > 0 && !RaidHUD.InventoryOpen && !GameSettings.Instance.IsPauseMenuOpen)
        {
            Vector3? lookAt = s_Aim;
            if (s_Path != null)
            {
                while (s_Corner < s_Path.Length && Vector3.Distance(p, s_Path[s_Corner]) < .8f) s_Corner++;
                if (s_Corner >= s_Path.Length) { s_Path = null; Status = "Arrived by KCC input"; Record(Status + " position=" + p.ToString("F2")); }
                else
                {
                    lookAt = s_Path[s_Corner] + Vector3.up * 1.4f;
                    if (Vector3.Distance(p, s_LastProgress) > .6f) { s_LastProgress = p; s_ProgressAt = now; }
                    if (now - s_ProgressAt > 12) { Record("Movement stalled at " + p.ToString("F2") + " corner=" + s_Corner); s_Path = null; Status = "Movement stalled"; }
                }
            }
            if (lookAt.HasValue)
            {
                Vector3 eye = p + Vector3.up * 1.4f, direction = lookAt.Value - eye;
                float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                float pitch = s_Aim.HasValue ? -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg : 0;
                float dy = Mathf.DeltaAngle(ghost.ControllerState.YawDegrees, yaw), dp = Mathf.DeltaAngle(ghost.ControllerState.PitchDegrees, pitch);
                delta = new Vector2(Mathf.Clamp(dy / .15f, -40, 40), Mathf.Clamp(-dp / .15f, -40, 40));
                forward = s_Path != null && Mathf.Abs(dy) < 25;
            }
        }
        bool holding = now < s_Until;
        var keys = forward ? holding ? new[] { Key.W, s_Hold } : new[] { Key.W } : holding ? new[] { s_Hold } : Array.Empty<Key>();
        InputSystem.QueueStateEvent(s_Keyboard, new KeyboardState(keys));
        InputSystem.QueueStateEvent(s_Mouse, new MouseState { delta = delta });
    }
    public static string Inspect()
    {
        string text = Status + "; game=" + GameSettings.Instance.GameState;
        if (Player(out var p, out var g)) text += "; position=" + p.ToString("F2") + "; health=" + g.CurrentHealth + "; oxygen=" + g.Oxygen.ToString("F1") + "; yaw=" + g.ControllerState.YawDegrees.ToString("F1");
        var world = Client;
        if (world != null && world.IsCreated)
        {
            using var q = world.EntityManager.CreateEntityQuery(typeof(RaidClientState));
            if (q.HasSingleton<RaidClientState>()) { var s = q.GetSingleton<RaidClientState>().Snapshot; text += "; raid=" + s.Phase + "; time=" + s.TimeLeft.ToString("F1") + "; save=" + s.SaveState; }
            using var m = world.EntityManager.CreateEntityQuery(typeof(RaidMissionClientState));
            if (m.HasSingleton<RaidMissionClientState>()) { var s = m.GetSingleton<RaidMissionClientState>().Snapshot; text += "; mission=" + s.DefinitionIndex + "/" + s.Phase + "; mask=" + s.CompletedMask + "; remaining=" + s.Remaining.ToString("F1"); }
        }
        Record(text); return text;
    }
    private static void Record(string text) => File.AppendAllText(Path.Combine(Output, "route.txt"), (Time.realtimeSinceStartupAsDouble - s_Started).ToString("F1") + "s " + text + "\n");
    public static void Release()
    {
        if (s_Keyboard == null && s_Mouse == null) return;
        Stop(); InputSystem.onBeforeUpdate -= Input; EditorApplication.update -= Tick; SceneManager.sceneLoaded -= Loaded;
        if (s_Keyboard != null) InputSystem.RemoveDevice(s_Keyboard); if (s_Mouse != null) InputSystem.RemoveDevice(s_Mouse);
        s_Keyboard = null; s_Mouse = null;
        if (s_HadName) PlayerPrefs.SetString("PlayerName", s_Name); else PlayerPrefs.DeleteKey("PlayerName"); PlayerPrefs.Save();
    }
}
