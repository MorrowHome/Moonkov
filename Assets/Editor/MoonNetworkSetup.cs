using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS.Moon;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.Editor
{
    /// <summary>Builds a separate lunar gameplay scene from authored demo assets and template network roots.</summary>
    public static class MoonNetworkSetup
    {
        public const string ScenePath = "Assets/MoonEnvironment/Scenes/MoonGameScene.unity";
        public const string SpawnScenePath = "Assets/MoonEnvironment/Scenes/MoonSpawnPointsSubScene.unity";
        private const string DemoPath = "Assets/MoonEnvironment/Scenes/MoonDemo.unity";
        private const string TemplatePath = "Assets/Scenes/GameScene.unity";

        [MenuItem("Tools/Moon Environment/Set Up Network Map")]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before setting up the network map.");
            bool hasMap = File.Exists(ScenePath), hasSpawns = File.Exists(SpawnScenePath);
            if (hasMap != hasSpawns)
                throw new InvalidOperationException("Network map setup is incomplete. Restore the missing scene before running setup again.");
            if (!hasMap) CreateScenes();
            RegisterBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("Moon network map is registered. Start from MainMenu to host or join MoonGameScene.");
        }

        private static Scene OpenSource(string path, out bool opened)
        {
            var scene = SceneManager.GetSceneByPath(path);
            opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            if (scene.isDirty) throw new InvalidOperationException("Save source scene changes first: " + path);
            return scene;
        }

        private static void CreateScenes()
        {
            var loadedTemplate = SceneManager.GetSceneByPath(TemplatePath);
            if (loadedTemplate.IsValid() && loadedTemplate.isLoaded)
                throw new InvalidOperationException("Close the template GameScene before creating the lunar map.");
            Scene previous = SceneManager.GetActiveScene();
            Scene map = default, spawns = default, demo = default, template = default;
            bool openedDemo = false, openedTemplate = false;
            try
            {
                demo = OpenSource(DemoPath, out openedDemo);
                if (!EditorSceneManager.SaveScene(demo, ScenePath, true))
                    throw new IOException("Could not copy the moon demo scene.");
                map = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(map);
                foreach (var root in map.GetRootGameObjects())
                    if (root.name == "Local Moon Explorer") UnityEngine.Object.DestroyImmediate(root);

                var environment = map.GetRootGameObjects().Select(x => x.GetComponent<MoonEnvironment>()).First(x => x);
                if (map.GetRootGameObjects().Any(x => x.GetComponentsInChildren<Camera>(true).Length > 0))
                    throw new InvalidOperationException("Remove local demo cameras before using this scene as a network map.");

                template = OpenSource(TemplatePath, out openedTemplate);
                var resourceAsset = template.GetRootGameObjects().Single(x => x.name == "GameResourcesSubScene")
                    .GetComponent<SubScene>().SceneAsset;
                foreach (string name in new[] { "GhostSpawnerManager", "LeaderboardSpawner" })
                {
                    var source = template.GetRootGameObjects().Single(x => x.name == name);
                    var instance = UnityEngine.Object.Instantiate(source);
                    instance.name = name;
                    SceneManager.MoveGameObjectToScene(instance, map);
                }
                // Unity permits only one active SubScene reference to the shared resource scene.
                if (openedTemplate)
                {
                    EditorSceneManager.CloseScene(template, true);
                    template = default;
                }
                else
                    throw new InvalidOperationException("Close the template GameScene before creating the lunar map.");
                var resources = new GameObject("GameResourcesSubScene").AddComponent<SubScene>();
                SceneManager.MoveGameObjectToScene(resources.gameObject, map);
                resources.SceneAsset = resourceAsset;
                resources.AutoLoadScene = true;

                UnityEngine.Physics.SyncTransforms();
                Vector3 centre = environment.SpawnPoint.position;
                spawns = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var spawnRoot = new GameObject("Moon Spawn Points");
                SceneManager.MoveGameObjectToScene(spawnRoot, spawns);
                int count = 0;
                // Spread players through the southern edge of the detailed terrain, facing into it.
                for (int z = 0; z < 6 && count < 8; z++)
                for (int x = -2; x <= 2 && count < 8; x++)
                {
                    Vector3 sample = centre + new Vector3(x * 8f, 0f, z * 10f);
                    if (!UnityEngine.Physics.Raycast(new Vector3(sample.x, 1200f, sample.z), Vector3.down,
                        out var hit, 1500f, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) continue;
                    if (!(hit.collider is TerrainCollider) || Vector3.Angle(hit.normal, Vector3.up) > 30f) continue;
                    Vector3 position = hit.point + Vector3.up * 0.25f;
                    if (UnityEngine.Physics.CheckCapsule(position + Vector3.up * 0.28f,
                        position + Vector3.up * 1.42f, 0.30f,
                        LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) continue;
                    var spawn = new GameObject("Moon Spawn " + (++count).ToString("00"));
                    spawn.transform.SetParent(spawnRoot.transform, false);
                    spawn.transform.SetPositionAndRotation(position, Quaternion.identity);
                    spawn.AddComponent<SpawnPointAuthoring>();
                }
                if (count < 8) throw new InvalidOperationException("Could not find eight clear lunar spawn points.");
                if (!EditorSceneManager.SaveScene(spawns, SpawnScenePath)) throw new IOException("Could not save lunar spawn scene.");
                EditorSceneManager.CloseScene(spawns, true);
                spawns = default;
                SceneManager.SetActiveScene(map);
                var spawnSubScene = new GameObject("Moon Spawn Points SubScene").AddComponent<SubScene>();
                spawnSubScene.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SpawnScenePath);
                spawnSubScene.AutoLoadScene = true;
                if (!EditorSceneManager.SaveScene(map, ScenePath)) throw new IOException("Could not save lunar gameplay scene.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (spawns.IsValid() && spawns.isLoaded) EditorSceneManager.CloseScene(spawns, true);
                if (map.IsValid() && map.isLoaded) EditorSceneManager.CloseScene(map, true);
                if (openedTemplate && template.IsValid() && template.isLoaded) EditorSceneManager.CloseScene(template, true);
                if (openedDemo && demo.IsValid() && demo.isLoaded) EditorSceneManager.CloseScene(demo, true);
            }
        }

        private static void RegisterBuildScenes()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (string path in new[] { ScenePath, SpawnScenePath })
            {
                var existing = scenes.Find(x => x.path == path);
                if (existing == null) scenes.Add(new EditorBuildSettingsScene(path, true));
                else existing.enabled = true;
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
