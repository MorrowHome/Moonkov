// 负责地图的叠加加载和卸载。普通场景加载后，再分别等服务端和客户端 World 把自己的 ECS 子场景读完。
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    /// <summary>
    /// 为一次战局会话统一加载或卸载 Unity 主场景和子场景。
    /// </summary>
    internal static class ScenesLoader {
        public static async Task LoadGameplayAsync(World server, World client) {
            // 先给两个 World 装好本次规则，再加载共享地图。这样子场景加载过程中的地图组件和系统能读到正确模式。
            GameManager.ConfigureRaidMode(server);
            GameManager.ConfigureRaidMode(client);
            await LoadGameplayScenesAsync();
            if (server != null) await WaitForAllSubScenesToLoadAsync(server, LoadingData.LoadingSteps.LoadServer);
            if (client != null) await WaitForAllSubScenesToLoadAsync(client, LoadingData.LoadingSteps.LoadClient);
        }

        public static async Task UnloadGameplayScenesAsync() {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.UnloadingWorld);
            await UnloadSceneAsync(GameManager.GameSceneName);
            await UnloadSceneAsync(ClassicDust2Map.SceneName);
            await UnloadSceneAsync(GameManager.MenuPreviewSceneName);
        }

        private static async Task UnloadSceneAsync(string sceneName) {
            var gameplay = SceneManager.GetSceneByName(sceneName);
            // 这里卸载以叠加方式打开的地图。仍作为活动场景使用的那一份会保留给当前流程。
            if (gameplay.IsValid() && gameplay != SceneManager.GetActiveScene()) {
                var unloadScene = SceneManager.UnloadSceneAsync(gameplay);
                UpdateLoadingStateAsync(LoadingData.LoadingSteps.UnloadingGameScene, unloadScene);
                await unloadScene;
            }
        }

        private static async Task LoadGameplayScenesAsync() {
            // 先按玩法挑外层场景。经典爆破再把本地沙2地图实例放进去，竞技场和爆破共用一部分临时战局布置。
            var mode = GameSettings.Instance.RequestedMode;
            string sceneName = mode switch {
                RaidGameMode.Arena => GameManager.MenuPreviewSceneName,
                RaidGameMode.Classic => ClassicDust2Map.SceneName,
                _ => GameManager.GameSceneName
            };
            await LoadSceneAsync(sceneName, LoadingData.LoadingSteps.LoadGameScene);
            if (!ArenaMatch.UsesTemporaryKit(mode) || MoonRaidMap.Active) return;
            var scene = SceneManager.GetSceneByName(sceneName);
            // 经典爆破的外层场景带基础组件，沙2地图从本地资源另外实例化。把实例移入新场景后，退局卸图时地图也会跟着清理。
            if (mode == RaidGameMode.Classic) {
                var prefab = ClassicDust2Map.LoadPrefab();
                if (!prefab || !prefab.Configured) throw new System.InvalidOperationException("沙2地图资源未配置。");
                var map = Object.Instantiate(prefab.gameObject);
                SceneManager.MoveGameObjectToScene(map, scene);
            }
            var menu = Object.FindAnyObjectByType<Client.MainMenu>(FindObjectsInactive.Include);
            var panel = menu ? menu.GetComponent<UnityEngine.UIElements.UIDocument>().panelSettings : null;
            MoonRaidMap.AttachArena(scene, panel);
        }

        private static async Task LoadSceneAsync(string sceneName, LoadingData.LoadingSteps step) {
            if (SceneManager.GetSceneByName(sceneName).isLoaded) return;
            // 场景在当前内容上叠加读入。加载任务还没结束时，另一个逐帧更新把真实进度报给加载界面。
            var sceneLoading = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            UpdateLoadingStateAsync(step, sceneLoading);
            await sceneLoading;
        }

        private static async Task WaitForAllSubScenesToLoadAsync(World world, LoadingData.LoadingSteps step) {
            if (world == null) return;
            LoadingData.Instance.UpdateLoading(step);
            // 同一个场景的 SubScene 在每个 World 都要就绪，因此这里查的是指定 World 里的 SceneReference。
            using var scenesQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SceneReference>());
            using var scenesLeftToLoad = scenesQuery.ToEntityListAsync(Allocator.Persistent, out var handle);
            handle.Complete();
            // 先记住待就绪的子场景总数。每完成一个就从等待清单移除，并按已经完成的比例推进这段进度。
            float count = scenesLeftToLoad.Length;
            while (scenesLeftToLoad.Length > 0) {
                for (int i = 0; i < scenesLeftToLoad.Length; i++) {
                    var sceneEntity = scenesLeftToLoad[i];
                    if (SceneSystem.IsSceneLoaded(world.Unmanaged, sceneEntity)) {
                        scenesLeftToLoad.RemoveAt(i);
                        float numLoaded = count - scenesLeftToLoad.Length;
                        float loadingProgress = numLoaded / count;
                        LoadingData.Instance.UpdateLoading(step, loadingProgress);
                        i--;
                    }
                }
                // 在循环里交回这一帧，让 Unity 有机会继续实际加载。下一帧再看还有哪个子场景没准备好。
                await Awaitable.NextFrameAsync();
            }
        }

        private static async void UpdateLoadingStateAsync(LoadingData.LoadingSteps step, AsyncOperation loadingTask) {
            while (loadingTask != null && !loadingTask.isDone) {
                LoadingData.Instance.UpdateLoading(step, loadingTask.progress);
                await Awaitable.NextFrameAsync();
            }
        }
    }
}