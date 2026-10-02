using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.GhostBridge
{

    public class GhostBridgeManager : MonoBehaviour
    {
        public static GhostBridgeManager Instance { get; private set; } = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            DontDestroyOnLoad(this);
        }

#region Server functions
        public bool IsServerListening()
        {
            // DriverStore dereferences a native pointer. Never cache a driver across
            // world lifetimes or read a default driver before the server is ready.
            if (!TryGetServerEntityManager(out var manager))
                return false;

            using var query = manager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            query.CompleteDependency();
            var driver = query.GetSingletonRW<NetworkStreamDriver>();
            ref var driverStore = ref driver.ValueRW.DriverStore;
            if (driverStore.IsCreated && driverStore.DriversCount > 0)
            {
                int driverId = driverStore.FirstDriver;
                return driverStore.GetDriverInstanceRO(driverId).driver.IsCreated &&
                       driverStore.GetDriverInstanceRO(driverId).driver.Listening;
            }
            return false;
        }

        public bool TryGetServerEntityManager(out EntityManager manager)
        {
            World serverWorld = null;
            foreach (var world in World.All)
            {
                if (world.IsCreated && !world.QuitUpdate &&
                    (world.Flags & WorldFlags.GameServer) == WorldFlags.GameServer)
                {
                    serverWorld = world;
                    break;
                }
            }

            if (serverWorld != null)
            {
                manager = serverWorld.EntityManager;
                return true;
            }
            else
            {
                manager = default(EntityManager);
                return false;    
            }
        }
#endregion End of Server functions
        
#region Client functions
        public struct LocalPlayerInfo
        {
            public FixedString64Bytes PlayerName;
            public uint InputUserId;
        };
        
        [System.NonSerialized]
        public LocalPlayerInfo LocalPlayer;
    }
#endregion End of Client functions    
}
