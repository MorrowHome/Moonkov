using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    /// <summary>
    /// This utility class is handling Unity Scenes and Subscenes loading/unloading for a gameplay session.
    /// </summary>
    static class ScenesLoader
    {
        public static async Task LoadMenuPreviewAsync(World client)
        {
            await LoadSceneAsync(GameManager.MenuPreviewSceneName, LoadingData.LoadingSteps.LoadGameScene);
            await WaitForAllSubScenesToLoadAsync(client, LoadingData.LoadingSteps.LoadClient);
        }

        public static async Task LoadGameplayAsync(World server, World client)
        {
            GameManager.ConfigureRaidMode(server);
            GameManager.ConfigureRaidMode(client);
            await LoadGameplayScenesAsync();
            if (server != null)
                await WaitForAllSubScenesToLoadAsync(server, LoadingData.LoadingSteps.LoadServer);
            if (client != null)
                await WaitForAllSubScenesToLoadAsync(client, LoadingData.LoadingSteps.LoadClient);
        }

        public static async Task UnloadGameplayScenesAsync()
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.UnloadingWorld);

            await UnloadSceneAsync(GameManager.GameSceneName);
            await UnloadSceneAsync(ClassicDust2Map.SceneName);
            await UnloadSceneAsync(GameManager.MenuPreviewSceneName);
        }

        static async Task UnloadSceneAsync(string sceneName)
        {
            var gameplay = SceneManager.GetSceneByName(sceneName);
            if (gameplay.IsValid() && gameplay != SceneManager.GetActiveScene())
            {
                var unloadScene = SceneManager.UnloadSceneAsync(gameplay);
                UpdateLoadingStateAsync(LoadingData.LoadingSteps.UnloadingGameScene, unloadScene);
                await unloadScene;
            }
        }

        static async Task LoadGameplayScenesAsync()
        {
            var mode = GameSettings.Instance.RequestedMode;
            string sceneName = mode switch
            {
                RaidGameMode.Arena => GameManager.MenuPreviewSceneName,
                RaidGameMode.Classic => ClassicDust2Map.SceneName,
                _ => GameManager.GameSceneName
            };
            await LoadSceneAsync(sceneName, LoadingData.LoadingSteps.LoadGameScene);
            if (!ArenaMatch.UsesTemporaryKit(mode) || MoonRaidMap.Active) return;
            var scene = SceneManager.GetSceneByName(sceneName);
            if (mode == RaidGameMode.Classic)
            {
                var prefab = ClassicDust2Map.LoadPrefab();
                if (!prefab || !prefab.Configured) throw new System.InvalidOperationException("沙2地图资源未配置。");
                var map = UnityEngine.Object.Instantiate(prefab.gameObject);
                SceneManager.MoveGameObjectToScene(map, scene);
            }
            var menu = UnityEngine.Object.FindAnyObjectByType<Client.MainMenu>(FindObjectsInactive.Include);
            var panel = menu ? menu.GetComponent<UnityEngine.UIElements.UIDocument>().panelSettings : null;
            MoonRaidMap.AttachArena(scene, panel);
        }

        static async Task LoadSceneAsync(string sceneName, LoadingData.LoadingSteps step)
        {
            if(SceneManager.GetSceneByName(sceneName).isLoaded)
                return;
            var sceneLoading = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            UpdateLoadingStateAsync(step, sceneLoading);
            await sceneLoading;
        }

        static async Task WaitForAllSubScenesToLoadAsync(World world, LoadingData.LoadingSteps step)
        {
            if (world == null)
                return;

            LoadingData.Instance.UpdateLoading(step);

            using var scenesQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SceneReference>());
            using var scenesLeftToLoad = scenesQuery.ToEntityListAsync(Allocator.Persistent, out var handle);
            handle.Complete();

            float count = scenesLeftToLoad.Length;
            while (scenesLeftToLoad.Length > 0)
            {
                for (var i = 0; i < scenesLeftToLoad.Length; i++)
                {
                    var sceneEntity = scenesLeftToLoad[i];
                    if (SceneSystem.IsSceneLoaded(world.Unmanaged, sceneEntity))
                    {
                        scenesLeftToLoad.RemoveAt(i);
                        var numLoaded = count - scenesLeftToLoad.Length;
                        var loadingProgress = numLoaded / count;
                        LoadingData.Instance.UpdateLoading(step, loadingProgress);
                        i--;
                    }
                }

                await Awaitable.NextFrameAsync();
            }
        }

        static async void UpdateLoadingStateAsync(LoadingData.LoadingSteps step, AsyncOperation loadingTask)
        {
            while (loadingTask != null && !loadingTask.isDone)
            {
                LoadingData.Instance.UpdateLoading(step, loadingTask.progress);
                await Awaitable.NextFrameAsync();
            }
        }
    }
}
