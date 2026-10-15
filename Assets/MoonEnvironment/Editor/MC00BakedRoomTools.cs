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

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Isolated G01 lighting prototype. Does not replace the formal map or source FBX.</summary>
    public static class MC00BakedRoomTools
    {
        public const string Folder = "Assets/MoonEnvironment/Variants/MC00BakedRoomTrial";
        public const string ScenePath = Folder + "/G01BakedRoom.unity";
        const string Backup = "Library/BuildValidation/MC00BakedRoom";
        const string WorkspaceKey = "MC00Bake.Workspace";
        const string PreviousPipelineKey = "MC00Bake.PreviousPipeline";
        const string MissingSessionValue = "<not captured>";
        static readonly Dictionary<Mesh, Mesh> readable = new();
        static readonly Queue<(Mesh mesh, string path)> unwrapQueue = new();
        static Scene trial;
        static int processed;
        public static string Status { get; private set; } = "Idle";

        [MenuItem("Tools/Moon Environment/Baked Room Trial/1 - Create G01 sample from loaded formal map")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Exit Play and finish the current bake first.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                throw new InvalidOperationException("The trial already exists. Open it instead of overwriting it.");
            var room = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .Single(t => t.name == "ORG_ROOM_G01_b11275");
            var building = room.GetComponentsInParent<Transform>().Single(t => t.name == "MC00_ROOT_Standard_YUp_Frame");
            var roomRenderers = room.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && !r.forceRenderingOff).ToArray();
            Bounds area = roomRenderers[0].bounds;
            foreach (var renderer in roomRenderers) area.Encapsulate(renderer.bounds);
            area.Expand(1);
            var sources = building.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && !r.forceRenderingOff &&
                r.gameObject.activeInHierarchy && r.bounds.Intersects(area) && r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh).ToArray();
            var fixtures = room.GetComponentsInChildren<Light>(true);
            var primary = fixtures.Where(l => l.transform.parent.name.Contains("room task light actual")).ToArray();
            if (primary.Length < 2) throw new InvalidOperationException("Missing authored G01 main fixtures.");
            var pairOfHeroes = primary.SelectMany(a => primary.Where(b => b != a).Select(b => (a, b)))
                .OrderByDescending(p => (p.a.transform.position - p.b.transform.position).sqrMagnitude).First();

            Directory.CreateDirectory(Backup);
            Directory.CreateDirectory(Folder + "/Meshes");
            Directory.CreateDirectory(Folder + "/Materials");
            // Preserve unsaved shared authoring before temporarily opening the sample.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty) continue;
                if (string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("An untitled scene must be saved first.");
                string sceneBackup = Backup + "/" + scene.name + "-before-save.unity";
                if (!File.Exists(sceneBackup)) File.Copy(scene.path, sceneBackup);
                EditorSceneManager.SaveScene(scene);
            }
            SaveWorkspaceState();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            trial = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(trial);
            var root = new GameObject("G01 baked circuit sample");
            var geometry = new GameObject("Static room geometry"); geometry.transform.SetParent(root.transform);
            var materialCopies = new Dictionary<Material, Material>();
            var buckets = new Dictionary<(Material, int, ShadowCastingMode, uint), List<CombineInstance>>();
            Vector3 origin = new(1609.8f, 211.63615f, 2122);
            foreach (var renderer in sources)
            {
                var materials = renderer.sharedMaterials.Select(original =>
                {
                    if (!original) return null;
                    if (!materialCopies.TryGetValue(original, out var copy))
                    {
                        copy = new Material(original) { name = original.name + " Trial" };
                        AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(Folder + "/Materials/" + copy.name + ".mat"));
                        materialCopies.Add(original, copy);
                    }
                    return copy;
                }).ToArray();
                string name = renderer.name.ToLowerInvariant();
                if (new[] { "leaf", "telescop", "sliding", "drawer", "hinge", "doordog", "removable", "closeddoorshield", "cabinet_door", "crossslide", "button", "indicator", "keycap", "access" }.Any(name.Contains))
                {
                    var moving = new GameObject(renderer.name); moving.transform.SetParent(root.transform, false);
                    moving.transform.SetPositionAndRotation(renderer.transform.position - origin, renderer.transform.rotation);
                    moving.transform.localScale = renderer.transform.lossyScale;
                    moving.AddComponent<MeshFilter>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    var movableRenderer = moving.AddComponent<MeshRenderer>(); movableRenderer.sharedMaterials = materials;
                    movableRenderer.shadowCastingMode = renderer.shadowCastingMode; movableRenderer.renderingLayerMask = renderer.renderingLayerMask;
                    movableRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    continue;
                }
                var mesh = Read(renderer.GetComponent<MeshFilter>().sharedMesh);
                for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                {
                    var material = materials[sub]; if (!material) continue;
                    // Spatial cells keep baking charts and the prototype's culling bounds bounded.
                    var center = renderer.bounds.center - origin;
                    int cell = Mathf.FloorToInt(center.x / 6) + 100 * Mathf.FloorToInt(center.y / 6) + 10000 * Mathf.FloorToInt(center.z / 6);
                    var key = (material, cell, renderer.shadowCastingMode, renderer.renderingLayerMask);
                    if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub,
                        transform = Matrix4x4.Translate(-origin) * renderer.localToWorldMatrix });
                }
            }
            int n = 0;
            foreach (var pair in buckets)
            {
                var go = new GameObject("Room surface " + n.ToString("D3")); go.transform.SetParent(geometry.transform, false);
                var mesh = new Mesh { name = go.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(pair.Value.ToArray(), true, true); mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = pair.Key.Item1;
                renderer.shadowCastingMode = pair.Key.Item3; renderer.renderingLayerMask = pair.Key.Item4;
                renderer.receiveGI = ReceiveGI.Lightmaps;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
                unwrapQueue.Enqueue((mesh, Folder + "/Meshes/RoomSurface" + (n++).ToString("D3") + ".asset"));
            }
            foreach (var mesh in readable.Values) UnityEngine.Object.DestroyImmediate(mesh);
            readable.Clear();
            var mixed = new List<Light>();
            for (int i = 0; i < fixtures.Length; i++)
            {
                var go = new GameObject("Circuit fixture " + i + " - " + fixtures[i].transform.parent.name); go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(fixtures[i].transform.position - origin, fixtures[i].transform.rotation);
                var light = go.AddComponent<Light>(); EditorUtility.CopySerialized(fixtures[i], light);
                // Two opposite main fixtures cover the working area; six supporting lights bake fully.
                bool hero = fixtures[i] == pairOfHeroes.a || fixtures[i] == pairOfHeroes.b;
                light.lightmapBakeType = hero ? LightmapBakeType.Mixed : LightmapBakeType.Baked;
                if (hero) mixed.Add(light);
                light.shadows = LightShadows.Soft;
                var extra = go.AddComponent<UniversalAdditionalLightData>();
                var lightSettings = new SerializedObject(extra);
                lightSettings.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 2;
                lightSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            var probeRoot = new GameObject("Room probes"); probeRoot.transform.SetParent(root.transform, false);
            var probes = new List<Vector3>();
            // A regular volume includes the adjacent shell; probe sampling is validated on the moving character.
            for (int x = -9; x <= 9; x += 3) for (int z = -9; z <= 9; z += 3)
                foreach (float y in new[] { .3f, 1.5f, 3f, 5f }) probes.Add(new Vector3(x, y, z));
            probeRoot.AddComponent<LightProbeGroup>().probePositions = probes.ToArray();
            var camera = new GameObject("Sample Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(new Vector3(0, 1.65f, 0), Quaternion.Euler(0, 20, 0));
            camera.nearClipPlane = .03f; camera.farClipPlane = 150; camera.fieldOfView = 70;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var actor = CreateCharacterVisual(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"));
            actor.name = "DollSinger dynamic shadow witness";
            actor.transform.SetPositionAndRotation(new Vector3(1, 0, 4), Quaternion.Euler(0, 180, 0));
            foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            { renderer.enabled = true; renderer.shadowCastingMode = ShadowCastingMode.On; renderer.lightProbeUsage = LightProbeUsage.BlendProbes; }
            var controller = root.AddComponent<BakedRoomLighting>();
            controller.Configure(root.transform, mixed.ToArray(), root.GetComponentsInChildren<MeshRenderer>(), true);
            RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0; RenderSettings.reflectionIntensity = 0;
            var settings = new LightingSettings { name = "G01 Shadowmask trial", bakedGI = true, realtimeGI = false,
                mixedBakeMode = MixedLightingMode.Shadowmask, lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
                lightmapResolution = 8, lightmapMaxSize = 1024, lightmapPadding = 4,
                directSampleCount = 32, indirectSampleCount = 64, environmentSampleCount = 32, maxBounces = 2 };
            AssetDatabase.CreateAsset(settings, Folder + "/LightingSettings.lighting"); Lightmapping.lightingSettings = settings;
            var pipeline = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/MoonEnvironment/Settings/MoonPipeline.asset"));
            pipeline.name = "G01 trial pipeline"; pipeline.shadowDistance = 40;
            var pipelineSettings = new SerializedObject(pipeline);
            pipelineSettings.FindProperty("m_LightProbeSystem").intValue = (int)LightProbeSystem.LegacyLightProbes;
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(pipeline, Folder + "/Pipeline.asset");
            controller.ConfigureTrial(pipeline, actor.transform);
            // Editor-only pipeline override is restored explicitly after validation.
            QualitySettings.renderPipeline = pipeline;
            EditorSceneManager.SaveScene(trial, ScenePath);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s != trial).ToArray())
                EditorSceneManager.CloseScene(scene, true);
            processed = 0; Status = "Generating lightmap UVs: 0/" + unwrapQueue.Count;
            EditorApplication.update -= UnwrapTick; EditorApplication.update += UnwrapTick;
        }

        static void UnwrapTick()
        {
            try
            {
                if (unwrapQueue.Count == 0)
                {
                    EditorApplication.update -= UnwrapTick; AssetDatabase.SaveAssets();
                    EditorSceneManager.SaveScene(trial); Status = "Ready to bake: " + processed + " static chunks"; Debug.Log(Status); return;
                }
                var item = unwrapQueue.Dequeue();
                if (!Unwrapping.GenerateSecondaryUVSet(item.mesh)) throw new InvalidOperationException("UV unwrap failed: " + item.mesh.name);
                AssetDatabase.CreateAsset(item.mesh, item.path); processed++;
                Status = "Generating lightmap UVs: " + processed + ", remaining " + unwrapQueue.Count;
            }
            catch (Exception e) { EditorApplication.update -= UnwrapTick; Status = "FAILED: " + e.Message; Debug.LogException(e); }
        }

        [MenuItem("Tools/Moon Environment/Baked Room Trial/2 - Bake powered room")]
        public static void BakeOn()
        {
            if (unwrapQueue.Count > 0 || Lightmapping.isRunning || EditorApplication.isPlaying) throw new InvalidOperationException("Sample is not ready.");
            if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open the isolated G01 sample.");
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>()) light.enabled = true;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Status = "Baking powered room";
            Lightmapping.bakeCompleted -= OnBaked; Lightmapping.bakeCompleted += OnBaked;
            if (!Lightmapping.BakeAsync()) { Lightmapping.bakeCompleted -= OnBaked; throw new InvalidOperationException("Bake did not start."); }
        }
        static void OnBaked()
        {
            Lightmapping.bakeCompleted -= OnBaked;
            Status = "Powered bake finished; lightmaps=" + LightmapSettings.lightmaps.Length;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); Debug.Log(Status);
        }

        [MenuItem("Tools/Moon Environment/Baked Room Trial/Restore previous workspace")]
        public static void RestoreWorkspace()
        {
            if (Lightmapping.isRunning || EditorApplication.isPlayingOrWillChangePlaymode || unwrapQueue.Count > 0)
                throw new InvalidOperationException("Finish UV generation, bake and Play first.");
            // Adopt an existing trial's legacy backup before its component restores globals.
            if (string.IsNullOrEmpty(SessionState.GetString(WorkspaceKey, "")))
            {
                if (SessionState.GetString(PreviousPipelineKey, MissingSessionValue) == MissingSessionValue) return;
                SaveWorkspaceState();
            }
            var state = JsonUtility.FromJson<WorkspaceState>(SessionState.GetString(WorkspaceKey, ""));
            UnityEngine.Object.FindAnyObjectByType<BakedRoomLighting>()?.Restore();
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
                throw new InvalidOperationException("Could not save the current scene before restoring the workspace.");
            EditorSceneManager.RestoreSceneManagerSetup(state.scenes);
            // Restore after closing the sample, so its OnDisable cannot overwrite these values.
            QualitySettings.renderPipeline = string.IsNullOrEmpty(state.pipeline) ? null : AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(state.pipeline);
            QualitySettings.shadowmaskMode = state.shadowmask;
            QualitySettings.antiAliasing = state.antiAliasing;
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(state.startScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(state.startScene);
            // QualitySettings setters restore memory, but an earlier serialized
            // sample override can remain on disk until the settings asset is saved.
            foreach (var settings in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset"))
                EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            SessionState.EraseString(WorkspaceKey);
            SessionState.EraseString(PreviousPipelineKey);
        }
        [Serializable] sealed class Setup { public SceneSetup[] scenes; }
        [Serializable] sealed class WorkspaceState
        {
            public SceneSetup[] scenes;
            public string pipeline, startScene;
            public ShadowmaskMode shadowmask;
            public int antiAliasing;
        }

        static void SaveWorkspaceState()
        {
            // Reopening the sample must never replace the original workspace with the trial.
            if (!string.IsNullOrEmpty(SessionState.GetString(WorkspaceKey, ""))) return;
            string legacyPipeline = SessionState.GetString(PreviousPipelineKey, MissingSessionValue);
            WorkspaceState state;
            if (legacyPipeline != MissingSessionValue)
            {
                // Migrate the already-open trial without overwriting its original scene backup.
                state = new WorkspaceState
                {
                    scenes = JsonUtility.FromJson<Setup>(File.ReadAllText(Backup + "/scene-setup.json")).scenes,
                    pipeline = legacyPipeline, shadowmask = ShadowmaskMode.DistanceShadowmask,
                    antiAliasing = 2, startScene = "Assets/Scenes/MainMenu.unity"
                };
            }
            else
            {
                state = new WorkspaceState
                {
                    scenes = EditorSceneManager.GetSceneManagerSetup(),
                    pipeline = AssetDatabase.GetAssetPath(QualitySettings.renderPipeline),
                    shadowmask = QualitySettings.shadowmaskMode, antiAliasing = QualitySettings.antiAliasing,
                    startScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)
                };
            }
            SessionState.SetString(WorkspaceKey, JsonUtility.ToJson(state));
        }

        static GameObject CreateCharacterVisual(GameObject source)
        {
            // Copy only the animation/rendering skeleton. Disabled network components
            // can still receive Awake and are inappropriate in this isolated sample.
            var map = new Dictionary<Transform, Transform>();
            foreach (var original in source.GetComponentsInChildren<Transform>(true))
            {
                var go = new GameObject(original.name); map[original] = go.transform;
                if (original != source.transform) go.transform.SetParent(map[original.parent], false);
                go.transform.localPosition = original.localPosition;
                go.transform.localRotation = original.localRotation;
                go.transform.localScale = original.localScale;
                go.SetActive(original.gameObject.activeSelf);
            }
            foreach (var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var copy = map[skin.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
                EditorUtility.CopySerialized(skin, copy);
                copy.bones = skin.bones.Select(b => b ? map[b] : null).ToArray();
                copy.rootBone = skin.rootBone ? map[skin.rootBone] : null;
                copy.probeAnchor = skin.probeAnchor && map.ContainsKey(skin.probeAnchor) ? map[skin.probeAnchor] : null;
                copy.enabled = true; copy.updateWhenOffscreen = true;
                for (var p = copy.transform; p; p = p.parent) p.gameObject.SetActive(true);
            }
            foreach (var animator in source.GetComponentsInChildren<Animator>(true))
            {
                var copy = map[animator.transform].gameObject.AddComponent<Animator>();
                EditorUtility.CopySerialized(animator, copy);
                copy.enabled = true; copy.applyRootMotion = false;
            }
            return map[source.transform].gameObject;
        }

        [MenuItem("Tools/Moon Environment/Baked Room Trial/Open completed G01 sample")]
        public static void OpenSample()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning || unwrapQueue.Count > 0) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SaveWorkspaceState();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            UnityEngine.Object.FindAnyObjectByType<BakedRoomLighting>()?.SetPowered(true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }
        static Mesh Read(Mesh source)
        {
            if (readable.TryGetValue(source, out var mesh)) return mesh;
            mesh = new Mesh { name = source.name };
            using (var all = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                var data = all[0]; mesh.SetVertexBufferParams(data.vertexCount, source.GetVertexAttributes());
                for (int s = 0; s < data.vertexBufferCount; s++) { var bytes = data.GetVertexData<byte>(s); mesh.SetVertexBufferData(bytes, 0, 0, bytes.Length, s); }
                if (source.indexFormat == IndexFormat.UInt16) { var ids = data.GetIndexData<ushort>(); mesh.SetIndexBufferParams(ids.Length, IndexFormat.UInt16); mesh.SetIndexBufferData(ids, 0, 0, ids.Length); }
                else { var ids = data.GetIndexData<uint>(); mesh.SetIndexBufferParams(ids.Length, IndexFormat.UInt32); mesh.SetIndexBufferData(ids, 0, 0, ids.Length); }
                mesh.subMeshCount = data.subMeshCount;
                for (int i = 0; i < data.subMeshCount; i++) mesh.SetSubMesh(i, data.GetSubMesh(i), MeshUpdateFlags.DontRecalculateBounds);
                mesh.bounds = source.bounds;
            }
            readable[source] = mesh; return mesh;
        }
    }
}
