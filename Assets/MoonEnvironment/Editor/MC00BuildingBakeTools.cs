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
using BakeManifest = Unity.MP_FPS.Moon.Editor.MC00BuildingBakeGeometry.Manifest;

namespace Unity.MP_FPS.Moon.Editor
{
    // The complete building is baked for each independent source circuit. This
    // preserves light spilling across rooms without exponential switch states.
    [InitializeOnLoad]
    public static class MC00BuildingBakeTools
    {
        public const string Folder = "Assets/MoonEnvironment/Variants/MC00BakedBuilding";
        public const string ScenePath = Folder + "/MC00Bake.unity";
        const string DataPath = Folder + "/Lighting.asset";
        const string JobPath = Folder + "/bake-job.json";
        public static string Status { get; private set; } = "Idle";
        [Serializable] class Job { public bool running, environment; public int next; public Color ambientFill; }
        [Serializable] class SceneBackup { public SceneSetup[] scenes; }
        [Serializable] class QualityBackup { public string pipeline, startScene; public int aa; public ShadowmaskMode shadowmask; }
        static Job job;
        static BakeManifest manifest;
        static BuildingBakedLightingData data;
        static double restartAt;

        static MC00BuildingBakeTools()
        {
            Lightmapping.bakeCompleted += Completed;
            EditorApplication.update += Tick;
            EditorApplication.delayCall += Resume;
        }
        public static BakeManifest ReadManifest() => JsonUtility.FromJson<BakeManifest>(File.ReadAllText(Folder + "/manifest.json"));
        static void Resume()
        {
            if (!File.Exists(JobPath)) return;
            job = JsonUtility.FromJson<Job>(File.ReadAllText(JobPath));
            if (!job.running) return;
            manifest = ReadManifest();
            EnsureHDREncoding();
            data = AssetDatabase.LoadAssetAtPath<BuildingBakedLightingData>(DataPath);
            Status = "Resumed baking circuit " + job.next;
            if (!Lightmapping.isRunning) restartAt = EditorApplication.timeSinceStartup + 2;
        }
        static void SaveJob() => File.WriteAllText(JobPath, JsonUtility.ToJson(job, true));
        static void Fail(Exception e)
        {
            if (job != null) { job.running = false; SaveJob(); }
            restartAt = 0; Status = "FAILED: " + e.Message; Debug.LogException(e);
        }

        [MenuItem("Tools/Moon Environment/MC00 Building Bake/2 - Bake independent circuits")]
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning ||
                SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open the completed MC00 bake scene in Edit Mode.");
            manifest = ReadManifest();
            if (manifest.status != "Ready") throw new InvalidOperationException("Finish geometry and UV preparation first.");
            EnsureHDREncoding();
            if (manifest.chunks.Count == 0 || manifest.roomIds.Length == 0) throw new InvalidOperationException("No geometry/circuits.");
            var all = UnityEngine.Object.FindObjectsByType<MeshFilter>();
            if (all.Any(m => m.gameObject.isStatic && (!m.sharedMesh || m.sharedMesh.uv2.Length == 0)))
                throw new InvalidOperationException("Finish generating all lightmap UVs first.");
            data = AssetDatabase.LoadAssetAtPath<BuildingBakedLightingData>(DataPath);
            if (!data)
            {
                data = ScriptableObject.CreateInstance<BuildingBakedLightingData>();
                data.rooms = manifest.roomIds.Select(id => new BuildingBakedLightingData.Room { id = id }).ToArray();
                AssetDatabase.CreateAsset(data, DataPath);
            }
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(Folder + "/LightingSettings.lighting");
            if (!settings)
            {
                settings = new LightingSettings { name = "MC00 independent room contributions", bakedGI = true, realtimeGI = false,
                    mixedBakeMode = MixedLightingMode.IndirectOnly, directionalityMode = LightmapsMode.NonDirectional,
                    lightmapper = LightingSettings.Lightmapper.ProgressiveGPU, lightmapResolution = 6,
                    lightmapMaxSize = 1024, lightmapPadding = 4, directSampleCount = 32,
                    indirectSampleCount = 64, environmentSampleCount = 32, maxBounces = 2 };
                AssetDatabase.CreateAsset(settings, Folder + "/LightingSettings.lighting");
            }
            var geometryParameters = AssetDatabase.LoadAssetAtPath<LightmapParameters>(Folder + "/MC00Geometry.giparams");
            if (!geometryParameters)
            {
                var defaults = LightmapParameters.GetLightmapParametersForLightingSettings(settings);
                geometryParameters = defaults ? UnityEngine.Object.Instantiate(defaults) : new LightmapParameters();
                geometryParameters.name = "MC00Geometry";
                AssetDatabase.CreateAsset(geometryParameters, Folder + "/MC00Geometry.giparams");
            }
            // MC00 sits kilometres from the origin. The default 0.1 mm ray offset
            // causes self-occlusion blotches there; 5 mm was verified on its walls.
            // Retain every other lightmap parameter and the authored material/UV data.
            geometryParameters.pushoff = .005f;
            LightmapParameters.SetLightmapParametersForLightingSettings(geometryParameters, settings);
            EditorUtility.SetDirty(geometryParameters); EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(geometryParameters); AssetDatabase.SaveAssetIfDirty(settings);
            Lightmapping.lightingSettings = settings;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0; RenderSettings.reflectionIntensity = 0; RenderSettings.skybox = null;
            string qualityBackup = MC00BuildingBakeGeometry.BackupFolder + "/quality.json";
            if (!File.Exists(qualityBackup)) File.WriteAllText(qualityBackup, JsonUtility.ToJson(new QualityBackup
            { pipeline = AssetDatabase.GetAssetPath(QualitySettings.renderPipeline), startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                aa = QualitySettings.antiAliasing, shadowmask = QualitySettings.shadowmaskMode }, true));
            var pipeline = Pipeline(); QualitySettings.renderPipeline = pipeline;
            EnsureProbes();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            job = new Job { running = true, next = data.baseline != null && data.baseline.probes ? 0 : -1 };
            while (job.next >= 0 && job.next < data.rooms.Length && data.rooms[job.next].onState != null && data.rooms[job.next].onState.probes) job.next++;
            SaveJob(); Next();
        }

