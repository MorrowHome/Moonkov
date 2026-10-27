using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Reproducible isolated lighting experiment; never opens/saves the gameplay map or enters Play.</summary>
    public static class StochasticLightingReview
    {
        public const string Output = "LocalData/StochasticLighting";

        [Serializable] private sealed class Measurement
        {
            public string light, mode;
            public double millisecondsPerRender;
        }

        [Serializable] private sealed class Report
        {
            public string device, utc, pipeline;
            public int width = 1600, height = 1000, rendersPerBlock = 32;
            public bool keywordRestored, parametersRestored, activeScenePreserved;
            public List<Measurement> measurements = new List<Measurement>();
            public string boundary = "Isolated Editor preview, original renderer with a temporary pipeline clone: 1024 main shadow atlas, one cascade, 20 m shadow distance. Synchronous render throughput includes CPU submission, GPU completion and one readback per block. This is NOT isolated GPU timing, gameplay FPS or target-device acceptance. Stock tent filtering and stochastic box filtering differ in kernel shape. Geometry, lights, resolution and camera are identical within each comparison.";
        }

        [MenuItem("Tools/Moon Environment/Stochastic Lighting/Render comparison")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Run the shadow experiment in idle Edit Mode.");
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) throw new InvalidOperationException("URP is required.");
            var shader = Shader.Find("MoonEnvironment/Industrial Dust");
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Environment shader unavailable.");
            Directory.CreateDirectory(Output);
            var active = SceneManager.GetActiveScene();
            bool dirty = active.isDirty;
            bool keyword = Shader.IsKeywordEnabled(MoonStochasticLighting.Keyword);
            var parameters = Shader.GetGlobalVector("_MoonStochasticShadowParams");
            var previousTarget = RenderTexture.active;
            var previousQuality = QualitySettings.renderPipeline;
            var preview = EditorSceneManager.NewPreviewScene();
            UniversalRenderPipelineAsset temporaryPipeline = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            Material material = null;
            var report = new Report { device = SystemInfo.graphicsDeviceName, utc = DateTime.UtcNow.ToString("O"), pipeline = pipeline.name };
            try
            {
                temporaryPipeline = Object.Instantiate(pipeline);
                temporaryPipeline.hideFlags = HideFlags.HideAndDontSave;
                temporaryPipeline.mainLightShadowmapResolution = 1024;
                temporaryPipeline.shadowCascadeCount = 1;
                temporaryPipeline.shadowDistance = 20;
                QualitySettings.renderPipeline = temporaryPipeline;

                GameObject Root(string name)
                {
                    var root = new GameObject(name);
                    SceneManager.MoveGameObjectToScene(root, preview);
                    return root;
                }
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetColor("_BaseColor", new Color(.55f, .48f, .36f));
                material.SetFloat("_DustAmount", 0);
                material.SetFloat("_Smoothness", .1f);
                void Box(string name, Vector3 position, Vector3 size)
                {
                    var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    SceneManager.MoveGameObjectToScene(box, preview);
                    box.name = name; box.transform.position = position; box.transform.localScale = size;
                    box.GetComponent<Renderer>().sharedMaterial = material;
                }
                Box("Ground", new Vector3(0, -.15f, 0), new Vector3(14, .3f, 14));
                Box("Back wall", new Vector3(0, 2.5f, 4), new Vector3(14, 5, .25f));
                for (int i = 0; i < 6; i++)
                    Box("Shadow bar " + i, new Vector3(-2.6f + i * .9f, 1.4f, .2f + i * .12f), new Vector3(.16f, 2.8f, .25f));
                Box("Block", new Vector3(2.3f, .6f, -1.3f), new Vector3(1.2f, 1.2f, 1.2f));
                var light = Root("Shadow source").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 2;
                light.color = new Color(1, .78f, .5f);
                light.shadows = LightShadows.Soft; light.shadowBias = .05f; light.shadowNormalBias = .3f;
                light.transform.rotation = Quaternion.Euler(38, -30, 0);
                light.GetUniversalAdditionalLightData().softShadowQuality = SoftShadowQuality.High;
                var fill = Root("Dim fill").AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = .12f;
                fill.transform.rotation = Quaternion.Euler(70, 150, 0);
                var camera = Root("Shadow review camera").AddComponent<Camera>();
                camera.enabled = false; camera.scene = preview;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.008f, .009f, .012f);
                camera.fieldOfView = 48; camera.nearClipPlane = .1f; camera.farClipPlane = 40;
                camera.transform.position = new Vector3(5.4f, 3.4f, -6.5f);
                camera.transform.LookAt(new Vector3(0, .7f, 1));
                var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false;
                var effect = camera.gameObject.AddComponent<MoonStochasticLighting>();
                target = new RenderTexture(report.width, report.height, 24, RenderTextureFormat.ARGB32);
                target.Create();
                pixels = new Texture2D(report.width, report.height, TextureFormat.RGB24, false);
                void Render()
                {
                    RenderPipeline.SubmitRenderRequest(camera,
                        new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                }
                void Read()
                {
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    pixels.Apply();
                }
                void Mode(int taps)
                {
                    effect.enabled = taps > 0;
                    var settings = new SerializedObject(effect);
                    settings.FindProperty("samples").intValue = taps == 3 ? 1 : Mathf.Max(1, taps);
                    settings.FindProperty("shadingProbability").floatValue = taps == 3 ? .65f : 1;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                void Capture(string name, int taps)
                {
                    Mode(taps); Render(); Read();
                    File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
                }
                // Force both variants of all three receiving shaders through the active backend.
                foreach (string name in new[] { "Moon/R29 Industrial Display", "Moon/R29 Local Ground Display", "MoonEnvironment/Industrial Dust" })
                {
                    material.shader = Shader.Find(name);
                    if (!material.shader || !material.shader.isSupported) throw new InvalidOperationException(name + " unsupported");
                    Mode(0); Render(); Mode(1); Render(); Read();
                    foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                        if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                            throw new InvalidOperationException(name + ": " + message.message);
                }
                material.shader = shader;
                material.SetColor("_BaseColor", new Color(.55f, .48f, .36f));
                material.SetFloat("_DustAmount", 0); material.SetFloat("_Smoothness", .1f);
                foreach (var kind in new[] { LightType.Directional, LightType.Spot, LightType.Point })
                {
                    light.type = kind;
                    if (kind != LightType.Directional)
                    {
                        light.transform.position = new Vector3(-2, 4.2f, -2.4f);
                        light.transform.LookAt(new Vector3(0, 0, 1));
                        light.intensity = 35; light.range = 15; light.spotAngle = 105; light.innerSpotAngle = 75;
                        light.shadowResolution = UnityEngine.Rendering.LightShadowResolution.High;
                    }
                    string prefix = kind.ToString().ToLowerInvariant();
                    Capture(prefix + "-stock", 0);
                    Capture(prefix + "-one", 1);
                    Capture(prefix + "-two", 2);
                    Capture(prefix + "-sparse", 3);
                    // First-use pipeline/driver work is not a rendering saving.
                    foreach (int taps in new[] { 0, 1, 3 })
                    {
                        Mode(taps);
                        for (int i = 0; i < 24; i++) Render();
                        Read();
                    }
                    // A-B-C-C-B-A reduces a simple warmup/temperature ordering bias.
                    foreach (int taps in new[] { 0, 1, 3, 3, 1, 0, 3, 0, 1, 1, 0, 3 })
                    {
                        Mode(taps);
                        for (int i = 0; i < 3; i++) Render();
                        Read(); // Drain warmup before starting the clock.
                        var watch = Stopwatch.StartNew();
                        for (int i = 0; i < report.rendersPerBlock; i++) Render();
                        Read(); // Include GPU completion, not just command submission.
                        watch.Stop();
                        report.measurements.Add(new Measurement { light = prefix, mode = taps == 0 ? "stock" : taps == 3 ? "sparse-65-percent" : taps + "-tap",
                            millisecondsPerRender = watch.Elapsed.TotalMilliseconds / report.rendersPerBlock });
                    }
                    // Fixed camera must be deterministic; moving the view must still render valid shadows.
                    Mode(3); Render(); Read(); var stable = pixels.GetRawTextureData<byte>().ToArray();
                    Render(); Read(); var repeated = pixels.GetRawTextureData<byte>();
                    for (int i = 0; i < stable.Length; i++)
                        if (stable[i] != repeated[i]) throw new InvalidOperationException("Stationary shadow flickered: " + prefix);
                    var pose = camera.transform.position;
                    camera.transform.position += new Vector3(.08f, 0, 0);
                    Capture(prefix + "-moved", 3);
                    camera.transform.position = pose;
                }
                // Hard/no-shadow lights must not pick up artificial noise.
                foreach (var shadows in new[] { LightShadows.Hard, LightShadows.None })
                {
                    light.shadows = shadows;
                    Mode(0); Render(); Read(); var before = pixels.GetRawTextureData<byte>().ToArray();
                    Mode(1); Render(); Read(); var after = pixels.GetRawTextureData<byte>();
                    for (int i = 0; i < before.Length; i++)
                        if (before[i] != after[i]) throw new InvalidOperationException(shadows + " light changed at byte " + i);
                }
                Capture("unshadowed-sparse", 3);
            }
            finally
            {
                // Closing the preview invokes the effect's OnDisable and restores shader state.
                EditorSceneManager.ClosePreviewScene(preview);
                RenderTexture.active = previousTarget;
                QualitySettings.renderPipeline = previousQuality;
                if (pixels) Object.DestroyImmediate(pixels);
                if (target) { target.Release(); Object.DestroyImmediate(target); }
                if (material) Object.DestroyImmediate(material);
                if (temporaryPipeline) Object.DestroyImmediate(temporaryPipeline);
            }
            report.keywordRestored = keyword == Shader.IsKeywordEnabled(MoonStochasticLighting.Keyword);
            report.parametersRestored = parameters == Shader.GetGlobalVector("_MoonStochasticShadowParams");
            report.activeScenePreserved = SceneManager.GetActiveScene() == active && active.isDirty == dirty;
            File.WriteAllText(Output + "/report.json", JsonUtility.ToJson(report, true));
            if (!report.keywordRestored || !report.parametersRestored || !report.activeScenePreserved)
                throw new InvalidOperationException("Shadow review did not restore Editor state.");
            UnityEngine.Debug.Log("Shadow comparison complete: " + Output + "/report.json");
        }
    }
}
