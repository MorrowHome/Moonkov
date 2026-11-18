// 场景开局先等服务端开始监听，再生成管理器 Ghost。生命周期系统还用这份名单判断何时允许其他 Ghost 激活。
using System.Collections.Generic;
using Unity.GhostBridge;
using Unity.NetCode;
using UnityEngine;

public interface IGhostManager {
}

public class ManagerGhostsSpawner : Singleton<ManagerGhostsSpawner> {
    [field: SerializeField] public List<GhostSpawner.GhostReference> ManagersToSpawn { get; private set; } = new();

    public override void Awake() {
        base.Awake();

        // 等本进程拥有服务端 World 后才生成 Ghost。
        // 纯客户端会收到服务端生成的管理器 Ghost，因此场景上的生成器只在有自己 ServerWorld 的进程中启用。
        if (!ClientServerBootstrap.HasServerWorld) gameObject.SetActive(false);
    }

    public void LateUpdate() {
        if (GhostBridgeManager.Instance != null && GhostBridgeManager.Instance.IsServerListening())
            // 监听起来、表现模板也加载完了，才遍历管理器名单。列表用完后停用生成器，后续帧沿用已生成的管理器。
            if (GhostEntityPrefabSystem.ServerInstance != null &&
                GhostEntityPrefabSystem.ServerInstance.PrefabsLoaded) {
                foreach (var manager in ManagersToSpawn) {
                    var managerPrefab = GhostSpawner.FindGhostPrefab(manager);
                    if (managerPrefab != null)
                        // 生命周期系统靠 IGhostManager 识别需要先绑定的角色。这里提示预制件漏配接口的情况。
                        Debug.Assert(managerPrefab.TryGetComponent<IGhostManager>(out _),
                            "Manager prefabs must have an IManager interface");
                    var netGuid = GhostGameObject.GenerateRandomHash();
                    if (!GhostSpawner.SpawnGhostPrefab(manager, Vector3.zero, Quaternion.identity, netGuid))
                        Debug.LogError(
                            $"[MANAGERGHOSTSPAWNER] Unable to spawn ghost manager {manager.GhostPrefab.AssetGUID}");
                }
                gameObject.SetActive(false);
            }
    }
}