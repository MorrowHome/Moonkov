using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Material study on the saved gameplay geometry, isolated from the user's open scenes.</summary>
    public static class InkArchitectureReview
    {
        public const string Output = "LocalData/InkStyleReview";
        private const string ShaderPath = "Assets/MoonEnvironment/Shaders/InkArchitecture.shader";

        [Serializable] private sealed class Frame
        {
            public string file;
            public float darkPercent, brightPercent, colourPercent;
        }
        [Serializable] private sealed class Report
        {
            public string utc, sourceScene, boundary, error;
            public bool editorStatePreserved, sourceMaterialsPreserved;
            public int renderers, materials, paper, ink, accent, ground;
            public List<Frame> frames = new List<Frame>();
        }
        private struct View
        {
            public string name;
            public Vector3 position, target;
            public View(string name, Vector3 position, Vector3 target)
            { this.name = name; this.position = position; this.target = target; }
        }

        [MenuItem("Tools/Moon Environment/Ink Architecture/Render formal map comparison")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请在编辑模式空闲时运行。不要为预览启动 Play。");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Ink Architecture shader unavailable.");
            Directory.CreateDirectory(Output);
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("O"), sourceScene = SourceWorldSceneInstall.ScenePath,
                boundary = "正式地图的隔离 Editor 材质试样。相机、模型和分辨率相同。未保存场景或改写源材质，未进入 Play。未验证角色、战斗可读性、运行时性能或 Player 构建。"
            };
            var active = SceneManager.GetActiveScene();
            var sceneStates = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .ToDictionary(s => s.handle, s => s.isDirty);
            var previousTarget = RenderTexture.active;
            var previousMaps = LightmapSettings.lightmaps;
            var previousMode = LightmapSettings.lightmapsMode;
            var previousProbes = LightmapSettings.lightProbes;
            var previousPipeline = QualitySettings.renderPipeline;
            bool previousAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            var originals = new Dictionary<Material, bool>();
            var clones = new Dictionary<Material, Material>();
            var scene = default(Scene);
            SourceWorldLighting lighting = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                // Preview scenes do not replace, save, or merge the currently open scene.
                scene = EditorSceneManager.OpenPreviewScene(SourceWorldSceneInstall.ScenePath);
                var roots = scene.GetRootGameObjects();
                var environment = roots.SelectMany(r => r.GetComponentsInChildren<MoonSceneLighting>(true)).Single();
                var pipeline = new SerializedObject(environment).FindProperty("pipeline").objectReferenceValue as UniversalRenderPipelineAsset;
                if (!pipeline) throw new InvalidOperationException("正式地图未配置 URP。");
                // MoonSceneLighting applies this pipeline in Play; preview scenes do not
                // execute that lifecycle. Borrow the same asset without editing it.
                QualitySettings.renderPipeline = pipeline;
                lighting = roots.SelectMany(r => r.GetComponentsInChildren<SourceWorldLighting>(true)).Single();
                lighting.Apply();
                var sourceRenderers = lighting.Bindings.Select(b => b.renderer).ToArray();
                report.renderers = sourceRenderers.Length;
                foreach (var material in sourceRenderers.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct())
                    originals.Add(material, EditorUtility.IsDirty(material));
                report.materials = originals.Count;
                var root = new GameObject("Ink architecture review camera");
                SceneManager.MoveGameObjectToScene(root, scene);
                var camera = root.AddComponent<Camera>();
                camera.enabled = false; camera.scene = scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.fieldOfView = 60; camera.nearClipPlane = .15f; camera.farClipPlane = 1600;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                // An sRGB LDR destination is required for PNG readback. Reading the HDR
                // target straight into a PNG would make the baseline artificially dark.
                target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                target.Create();
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                var views = new[]
                {
                    new View("exterior", new Vector3(1840,255,2255), new Vector3(1873,240,2298)),
                    new View("passage", new Vector3(1993.8f,254.2f,2457.6f), new Vector3(1998.4f,255.3f,2463))
                };
                void Capture(View view, string mode)
                {
                    camera.transform.position = view.position; camera.transform.LookAt(view.target);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                    string file = Output + "/" + view.name + "-" + mode + ".png";
                    File.WriteAllBytes(file, pixels.EncodeToPNG());
                    var colours = pixels.GetPixels32();
                    int dark = 0, bright = 0, colour = 0;
                    foreach (var c in colours)
                    {
                        int max = Math.Max(c.r, Math.Max(c.g,c.b)), min = Math.Min(c.r, Math.Min(c.g,c.b));
                        float value = (c.r*.2126f+c.g*.7152f+c.b*.0722f)/255;
                        if (value < .08f) dark++;
                        if (value > .75f) bright++;
                        if (max-min > 25) colour++;
                    }
                    report.frames.Add(new Frame { file = file, darkPercent = 100f*dark/colours.Length,
                        brightPercent = 100f*bright/colours.Length, colourPercent = 100f*colour/colours.Length });
                }
                foreach (var view in views) Capture(view, "before");
                // The print material owns its value structure independently of the old
                // HDR lighting. Keep the authored bake intact for the original look.
                lighting.Restore();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.allowHDR = false;
                foreach (var renderer in sourceRenderers)
                {
                    renderer.lightmapIndex = -1;
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                    {
                        if (!original) return original;
                        if (clones.TryGetValue(original, out var cached)) return cached;
                        int role = Role(original.name);
                        var material = new Material(shader) { name = original.name + " (ink preview)", hideFlags = HideFlags.HideAndDontSave };
                        clones.Add(original, material);
                        material.SetFloat("_Role", role);
                        // ShaderLab Color properties perform the linear-space conversion.
                        material.SetColor("_PaperColor", new Color32(221,221,219,255));
                        material.SetColor("_InkColor", new Color32(6,6,8,255));
                        material.SetColor("_AccentColor", new Color32(55,38,177,255));
                        material.SetFloat("_Wear", role == 1 ? 0 : .58f);
                        if (original.renderQueue >= (int)RenderQueue.Transparent)
                        {
                            material.SetFloat("_Opacity", .22f);
                            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                            material.SetFloat("_ZWrite", 0);
                            material.SetOverrideTag("RenderType", "Transparent");
                            material.renderQueue = (int)RenderQueue.Transparent;
                        }
                        if (role == 0) report.paper++; else if (role == 1) report.ink++;
                        else if (role == 2) report.accent++; else report.ground++;
                        return material;
                    }).ToArray();
                }
                // Rebuild the render pipeline's camera/material state after the HDR-to-LDR switch.
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                foreach (var view in views) Capture(view, "ink");
                if (report.frames.Where(f => f.file.EndsWith("-ink.png")).Any(f => f.darkPercent > 99))
                    throw new InvalidOperationException("材质预览为空或全黑。不能把此帧当作有效结果。");
                var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
                if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(e => e.message)));
            }
            catch (Exception exception)
            {
                report.error = exception.ToString();
                throw;
            }
            finally
            {
                if (lighting) lighting.Restore();
                RenderTexture.active = previousTarget;
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                LightmapSettings.lightmaps = previousMaps; LightmapSettings.lightmapsMode = previousMode;
                LightmapSettings.lightProbes = previousProbes;
                QualitySettings.renderPipeline = previousPipeline;
                ShaderUtil.allowAsyncCompilation = previousAsyncCompilation;
                foreach (var material in clones.Values) if (material) Object.DestroyImmediate(material);
                if (pixels) Object.DestroyImmediate(pixels);
                if (target) { target.Release(); Object.DestroyImmediate(target); }
                report.editorStatePreserved = SceneManager.GetActiveScene() == active && QualitySettings.renderPipeline == previousPipeline &&
                    SceneManager.sceneCount == sceneStates.Count && Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                        .All(s => sceneStates.TryGetValue(s.handle, out bool dirty) && dirty == s.isDirty);
                report.sourceMaterialsPreserved = originals.All(pair => pair.Key && EditorUtility.IsDirty(pair.Key) == pair.Value);
                File.WriteAllText(Output + "/report.json", JsonUtility.ToJson(report, true));
            }
            if (!report.editorStatePreserved || !report.sourceMaterialsPreserved)
                throw new InvalidOperationException("预览未完整恢复编辑器状态。请检查报告。");
            Debug.Log("黑白建筑材质预览完成：" + Path.GetFullPath(Output));
        }

        private static int Role(string name)
        {
            string n = name.ToLowerInvariant();
            // A sparse, explicit accent family; lights and every safety rail do not all become blue.
            if (n.Contains("wayfinding") || n.Contains("service_teal") || n.Contains("restricted_engineering_orange") ||
                n.Contains("screenamber") || n.Contains("key_state") || n.Contains("r8_restrained_safety_ochre") ||
                n.Contains("r14_orange_coating") || n.Contains("nr01_muted_ochre")) return 2;
            if (n.Contains("regolith") || n.Contains("ground") || n.Contains("dry_dust_concrete_floor")) return 3;
            if (n.Contains("graphite") || n.Contains("dark") || n.Contains("gasket") || n.Contains("elastomer") ||
                n.Contains("recessed") || n.Contains("structural_steel") || n.Contains("glass") || n.Contains("glazing") ||
                n.Contains("screenink") || n.Contains("lettering")) return 1;
            return 0;
        }
    }
}
