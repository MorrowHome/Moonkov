using System;
using System.Collections.Generic;
using System.IO;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AlienNavigationChecks
{
    [Serializable] private sealed class Case { public string name, failure; public bool arrived; public float distance; public int transitions; }
    [Serializable] private sealed class Report { public List<Case> cases = new List<Case>(); }
    [MenuItem("Tools/Moonkov/Aliens/Check MC00 doorway and roof routes")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var scene = SceneManager.GetSceneByPath(AlienAmbushSetup.ScenePath);
        Transform frame = null; AlienSurfaceNavigation graph = null;
        foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "MC00_ROOT_Standard_YUp_Frame") frame = t;
            if (t.name == AlienAmbushSetup.NavigationName) graph = t.GetComponent<AlienSurfaceNavigation>();
        }
        if (!frame || !graph) throw new InvalidOperationException("Open the formal MC00 encounter first.");
        var report = new Report();
        try
        {
            Check(graph, frame.TransformPoint(new Vector3(0, .8f, 12)), frame.TransformPoint(new Vector3(-9, .8f, 16.5f)), false, "Corridor through G02 doorway", report);
            Check(graph, frame.TransformPoint(new Vector3(-9, .8f, 16.5f)), frame.TransformPoint(new Vector3(-5, 5.2f, 16)), true, "G02 floor to ceiling perch", report);
            foreach (var c in report.cases) if (!c.arrived) throw new InvalidOperationException(c.name + ": " + c.failure + ", remaining=" + c.distance);
            Debug.Log("MC00 doorway and roof routes passed against actual map collision.");
        }
        finally { Directory.CreateDirectory("Library/BuildValidation/AlienIntegration"); File.WriteAllText("Library/BuildValidation/AlienIntegration/navigation.json", JsonUtility.ToJson(report, true)); }
    }
    private static void Check(AlienSurfaceNavigation graph, Vector3 from, Vector3 to, bool ceiling, string name, Report report)
    {
        const float scale = .45f, dt = 1f / 30;
        int mask = LayerMask.GetMask("Default", "Ground");
        var result = new Case { name = name }; report.cases.Add(result);
        if (!Physics.Raycast(from + Vector3.up * 1.2f, Vector3.down, out var start, 3, mask)) { result.failure = "No starting floor"; return; }
        if (!Physics.Raycast(to + (ceiling ? Vector3.zero : Vector3.up * 1.2f), ceiling ? Vector3.up : Vector3.down, out var end, 3, mask))
        { result.failure = "No destination surface"; return; }
        Vector3 position = start.point + start.normal * SurfaceMotor.Height * scale;
        Vector3 goal = end.point + end.normal * SurfaceMotor.Height * scale;
        Vector3 forward = Vector3.ProjectOnPlane(goal - position, start.normal).normalized;
        var motor = new SurfaceMotor(position, Quaternion.LookRotation(forward, start.normal), mask, scale);
        var route = new AlienSurfaceNavigation.Route();
        for (int i = 0; i < 450; i++)
        {
            result.distance = Vector3.Distance(motor.Current.Position, goal);
            if (result.distance < graph.SampleSpacing * 1.1f && Vector3.Dot(motor.Current.Up, end.normal) > .85f) { result.arrived = true; break; }
            if (!graph.TryGetSteering(route, motor, goal, i * dt, out var waypoint))
            { result.failure = route.LastFailure; motor.Tick(Vector2.zero, 0, 0, dt); continue; }
            var pose = motor.Current;
            Vector3 intent = Vector3.ProjectOnPlane(waypoint - pose.Position, pose.Up).normalized;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(pose.Forward, intent, pose.Up) / dt, -300, 300);
            var basis = pose.Rotation * Quaternion.AngleAxis(motor.Transitioning ? 0 : yaw * dt, Vector3.up);
            Vector2 input = new Vector2(Vector3.Dot(intent, basis * Vector3.right), Vector3.Dot(intent, basis * Vector3.forward));
            motor.Tick(input, 2.5f, yaw, dt);
        }
        result.transitions = motor.Transitions;
        if (!result.arrived && string.IsNullOrEmpty(result.failure)) result.failure = "Travel time bound exceeded";
    }
}
