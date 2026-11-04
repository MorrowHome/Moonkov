using System;
using System.Collections.Generic;
using System.IO;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class AlienAttackDemoChecks
{
    private static AlienAttackDemo s_Demo;
    private static int s_Case;
    private static bool s_Captured, s_AudioHeard;
    private static double s_CaseStarted;
    private static readonly List<string> s_Live = new();
    private static readonly List<string> s_Errors = new();
    private static string Output => Path.GetFullPath("LocalData/AlienAttackDemoReview");

    public static void BeginLiveReview()
    {
        Require(EditorApplication.isPlaying, "Play the demo first.");
        s_Demo = Object.FindAnyObjectByType<AlienAttackDemo>();
        Require(s_Demo && s_Demo.Attacks.Count == 16, "Demo not initialized.");
        var root = s_Demo.GetComponent<UIDocument>().rootVisualElement;
        s_Demo.SetPaused(true);
        var pause = root.Q<Button>("pause"); pause.Focus();
        using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = pause; pause.SendEvent(submit); }
        Require(!s_Demo.Paused, "Pause button did not resume through UI navigation.");
        int previous = s_Demo.Selected;
        var next = root.Q<Button>("next"); next.Focus();
        using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = next; next.SendEvent(submit); }
        Require(s_Demo.Selected == (previous + 1) % 16, "Next attack button did not select the next move.");
        s_Demo.CycleAll = false; s_Demo.SetDodge(false); s_Demo.SetLiquid(false); s_Demo.SetEnraged(false);
        s_Live.Clear(); s_Errors.Clear(); Directory.CreateDirectory(Output);
        s_Case = 0; StartLiveCase();
        EditorApplication.update -= ReviewUpdate; EditorApplication.update += ReviewUpdate;
        Application.logMessageReceived -= OnReviewLog; Application.logMessageReceived += OnReviewLog;
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }

    private static void StartLiveCase()
    {
        s_Captured = s_AudioHeard = false; s_CaseStarted = EditorApplication.timeSinceStartup;
        if (s_Case == 16) s_Demo.SetLiquid(true);
        s_Demo.Select(s_Case == 16 ? 0 : s_Case); s_Demo.SetPaused(false);
    }

    private static void OnReviewLog(string message, string trace, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) &&
            (trace.Contains("AlienAttackDemo") || trace.Contains("Unity.MP_FPS.Alien"))) s_Errors.Add(message);
    }

    private static void ReviewUpdate()
    {
        try
        {
            Require(EditorApplication.isPlaying && s_Demo, "Live review interrupted.");
            Require(EditorApplication.timeSinceStartup - s_CaseStarted < 25, "Live attack timed out.");
            foreach (var emitter in s_Demo.Audio.m_SoundEmitterActiveList)
                foreach (var source in emitter.ActiveSoundGameObjects)
                    s_AudioHeard |= source.m_AudioSource && source.m_AudioSource.isPlaying;
            var state = s_Demo.Snapshot;
            bool pose = s_Demo.Attacks[s_Demo.Selected].Kind == AlienAttackKind.FaceLeap
                ? state.Action == AlienAction.Attached : state.Action == AlienAction.Strike;
            if (!s_Captured && pose && state.ActionTime >= .10f)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(Output, "attack-" + s_Case.ToString("00") + ".png"));
                s_Captured = true;
            }
            if (!s_Demo.Finished) return;
            Require(s_Captured && s_AudioHeard && s_Demo.Damage > 0, "Live attack is incomplete: " + s_Case);
            Require(s_Errors.Count == 0, string.Join("; ", s_Errors));
            s_Live.Add(s_Case + ": " + s_Demo.Profiles[s_Demo.Attacks[s_Demo.Selected].ProfileIndex].name + "/" +
                s_Demo.Attacks[s_Demo.Selected].Kind + ", native frames, AudioSource playing, damage=" + s_Demo.Damage.ToString("0.##"));
            if (++s_Case < 17) { StartLiveCase(); return; }
            File.WriteAllLines(Path.Combine(Output, "live-review.txt"), s_Live);
            StopReview();
            s_Demo.SetLiquid(false); s_Demo.CycleAll = true; s_Demo.Select(0); s_Demo.SetPaused(true);
            Debug.Log("ALIEN_ATTACK_DEMO_LIVE_PASSED: 17 normal-speed cases with audio, UI navigation and screenshots.");
        }
        catch (Exception exception)
        {
            StopReview(); if (s_Demo) s_Demo.SetPaused(true);
            File.WriteAllText(Path.Combine(Output, "live-review-error.txt"), exception.ToString()); Debug.LogException(exception);
        }
    }

    private static void StopReview()
    { EditorApplication.update -= ReviewUpdate; Application.logMessageReceived -= OnReviewLog; }

    [MenuItem("Tools/Moonkov/Aliens/Check open attack demonstration")]
    public static void Run()
    {
        Require(EditorApplication.isPlaying && SceneManager.GetActiveScene().path == AlienAttackDemoSetup.ScenePath, "Play the standalone attack demo first.");
        var demo = Object.FindAnyObjectByType<AlienAttackDemo>();
        Require(demo && demo.Attacks.Count == 16 && demo.Profiles.Length == 12, "Current roster is incomplete.");
        Require(!GameManager.Instance, "Demo started the raid GameManager.");
        var passed = new List<string>();
        demo.SetPaused(true); demo.CycleAll = false; demo.SetLiquid(false); demo.SetEnraged(false);
        for (int i = 0; i < demo.Attacks.Count; i++)
        {
            demo.SetDodge(false); demo.Select(i);
            RunCase(demo, false, passed);
            demo.SetDodge(true);
            RunCase(demo, true, passed);
        }
        demo.SetDodge(false); demo.SetEnraged(true);
        for (int i = 0; i < demo.Attacks.Count; i++)
            if (demo.Profiles[demo.Attacks[i].ProfileIndex].Boss) { demo.Select(i); RunCase(demo, false, passed); }
        demo.SetEnraged(false); demo.SetDodge(false); demo.Select(0);
        var ui = demo.GetComponent<UIDocument>().rootVisualElement;
        Require(ui.Q<ScrollView>("roster").childCount == 12, "Roster buttons missing.");
        Require(ui.Q<UnityEngine.UIElements.Image>("stageImage").image, "Camera output is not wired.");
        Require(demo.Observer.targetTexture && demo.Observer.targetTexture.width >= 64, "Invalid demo viewport.");
        Directory.CreateDirectory("LocalData/AlienAttackDemoReview");
        File.WriteAllLines("LocalData/AlienAttackDemoReview/attack-contract.txt", passed);
        Debug.Log("ALIEN_ATTACK_DEMO_CHECKS_PASSED: " + passed.Count + " attack cases, native camera and roster.");
    }

    private static void RunCase(AlienAttackDemo demo, bool dodge, List<string> passed)
    {
        var kind = demo.Attacks[demo.Selected].Kind;
        bool windup = false, strike = false, attached = false;
        var sounds = new HashSet<string>();
        for (int frame = 0; frame < 500 && !demo.Finished; frame++)
        {
            demo.TickDemonstration(1f / 30);
            windup |= demo.Snapshot.Action == AlienAction.Windup;
            strike |= demo.Snapshot.Action == AlienAction.Strike;
            attached |= demo.Snapshot.Action == AlienAction.Attached;
            foreach (var emitter in demo.Audio.m_SoundEmitterActiveList)
                if (emitter.Active && emitter.Definition) sounds.Add(emitter.Definition.name);
        }
        string name = demo.Profiles[demo.Attacks[demo.Selected].ProfileIndex].name + "/" + kind + (demo.Enraged ? "/phase2" : "") + (dodge ? "/dodge" : "/hit");
        Require(windup && strike && demo.Finished, name + " did not show a complete attack.");
        Require(dodge ? demo.Damage == 0 : demo.Damage > 0, name + " unexpected damage " + demo.Damage);
        if (kind == AlienAttackKind.FaceLeap && !dodge) Require(attached, "Face parasite did not attach.");
        Require(sounds.Count > 0, name + " has no real sound emitters.");
        passed.Add(name + ": hits=" + demo.Hits + ", damage=" + demo.Damage.ToString("0.##") + ", sounds=" + string.Join(",", sounds));
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
