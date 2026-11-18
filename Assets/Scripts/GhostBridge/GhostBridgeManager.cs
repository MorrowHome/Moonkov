// 跨场景的桥接管理器。地图生成器来问服务端是否已经开始监听，再决定能不能生成由服务器掌管的物体。
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.GhostBridge {
    public class GhostBridgeManager : MonoBehaviour {
        public static GhostBridgeManager Instance { get; private set; } = null;

        private void Awake() {
            // 常驻场景重复加载时保留第一份管理器。生成器只需问同一个入口当前房主能否生成实体。
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start() { DontDestroyOnLoad(this); }

        #region Server functions

        public bool IsServerListening() {
            // 每次从当前服务端 World 查驱动。退局时 World 和它的原生驱动会一起释放，旧引用不能用来判断新战局是否准备好。
            // DriverStore dereferences a native pointer. Never cache a driver across
            // world lifetimes or read a default driver before the server is ready.
            if (!TryGetServerEntityManager(out var manager)) return false;
            using var query = manager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            if (query.IsEmptyIgnoreFilter) return false;
            // 驱动组件含有原生资源，读它之前先等其他 ECS 作业用完。接着确认真的建好了驱动且处于 Listening。
            query.CompleteDependency();
            var driver = query.GetSingletonRW<NetworkStreamDriver>();
            ref var driverStore = ref driver.ValueRW.DriverStore;
            if (driverStore.IsCreated && driverStore.DriversCount > 0) {
                int driverId = driverStore.FirstDriver;
                return driverStore.GetDriverInstanceRO(driverId).driver.IsCreated &&
                       driverStore.GetDriverInstanceRO(driverId).driver.Listening;
            }
            return false;
        }

        public bool TryGetServerEntityManager(out EntityManager manager) {
            World serverWorld = null;
            foreach (var world in World.All)
                // 遍历时只认仍有效、还在更新的服务端 World。正在退出的那个即使还在 World 列表里，也不能接收新生成物体的请求。
                if (world.IsCreated && !world.QuitUpdate &&
                    (world.Flags & WorldFlags.GameServer) == WorldFlags.GameServer) {
                    serverWorld = world;
                    break;
                }
            if (serverWorld != null) {
                manager = serverWorld.EntityManager;
                return true;
            } else {
                manager = default;
                return false;
            }
        }

        #endregion End of Server functions

        #region Client functions

        public struct LocalPlayerInfo {
            public FixedString64Bytes PlayerName;
            public uint InputUserId;
        };

        [NonSerialized] public LocalPlayerInfo LocalPlayer;
    }

    #endregion End of Client functions
}