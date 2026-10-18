using System;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs the actual authoritative system in a disposable ECS world, without opening/saving a gameplay scene.
public static class EnergyGrenadeServerChecks
{
    [MenuItem("Moonkov/Energy Grenade/Run Authority Checks")]
    public static void Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks++; }
        var scene = EditorSceneManager.NewPreviewScene();
        var previousMap = MoonRaidMap.Active;
        using var world = new World("Energy grenade authority fixture", WorldFlags.GameServer);
        var em = world.EntityManager;
        var activeProperty = typeof(MoonRaidMap).GetProperty("Active");
        try
        {
            var mapObject = new GameObject("Grenade fixture map"); mapObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(mapObject, scene);
            var map = mapObject.AddComponent<MoonRaidMap>();
            activeProperty.SetValue(null, map);
            var system = world.GetOrCreateSystemManaged<EnergyGrenadeServerSystem>();
            var timeEntity = em.CreateEntity(typeof(NetworkTime));
            double clock = 0;
            uint frame = 100;
            void Tick()
            {
                clock += 1.0 / 60; frame++;
                world.SetTime(new TimeData(clock, 1f / 60));
                em.SetComponentData(timeEntity, new NetworkTime { ServerTick = new NetworkTick(frame) });
                system.Update();
            }
            (Entity, GhostGameObject) Victim(string name, Vector3 position)
            {
                var root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = position;
                var ghost = root.AddComponent<GhostGameObject>();
                var entity = em.CreateEntity(typeof(PredictedPlayerGhost), typeof(GhostOwner));
                em.SetComponentData(entity, new PredictedPlayerGhost { CurrentHealth = 1000, MaxHealth = 1000 });
                em.AddComponentObject(entity, new GhostGameObjectLink { LinkedInstance = ghost });
                return (entity, ghost);
            }
            var (shooter, shooterRoot) = Victim("Thrower", Vector3.zero);
            em.SetComponentData(shooter, new GhostOwner { NetworkId = 7 });
            var graph = InventoryGraph.Create(false);
            graph.AddSupply(EnergyGrenadeRules.Code, 3);
            var item = graph.Items.Find(i => i.Code == EnergyGrenadeRules.Code);
            item.Parent = "pockets"; item.Region = "1";
            var inventory = new RaidInventoryState { Graph = graph, RaidId = 4 };
            var connection = em.CreateEntity(typeof(RaidSession), typeof(JoinedClient));
            em.SetComponentData(connection, new RaidSession { RaidId = 4, Phase = RaidPhase.Active });
            em.SetComponentData(connection, new JoinedClient { PlayerEntity = shooter });
            em.AddComponentObject(connection, inventory);
            uint requestId = 1;
            void Request(int version, int raid = 4)
            {
                var rpc = em.CreateEntity();
                em.AddComponentData(rpc, new RaidGrenadeThrowRpc { RaidId = raid, ItemId = item.Id,
                    RequestId = requestId, ExpectedVersion = version });
                em.AddComponentData(rpc, new ReceiveRpcCommandRequest { SourceConnection = connection });
                Tick();
            }
            int initialVersion = graph.Version;
            Request(initialVersion);
            Check(inventory.Error == InventoryError.None && item.Quantity == 2 && graph.Version == initialVersion + 1, "Server accepts throw and spends one");
            Request(initialVersion);
            Check(item.Quantity == 2, "Repeated request ID cannot throw twice");
            requestId++; Request(graph.Version);
            Check(inventory.Error != InventoryError.None && item.Quantity == 2, "Server cooldown rejects rapid throw");
            requestId++; Request(graph.Version, 3);
            Check(inventory.Error != InventoryError.None && item.Quantity == 2, "Prior raid cannot throw");
            while (clock < 1) Tick();
            requestId++; Request(initialVersion);
            Check(inventory.Error == InventoryError.Stale && item.Quantity == 2, "Server rejects stale inventory version");
            var health = em.GetComponentData<PredictedPlayerGhost>(shooter); health.CurrentHealth = 0;
            em.SetComponentData(shooter, health); requestId++; Request(graph.Version);
            Check(inventory.Error != InventoryError.None && item.Quantity == 2, "Dead player cannot throw");
            health.CurrentHealth = 1000; em.SetComponentData(shooter, health);
            // With no floor, the exact ballistic endpoint is known. Victims stand at that endpoint.
            Vector3 end = Ballistics.Position(new Vector3(0,1.6f,.55f), new Vector3(0,2,14), EnergyGrenadeRules.Gravity, EnergyGrenadeRules.Fuse);
            shooterRoot.transform.position = end - Vector3.up * .8f;
            var (open, _) = Victim("Uncovered target", end + Vector3.left * 2 - Vector3.up * .8f);
            var openHealth = em.GetComponentData<PredictedPlayerGhost>(open);
            RaidHealth.Initialize(ref openHealth);
            em.SetComponentData(open, openHealth);
            var (covered, _) = Victim("Covered target", end + Vector3.right * 2 - Vector3.up * .8f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = end + Vector3.right;
            wall.transform.localScale = new Vector3(.08f,10,10);
            Physics.SyncTransforms();
            while (clock < 3.2) Tick();
            Check(em.GetComponentData<PredictedPlayerGhost>(shooter).CurrentHealth < 600, "Thrower receives own blast damage");
            float expected = 440 - EnergyGrenadeRules.DamageAt(2);
            Check(Mathf.Abs(em.GetComponentData<PredictedPlayerGhost>(open).CurrentHealth - expected) < .1f,
                "Blast distributes damage across body parts once, conserving damage");
            Check(em.GetComponentData<PredictedPlayerGhost>(covered).CurrentHealth == 1000, "Covered victim protected");
            using var query = em.CreateEntityQuery(typeof(EnergyGrenadeStateRpc), typeof(SendRpcCommandRequest));
            using var poses = query.ToComponentDataArray<EnergyGrenadeStateRpc>(Allocator.Temp);
            int explosions = 0;
            foreach (var pose in poses) if (pose.Exploded != 0) explosions++;
            Check(explosions == 1 && item.Quantity == 2, "One explosion event and one total item spent");
            Debug.Log("Energy grenade authority checks: " + checks + " passed using actual server system. Includes replay/cooldown/raid/death rejection, self damage and cover. Transport and gameplay Play acceptance remain separate.");
        }
        finally
        {
            activeProperty.SetValue(null, previousMap);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
