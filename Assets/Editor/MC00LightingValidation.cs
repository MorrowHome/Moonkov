using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;

namespace Unity.MP_FPS.Moon.Editor
{
    // A fixed camera in the actual offline raid, with a disposable profile. Runs
    // one bounded sample per explicit invocation; no automatic A/B experiments.
    [InitializeOnLoad]
    public static class MC00LightingValidation
    {
        const string Folder = "Library/BuildValidation/MC00BakedBuilding";
        static bool boot, waiting, sampling;
        static double ready, begins;
        static int lastFrame;
        static string label;
        static Camera fixedCamera;
        static readonly List<float> frameTimes = new(), draws = new(), shadows = new();
        public static string Status { get; private set; } = "Idle";
        [Serializable] class Report { public string label; public int frames, width, height; public float medianMs, p95Ms, drawCalls, shadowCasters; public Vector3 position; }
        static MC00LightingValidation()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) boot = waiting = sampling = false; };
        }
        public static void Boot(string sampleLabel)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) throw new InvalidOperationException("Editor must be idle.");
            Directory.CreateDirectory(Folder); label = sampleLabel; boot = true;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainMenu.unity");
            SessionState.SetString("MC00Baked.ValidationLabel", sampleLabel);
            EditorApplication.EnterPlaymode(); Status = "Starting formal raid";
        }
        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (!boot && !waiting && !sampling && SessionState.GetString("MC00Baked.ValidationLabel", "") != "")
            { label = SessionState.GetString("MC00Baked.ValidationLabel", ""); SessionState.EraseString("MC00Baked.ValidationLabel"); boot = true; }
            if (boot && GameManager.Instance && GameSettings.Instance.MainMenuSceneLoaded)
            {
                boot = false;
                var store = new OfflineProfileStore(Path.GetFullPath(Folder + "/Profile-" + DateTime.UtcNow.ToString("HHmmss") + "/profile.json"));
                typeof(PlayerProfileClient).GetField("s_Offline", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, store);
                typeof(PlayerProfileClient).GetMethod("RefreshOffline", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                GameManager.Instance.StartGameAsync(CreationType.Offline); waiting = true; ready = 0;
                SessionState.EraseString("MC00Baked.ValidationLabel");
            }
            if (waiting && Camera.main && SceneManager.GetSceneByName(GameManager.GameSceneName).isLoaded)
            {
                if (ready == 0) ready = EditorApplication.timeSinceStartup + 8;
                if (EditorApplication.timeSinceStartup >= ready) { waiting = false; Place(); Sample(label); }
            }
            if (!sampling || EditorApplication.isPaused || Time.frameCount == lastFrame || EditorApplication.timeSinceStartup < begins) return;
            lastFrame = Time.frameCount;
            frameTimes.Add(Time.unscaledDeltaTime * 1000); draws.Add(UnityStats.drawCalls); shadows.Add(UnityStats.shadowCasters);
            if (EditorApplication.timeSinceStartup - begins < 6) return;
            sampling = false;
            var report = new Report { label = label, frames = frameTimes.Count, width = fixedCamera.pixelWidth, height = fixedCamera.pixelHeight,
                medianMs = Percentile(frameTimes, .5f), p95Ms = Percentile(frameTimes, .95f), drawCalls = Percentile(draws, .5f),
                shadowCasters = Percentile(shadows, .5f), position = fixedCamera.transform.position };
            File.WriteAllText(Folder + "/" + label + ".json", JsonUtility.ToJson(report, true));
            ScreenCapture.CaptureScreenshot(Folder + "/" + label + ".png");
            Status = "Captured " + label + ": " + report.medianMs.ToString("F2") + " ms"; Debug.Log(Status);
        }
        static void Place()
        {
            var original = Camera.main;
            fixedCamera = new GameObject("MC00 fixed performance camera").AddComponent<Camera>();
            fixedCamera.CopyFrom(original);
            var additional = original.GetComponent<UniversalAdditionalCameraData>();
            if (additional) EditorUtility.CopySerialized(additional, fixedCamera.gameObject.AddComponent<UniversalAdditionalCameraData>());
            original.enabled = false; original.tag = "Untagged"; fixedCamera.tag = "MainCamera";
            fixedCamera.transform.SetPositionAndRotation(new Vector3(1609.8f, 213.28615f, 2122), Quaternion.Euler(0, 20, 0));
            GameInput.Actions.Disable();
            foreach (var input in UnityEngine.Object.FindObjectsByType<UnityEngine.InputSystem.PlayerInput>()) input.DeactivateInput();
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        }
        public static void Sample(string sampleLabel)
        {
            label = sampleLabel; frameTimes.Clear(); draws.Clear(); shadows.Clear(); begins = EditorApplication.timeSinceStartup + 3;
            lastFrame = -1; sampling = true; Status = "Sampling " + label;
        }
        static float Percentile(List<float> values, float percentile)
        { values.Sort(); return values[Mathf.Clamp(Mathf.CeilToInt(values.Count * percentile) - 1, 0, values.Count - 1)]; }
    }
}
