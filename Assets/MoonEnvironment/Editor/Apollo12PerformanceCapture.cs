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
    /// <summary>Opt-in, bounded capture for manual traversal. Never starts Play or changes quality.</summary>
    public static class Apollo12PerformanceCapture
    {
        private static readonly List<float> frames = new List<float>(2048);
        private static readonly List<float> cpu = new List<float>(2048);
        private static readonly List<float> gpu = new List<float>(2048);
        private static readonly List<FrameSample> frameSamples = new List<FrameSample>(2048);
        private static ProfilerRecorder cpuRecorder, gpuRecorder, brgRecorder, brgIndirectRecorder;
        private static Camera captureCamera;
        private static double startsAt;
        private static int lastFrame;
        private static Report report;

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 2;
            public string utc, scene, device, pipeline, upscaler;
            public int outputWidth, outputHeight, samples;
            public float renderScale, measuredSeconds, frameMedianMs, frameP95Ms, frameP99Ms;
            public float framesOver16_67MsPercent, mainThreadMedianMs, gpuMedianMs;
            public bool gpuTimingAvailable;
            public float mainThreadP95Ms, gpuP95Ms, gpuP99Ms;
            public bool gpuOcclusionActive;
            public string gpuDrawingMode;
            public FrameSample[] slowestFrames;
            public string boundary = "Editor capture; Main Thread includes waits; recorder values are latest available and may lag frame time. Zero GPU time means unavailable. Drawing counters can include other Editor views. Not a target-hardware or standalone acceptance.";
        }

        [Serializable]
        private struct FrameSample
        {
            public int frame;
            public float frameMs, mainThreadMs, gpuMs;
            public int draws, shadowCasters, setPass;
            public long brgDraws;
            public Vector3 cameraPosition, cameraForward;
        }

        [MenuItem("Tools/Moon Environment/Apollo 12/Capture Frame Times (10 seconds in Play)")]
        public static void Begin()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused ||
                SceneManager.GetActiveScene().path != MoonBaseApollo12Tools.ScenePath)
                throw new InvalidOperationException("Run the Apollo 12 preview, then start this capture at the slow location.");
            Cancel();
            frames.Clear(); cpu.Clear(); gpu.Clear(); frameSamples.Clear();
            captureCamera = Camera.main;
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            string upscaler = "none";
            if (pipeline)
            {
                var settings = new SerializedObject(pipeline);
                var named = settings.FindProperty("m_SelectedUpscalerName");
                var legacy = settings.FindProperty("m_UpscalingFilter");
                upscaler = named != null ? named.stringValue : legacy != null ? legacy.enumDisplayNames[legacy.enumValueIndex] : "unknown";
            }
            report = new Report
            {
                utc = DateTime.UtcNow.ToString("O"), scene = SceneManager.GetActiveScene().path,
                device = SystemInfo.graphicsDeviceName, outputWidth = Screen.width, outputHeight = Screen.height,
                pipeline = pipeline ? pipeline.name : "none", renderScale = pipeline ? pipeline.renderScale : 1,
                upscaler = upscaler,
                gpuDrawingMode = pipeline ? pipeline.gpuResidentDrawerMode.ToString() : "none",
                gpuOcclusionActive = GPUResidentDrawer.IsInstanceOcclusionCullingEnabled()
            };
            cpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            gpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1);
            brgRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "BRG Draw Calls Count", 1);
            brgIndirectRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "BRG Indirect Draw Calls Count", 1);
            startsAt = EditorApplication.timeSinceStartup + 1; // Refocus Game after opening the menu.
            lastFrame = Time.frameCount;
            EditorApplication.update += Sample;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            Debug.Log("Apollo12: frame capture starts in 1 second. Focus Game and follow the slow route for 10 seconds.");
        }

        private static void Sample()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused ||
                SceneManager.GetActiveScene().path != report.scene)
            {
                Cancel();
                Debug.Log("Apollo12: capture cancelled because Play/scene state changed; previous report preserved.");
                return;
            }
            double elapsed = EditorApplication.timeSinceStartup - startsAt;
            if (elapsed < 0 || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            frames.Add(Time.unscaledDeltaTime * 1000);
            if (cpuRecorder.Valid && cpuRecorder.Count > 0 && cpuRecorder.LastValue > 0)
                cpu.Add(cpuRecorder.LastValue / 1000000f);
            if (gpuRecorder.Valid && gpuRecorder.Count > 0 && gpuRecorder.LastValue > 0)
                gpu.Add(gpuRecorder.LastValue / 1000000f);
            frameSamples.Add(new FrameSample
            {
                frame = lastFrame, frameMs = Time.unscaledDeltaTime * 1000,
                mainThreadMs = Latest(cpuRecorder) / 1000000f, gpuMs = Latest(gpuRecorder) / 1000000f,
                draws = UnityEditor.UnityStats.drawCalls, shadowCasters = UnityEditor.UnityStats.shadowCasters,
                setPass = UnityEditor.UnityStats.setPassCalls, brgDraws = Latest(brgRecorder) + Latest(brgIndirectRecorder),
                cameraPosition = captureCamera ? captureCamera.transform.position : Vector3.zero,
                cameraForward = captureCamera ? captureCamera.transform.forward : Vector3.zero
            });
            if (elapsed < 10 && frames.Count < 8192) return;
            Cancel();
            report.samples = frames.Count;
            report.measuredSeconds = (float)elapsed;
            report.framesOver16_67MsPercent = 100f * frames.FindAll(ms => ms > 1000f / 60).Count / frames.Count;
            frames.Sort(); cpu.Sort(); gpu.Sort();
            report.frameMedianMs = Percentile(frames, .5f);
            report.frameP95Ms = Percentile(frames, .95f);
            report.frameP99Ms = Percentile(frames, .99f);
            report.mainThreadMedianMs = Percentile(cpu, .5f);
            report.gpuTimingAvailable = gpu.Count > 0;
            report.gpuMedianMs = Percentile(gpu, .5f);
            report.mainThreadP95Ms = Percentile(cpu, .95f);
            report.gpuP95Ms = Percentile(gpu, .95f);
            report.gpuP99Ms = Percentile(gpu, .99f);
            frameSamples.Sort((a, b) => b.frameMs.CompareTo(a.frameMs));
            report.slowestFrames = frameSamples.GetRange(0, Mathf.Min(12, frameSamples.Count)).ToArray();
            const string path = "Library/BuildValidation/Apollo12Performance.json";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log($"Apollo12: {report.samples} frames; median {report.frameMedianMs:F2} ms, P95 {report.frameP95Ms:F2} ms, P99 {report.frameP99Ms:F2} ms. Saved {path}");
        }

        private static float Percentile(List<float> values, float fraction) => values.Count == 0 ? 0 : values[Mathf.Clamp(Mathf.CeilToInt(values.Count * fraction) - 1, 0, values.Count - 1)];
        private static long Latest(ProfilerRecorder recorder) => recorder.Valid && recorder.Count > 0 ? recorder.LastValue : 0;

        private static void Cancel()
        {
            EditorApplication.update -= Sample;
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            cpuRecorder.Dispose(); gpuRecorder.Dispose(); brgRecorder.Dispose(); brgIndirectRecorder.Dispose();
            cpuRecorder = default; gpuRecorder = default; brgRecorder = default; brgIndirectRecorder = default;
        }
    }
}
