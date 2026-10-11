using System;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Manual, fixed-view attribution experiment. Temporary changes never save to assets.</summary>
    public static class Apollo12LightingCostCapture
    {
        private struct LightState
        {
            public Light light;
            public bool enabled;
            public float intensity;
            public LightShadows shadows;
        }

        [Serializable] private sealed class Phase
        {
            public string name;
            public int samples;
            public float frameMedianMs, frameP95Ms, mainThreadMedianMs, gpuMedianMs, gpuP95Ms;
            public float drawCallsMedian, shadowCastersMedian;
            public bool gpuTimingAvailable;
        }

        [Serializable] private sealed class Report
        {
            public string utc, scene, device, pipeline;
            public int width, height, managedLights, enabledShadowLights, vSyncCount, targetFrameRate;
            public float renderScale, maxCameraMovement, maxCameraRotation;
            public bool comparablePose;
            public Vector3 cameraPosition, cameraForward;
            public Phase[] phases = new Phase[3];
            public string boundary = "Editor fixed-pose A/B/C, 2 s settle + 3 s per phase. Shadow-off may leak through walls and is diagnostic only. Main Thread includes waits; GPU recorder may lag. Draw counters include other Editor views. No target-device acceptance. Lower frame time after removing shadows includes both shadow rendering and shadow sampling savings.";
        }

        private static readonly List<LightState> lights = new List<LightState>();
        private static readonly List<LocalLightBudget> budgets = new List<LocalLightBudget>();
        private static readonly List<float> frames = new List<float>(), cpu = new List<float>(), gpu = new List<float>();
        private static readonly List<float> draws = new List<float>(), casters = new List<float>();
        private static ProfilerRecorder cpuRecorder, gpuRecorder;
        private static Camera camera;
        private static Quaternion startRotation;
        private static Report report;
        private static double phaseStart;
        private static int phaseIndex, lastFrame;
        private static bool running;

        [MenuItem("Tools/Moon Environment/Apollo 12/Compare Indoor Lighting Cost (15 seconds in Play)")]
        public static void Begin()
        {
            if (running) throw new InvalidOperationException("Lighting comparison is already running.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused ||
                SceneManager.GetActiveScene().path != MoonBaseApollo12Tools.ScenePath || !Camera.main)
                throw new InvalidOperationException("Enter the Apollo 12 preview and stand at the slow location first.");
            var found = UnityEngine.Object.FindObjectsByType<LocalLightBudget>(FindObjectsSortMode.None);
            // Validate before any mutation. Distance budgeting would confound this experiment.
            foreach (var budget in found)
                if (budget.isActiveAndEnabled && new SerializedObject(budget).FindProperty("limitLightsByDistance").boolValue)
                    throw new InvalidOperationException("Disable Limit Lights By Distance before comparing lighting costs.");
            try
            {
                lights.Clear(); budgets.Clear();
                var unique = new HashSet<Light>();
                foreach (var budget in found)
                {
                    if (!budget.isActiveAndEnabled) continue;
                    var fixtures = new SerializedObject(budget).FindProperty("fixtures");
                    for (int i = 0; i < fixtures.arraySize; i++)
                    {
                        var light = fixtures.GetArrayElementAtIndex(i).objectReferenceValue as Light;
                        if (!light || !unique.Add(light)) continue;
                        lights.Add(new LightState { light = light, enabled = light.enabled,
                            intensity = light.intensity, shadows = light.shadows });
                    }
                    budgets.Add(budget);
                }
                if (lights.Count == 0) throw new InvalidOperationException("No managed indoor lights found.");
                foreach (var budget in budgets) budget.enabled = false;
                RestoreLights();
                camera = Camera.main; startRotation = camera.transform.rotation;
                var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                report = new Report { utc = DateTime.UtcNow.ToString("O"), scene = SceneManager.GetActiveScene().path,
                    device = SystemInfo.graphicsDeviceName, pipeline = pipeline ? pipeline.name : "none",
                    width = Screen.width, height = Screen.height, renderScale = pipeline ? pipeline.renderScale : 1,
                    managedLights = lights.Count, cameraPosition = camera.transform.position, cameraForward = camera.transform.forward,
                    vSyncCount = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate };
                foreach (var state in lights)
                    if (state.enabled && state.light.gameObject.activeInHierarchy && state.shadows != LightShadows.None)
                        report.enabledShadowLights++;
                cpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
                gpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1);
                running = true; phaseIndex = 0; StartPhase();
                EditorApplication.update += Sample;
                EditorApplication.playModeStateChanged += PlayStateChanged;
                AssemblyReloadEvents.beforeAssemblyReload += Cancel;
                Debug.Log("Apollo12 lighting comparison: focus Game, keep position/view fixed for 15 seconds. Lights change temporarily and restore automatically; do not operate switches.");
            }
            catch { Cancel(); throw; }
        }

        private static void StartPhase()
        {
            RestoreLights();
            foreach (var state in lights)
            {
                if (!state.light) continue;
                if (phaseIndex == 1) state.light.shadows = LightShadows.None;
                if (phaseIndex == 2) state.light.enabled = false;
            }
            frames.Clear(); cpu.Clear(); gpu.Clear(); draws.Clear(); casters.Clear();
            phaseStart = EditorApplication.timeSinceStartup;
            lastFrame = Time.frameCount;
        }

        private static void Sample()
        {
            try
            {
                if (!EditorApplication.isPlaying || EditorApplication.isPaused || !camera ||
                    SceneManager.GetActiveScene().path != report.scene || Screen.width != report.width || Screen.height != report.height)
                { Cancel(); Debug.Log("Apollo12 lighting comparison cancelled; light state restored, previous reports retained."); return; }
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                double elapsed = EditorApplication.timeSinceStartup - phaseStart;
                // Ignore refocusing and the transition frame; require the same pose for every measured phase.
                if (elapsed < 2) return;
                report.maxCameraMovement = Mathf.Max(report.maxCameraMovement, Vector3.Distance(report.cameraPosition, camera.transform.position));
                report.maxCameraRotation = Mathf.Max(report.maxCameraRotation, Quaternion.Angle(startRotation, camera.transform.rotation));
                frames.Add(Time.unscaledDeltaTime * 1000);
                if (cpuRecorder.Valid && cpuRecorder.Count > 0 && cpuRecorder.LastValue > 0) cpu.Add(cpuRecorder.LastValue / 1000000f);
                if (gpuRecorder.Valid && gpuRecorder.Count > 0 && gpuRecorder.LastValue > 0) gpu.Add(gpuRecorder.LastValue / 1000000f);
                draws.Add(UnityStats.drawCalls); casters.Add(UnityStats.shadowCasters);
                if (elapsed < 5 && frames.Count < 4096) return;
                frames.Sort(); cpu.Sort(); gpu.Sort(); draws.Sort(); casters.Sort();
                report.phases[phaseIndex] = new Phase
                {
                    name = new[] { "full_lighting", "indoor_shadows_off", "indoor_lights_off" }[phaseIndex], samples = frames.Count,
                    frameMedianMs = Percentile(frames, .5f), frameP95Ms = Percentile(frames, .95f),
                    mainThreadMedianMs = Percentile(cpu, .5f), gpuMedianMs = Percentile(gpu, .5f), gpuP95Ms = Percentile(gpu, .95f),
                    gpuTimingAvailable = gpu.Count > 0, drawCallsMedian = Percentile(draws, .5f), shadowCastersMedian = Percentile(casters, .5f)
                };
                if (++phaseIndex < 3) { StartPhase(); return; }
                report.comparablePose = report.maxCameraMovement < .25f && report.maxCameraRotation < 2;
                Cancel();
                const string directory = "Library/BuildValidation";
                Directory.CreateDirectory(directory);
                string path = directory + "/Apollo12LightingCost-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json";
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log($"Apollo12 lighting comparison saved {path}. Same pose: {report.comparablePose}. Full / no indoor shadows / no indoor lights median: {report.phases[0].frameMedianMs:F2} / {report.phases[1].frameMedianMs:F2} / {report.phases[2].frameMedianMs:F2} ms. All states restored.");
            }
            catch (Exception error) { Cancel(); Debug.LogException(error); }
        }

        private static float Percentile(List<float> values, float p) => values.Count == 0 ? 0 : values[Mathf.Clamp(Mathf.CeilToInt(values.Count * p) - 1, 0, values.Count - 1)];
        private static void PlayStateChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Cancel(); }
        private static void RestoreLights()
        {
            foreach (var state in lights)
                if (state.light) { state.light.shadows = state.shadows; state.light.intensity = state.intensity; state.light.enabled = state.enabled; }
        }

        private static void Cancel()
        {
            EditorApplication.update -= Sample;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            cpuRecorder.Dispose(); gpuRecorder.Dispose(); cpuRecorder = default; gpuRecorder = default;
            RestoreLights();
            // Restore light power first, so OnEnable captures the original power states.
            foreach (var budget in budgets) if (budget) budget.enabled = true;
            budgets.Clear(); lights.Clear(); running = false;
        }
    }
}
