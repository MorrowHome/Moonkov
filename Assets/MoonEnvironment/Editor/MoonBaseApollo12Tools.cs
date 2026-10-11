using System;
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
    /// <summary>
    /// Builds a separate, walkable Apollo 12 base candidate. Never regenerates the
    /// original MoonGameScene, its terrain, or the artist's modular FBX. Measured
    /// terrain and the small construction grading remain separate TerrainData.
    /// </summary>
    public static class MoonBaseApollo12Tools
    {
        public const string Root = "Assets/MoonEnvironment/Variants/Apollo12Base";
        public const string ScenePath = Root + "/Scenes/MoonBaseApollo12.unity";
        public const string LaptopPipelinePath = Root + "/Settings/Apollo12LaptopPipeline.asset";
        private const string ModelPath = "Assets/FBX/Map/Moonkov_Expanded_Base_Latest (3).fbx";

        [Serializable]
        private sealed class Dem
        {
            public int resolution;
            public float sizeMeters, verticalOffset, terrainHeight;
            public float baseX, baseZ, baseUnityY;
        }

        [MenuItem("Tools/Moon Environment/Apollo 12/Open Base Preview")]
        public static void Open()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Moon Environment/Apollo 12/Create Separate Base Preview (if missing)")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Existing base scene is protected; open it instead.");
            var previous = SceneManager.GetActiveScene();
            if (previous.isDirty) throw new InvalidOperationException("Save your current scene before building a new candidate.");
            var dem = JsonUtility.FromJson<Dem>(File.ReadAllText(Root + "/Source/dem.json"));
            var model = Require<GameObject>(ModelPath);
            var playerPrefab = Require<GameObject>("Assets/DollSinger/Prefabs/DollSingerPlayer.prefab");
            foreach (string folder in new[] { "Terrain", "Materials", "Prefabs", "Scenes" })
                Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();

            CreateTerrainData("Apollo12Observed", "apollo12_observed", dem);
            var graded = CreateTerrainData("Apollo12BaseGraded", "apollo12_base", dem);
            var regolith = new Material(Require<Material>("Assets/MoonEnvironment/Materials/LunarRegolith.mat"));
            regolith.name = "Apollo12Regolith";
            regolith.SetFloat("_LocalTerrainReady", 0);
            regolith.SetFloat("_ImpactReady", 0);
            AssetDatabase.CreateAsset(regolith, Root + "/Materials/Apollo12Regolith.mat");
            var sky = new Material(Require<Material>("Assets/MoonEnvironment/Materials/LunarSpaceSky.mat"));
            sky.name = "Apollo12Sky";
            // The preview uses a deliberately fixed low Sun, not the old site's astronomical coordinates.
            sky.SetFloat("_AstronomicalSky", 0);
            AssetDatabase.CreateAsset(sky, Root + "/Materials/Apollo12Sky.mat");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root = new GameObject("Apollo 12 / real DEM + modular base 3");
                var terrainObject = Terrain.CreateTerrainGameObject(graded);
                terrainObject.name = "Apollo12 Terrain / native 2m / local construction grading";
                terrainObject.transform.SetParent(root.transform, false);
                var terrain = terrainObject.GetComponent<Terrain>();
                terrain.materialTemplate = regolith;
                terrain.drawInstanced = false; // Existing custom terrain shader uses mesh positions.
                terrain.heightmapPixelError = 2;
                terrain.basemapDistance = 4000;
                terrain.shadowCastingMode = ShadowCastingMode.TwoSided;

                // Keep the FBX as a nested prefab, with every component still individually editable.
                var buildings = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                buildings.name = "Base 3 / modular FBX";
                buildings.transform.position = new Vector3(dem.baseX - .5f, dem.baseUnityY + .03f, dem.baseZ + 8.5f);
                int disabledPresentation = 0;
                foreach (Transform child in buildings.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "STUDY_Presentation_Plane_NOT_TERRAIN")
                    {
                        child.gameObject.SetActive(false);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                        disabledPresentation++;
                    }
                }
                // Artist inspection cameras and duplicate presentation suns must not render in gameplay.
                foreach (var camera in buildings.GetComponentsInChildren<Camera>(true))
                {
                    camera.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
                }
                foreach (var light in buildings.GetComponentsInChildren<Light>(true))
                {
                    if (light.type != LightType.Directional) continue;
                    light.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                }
                ConvertImportedAreaLights(buildings);
                ConfigureLightBudget(buildings);
                BuildingLightingTools.Calibrate(root);
                BuildingShadowTools.Optimize(buildings);
                MC00MeshReuseTools.OptimizeHierarchy(buildings);

                var sun = new GameObject("Lunar Sun / fixed preview 12 degrees").AddComponent<Light>();
                sun.transform.SetParent(root.transform, false);
                sun.type = LightType.Directional;
                sun.color = Color.white;
                sun.intensity = 1.6f;
                sun.shadows = LightShadows.Soft;
                sun.shadowBias = .015f;
                sun.shadowNormalBias = .025f;
                sun.transform.rotation = Quaternion.Euler(12, -35, 0);
                var lighting = new GameObject("Apollo12 Scene Lighting").AddComponent<MoonSceneLighting>();
                lighting.transform.SetParent(root.transform, false);
                var settings = new SerializedObject(lighting);
                settings.FindProperty("sun").objectReferenceValue = sun;
                settings.FindProperty("pipeline").objectReferenceValue = EnsureLaptopPipeline();
                settings.FindProperty("spaceSkybox").objectReferenceValue = sky;
                settings.FindProperty("latitude").floatValue = -3.0f;
                settings.FindProperty("longitude").floatValue = -23.47f;
                settings.FindProperty("clockMode").enumValueIndex = 0;
                // The legacy meter only detects sunlight, so an electrically lit
                // room would incorrectly receive up to +4 EV. Use fixed exposure here.
                settings.FindProperty("adaptExposure").boolValue = false;
                settings.FindProperty("ambientFill").colorValue = new Color(.025f, .03f, .04f, 1);
                settings.ApplyModifiedPropertiesWithoutUndo();

                var spawn = new GameObject("Preview Spawn / south approach").transform;
                spawn.SetParent(root.transform, false);
                Vector3 position = new Vector3(dem.baseX + 35, 0, dem.baseZ - 205);
                position.y = terrain.SampleHeight(position) + .15f;
                spawn.position = position;

                PrefabUtility.SaveAsPrefabAssetAndConnect(root, Root + "/Prefabs/Apollo12Base.prefab", InteractionMode.AutomatedAction);
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
                player.name = "DollSingerPlayer";
                player.transform.SetPositionAndRotation(position, Quaternion.identity);
                // Reuse local movement/IK. A preview-only Start hook selects first person
                // after the shared camera's Awake/BindPlayer has finished.
                foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!behaviour || behaviour.GetType().Name != "DollSingerView") continue;
                    var previewType = Type.GetType("Unity.MP_FPS.Moon.Preview.Apollo12PreviewStart, MoonEnvironment.Apollo12Preview", true);
                    var preview = player.AddComponent(previewType);
                    var setup = new SerializedObject(preview);
                    setup.FindProperty("view").objectReferenceValue = behaviour;
                    setup.ApplyModifiedPropertiesWithoutUndo();
                }
                // Gameplay lives in Assembly-CSharp; keep the environment assembly independent.
                Type.GetType("DollSingerGameplayPreviewTools, Assembly-CSharp-Editor", true)
                    .GetMethod("Install").Invoke(null, new object[] { player });
                foreach (var camera in player.GetComponentsInChildren<Camera>(true)) camera.farClipPlane = 3000;
                var volume = new GameObject("Moon Space Volume").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = Require<VolumeProfile>("Assets/MoonEnvironment/Settings/MoonSpacePostProcessing.asset");
                RenderSettings.skybox = sky;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientIntensity = 0;
                RenderSettings.reflectionIntensity = 0;
                RenderSettings.sun = sun;
                UnityEngine.Physics.SyncTransforms();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Failed to save new scene.");
                AssetDatabase.SaveAssets();
                Debug.Log($"APOLLO12_BASE_CREATED: 2048m, native 2m, separate observed/graded data, model (3), {disabledPresentation} presentation plane disabled. Scene: {ScenePath}");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static TerrainData CreateTerrainData(string name, string source, Dem dem)
        {
            string path = Root + "/Terrain/" + name + ".asset";
            if (File.Exists(path)) throw new InvalidOperationException("Terrain already exists; refusing to overwrite " + path);
            byte[] bytes = File.ReadAllBytes(Root + "/Source/" + source + ".bytes");
            if (bytes.Length != dem.resolution * dem.resolution * sizeof(float)) throw new InvalidDataException("Height data size mismatch.");
            var heights = new float[dem.resolution, dem.resolution];
            using (var reader = new BinaryReader(new MemoryStream(bytes)))
                for (int z = 0; z < dem.resolution; z++)
                    for (int x = 0; x < dem.resolution; x++)
                    {
                        float height = (reader.ReadSingle() - dem.verticalOffset) / dem.terrainHeight;
                        if (float.IsNaN(height) || height < 0 || height > 1) throw new InvalidDataException("Invalid height; do not silently clip DEM.");
                        heights[z, x] = height;
                    }
            var data = new TerrainData { name = name, heightmapResolution = dem.resolution, size = new Vector3(dem.sizeMeters, dem.terrainHeight, dem.sizeMeters) };
            data.SetHeights(0, 0, heights);
            AssetDatabase.CreateAsset(data, path);
            return data;
        }

        /// <summary>
        /// FBX exports DCC area lights as Realtime Rectangle lights, which URP
        /// cannot render. Use one downward/wall-facing spot per authored fixture.
        /// This is a tunable preview approximation, not a photometric conversion
        /// of the source application's watt values. Never edits the source FBX.
        /// </summary>
        public static int ConvertImportedAreaLights(GameObject buildings)
        {
            int converted = 0;
            foreach (var light in buildings.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Rectangle && light.type != LightType.Disc) continue;
                Undo.RecordObject(light, "Convert unsupported imported area light");
                light.type = LightType.Spot;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.spotAngle = 130;
                light.innerSpotAngle = 95;
                light.range = Mathf.Max(light.range, 16);
                light.intensity = Mathf.Clamp(light.intensity * .02f, 2, 24);
                // Retain wall occlusion. Low-resolution single-frustum spot shadows
                // avoid the six shadow faces of a point-light replacement.
                light.shadows = LightShadows.Soft;
                light.shadowNearPlane = .05f;
                light.shadowBias = .03f;
                light.shadowNormalBias = .15f;
                var data = light.GetComponent<UniversalAdditionalLightData>();
                if (!data) data = Undo.AddComponent<UniversalAdditionalLightData>(light.gameObject);
                Undo.RecordObject(data, "Set indoor shadow resolution tier");
                // URP's public tier setter is play-mode-only; author through its
                // serialized property so the setting persists on the prefab.
                var shadowSettings = new SerializedObject(data);
                shadowSettings.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                shadowSettings.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                converted++;
            }
            return converted;
        }

        public static void ConfigureLightBudget(GameObject buildings)
        {
            var budget = buildings.GetComponent<LocalLightBudget>();
            if (!budget) budget = Undo.AddComponent<LocalLightBudget>(buildings);
            var lights = Array.FindAll(buildings.GetComponentsInChildren<Light>(true),
                light => light.type == LightType.Spot || light.type == LightType.Point);
            var settings = new SerializedObject(budget);
            var fixtures = settings.FindProperty("fixtures");
            fixtures.arraySize = lights.Length;
            for (int i = 0; i < lights.Length; i++)
            {
                fixtures.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
                // All managed spots must use the same bounded tier. A few original
                // high-tier spots can otherwise resize the whole atlas as we walk.
                var data = lights[i].GetComponent<UniversalAdditionalLightData>();
                if (!data) data = Undo.AddComponent<UniversalAdditionalLightData>(lights[i].gameObject);
                var shadowSettings = new SerializedObject(data);
                shadowSettings.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                shadowSettings.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            }
            settings.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(budget);
        }

        /// <summary>
        /// Separate 1440p-output candidate for the laptop target. This is a
        /// starting quality budget, not a claim of 60 FPS on untested hardware.
        /// Existing candidate edits and the shared MoonPipeline are preserved.
        /// </summary>
        public static UniversalRenderPipelineAsset EnsureLaptopPipeline()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(LaptopPipelinePath);
            if (existing) return existing;
            if (!AssetDatabase.IsValidFolder(Root + "/Settings")) AssetDatabase.CreateFolder(Root, "Settings");
            var pipeline = Object.Instantiate(Require<UniversalRenderPipelineAsset>("Assets/MoonEnvironment/Settings/MoonPipeline.asset"));
            pipeline.name = "Apollo12LaptopPipeline";
            pipeline.renderScale = .75f; // 2560x1440 output -> 1920x1080 internal.
            pipeline.msaaSampleCount = 1; // STP supplies temporal AA; MSAA would disable STP.
            pipeline.mainLightShadowmapResolution = 4096;
            pipeline.shadowDistance = 350;
            pipeline.shadowCascadeCount = 4;
            pipeline.cascade4Split = new Vector3(.02f, .10f, .35f);
            pipeline.cascadeBorder = .2f;
            var settings = new SerializedObject(pipeline);
            // Support both the legacy and Unity 6.3+ upscaler selection fields.
            settings.FindProperty("m_UpscalingFilter").intValue = 4;
            var upscaler = settings.FindProperty("m_SelectedUpscalerName");
            if (upscaler != null) upscaler.stringValue = "Spatial-Temporal Post-Processing";
            settings.FindProperty("m_SoftShadowQuality").intValue = 2;
            settings.FindProperty("m_AdditionalLightsShadowResolutionTierLow").intValue = 256;
            settings.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 4096;
            settings.ApplyModifiedPropertiesWithoutUndo();
            ConfigureGpuDrawing(pipeline);
            AssetDatabase.CreateAsset(pipeline, LaptopPipelinePath);
            return pipeline;
        }

        public static void ConfigureGpuDrawing(UniversalRenderPipelineAsset pipeline)
        {
            pipeline.gpuResidentDrawerMode = GPUResidentDrawerMode.InstancedDrawing;
            pipeline.gpuResidentDrawerEnableOcclusionCullingInCameras = true;
            pipeline.smallMeshScreenPercentage = 0; // No new distance/size-based detail popping.
            // Unity's validator requires this even in Editor; otherwise GRD falls
            // back, and a player build can strip its DOTS instancing variants.
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var stripping = graphics.FindProperty("m_BrgStripping");
            stripping.intValue = (int)UnityEditor.Rendering.BatchRendererGroupStrippingMode.KeepAll;
            graphics.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
        }

        private static T Require<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new FileNotFoundException("Required asset missing: " + path);
            return asset;
        }
    }
}