        public static UniversalRenderPipelineAsset Pipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Folder + "/MC00BakedPipeline.asset");
            if (pipeline) return pipeline;
            pipeline = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/MoonEnvironment/Settings/MoonPipeline.asset"));
            pipeline.name = "MC00 baked lighting pipeline";
            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("m_LightProbeSystem").intValue = (int)LightProbeSystem.LegacyLightProbes;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(pipeline, Folder + "/MC00BakedPipeline.asset");
            return pipeline;
        }

        public static void BakeEnvironment(Color ambientFill)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning ||
                SceneManager.GetActiveScene().path != ScenePath || job != null && job.running)
                throw new InvalidOperationException("Finish the circuit bakes first.");
            manifest = ReadManifest(); data = AssetDatabase.LoadAssetAtPath<BuildingBakedLightingData>(DataPath);
            if (!data || data.baseline == null || !data.baseline.probes || data.rooms.Any(r => r.onState == null || !r.onState.probes))
                throw new InvalidOperationException("Complete every room contribution before baking the environment.");
            job = new Job { running = true, environment = true, next = manifest.roomIds.Length, ambientFill = ambientFill };
            SaveJob(); Next();
        }

        static void EnsureHDREncoding()
        {
            // Unity exposes this through Player Settings but the C# binding is internal.
            // Use the installed Editor's enum rather than duplicating serialized settings.
            var method = typeof(PlayerSettings).GetMethod("SetLightmapEncodingQualityForPlatform",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (method == null) throw new NotSupportedException("This Editor has no lightmap encoding setter.");
            var quality = Enum.Parse(method.GetParameters()[1].ParameterType, "High");
            method.Invoke(null, new[] { (object)BuildTarget.StandaloneWindows64, quality });
            AssetDatabase.SaveAssets();
        }

        static void EnsureProbes()
        {
            if (UnityEngine.Object.FindAnyObjectByType<LightProbeGroup>()) return;
            var positions = new HashSet<Vector3>();
            foreach (var group in manifest.lights.GroupBy(l => l.roomId))
            {
                var lamps = group.Select(l => GameObject.Find(l.cloneName).transform.position).ToArray();
                Bounds b = new(lamps[0], Vector3.zero);
                foreach (var p in lamps) b.Encapsulate(p);
                b.Expand(new Vector3(10, 0, 10));
                for (float y = b.min.y - 4.8f; y <= b.max.y - .3f; y += 1.5f)
                    for (float x = b.min.x; x <= b.max.x; x += 3)
                        for (float z = b.min.z; z <= b.max.z; z += 3)
                            positions.Add(new Vector3(Mathf.Round(x * 2) / 2, Mathf.Round(y * 2) / 2, Mathf.Round(z * 2) / 2));
            }
            var go = new GameObject("MC00 baked light probes");
            // Black guard probes bound the building's influence for exterior characters.
            Bounds buildingBounds = manifest.rooms[0].bounds;
            foreach (var room in manifest.rooms) buildingBounds.Encapsulate(room.bounds);
            buildingBounds.Expand(100);
            foreach (float x in new[] { buildingBounds.min.x, buildingBounds.center.x, buildingBounds.max.x })
                foreach (float z in new[] { buildingBounds.min.z, buildingBounds.center.z, buildingBounds.max.z })
                    foreach (float y in new[] { buildingBounds.min.y, buildingBounds.max.y }) positions.Add(new Vector3(x, y, z));
            go.AddComponent<LightProbeGroup>().probePositions = positions.ToArray();
        }

        static void Tick()
        {
            if (restartAt <= 0 || EditorApplication.timeSinceStartup < restartAt || EditorApplication.isCompiling || Lightmapping.isRunning) return;
            restartAt = 0;
            try { Next(); } catch (Exception e) { Fail(e); }
        }
        static void Next()
        {
            if (job == null || !job.running) return;
            if (!job.environment && job.next >= manifest.roomIds.Length)
            {
                job.running = false; SaveJob();
                Status = "Complete: baseline + " + manifest.roomIds.Length + " independent circuits";
                Debug.Log(Status); AssetDatabase.SaveAssets(); return;
            }
            string active = job.environment || job.next < 0 ? null : manifest.roomIds[job.next];
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = job.environment ? job.ambientFill : Color.black;
            RenderSettings.ambientIntensity = 1;
            RenderSettings.reflectionIntensity = 0; RenderSettings.skybox = null;
            foreach (var record in manifest.lights)
            {
                var light = GameObject.Find(record.cloneName).GetComponent<Light>();
                light.enabled = record.roomId == active;
                light.lightmapBakeType = record.key ? LightmapBakeType.Mixed : LightmapBakeType.Baked;
            }
            Status = job.environment ? "Baking formal ambient environment" :
                "Baking " + (active ?? "all-off baseline") + " (" + (job.next + 2) + "/" + (manifest.roomIds.Length + 1) + ")";
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); SaveJob();
            if (!Lightmapping.BakeAsync()) throw new InvalidOperationException("Unity did not start the bake.");
        }
        static void Completed()
        {
            if (job == null || !job.running || SceneManager.GetActiveScene().path != ScenePath) return;
            try
            {
                Capture();
                if (job.environment)
                {
                    job.running = false; SaveJob();
                    Status = "Complete: black baseline, 19 circuits and formal ambient environment";
                    Debug.Log(Status); return;
                }
                job.next++; SaveJob();
                Status = "Captured " + (job.next + 1) + "/" + (manifest.roomIds.Length + 1);
                restartAt = EditorApplication.timeSinceStartup + 1;
            }
            catch (Exception e) { Fail(e); }
        }
        static void Capture()
        {
            bool zeroBaseline = !job.environment && job.next < 0;
            if (zeroBaseline) ValidateZeroBaselineScene();
            if (LightmapSettings.lightmaps.Length == 0 || !LightmapSettings.lightProbes || LightmapSettings.lightProbes.count == 0)
                throw new InvalidOperationException("Bake returned no lightmaps/probes.");
            foreach (var chunk in manifest.chunks)
            {
                var renderer = GameObject.Find(chunk.name).GetComponent<MeshRenderer>();
                if (job.next < 0) { chunk.lightmapIndex = renderer.lightmapIndex; chunk.scaleOffset = renderer.lightmapScaleOffset; }
                else if (chunk.lightmapIndex != renderer.lightmapIndex || (chunk.scaleOffset - renderer.lightmapScaleOffset).sqrMagnitude > .00000001f)
                    throw new InvalidOperationException("Lightmap layout changed: " + chunk.name + ". Contributions cannot be composed.");
            }
            if (job.next >= 0)
            {
                if (data.baseline.colorMaps.Length != LightmapSettings.lightmaps.Length || data.baseline.probes.count != LightmapSettings.lightProbes.count)
                    throw new InvalidOperationException("Atlas/probe counts changed.");
                var a = data.baseline.probes.positions; var b = LightmapSettings.lightProbes.positions;
                for (int i = 0; i < a.Length; i++) if ((a[i] - b[i]).sqrMagnitude > .00000001f) throw new InvalidOperationException("Probe layout changed.");
            }
            string folder = Folder + "/States/" + (job.environment ? "Environment" : job.next < 0 ? "Baseline" : "Circuit" + job.next.ToString("D2"));
            // An interrupted capture may have imported only some atlases. Preserve
            // that attempt and publish the retry only after every asset is ready.
            if (Directory.Exists(folder)) folder = AssetDatabase.GenerateUniqueAssetPath(folder);
            Directory.CreateDirectory(folder); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var state = new BuildingBakedLightingData.State { colorMaps = new Texture2D[LightmapSettings.lightmaps.Length] };
            for (int i = 0; i < state.colorMaps.Length; i++)
            {
                var source = LightmapSettings.lightmaps[i].lightmapColor;
                if (job.next >= 0 && (source.width != data.baseline.colorMaps[i].width || source.height != data.baseline.colorMaps[i].height))
                    throw new InvalidOperationException("Atlas dimensions changed.");
                string path = folder + "/Atlas" + i + ".exr";
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path)) throw new IOException("Cannot preserve " + path);
                // Raw EXR radiance is imported as linear BC6H; never sum encoded RGBM.
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None; importer.mipmapEnabled = true;
                importer.streamingMipmaps = false; importer.wrapMode = TextureWrapMode.Clamp;
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true,
                    maxTextureSize = 1024, format = TextureImporterFormat.BC6H, textureCompression = TextureImporterCompression.CompressedHQ });
                importer.SaveAndReimport();
                state.colorMaps[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            state.probes = UnityEngine.Object.Instantiate(LightmapSettings.lightProbes);
            state.probes.name = "MC00 circuit probes"; state.probes.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(state.probes, folder + "/Probes.asset");
            if (zeroBaseline) WriteZeroBaseline(state);
            if (job.environment) data.environment = state;
            else if (job.next < 0) data.baseline = state; else data.rooms[job.next].onState = state;
            File.WriteAllText(Folder + "/manifest.json", JsonUtility.ToJson(manifest, true));
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
        }

        // Repair an already captured reference without rebaking any room or changing
        // its atlas GUIDs, dimensions, UV bindings, or native probe positions.
        public static void ExportZeroBaseline()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning || job != null && job.running)
                throw new InvalidOperationException("Finish the bake before exporting the zero baseline.");
            ValidateZeroBaselineScene();
            var lighting = AssetDatabase.LoadAssetAtPath<BuildingBakedLightingData>(DataPath);
            WriteZeroBaseline(lighting ? lighting.baseline : null);
            Status = "Exported physical zero baseline; room and environment states unchanged";
        }

        static void ValidateZeroBaselineScene()
        {
            var scene = SceneManager.GetActiveScene();
            var ambient = RenderSettings.ambientLight;
            if (SceneManager.sceneCount != 1 || scene.path != ScenePath || RenderSettings.ambientMode != AmbientMode.Flat ||
                ambient.r != 0 || ambient.g != 0 || ambient.b != 0 || RenderSettings.skybox)
                throw new InvalidOperationException("A zero baseline requires only the MC00 bake scene and a black Flat environment without a skybox.");
            var ambientProbe = RenderSettings.ambientProbe;
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++)
                if (!(Mathf.Abs(ambientProbe[c, k]) <= .000001f))
                    throw new InvalidOperationException("The ambient probe must be zero before exporting the zero baseline.");
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentsInChildren<Light>(true).Any(light => light.enabled))
                    throw new InvalidOperationException("Disable every bake-scene light before exporting the zero baseline.");
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.ContributeGI) == 0) continue;
                    foreach (var material in renderer.sharedMaterials)
                        if (material && (material.globalIlluminationFlags & (MaterialGlobalIlluminationFlags.BakedEmissive |
                            MaterialGlobalIlluminationFlags.EmissiveIsBlack)) == MaterialGlobalIlluminationFlags.BakedEmissive)
                            throw new InvalidOperationException("A zero baseline cannot discard baked emission: " + material.name);
                }
            }
        }

        static void WriteZeroBaseline(BuildingBakedLightingData.State state)
        {
            if (state == null || state.colorMaps == null || state.colorMaps.Length == 0 || !state.probes || state.probes.count == 0)
                throw new InvalidOperationException("Capture a complete baseline layout before exporting zero radiance.");
            var paths = state.colorMaps.Select(map => map ? AssetDatabase.GetAssetPath(map) : "").ToArray();
            if (paths.Any(path => !path.StartsWith(Folder + "/States/", StringComparison.Ordinal) ||
                !path.EndsWith(".exr", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Baseline maps must be captured MC00 EXR assets.");
            for (int i = 0; i < paths.Length; i++)
            {
                var map = state.colorMaps[i];
                var zero = new Texture2D(map.width, map.height, TextureFormat.RGBAFloat, false, true);
                try
                {
                    var pixels = new Color[map.width * map.height];
                    // Alpha 1 marks valid texels; RGB alone represents zero radiance.
                    for (int p = 0; p < pixels.Length; p++) pixels[p] = Color.black;
                    zero.SetPixels(pixels); zero.Apply(false, false);
                    File.WriteAllBytes(paths[i], zero.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP));
                }
                finally { UnityEngine.Object.DestroyImmediate(zero); }
                AssetDatabase.ImportAsset(paths[i], ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            state.probes.bakedProbes = new SphericalHarmonicsL2[state.probes.count];
            EditorUtility.SetDirty(state.probes); AssetDatabase.SaveAssetIfDirty(state.probes);
        }

        [MenuItem("Tools/Moon Environment/MC00 Building Bake/3 - Install in loaded formal map")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) throw new InvalidOperationException("Finish bake and Play first.");
            manifest = ReadManifest(); data = AssetDatabase.LoadAssetAtPath<BuildingBakedLightingData>(DataPath);
            if (!data || data.baseline == null || !data.baseline.probes || data.rooms.Any(r => r.onState == null || !r.onState.probes)) throw new InvalidOperationException("All circuits must be baked before installing.");
            if (data.environment == null || !data.environment.probes) throw new InvalidOperationException("Bake the formal ambient environment before installing.");
            var root = GameObject.Find("MC00_ROOT_Standard_YUp_Frame");
            if (!root) throw new InvalidOperationException("Load the formal map first.");
            if (root.scene.path != manifest.sourceScene || (root.transform.lossyScale - Vector3.one).sqrMagnitude > .000001f)
                throw new InvalidOperationException("The formal building scene/scale changed since baking.");
            if (root.GetComponentInChildren<BuildingBakedLighting>(true)) throw new InvalidOperationException("Already installed; refusing to duplicate geometry.");
            // Resolve every dependency before changing the source representation.
            // Authored sign names contain '/', so Transform.Find cannot parse these
            // stored paths. Build the same literal path keys used by preparation.
            var nodes = root.GetComponentsInChildren<Transform>(true).Where(t => t != root.transform)
                .ToDictionary(t => AnimationUtility.CalculateTransformPath(t, root.transform), t => t);
            var controls = root.GetComponentsInChildren<LightCircuitControls>(true).Single();
            var composer = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MoonEnvironment/Shaders/ComposeRoomLightmaps.compute");
            if (!composer || !composer.HasKernel("Initialize") || !composer.HasKernel("Accumulate") || !composer.HasKernel("Finalize"))
                throw new InvalidOperationException("Missing lightmap composition kernels.");
            var meshes = manifest.chunks.Select(c => AssetDatabase.LoadAssetAtPath<Mesh>(c.meshPath)).ToArray();
            var materials = manifest.chunks.Select(c => AssetDatabase.LoadAssetAtPath<Material>(c.materialPath)).ToArray();
            var sources = manifest.chunks.Select(c => c.sourcePaths.Select(p => nodes.TryGetValue(p, out var t) ? t.GetComponent<MeshRenderer>() : null).ToArray()).ToArray();
            for (int i = 0; i < manifest.chunks.Count; i++)
                if (!meshes[i] || !materials[i] || sources[i].Length == 0 || sources[i].Any(r => !r) ||
                    manifest.chunks[i].lightmapIndex < 0 || manifest.chunks[i].lightmapIndex >= data.baseline.colorMaps.Length)
                    throw new InvalidOperationException("Missing or invalid baked geometry dependency: " + manifest.chunks[i].name);
            var fixtures = manifest.lights.ToDictionary(l => l.sourcePath, l => nodes.TryGetValue(l.sourcePath, out var t) ? t.GetComponent<Light>() : null);
            if (fixtures.Values.Any(l => !l) || manifest.lights.Any(l => l.key && fixtures[l.sourcePath].shadows == LightShadows.None))
                throw new InvalidOperationException("Missing fixture or realtime key shadow.");
            var circuits = manifest.roomIds.Select(id => new BuildingBakedLighting.Circuit { id = id,
                fixtures = manifest.lights.Where(l => l.roomId == id).Select(l => fixtures[l.sourcePath]).ToArray(),
                realtimeLights = manifest.lights.Where(l => l.roomId == id && l.key).Select(l => fixtures[l.sourcePath]).ToArray() }).ToArray();
            var pipeline = Pipeline();
            var sceneLighting = UnityEngine.Object.FindObjectsByType<MoonSceneLighting>().Where(l => l.gameObject.scene == root.scene).ToArray();
            if (sceneLighting.Length != 1) throw new InvalidOperationException("Expected one formal scene lighting controller.");
            File.Copy(root.scene.path, MC00BuildingBakeGeometry.BackupFolder + "/before-install.unity", false);
            var oldClusters = root.GetComponentsInChildren<BuildingRenderClusters>(true);
            foreach (var old in oldClusters) old.enabled = false;
            var holder = new GameObject("MC00 baked building representation"); holder.transform.SetParent(root.transform, false);
            var bindings = new List<BuildingBakedLighting.RendererBinding>();
            var clusters = new List<BuildingRenderClusters.Group>();
            for (int i = 0; i < manifest.chunks.Count; i++)
            {
                var chunk = manifest.chunks[i];
                var go = new GameObject(chunk.name); go.transform.SetParent(holder.transform, false);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); go.transform.localScale = Vector3.one;
                go.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                go.layer = chunk.layer;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = materials[i];
                renderer.shadowCastingMode = (ShadowCastingMode)chunk.shadowMode; renderer.renderingLayerMask = chunk.renderingLayer;
                renderer.receiveShadows = chunk.receiveShadows;
                renderer.receiveGI = ReceiveGI.Lightmaps; renderer.lightmapIndex = -1; renderer.lightmapScaleOffset = chunk.scaleOffset;
                bindings.Add(new BuildingBakedLighting.RendererBinding { renderer = renderer, lightmapIndex = chunk.lightmapIndex, scaleOffset = chunk.scaleOffset });
                clusters.Add(new BuildingRenderClusters.Group { combined = renderer, sources = sources[i] });
            }
            holder.AddComponent<BuildingRenderClusters>().Configure(clusters.ToArray());
            var controller = holder.AddComponent<BuildingBakedLighting>();
            controller.Configure(data, bindings.ToArray(), circuits, composer);
            // These retained lights also illuminate close-up toon characters.
            // Filter their self-shadow silhouettes without raising atlas size or
            // disabling architectural/dynamic shadow casting and receiving.
            foreach (var key in circuits.SelectMany(c => c.realtimeLights))
            {
                var lightData = key.GetComponent<UniversalAdditionalLightData>();
                if (!lightData) lightData = key.gameObject.AddComponent<UniversalAdditionalLightData>();
                lightData.usePipelineSettings = false;
                lightData.softShadowQuality = SoftShadowQuality.High;
                PrefabUtility.RecordPrefabInstancePropertyModifications(lightData);
                EditorUtility.SetDirty(lightData);
            }
            // Keep existing button IDs and emission slots intact; change only their owner.
            controls.ConfigureBaked(controller); EditorUtility.SetDirty(controls);
            // Baked static light remains visible through doors. Stop the old whole-building
            // gate from hiding original fixtures while the new representation is active.
            foreach (var visibility in root.GetComponentsInChildren<InteriorVisibility>(true)) visibility.enabled = false;
            foreach (var lighting in sceneLighting)
            {
                if (lighting.gameObject.scene != root.scene) continue;
                var settings = new SerializedObject(lighting); settings.FindProperty("pipeline").objectReferenceValue = pipeline;
                settings.FindProperty("adaptInteriorShadows").boolValue = true; settings.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveScene(root.scene);
            Status = "Installed: " + bindings.Count + " chunks, " + circuits.Length + " independent circuits, " + circuits.Sum(c => c.realtimeLights.Length) + " realtime key lights";
            Debug.Log(Status);
        }

        public static void RestoreFormalWorkspace()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning || job != null && job.running)
                throw new InvalidOperationException("Finish the bake before restoring the formal workspace.");
            var backup = MC00BuildingBakeGeometry.BackupFolder;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<SceneBackup>(File.ReadAllText(backup + "/scene-setup.json")).scenes);
            var quality = JsonUtility.FromJson<QualityBackup>(File.ReadAllText(backup + "/quality.json"));
            QualitySettings.renderPipeline = string.IsNullOrEmpty(quality.pipeline) ? null : AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(quality.pipeline);
            QualitySettings.antiAliasing = quality.aa; QualitySettings.shadowmaskMode = quality.shadowmask;
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(quality.startScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(quality.startScene);
            foreach (var settings in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")) EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }
}
