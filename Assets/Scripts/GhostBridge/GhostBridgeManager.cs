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