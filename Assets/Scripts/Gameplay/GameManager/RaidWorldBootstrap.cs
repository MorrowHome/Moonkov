using System;
using Unity.Entities;
using Unity.GhostBridge;
using Unity.NetCode;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Unity.MP_FPS {
    internal static class RaidWorldBootstrap {
        internal static (World server, World client) Create(GameConnection connection, CreationType type) {
            var session = connection.Session;
            bool host = session?.IsHost ?? (type is CreationType.Host or CreationType.LanHost or CreationType.Offline);
            if (session == null) SetDirectDriver(connection, host);
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.CreateWorld);
            DestroyLocalSimulationWorld();
#if UNITY_EDITOR
            if (session != null && connection.SessionConnectionType == NetworkType.Relay &&
                MultiplayerPlayModePreferences.RequestedNumThinClients > 0) {
                Debug.Log("ThinClient is disabled when Relay connection is used.");
                MultiplayerPlayModePreferences.RequestedNumThinClients = 0;
            }
#endif
            World server = host ? Prepare(ClientServerBootstrap.CreateServerWorld("ServerWorld")) : null;
            World client = session == null || !session.IsServer()
                ? Prepare(ClientServerBootstrap.CreateClientWorld("ClientWorld"))
                : null;
            if (session == null) MarkLocalModes(type, server, client);
            return (server, client);
        }

        private static World Prepare(World world) {
            ConfigureRaidMode(world);
            GhostGameObjectUpdateSystem.GatherWorldSystems(world);
            return world;
        }

        private static void SetDirectDriver(GameConnection connection, bool host) {
            var role = host ? NetworkRole.Host : NetworkRole.Client;
            if (connection.PeerRoom != null)
                NetworkStreamReceiveSystem.DriverConstructor = new EntityDriverConstructor(role, connection.PeerRoom);
            else if (connection.IceSession != null)
                NetworkStreamReceiveSystem.DriverConstructor = new EntityDriverConstructor(role, connection.IceSession.Transport);
            else
                NetworkStreamReceiveSystem.DriverConstructor = new EntityDriverConstructor(role,
                    !PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan);
        }

        private static void MarkLocalModes(CreationType type, World server, World client) {
            if (type == CreationType.LanHost) server.EntityManager.CreateEntity(typeof(LanRaidMode));
            if (type is CreationType.LanHost or CreationType.LanJoin) client.EntityManager.CreateEntity(typeof(LanRaidMode));
            if (type != CreationType.Offline) return;

            var mode = GameSettings.Instance.RequestedMode;
            server.EntityManager.CreateEntity(typeof(OfflineRaidMode));
            if (mode == RaidGameMode.Expedition && PlayerProfileClient.Tutorial?.Status == Campaign.TutorialStatus.Active) {
                server.EntityManager.CreateEntity(typeof(TutorialRaidMode));
                client.EntityManager.CreateEntity(typeof(TutorialRaidMode));
            }
            client.EntityManager.CreateEntity(typeof(OfflineRaidMode));
            if (mode == RaidGameMode.BountyClash) {
                server.EntityManager.CreateEntity(typeof(ClashSoloMode));
                client.EntityManager.CreateEntity(typeof(ClashSoloMode));
            }
        }

        internal static void ConfigureRaidMode(World world) {
            if (world == null || !world.IsCreated) return;
            using var query = world.EntityManager.CreateEntityQuery(typeof(RaidModeState));
            if (!query.IsEmptyIgnoreFilter) return;
            // 每个 World 各有一份玩法与规则版本。需要临时装备的对局，实际比分、轮次等 ArenaMatch 状态只放在服务端 World。
            var entity = world.EntityManager.CreateEntity(typeof(RaidModeState));
            world.EntityManager.SetComponentData(entity,
                new RaidModeState {
                    Mode = GameSettings.Instance.RequestedMode,
                    RulesVersion = GameSettings.Instance.RequestedRulesVersion
                });
            if (world.IsServer() && ArenaMatch.UsesTemporaryKit(GameSettings.Instance.RequestedMode))
                world.EntityManager.AddComponentObject(entity, new ArenaMatch(GameSettings.Instance.RequestedMode));
        }

        internal static void DestroyLocalSimulationWorld() {
            foreach (var world in World.All)
                if (world.Flags == WorldFlags.Game) {
                    world.Dispose();
                    break;
                }
        }

        internal static void Listen(GameConnection connection, CreationType type, World server, World client) {
            using var query = server.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            query.CompleteDependency();
            var driver = query.GetSingletonRW<NetworkStreamDriver>();
            if (!driver.ValueRW.Listen(connection.ListenEndpoint))
                throw new InvalidOperationException("Cannot start server. The port may be in use.");
            if (type != CreationType.Offline && connection.PeerRoom == null) return;

            // 端口为 0 时，每种传输会分到不同端口。本机客户端要连同类驱动的实际端口。
            using var clientQuery = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamDriver>());
            var clientDrivers = clientQuery.GetSingleton<NetworkStreamDriver>().DriverStore;
            var transport = clientDrivers.GetDriverType(clientDrivers.FirstDriver);
            var serverDrivers = driver.ValueRO.DriverStore;
            for (int id = serverDrivers.FirstDriver; id < serverDrivers.LastDriver; id++) {
                if (serverDrivers.GetDriverType(id) != transport) continue;
                connection.UseLocalServerEndpoint(driver.ValueRW.GetLocalEndPoint(id));
                return;
            }
            throw new InvalidOperationException("No matching local server transport.");
        }
    }
}
