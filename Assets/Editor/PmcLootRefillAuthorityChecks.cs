using System;
using System.Linq;
using System.Reflection;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Real refill update in a disposable server World and preview scene. No profile or transport.
public static class PmcLootRefillAuthorityChecks
{
    private delegate void Refill(ref ServerGameSystem system, ref SystemState state);

    [MenuItem("Moonkov/AI/Check Shared Loot Refill")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit Mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var previous = MoonRaidMap.Active;
        using var world = new World("PMC loot refill checks", WorldFlags.GameServer);
        try
        {
            var root = new GameObject("Temporary raid refill map"); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, scene);
            var map = root.AddComponent<MoonRaidMap>();
            typeof(MoonRaidMap).GetField("m_LootPositions", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(map, new[] { Vector3.zero });
            typeof(MoonRaidMap).GetField("m_LootRespawnSeconds", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(map, 1f);
            typeof(MoonRaidMap).GetProperty("Active").SetValue(null, map);

            var manager = world.EntityManager;
            var handle = world.GetOrCreateSystem<ServerGameSystem>();
            var update = (Refill)typeof(ServerGameSystem).GetMethod("RefreshLootContainers", BindingFlags.Instance | BindingFlags.NonPublic)
                .CreateDelegate(typeof(Refill));
            using var lootQuery = manager.CreateEntityQuery(typeof(RaidLootWorld));
            var loot = manager.GetComponentObject<RaidLootContainers>(lootQuery.GetSingletonEntity());
            loot.Initialize(1, _ => RaidLootProfile.Industrial, map);
            var cache = loot.Containers[0];
            cache.Items.RemoveAll(item => InventoryCatalog.Get(item.Code).Kind != ItemKind.Root);
            int version = cache.Version;

            var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
            var player = manager.CreateEntity(typeof(PredictedPlayerGhost), typeof(LocalTransform));
            manager.SetComponentData(player, health); manager.SetComponentData(player, LocalTransform.FromPosition(new float3(100, 0, 0)));
            var connection = manager.CreateEntity(typeof(RaidSession), typeof(JoinedClient), typeof(NetworkId));
            manager.SetComponentData(connection, new RaidSession { Phase = RaidPhase.Active });
            manager.SetComponentData(connection, new JoinedClient { PlayerEntity = player });
            manager.SetComponentData(connection, new NetworkId { Value = 1 });

            var npc = manager.CreateEntity(typeof(DollSingerEnemy), typeof(Simulate), typeof(PredictedPlayerGhost));
            manager.SetComponentData(npc, health);
            var brain = new DollSingerEnemyBrain { Action = PmcAction.Scavenge, Cache = 0, LootUntil = 5 };
            manager.AddComponentObject(npc, brain);
            void Tick(double now)
            {
                world.SetTime(new TimeData(now, 1));
                ref var server = ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
                ref var state = ref world.Unmanaged.ResolveSystemStateRef(handle);
                update(ref server, ref state);
            }

            Tick(1); Tick(3);
            if (loot.RefillGeneration(0) != 0 || cache.Version != version || RaidLootRefillState.HasContents(cache))
                throw new InvalidOperationException("The server refilled an empty cache during the PMC search.");
            brain.Action = PmcAction.Engage;
            Tick(4);
            if (loot.RefillGeneration(0) != 1 || cache.Version != version + 1 || !RaidLootRefillState.HasContents(cache))
                throw new InvalidOperationException("The server failed to refill after the search was interrupted.");
            Debug.Log("PMC shared loot refill checks passed in an isolated server World. Real raid acceptance remains separate.");
        }
        finally
        {
            typeof(MoonRaidMap).GetProperty("Active").SetValue(null, previous);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
