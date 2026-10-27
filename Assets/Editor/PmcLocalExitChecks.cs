using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PmcLocalExitChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Moonkov/AI/Check Local Exit Safety")]
    public static void RunFromEditor() { Debug.Log(Run()); Debug.Log(RunPhysics()); }

    public static string Run()
    {
        s_Checks = 0;
        Check(PmcLocalExitRules.Nearby(Vector3.zero, Vector3.forward * 10) &&
            PmcLocalExitRules.Nearby(Vector3.zero, Vector3.forward * 30) &&
            !PmcLocalExitRules.Nearby(Vector3.zero, Vector3.forward * 31), "nearby humans prevent local departure");
        Check(PmcLocalExitRules.Nearby(new Vector3(float.NaN, 0, 0), Vector3.zero), "invalid positions fail closed");
        Check(PmcLocalExitRules.InSightCone(Vector3.up * 1.6f, Vector3.forward, Vector3.forward * 50) &&
            !PmcLocalExitRules.InSightCone(Vector3.up * 1.6f, Vector3.forward, Vector3.back * 50) &&
            !PmcLocalExitRules.InSightCone(Vector3.up * 1.6f, Vector3.forward, Vector3.forward * 101),
            "distant observers need a view toward the local exit");
        Check(PmcLocalExitRules.InSightCone(Vector3.up * 1.6f, Vector3.zero, Vector3.forward * 50),
            "an unknown view direction is treated conservatively");
        float progress = 0;
        Check(!PmcLocalExitRules.Advance(ref progress, true, true, false, 7, 8) && progress == 7,
            "unobserved local extraction waits for its full timer");
        Check(!PmcLocalExitRules.Advance(ref progress, true, true, true, .5f, 8) && progress == 0,
            "a human observing the local exit resets progress");
        Check(!PmcLocalExitRules.Advance(ref progress, true, true, false, 7.9f, 8) &&
            PmcLocalExitRules.Advance(ref progress, true, true, false, .2f, 8),
            "local departure can resume after a full unobserved wait");
        Check(!PmcLocalExitRules.Advance(ref progress, false, true, false, 1, 8) && progress == 0,
            "leaving the exit or facing a threat cancels progress");
        Check(PmcLocalExitRules.Advance(ref progress, true, false, true, 8, 8),
            "an authored extraction point retains its existing behavior");
        progress = 0;
        Check(!PmcLocalExitRules.Advance(ref progress, true, true, false, float.NaN, 8) && progress == 0 &&
            !PmcLocalExitRules.Advance(ref progress, true, true, false, 1, 0), "invalid timing cannot complete extraction");
        return "PASS " + s_Checks + " local exit safety checks";
    }

    public static string RunPhysics()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var origin = new Vector3(120000, 2000, 120000);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Temporary local exit wall";
        wall.transform.position = origin + Vector3.forward * 25 + Vector3.up;
        wall.transform.localScale = new Vector3(4, 5, 1);
        int checks = 0;
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        try
        {
            var observer = new RaidSpawnObserver { Position = origin, Forward = Vector3.forward, CheckVisibility = true };
            var observers = new List<RaidSpawnObserver> { observer };
            var actor = origin + Vector3.forward * 50;
            var physics = scene.GetPhysicsScene();
            wall.SetActive(false); Physics.SyncTransforms();
            Require(PmcLocalExitRules.Observed(physics, actor, observers), "visible local exit was not protected");
            observer.Forward = Vector3.back; observers[0] = observer;
            Require(!PmcLocalExitRules.Observed(physics, actor, observers), "an exit behind a distant observer remained blocked");
            observer.Forward = Vector3.forward; observers[0] = observer;
            wall.SetActive(true); Physics.SyncTransforms();
            Require(!PmcLocalExitRules.Observed(physics, actor, observers), "a wall did not occlude both body samples");
            Require(PmcLocalExitRules.Observed(physics, origin + Vector3.forward * 10, observers),
                "a nearby human did not block disappearance through cover");
            var second = origin + Vector3.right * 35;
            observers.Add(new RaidSpawnObserver { Position = second, Forward = (actor - second).normalized, CheckVisibility = true });
            Require(PmcLocalExitRules.Observed(physics, actor, observers), "a second human with sight did not block departure");
            return "PASS " + checks + " local exit physics checks";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(wall);
            SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
