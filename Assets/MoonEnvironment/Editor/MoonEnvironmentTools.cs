using System;
using System.IO;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.Moon.Editor
{
    public static class MoonEnvironmentTools
    {
        public const string Root = "Assets/MoonEnvironment";
        public const string ScenePath = Root + "/Scenes/MoonDemo.unity";
        public const string PrefabPath = Root + "/Prefabs/MoonEnvironment.prefab";
        private const string PipelinePath = Root + "/Settings/MoonPipeline.asset";

        [Serializable] private sealed class Layout { public Rock[] rocks; public TerrainPlacement[] terrains; }
        [Serializable] private sealed class TerrainPlacement { public string name; public Vector3 position; }
        [Serializable] private sealed class Rock
        {
            public string model;
            public Vector3 position, scale;
            public Quaternion rotation;
            public Vector4 plane;
            public float blendHeight, reflectance, freshness;
        }

        [MenuItem("Tools/Moon Environment/Open Demo")]
        public static void OpenDemo()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Moon Environment/Add Environment to Current Scene")]
        public static void AddEnvironment()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            GameObject prefab = Require<GameObject>(PrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Add moon environment");
            Selection.activeGameObject = instance;
        }

        [MenuItem("Tools/Moon Environment/Create Imported Environment (if missing)")]
        public static void CreateImportedEnvironment()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            if (File.Exists(ScenePath) || File.Exists(PrefabPath))
                throw new InvalidOperationException("Existing authored assets are protected. Use Open Demo or Add Environment.");
            Layout layout = JsonUtility.FromJson<Layout>(File.ReadAllText(Root + "/Editor/Source/layout.json"));
            Material regolith = Require<Material>(Root + "/Materials/LunarRegolith.mat");
            Material rock01 = Require<Material>(Root + "/Materials/moon_rock_01.mat");
            Material rock03 = Require<Material>(Root + "/Materials/moon_rock_03.mat");
            if (regolith.shader.name != "MoonEnvironment/Regolith" ||
                rock01.shader.name != "MoonEnvironment/LunarRock" ||
                rock03.shader.name != "MoonEnvironment/LunarRock" ||
                ShaderUtil.ShaderHasError(regolith.shader) || ShaderUtil.ShaderHasError(rock01.shader))
                throw new InvalidOperationException("Resolve moon shader errors before creating the scene.");
            var model01 = Require<GameObject>(Root + "/Art/Models/moon_rock_01_4k.fbx");
            var model03 = Require<GameObject>(Root + "/Art/Models/moon_rock_03_4k.fbx");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root = new GameObject("Moon Environment");
                var detailRoot = new GameObject("Near Field");
                detailRoot.transform.SetParent(root.transform, false);
                Terrain far = CreateTerrain(layout.terrains[0], "ObservedTerrain.asset", regolith, root.transform);
                Terrain near = CreateTerrain(layout.terrains[1], "DetailedTerrain.asset", regolith, detailRoot.transform);
                var rocksRoot = new GameObject("Rocks");
                rocksRoot.transform.SetParent(detailRoot.transform, false);
                // The imported recipe preserves all 6000 placements, rotations and contact planes.
                foreach (Rock rock in layout.rocks)
                {
                    bool first = rock.model == "moon_rock_01_4k";
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(first ? model01 : model03, rocksRoot.transform);
                    instance.name = first ? "Moon Rock 01" : "Moon Rock 03";
                    instance.transform.SetPositionAndRotation(rock.position, rock.rotation);
                    instance.transform.localScale = rock.scale;
                    foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
                        renderer.sharedMaterial = first ? rock01 : rock03;
                    foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
                    {
                        if (!filter.sharedMesh) continue;
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                    }
                    var grounding = instance.AddComponent<MoonRockGrounding>();
                    grounding.Configure(rock.plane, rock.blendHeight, rock.reflectance);
                    grounding.SetFreshness(rock.freshness);
                }
                var sunObject = new GameObject("Lunar Sun");
                sunObject.transform.SetParent(root.transform, false);
                Light sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.6f;
                sun.color = Color.white;
                sun.shadows = LightShadows.Soft;
                sun.shadowBias = 0.015f;
                sun.shadowNormalBias = 0.025f;
                sun.transform.localRotation = Quaternion.Euler(25, 120, 0);
                var spawn = new GameObject("Spawn Point").transform;
                spawn.SetParent(root.transform, false);
                spawn.position = new Vector3(2048, near.SampleHeight(new Vector3(2048, 0, 1816)) + 0.15f, 1816);
                var environment = root.AddComponent<MoonEnvironment>();
                var serialized = new SerializedObject(environment);
                SetReference(serialized, "surroundingTerrain", far);
                SetReference(serialized, "detailRoot", detailRoot);
                SetReference(serialized, "regolith", regolith);
                SetReference(serialized, "sun", sun);
                SetReference(serialized, "spawnPoint", spawn);
                SerializedProperty materials = serialized.FindProperty("rockMaterials");
                materials.arraySize = 2;
                materials.GetArrayElementAtIndex(0).objectReferenceValue = rock01;
                materials.GetArrayElementAtIndex(1).objectReferenceValue = rock03;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
                var lighting = new GameObject("Moon Scene Lighting").AddComponent<MoonSceneLighting>();
                serialized = new SerializedObject(lighting);
                SetReference(serialized, "sun", sun);
                SetReference(serialized, "pipeline", CreatePipeline());
                serialized.ApplyModifiedPropertiesWithoutUndo();
                ConfigureLighting(sun);
                CreatePlayer(environment);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save MoonDemo scene.");
                AssetDatabase.SaveAssets();
                Debug.Log($"MOON_IMPORTED: {layout.rocks.Length} rocks, 2 original TerrainData assets; URP prefab and local demo saved.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Terrain CreateTerrain(TerrainPlacement placement, string dataName, Material material, Transform parent)
        {
            TerrainData data = Require<TerrainData>(Root + "/Terrain/" + dataName);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = placement.name;
            terrainObject.transform.SetParent(parent, false);
            terrainObject.transform.position = placement.position;
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            terrain.materialTemplate = material;
            // Preserve non-instanced Terrain geometry; custom vertex code uses actual mesh positions.
            terrain.drawInstanced = false;
            terrain.heightmapPixelError = 0.5f;
            terrain.basemapDistance = 8000;
            terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
            return terrain;
        }

        private static RenderPipelineAsset CreatePipeline()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (existing) return existing;
            var source = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!source) throw new InvalidOperationException("The project must use URP.");
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.name = "MoonRenderer";
            AssetDatabase.CreateAsset(renderer, Root + "/Settings/MoonRenderer.asset");
            var pipeline = Object.Instantiate(source);
            pipeline.name = "MoonPipeline";
            var serialized = new SerializedObject(pipeline);
            SerializedProperty renderers = serialized.FindProperty("m_RendererDataList");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.FindProperty("m_DefaultRendererIndex").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            pipeline.shadowDistance = 1800;
            pipeline.shadowCascadeCount = 4;
            pipeline.cascade4Split = new Vector3(0.004f, 0.025f, 0.16f);
            pipeline.mainLightShadowmapResolution = 8192;
            pipeline.msaaSampleCount = 4;
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = false;
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
            return pipeline;
        }

        private static void CreatePlayer(MoonEnvironment environment)
        {
            var player = new GameObject("Local Moon Explorer");
            player.layer = 2; // Ignore Raycast; never include the capsule in terrain placement rays.
            player.transform.position = environment.SpawnPoint.position;
            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = new Vector3(0, 0.9f, 0);
            var input = player.AddComponent<StarterAssetsInputs>();
            var playerInput = player.AddComponent<PlayerInput>();
            playerInput.actions = Require<InputActionAsset>("Assets/Starter Assets/Runtime/InputSystem/StarterAssets.inputactions");
            playerInput.defaultActionMap = "Player";
            playerInput.defaultControlScheme = "KeyboardMouse";
            playerInput.notificationBehavior = PlayerNotifications.SendMessages;
            var movement = player.AddComponent<FirstPersonController>();
            movement.Gravity = -1.62f;
            movement.MoveSpeed = 4;
            movement.SprintSpeed = 7;
            movement.JumpHeight = 1.2f;
            movement.GroundedRadius = 0.25f;
            movement.GroundLayers = Physics.DefaultRaycastLayers;
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 1.65f, 0);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 8000;
            camera.fieldOfView = 75;
            camera.allowHDR = true;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            movement.CinemachineCameraTarget = cameraObject;
            var controls = player.AddComponent<MoonDemoControls>();
            var serialized = new SerializedObject(controls);
            SetReference(serialized, "environment", environment);
            SetReference(serialized, "movement", movement);
            SetReference(serialized, "playerInput", playerInput);
            SetReference(serialized, "inputs", input);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureLighting(Light sun)
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0;
            RenderSettings.reflectionIntensity = 0;
            RenderSettings.sun = sun;
        }

        private static T Require<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new FileNotFoundException("Required moon asset is missing: " + path);
            return asset;
        }

        private static void SetReference(SerializedObject owner, string field, Object value)
            => owner.FindProperty(field).objectReferenceValue = value;
    }
}
