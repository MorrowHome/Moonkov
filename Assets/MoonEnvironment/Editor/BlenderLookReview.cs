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
    [InitializeOnLoad]
    internal static class LowSampleSceneSaveGuard
    {
        static LowSampleSceneSaveGuard()
        {
            EditorSceneManager.sceneSaving += (scene, path) =>
            {
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var scope in root.GetComponentsInChildren<MoonLowSampleMaterialScope>(true)) scope.Restore();
            };
            EditorSceneManager.sceneSaved += scene =>
            {
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var scope in root.GetComponentsInChildren<MoonLowSampleMaterialScope>(true)) scope.Refresh();
            };
        }
    }

    /// <summary>Same-camera comparison on the actual imported map; never saves or edits source materials.</summary>
    public static class BlenderLookReview
    {
        public const string Output = "LocalData/BlenderLookStudy";
        private const string DisplayScene = "Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity";
        public const string LookScene = "Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_LowSample_Display.unity";

        [MenuItem("Tools/Moon Environment/Blender Look/Create look display scene")]
        public static void CreateLookScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || File.Exists(LookScene))
                throw new InvalidOperationException("Run in Edit Mode; the destination scene must not already exist.");
            var active = SceneManager.GetActiveScene();
            if (SceneManager.GetSceneByPath(DisplayScene).isLoaded)
                throw new InvalidOperationException("Close the original display scene first.");
            var scene = EditorSceneManager.OpenScene(DisplayScene, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var roots = scene.GetRootGameObjects();
                var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single();
                var effect = camera.gameObject.AddComponent<MoonStochasticLighting>();
                SetPreviewDefaults(effect);
                camera.transform.position = new Vector3(1840, 255, 2255);
                camera.transform.LookAt(new Vector3(1873, 240, 2298));
                camera.fieldOfView = 48;
                foreach (var root in roots)
                {
                    if (!root.GetComponentsInChildren<MeshRenderer>(true).Any()) continue;
                    var scope = root.AddComponent<MoonLowSampleMaterialScope>();
                    scope.Configure(Shader.Find("MoonEnvironment/Low Sample Lit"));
                }
                SetReferenceFill();
                if (!EditorSceneManager.SaveScene(scene, LookScene)) throw new IOException("Could not save low sample display scene.");
            }
            finally
            {
                SceneManager.SetActiveScene(active);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Tools/Moon Environment/Blender Look/Render map comparison")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Run in idle Edit Mode.");
            var active = SceneManager.GetActiveScene();
            bool dirty = active.isDirty;
            if (SceneManager.GetSceneByPath(DisplayScene).isLoaded)
                throw new InvalidOperationException("Close the LR06 display scene before running the isolated comparison.");
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline) throw new InvalidOperationException("URP required.");
            var quality = QualitySettings.renderPipeline;
            var shader = Shader.Find("MoonEnvironment/Low Sample Lit");
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Low Sample Lit shader unavailable.");
            Directory.CreateDirectory(Output);
            var clones = new Dictionary<Material, Material>();
            var scene = default(Scene);
            UniversalRenderPipelineAsset temporaryPipeline = null;
            RenderTexture target = null;
            Texture2D pixels = null;
            var oldTarget = RenderTexture.active;
            var noiseBefore = Shader.GetGlobalVector("_MoonLightingNoiseParams");
            var shadowBefore = Shader.GetGlobalVector("_MoonStochasticShadowParams");
            bool keywordBefore = Shader.IsKeywordEnabled(MoonStochasticLighting.Keyword);
            int compatibleMaterials = 0;
            try
            {
                scene = EditorSceneManager.OpenScene(DisplayScene, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                temporaryPipeline = Object.Instantiate(pipeline);
                temporaryPipeline.hideFlags = HideFlags.HideAndDontSave;
                temporaryPipeline.shadowDistance = 200;
                temporaryPipeline.mainLightShadowmapResolution = 2048;
                // MSAA filters geometry coverage, while interior lighting grain remains single-pixel.
                temporaryPipeline.msaaSampleCount = 4;
                QualitySettings.renderPipeline = temporaryPipeline;
                var roots = scene.GetRootGameObjects();
                var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single();
                camera.enabled = false;
                camera.scene = scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.fieldOfView = 48;
                camera.allowHDR = true;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                var effect = camera.gameObject.AddComponent<MoonStochasticLighting>();
                SetPreviewDefaults(effect);
                effect.enabled = false;
                var lights = roots.SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToArray();
                var sun = lights.Single(l => l.type == LightType.Directional);
                var originalAmbient = RenderSettings.ambientLight;
                var originalProbe = RenderSettings.ambientProbe;
                var originalSunColor = sun.color;
                float originalSunIntensity = sun.intensity;
                var renderers = roots.SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
                var slots = renderers.ToDictionary(r => r, r => r.sharedMaterials);
                target = new RenderTexture(960, 600, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                target.Create();
                pixels = new Texture2D(960, 600, TextureFormat.RGB24, false);
                byte[] Capture(string label)
                {
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 960, 600), 0, 0);
                    pixels.Apply();
                    if (label != null) File.WriteAllBytes(Output + "/" + label + ".png", pixels.EncodeToPNG());
                    return pixels.GetRawTextureData<byte>().ToArray();
                }
                void Materials(bool sampled)
                {
                    foreach (var pair in slots)
                        pair.Key.sharedMaterials = pair.Value.Select(m =>
                        {
                            if (!sampled || !m || m.shader.name != "Universal Render Pipeline/Lit" || m.GetFloat("_Surface") > .5f) return m;
                            if (!clones.TryGetValue(m, out var clone))
                            {
                                clone = new Material(m) { shader = shader, hideFlags = HideFlags.HideAndDontSave };
                                clones.Add(m, clone);
                            }
                            return clone;
                        }).ToArray();
                }
                foreach (var view in new[] { "building", "corridor" })
                {
                    camera.transform.position = view == "building" ? new Vector3(1840, 255, 2255) : new Vector3(1993.8f, 254.2f, 2457.6f);
                    camera.transform.LookAt(view == "building" ? new Vector3(1873, 240, 2298) : new Vector3(1998.4f, 255.3f, 2463));
                    Materials(false);
                    effect.enabled = false;
                    RenderSettings.ambientLight = originalAmbient;
                    RenderSettings.ambientProbe = originalProbe;
                    sun.color = originalSunColor; sun.intensity = originalSunIntensity;
                    Capture("unity_" + view + "_before");
                    // Reduce the unoccluded flat fill. No claim that this supplies Cycles GI.
                    SetReferenceFill();
                    sun.color = new Color(.8f, .88f, 1); sun.intensity = .8f;
                    Materials(true);
                    Capture("unity_" + view + "_lighting");
                    effect.enabled = true;
                    var first = Capture("unity_" + view + "_sampled");
                    var second = Capture(null);
                    if (!first.SequenceEqual(second)) throw new InvalidOperationException("Frozen noise is not deterministic: " + view);
                    camera.transform.position += new Vector3(.08f, 0, 0);
                    Capture("unity_" + view + "_moved");
                }
                compatibleMaterials = clones.Count;
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        throw new InvalidOperationException(message.message);
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                QualitySettings.renderPipeline = quality;
                RenderTexture.active = oldTarget;
                foreach (var clone in clones.Values) Object.DestroyImmediate(clone);
                if (pixels) Object.DestroyImmediate(pixels);
                if (target) { target.Release(); Object.DestroyImmediate(target); }
                if (temporaryPipeline) Object.DestroyImmediate(temporaryPipeline);
            }
            if (active != SceneManager.GetActiveScene() || active.isDirty != dirty ||
                Shader.GetGlobalVector("_MoonLightingNoiseParams") != noiseBefore ||
                Shader.GetGlobalVector("_MoonStochasticShadowParams") != shadowBefore ||
                Shader.IsKeywordEnabled(MoonStochasticLighting.Keyword) != keywordBefore)
                throw new InvalidOperationException("Comparison did not restore original state.");
            File.WriteAllText(Output + "/UnityLookReport.json", JsonUtility.ToJson(new Report
            {
                device = SystemInfo.graphicsDeviceName, compatibleMaterials = compatibleMaterials,
                originalScene = active.path, originalStateRestored = true
            }, true));
            Debug.Log("Blender look comparison complete: " + Path.GetFullPath(Output));
        }

        private static void SetReferenceFill()
        {
            RenderSettings.ambientLight = new Color(.035f, .04f, .055f);
            // Editor render requests in the same tick otherwise reuse the old SH.
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(new Color(.0024f, .003f, .0048f));
            RenderSettings.ambientProbe = probe;
        }

        public static void SetPreviewDefaults(MoonStochasticLighting effect)
        {
            var settings = new SerializedObject(effect);
            settings.FindProperty("lightSamplingProbability").floatValue = 1;
            settings.FindProperty("directNoise").floatValue = .38f;
            settings.FindProperty("indirectNoise").floatValue = .7f;
            settings.FindProperty("grainPixels").floatValue = 1;
            settings.FindProperty("noiseUpdatesPerSecond").floatValue = 0;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/Moon Environment/Blender Look/Enable on selected camera")]
        private static void EnableSelectedCamera()
        {
            var camera = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<Camera>() : null;
            if (!camera) throw new InvalidOperationException("Select a Camera first.");
            var effect = camera.GetComponent<MoonStochasticLighting>();
            if (!effect) effect = Undo.AddComponent<MoonStochasticLighting>(camera.gameObject);
            Undo.RecordObject(effect, "Low sample lighting preset");
            SetPreviewDefaults(effect);
            effect.enabled = true;
            EditorUtility.SetDirty(effect);
        }

        [Serializable] private sealed class Report
        {
            public string device, originalScene;
            public int compatibleMaterials;
            public bool originalStateRestored;
            public string boundary = "Real imported LR06 map, temporary material copies, 960x600, 4x MSAA, no post processing. Stable lighting variation is an artistic approximation, not Cycles paths or transferred area-light GI. Existing source material approximations, UV defects and proxy lights remain. Original scene and material assets are not saved. No gameplay, Player or target-device performance acceptance.";
        }
    }
}
