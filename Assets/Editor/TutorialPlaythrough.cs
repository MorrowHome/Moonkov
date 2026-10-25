using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

// Reproducible operator: actual KCC inputs, native device buttons, and the normal UI transfer action.
// Never changes poses, health, clocks, mission masks or settlement payloads.
[InitializeOnLoad]
public static class TutorialPlaythrough
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int s_Step;
    private static double s_Started, s_Next, s_PhaseStarted;
    private static string s_Log;
    public static string Status { get; private set; } = "Idle";
    static TutorialPlaythrough()
    {
        EditorApplication.playModeStateChanged += value => { if (value == PlayModeStateChange.ExitingPlayMode) Stop(); };
    }
    public static string Begin()
    {
        if (!EditorApplication.isPlaying || PlayerProfileClient.IsOffline) throw new InvalidOperationException("Start in a fresh menu.");
        TutorialPlayReview.Open(); TutorialPlayReview.Equip();
        while (PlayerProfileClient.OfflineStore.ReadTutorial().IntroPage < 3) PlayerProfileClient.OfflineStore.UpdateTutorial(nextIntro: true);
        PlayerProfileClient.OfflineStore.UpdateTutorial(steps: 7);
        PlayerProfileClient.ValidateAsync("", System.Threading.CancellationToken.None).GetAwaiter().GetResult();
        s_Log = Path.Combine(Path.GetDirectoryName(TutorialPlayReview.ProfilePath), "playthrough.txt");
        s_Started = s_PhaseStarted = EditorApplication.timeSinceStartup; s_Next = 0; s_Step = 0;
        Status = "Loading normal solo expedition"; Record(Status);
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        GameManager.Instance.StartGameAsync(CreationType.Offline);
        return TutorialPlayReview.ProfilePath;
    }
    private static void Record(string message) => File.AppendAllText(s_Log, (EditorApplication.timeSinceStartup - s_Started).ToString("F1") + "s " + message + "\n");
    private static void Advance(int step, string message, double wait = .7)
    {
        s_Step = step; s_PhaseStarted = EditorApplication.timeSinceStartup; s_Next = s_PhaseStarted + wait;
        Status = message; Record(message);
    }
    private static void Walk(string anchor, int next)
    {
        MissionRoleReview.Walk(anchor); MissionRoleReview.Hold(Key.LeftShift, 120);
        Advance(next, "Walking " + anchor);
    }
    private static void Aim(string anchor, int next)
    { MissionRoleReview.Aim(anchor); Advance(next, "Aiming " + anchor, 1.8); }
    private static void Hold(int next)
    { MissionRoleReview.Hold(Key.F, 8); Advance(next, "Holding actual F", 1); }
    private static bool Arrived => MissionRoleReview.Status == "Arrived by KCC input";
    private static bool Has(RaidMissionSnapshotRpc snapshot, uint bit) => (snapshot.CompletedMask & bit) != 0;
    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < s_Next) return;
        s_Next = now + .25;
        try
        {
            var world = ClientServerBootstrap.ClientWorld;
            if (now - s_Started > 800) throw new InvalidOperationException("Review exceeded 800 seconds.");
            if (GameSettings.Instance.GameState != GlobalGameState.InGame || world == null || !world.IsCreated) return;
            using var r = world.EntityManager.CreateEntityQuery(typeof(RaidClientState));
            using var m = world.EntityManager.CreateEntityQuery(typeof(RaidMissionClientState));
            if (r.IsEmptyIgnoreFilter || m.IsEmptyIgnoreFilter) return;
            var raid = r.GetSingleton<RaidClientState>().Snapshot;
            var mission = m.GetSingleton<RaidMissionClientState>().Snapshot;
            if (raid.Phase != RaidPhase.Active)
            {
                if (raid.SaveState != RaidSaveState.Saved) return;
                Record(MissionRoleReview.Inspect());
                var progress = PlayerProfileClient.OfflineStore.ReadTutorial();
                Status = raid.Phase == RaidPhase.Extracted && progress.Status == Unity.MP_FPS.Campaign.TutorialStatus.Completed ? "PASS real extraction and durable tutorial completion" : "FAIL " + raid.Phase;
                Record(Status); ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(s_Log), "result.png")); Stop(); return;
            }
            switch (s_Step)
            {
                case 0:
                    using (var owned = world.EntityManager.CreateEntityQuery(typeof(PredictedPlayerGhost), typeof(GhostOwnerIsLocal)))
                        if (owned.IsEmptyIgnoreFilter) break;
                    MissionRoleReview.AttachCurrent(); Walk("entry", 1); break;
                case 1: if (Arrived) Aim("entry", 2); break;
                case 2: Hold(3); break;
                case 3:
                    if (Has(mission, 16)) Walk("power_a", 4);
                    else if (now - s_PhaseStarted > 9) Advance(2, "Retry entrance hold"); break;
                case 4: if (Arrived) Aim("power_a", 5); break;
                case 5: MissionRoleReview.Hold(Key.F, .25f); Advance(6, "Open wiring UI", .8); break;
                case 6:
                    string result = TutorialPlayReview.Submit("断开烧损支路，保留备用母线");
                    if (result.StartsWith("Submitted")) Advance(7, result);
                    else if (now - s_PhaseStarted > 3) Advance(5, "Retry wiring prompt"); break;
                case 7: if (Has(mission, 1)) Walk("power_b", 8); break;
                case 8: if (Arrived) Aim("power_b", 9); break;
                case 9: Hold(10); break;
                case 10:
                    if (Has(mission, 2)) Walk("sampler", 11);
                    else if (now - s_PhaseStarted > 9) Advance(9, "Retry B hold"); break;
                case 11: if (Arrived) Aim("sampler", 12); break;
                case 12: Hold(13); break;
                case 13:
                    if (Has(mission, 4)) Advance(14, "Cutter running at actual speed");
                    else if (now - s_PhaseStarted > 9) Advance(12, "Retry cutter hold"); break;
                case 14:
                    if (Has(mission, 8)) { MissionRoleReview.Hold(Key.F, .25f); Advance(15, "Open actual output slot", 1); }
                    break;
                case 15:
                    if (!RaidHUD.InventoryOpen)
                    {
                        if (now - s_PhaseStarted > 3) { MissionRoleReview.Hold(Key.F, .25f); s_PhaseStarted = now; }
                        break;
                    }
                    var hud = UnityEngine.Object.FindFirstObjectByType<RaidHUD>();
                    var view = typeof(RaidHUD).GetField("m_InventoryView", Private).GetValue(hud);
                    var graph = (InventoryGraph)view.GetType().GetField("m_Graph", Private).GetValue(view);
                    if (graph == null) break;
                    var item = graph.Items.FirstOrDefault(i => i.Code == "mooncore_fragment" && !graph.Carried(i));
                    if (item == null) break;
                    view.GetType().GetMethod("QuickTransfer", Private).Invoke(view, new object[] { item });
                    Advance(16, "Normal inventory UI transfer requested", 1); break;
                case 16:
                    using (var inventory = world.EntityManager.CreateEntityQuery(typeof(RaidInventoryClientState)))
                    {
                        if (inventory.IsEmptyIgnoreFilter) break;
                        var state = world.EntityManager.GetComponentObject<RaidInventoryClientState>(inventory.GetSingletonEntity());
                        if (state.Graph == null || !state.Graph.Items.Any(i => i.Code == "mooncore_fragment" && state.Graph.Carried(i))) break;
                    }
                    MissionRoleReview.Hold(Key.Tab, .2f); Advance(17, "Confirmed real carried sample", .6); break;
                case 17: if (!RaidHUD.InventoryOpen) Walk("exit", 18); break;
                case 18: if (Arrived) Advance(19, "Waiting actual extraction dwell"); break;
            }
            if (MissionRoleReview.Status == "Movement stalled") throw new InvalidOperationException(MissionRoleReview.Inspect());
        }
        catch (Exception e) { Status = "FAIL " + e.GetBaseException().Message; Record(Status); Stop(); }
    }
    public static void Stop() { EditorApplication.update -= Tick; MissionRoleReview.Release(); }
}
