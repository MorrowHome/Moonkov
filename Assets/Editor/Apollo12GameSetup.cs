using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.MP_FPS.Moon;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.Editor
{
    /// <summary>
    /// Creates a separate gameplay map using existing network/raid authoring.
    /// Never rebuilds an existing map or changes the original terrain/FBX.
    /// Runtime actors still come from GameResourcesSubScene and the server.
    /// </summary>
    public static class Apollo12GameSetup
    {
        public const string ScenePath = "Assets/MoonEnvironment/Variants/Apollo12Base/Scenes/Apollo12GameScene.unity";
        public const string SpawnPath = "Assets/MoonEnvironment/Variants/Apollo12Base/Scenes/Apollo12SpawnPointsSubScene.unity";
        private const string EnvironmentPath = "Assets/MoonEnvironment/Variants/Apollo12Base/Prefabs/Apollo12Base.prefab";
        private const string OptimizedEnvironmentPath = "Assets/MoonEnvironment/Variants/MC00LightControlsFullV02/Prefabs/Apollo12MC00Candidate.prefab";

        [MenuItem("Tools/Moon Environment/Apollo 12/Use optimized MC00 in gameplay")]
        public static void UseOptimizedMC00()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play before replacing the gameplay environment.");
            var map = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !map.IsValid() || !map.isLoaded;
            var previous = SceneManager.GetActiveScene();
            if (openedHere) map = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            GameObject replacement = null, original = null;
            Light previousSun = null;
            GlobalObjectId previousSunId = default;
            Material previousSky = null;
            int undoGroup = -1;
            try
            {
                if (map.isDirty) throw new InvalidOperationException("Save or discard existing map edits before replacing the environment.");
                SceneManager.SetActiveScene(map);
                previousSun = RenderSettings.sun;
                if (previousSun) previousSunId = GlobalObjectId.GetGlobalObjectIdSlow(previousSun);
                previousSky = RenderSettings.skybox;
                foreach (var root in map.GetRootGameObjects())
                {
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                    if (path == OptimizedEnvironmentPath) return;
                    if (path == EnvironmentPath) original = root;
                }
                if (!original) throw new InvalidOperationException("Expected the original Apollo12 environment instance.");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OptimizedEnvironmentPath);
                if (!prefab) throw new InvalidOperationException("The optimized Full v02 environment is missing.");
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Use optimized MC00 in gameplay");
                Undo.RegisterCompleteObjectUndo(Unsupported.GetSerializedAssetInterfaceSingleton("RenderSettings"),
                    "Replace environment lighting");
                replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, map);
                Undo.RegisterCreatedObjectUndo(replacement, "Create optimized environment");
                replacement.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                replacement.transform.localScale = original.transform.localScale;
                var oldLighting = original.GetComponentInChildren<MoonSceneLighting>();
                var lighting = replacement.GetComponentInChildren<MoonSceneLighting>();
                if (!lighting || !oldLighting || replacement.GetComponentsInChildren<Camera>(true)
                        .Any(c => c.enabled && c.gameObject.activeInHierarchy))
                    throw new InvalidOperationException("Gameplay replacement must have lighting and no active preview camera.");
                if (replacement.GetComponentInChildren<Terrain>().terrainData != original.GetComponentInChildren<Terrain>().terrainData)
                    throw new InvalidOperationException("Terrain changed; gameplay coordinates need a separate migration.");

                // Geometry stays replaceable. The scene owns its network clock and outdoor
                // rendering budget: a 30 m interior trial must not replace whole-map shadows.
                var oldSettings = new SerializedObject(oldLighting);
                var settings = new SerializedObject(lighting);
                foreach (string field in new[] { "pipeline", "clockMode", "epochUtc", "latitude", "longitude", "astronomicalTimeScale" })
                    settings.CopyFromSerializedProperty(oldSettings.FindProperty(field));
                settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(lighting);
                if (!lighting.GetComponent<ExpeditionClockPresentation>())
                    lighting.gameObject.AddComponent<ExpeditionClockPresentation>();

                Undo.RecordObject(original, "Disable original environment");
                original.SetActive(false);
                UnityEngine.Physics.SyncTransforms();
                var raid = map.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MoonRaidMap>()).Single();
                var points = new SerializedObject(raid).FindProperty("m_LootPositions");
                for (int i = 4; i <= 7; i++)
                {
                    Vector3 point = points.GetArrayElementAtIndex(i).vector3Value;
                    var hits = UnityEngine.Physics.RaycastAll(point + Vector3.up * .7f, Vector3.down, 2,
                        ~0, QueryTriggerInteraction.Ignore).Where(h => h.collider.gameObject.scene == map && h.normal.y > .85f)
                        .OrderBy(h => Mathf.Abs(h.point.y - (point.y - .35f))).ToArray();
                    if (hits.Length == 0 || Mathf.Abs(hits[0].point.y - (point.y - .35f)) > .3f || !ClearStandingSpace(map, hits[0].point))
                        throw new InvalidOperationException("MC00 loot " + i + " needs placement review at " + point);
                }
                RenderSettings.sun = settings.FindProperty("sun").objectReferenceValue as Light;
                RenderSettings.skybox = settings.FindProperty("spaceSkybox").objectReferenceValue as Material;
                Undo.DestroyObjectImmediate(original);
                EditorSceneManager.MarkSceneDirty(map);
                if (!EditorSceneManager.SaveScene(map)) throw new IOException("Could not save optimized gameplay map.");
                Undo.CollapseUndoOperations(undoGroup);
                replacement = null;
                Debug.Log("Formal Apollo12 now uses Full v02 MC00: original spawn/loot/extraction retained, four MC00 loot positions checked, gameplay clock and outdoor pipeline retained.");
            }
            catch
            {
                if (undoGroup >= 0)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    RenderSettings.sun = previousSun ? previousSun : GlobalObjectId.GlobalObjectIdentifierToObjectSlow(previousSunId) as Light;
                    RenderSettings.skybox = previousSky;
                }
                throw;
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedHere && map.IsValid() && map.isLoaded) EditorSceneManager.CloseScene(map, true);
            }
        }

        [MenuItem("Tools/Moon Environment/Apollo 12/Set Up Gameplay Map (preserve existing)")]
        public static void SetUp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before authoring the map.");
            bool mapExists = File.Exists(ScenePath), spawnsExist = File.Exists(SpawnPath);
            if (mapExists != spawnsExist) throw new InvalidOperationException("Map setup is incomplete; preserve and inspect the existing scene before retrying.");
            if (!mapExists) Create();
            RegisterBuildScenes();
            Debug.Log("Apollo12 gameplay registered. Enter through MainMenu; existing gameplay scenes were retained.");
        }

        private static void Create()
        {
            var original = SceneManager.GetSceneByPath(MoonNetworkSetup.ScenePath);
            if (original.IsValid() && original.isLoaded)
                throw new InvalidOperationException("Close the old MoonGameScene before creating the new map, to avoid duplicate resource SubScenes.");
            if (!AssetDatabase.CopyAsset(MoonNetworkSetup.ScenePath, ScenePath)) throw new IOException("Could not copy gameplay authoring.");
            Scene previous = SceneManager.GetActiveScene(), map = default, spawns = default;
            try
            {
                map = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(map);
                foreach (var root in map.GetRootGameObjects())
                    if (root.GetComponent<MoonEnvironment>() || root.GetComponent<MoonSceneLighting>()) Object.DestroyImmediate(root);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
                if (!prefab) throw new InvalidOperationException("Apollo12 environment prefab is missing.");
                var environment = (GameObject)PrefabUtility.InstantiatePrefab(prefab, map);
                var terrain = environment.GetComponentInChildren<Terrain>();
                var lighting = environment.GetComponentInChildren<MoonSceneLighting>();
                lighting.gameObject.AddComponent<ExpeditionClockPresentation>();
                // Presentation follows the existing server clock, not the local preview clock.
                var lightingSettings = new SerializedObject(lighting);
                lightingSettings.FindProperty("clockMode").enumValueIndex = (int)MoonSceneLighting.ClockMode.GameplayDay;
                lightingSettings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(lighting);
                var sky = lightingSettings.FindProperty("spaceSkybox").objectReferenceValue as Material;
                RenderSettings.skybox = sky;
                RenderSettings.sun = lightingSettings.FindProperty("sun").objectReferenceValue as Light;

                var modules = environment.GetComponentsInChildren<Transform>(true);
                var baseRoot = environment.GetComponentInChildren<LocalLightBudget>().transform;
                float baseY = baseRoot.position.y;
                UnityEngine.Physics.SyncTransforms();
                var spawnPositions = new List<Vector3>();
                // Southeast approach to MC00, clear of the building shell. Face toward MC00.
                for (int z = 0; z < 2; z++)
                for (int x = 0; x < 4; x++)
                    spawnPositions.Add(FindOutdoor(map, terrain, new Vector3(1178 + x * 7, 0, 1210 + z * 9), spawnPositions, 3));

                var loot = new List<Vector3>();
                foreach (var p in new[] { new Vector3(1184, 0, 1188), new Vector3(1184, 0, 1238),
                    new Vector3(1200, 0, 1270), new Vector3(1192, 0, 1304) })
                    loot.Add(FindOutdoor(map, terrain, p, loot, 6) + Vector3.up * .35f);
                int interiorCount = 0;
                foreach (string moduleName in new[] { "MC00_ROOT", "EB08_ROOT", "RB00_ROOT", "PH01_ROOT", "EB05_ROOT" })
                {
                    var module = modules.Single(t => t.name == moduleName);
                    int desired = moduleName == "MC00_ROOT" ? 4 : 1;
                    var floors = module.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            r.bounds.size.x >= 1.5f && r.bounds.size.z >= 1.5f && r.bounds.max.y < baseY + 3)
                        .OrderBy(r => r.bounds.center.y).ThenBy(r => r.name).ToArray();
                    foreach (var floor in floors)
                    {
                        if (desired == 0) break;
                        if (!TryFloorPoint(map, floor, loot, out var point)) continue;
                        loot.Add(point + Vector3.up * .35f); desired--; interiorCount++;
                    }
                    // A future export may not retain floor naming. Keep the map playable
                    // with a clear exterior cache rather than placing one inside a wall.
                    for (int i = 0; i < desired; i++)
                        loot.Add(FindOutdoor(map, terrain, module.position + new Vector3(22 + i * 7, 0, -22), loot, 6) + Vector3.up * .35f);
                }
                var raid = map.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MoonRaidMap>()).Single();
                var raidData = new SerializedObject(raid);
                var points = raidData.FindProperty("m_LootPositions"); points.arraySize = loot.Count;
                for (int i = 0; i < loot.Count; i++) points.GetArrayElementAtIndex(i).vector3Value = loot[i];
                var extraction = FindOutdoor(map, terrain, new Vector3(1219, 0, 1043), new List<Vector3>(), 0);
                raidData.FindProperty("m_ExtractionPosition").vector3Value = extraction;
                raidData.ApplyModifiedPropertiesWithoutUndo();

                var spawnRef = map.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SubScene>())
                    .Single(s => AssetDatabase.GetAssetPath(s.SceneAsset) == MoonNetworkSetup.SpawnScenePath);
                spawns = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var spawnRoot = new GameObject("Apollo12 Spawn Points"); SceneManager.MoveGameObjectToScene(spawnRoot, spawns);
                Vector3 face = modules.Single(t => t.name == "MC00_ROOT").position;
                for (int i = 0; i < spawnPositions.Count; i++)
                {
                    var go = new GameObject("Apollo12 Spawn " + (i + 1).ToString("00")); go.transform.SetParent(spawnRoot.transform, false);
                    Vector3 direction = face - spawnPositions[i]; direction.y = 0;
                    go.transform.SetPositionAndRotation(spawnPositions[i] + Vector3.up * .2f, Quaternion.LookRotation(direction));
                    go.AddComponent<SpawnPointAuthoring>();
                }
                if (!EditorSceneManager.SaveScene(spawns, SpawnPath)) throw new IOException("Could not save Apollo12 spawn points.");
                EditorSceneManager.CloseScene(spawns, true); spawns = default;
                SceneManager.SetActiveScene(map);
                spawnRef.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SpawnPath);
                spawnRef.AutoLoadScene = true; spawnRef.name = "Apollo12 Spawn Points SubScene";
                if (map.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Any(c => c.enabled && c.gameObject.activeInHierarchy))
                    throw new InvalidOperationException("Gameplay map must not include an active preview camera.");
                if (!EditorSceneManager.SaveScene(map)) throw new IOException("Could not save Apollo12 gameplay scene.");
                Debug.Log($"Apollo12 gameplay saved: {spawnPositions.Count} spawns, {loot.Count} caches ({interiorCount} indoors), extraction {extraction}; original raid rules and 3 server-controlled enemies retained.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (spawns.IsValid() && spawns.isLoaded) EditorSceneManager.CloseScene(spawns, true);
                if (map.IsValid() && map.isLoaded) EditorSceneManager.CloseScene(map, true);
            }
        }

        private static bool ClearStandingSpace(Scene scene, Vector3 ground)
        {
            return !UnityEngine.Physics.OverlapCapsule(ground + Vector3.up * .42f, ground + Vector3.up * 1.5f,
                .35f, ~0, QueryTriggerInteraction.Ignore).Any(c => c.gameObject.scene == scene);
        }

        private static bool Separated(Vector3 p, List<Vector3> others, float spacing) =>
            others.All(o => new Vector2(p.x - o.x, p.z - o.z).sqrMagnitude >= spacing * spacing);

        private static Vector3 FindOutdoor(Scene scene, Terrain terrain, Vector3 desired, List<Vector3> used, float spacing)
        {
            for (int ring = 0; ring <= 18; ring++)
            for (int z = -ring; z <= ring; z++)
            for (int x = -ring; x <= ring; x++)
            {
                if (ring > 0 && Math.Max(Math.Abs(x), Math.Abs(z)) != ring) continue;
                Vector3 p = desired + new Vector3(x * 2, 0, z * 2);
                Vector3 local = p - terrain.transform.position;
                if (local.x < 0 || local.z < 0 || local.x > terrain.terrainData.size.x || local.z > terrain.terrainData.size.z) continue;
                if (Vector3.Angle(terrain.terrainData.GetInterpolatedNormal(local.x / terrain.terrainData.size.x,
                    local.z / terrain.terrainData.size.z), Vector3.up) > 25) continue;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                if (Separated(p, used, spacing) && ClearStandingSpace(scene, p)) return p;
            }
            throw new InvalidOperationException("No clear outdoor point near " + desired);
        }

        private static bool TryFloorPoint(Scene scene, MeshRenderer floor, List<Vector3> used, out Vector3 point)
        {
            var collider = floor.GetComponent<Collider>();
            if (collider && collider.enabled && !collider.isTrigger)
                foreach (var offset in new[] { Vector3.zero, new Vector3(.6f, 0, 0), new Vector3(-.6f, 0, 0), new Vector3(0, 0, .6f) })
                {
                    var ray = new Ray(floor.bounds.center + offset + Vector3.up * 2, Vector3.down);
                    if (!collider.Raycast(ray, out var hit, 4) || hit.normal.y < .9f) continue;
                    if (!Separated(hit.point, used, 6) || !ClearStandingSpace(scene, hit.point)) continue;
                    point = hit.point; return true;
                }
            point = default; return false;
        }

        private static void RegisterBuildScenes()
        {
            // Unity 6 can redirect `scenes` to the active profile. Always update
            // the global client list explicitly, and each overriding profile below.
            EditorBuildSettings.globalScenes = WithGameplayScenes(EditorBuildSettings.globalScenes);
            // Dedicated-server profiles override the global list and need the same map.
            foreach (string guid in AssetDatabase.FindAssets("t:BuildProfile", new[] { "Assets/Settings/Build Profiles" }))
            {
                var profile = AssetDatabase.LoadAssetAtPath<UnityEditor.Build.Profile.BuildProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (!profile || !profile.overrideGlobalScenes) continue;
                profile.scenes = WithGameplayScenes(profile.scenes);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            }
        }

        private static EditorBuildSettingsScene[] WithGameplayScenes(EditorBuildSettingsScene[] source)
        {
            var scenes = source.GroupBy(s => s.path).Select(g => g.First()).ToList();
            foreach (string path in new[] { ScenePath, SpawnPath })
            {
                int index = scenes.FindIndex(s => s.path == path);
                // Recreate entries through the API so native GUID and path stay paired.
                var entry = new EditorBuildSettingsScene(path, true);
                if (index < 0) scenes.Add(entry); else scenes[index] = entry;
            }
            return scenes.ToArray();
        }
    }
}
