using System;
using System.Collections.Generic;
using System.Reflection;
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Isolated server-entity and real motor checks. No Play mode, transport or profile.
public static class PmcWeaponVarietyReview
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use an idle Editor in Edit Mode.");
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var holder = new GameObject("Temporary PMC variety checks") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false);
        var actorObject = new GameObject("Temporary PMC motor") { hideFlags = HideFlags.HideAndDontSave };
        var instance = typeof(WeaponManager).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
        var oldManager = instance.GetValue(null);
        var pendingField = typeof(LeaderboardManager).GetField("_pendingPlayers", PrivateStatic);
        var oldPending = pendingField.GetValue(null);
        var retired = (HashSet<int>)typeof(LeaderboardManager).GetField("s_RetiredRaidActors", PrivateStatic).GetValue(null);
        var oldRetired = new List<int>(retired);
        var kits = new List<string>();
        try
        {
            pendingField.SetValue(null, new Queue<(int, FixedString64Bytes)>());
            var manager = holder.AddComponent<WeaponManager>();
            manager.WeaponRegistry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
            instance.SetValue(null, manager);
            var map = holder.AddComponent<MoonRaidMap>();
            using var world = new World("PMC variety checks", WorldFlags.GameServer);
            var system = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            var systemType = typeof(DollSingerEnemySystem);
            systemType.GetField("m_SpawnRandom", PrivateInstance).SetValue(system, new Unity.Mathematics.Random(1973));
            systemType.GetField("m_NextId", PrivateInstance).SetValue(system, -2000000000);
            var spawn = systemType.GetMethod("SpawnActor", PrivateInstance);
            var think = systemType.GetMethod("Think", PrivateInstance);
            var drop = systemType.GetMethod("DropCorpse", PrivateInstance);
            var em = world.EntityManager;
            var prefab = em.CreateEntity(typeof(GhostOwner), typeof(LocalTransform), typeof(PredictedPlayerGhost),
                typeof(GhostGameObjectGuid), typeof(PlayerGhost.PlayerData));
            var lootEntity = em.CreateEntity(typeof(RaidLootWorld));
            var loot = new RaidLootContainers(); em.AddComponentObject(lootEntity, loot);
            var target = em.CreateEntity();
            var actor = actorObject.AddComponent<PlayerGhost>();
            var eye = new GameObject("Temporary shot origin").transform;
            eye.SetParent(actorObject.transform, false); eye.localPosition = Vector3.up * 1.15f;
            typeof(PlayerGhost).GetField("<ShotOrigin>k__BackingField", PrivateInstance).SetValue(actor, eye);
            var origin = Vector3.one * 9000;
            actorObject.transform.position = origin;
            bool Close(string code) => code == "shotgun" || code == "cicada" || code == "thorn";
            bool Selects(PlayerInput input, int slot) => slot == 1 ? input.EquipHalo : input.EquipRevolver;
            var seen = new HashSet<string>();
            for (int i = 0; i < 10; i++)
            {
                var entity = (Entity)spawn.Invoke(system, new object[] { map, prefab, origin, 10d, 2, null });
                var brain = em.GetComponentObject<DollSingerEnemyBrain>(entity);
                var graph = brain.Inventory;
                var primary = graph.Equipped("Primary"); var secondary = graph.Equipped("Secondary");
                kits.Add(primary.Code + "/" + secondary.Code);
                Check(seen.Add(primary.Code) && primary.Code != secondary.Code, "SpawnActor repeated a primary or ignored the varied inventory.");
                Check(graph.Count("medkit", true) == 2 && graph.Count("cells", true) == map.EnemyCells,
                    "Weapon selection changed the spawn supplies.");
                var health = em.GetComponentData<PredictedPlayerGhost>(entity);
                int closeSlot = Close(primary.Code) ? 1 : 2, longSlot = closeSlot == 1 ? 2 : 1;
                health.EquippedWeaponSlot = longSlot;
                DollSingerWeapons.SyncEquipment(ref health, graph, manager.WeaponRegistry);
                health.WeaponEquipRemaining = 0;
                brain.Action = PmcAction.Engage; brain.Target = target; brain.Visible = true; brain.Confidence = 1;
                brain.Yaw = brain.Pitch = 0; brain.VisibleSince = 8; brain.LastSeenTime = 10;
                brain.LastHealth = health.CurrentHealth; brain.Goal = brain.TacticalGoal = origin;
                brain.NextDecision = brain.NextSense = brain.NextPath = brain.NextAimError = 100;
                brain.NextNearbyLootScan = brain.NextShieldSupplyCheck = brain.NextOxygenSupplyCheck = 100;
                brain.LastSeen = eye.position + Vector3.forward * 8;
                PlayerInput Think(double now)
                {
                    var args = new object[] { entity, brain, actor, LocalTransform.FromPosition(origin), health, map, now, 1f / 60 };
                    var result = (PlayerInput)think.Invoke(system, args);
                    health = (PredictedPlayerGhost)args[4];
                    return result;
                }
                var close = Think(10);
                Check(Selects(close, closeSlot) && !close.Shoot && !close.Reload && brain.NextTacticalSwitch == 13,
                    "The real motor did not request the varied close weapon safely.");
                Check(DollSingerWeapons.TryEquip(ref health, close, manager.WeaponRegistry), "The real close-weapon request failed.");
                DollSingerWeapons.SyncEquipment(ref health, graph, manager.WeaponRegistry); health.WeaponEquipRemaining = 0;
                brain.LastSeen = eye.position + Vector3.forward * 60; brain.LastSeenTime = 11;
                var early = Think(11);
                Check(!early.EquipHalo && !early.EquipRevolver, "The varied kit bypassed the three-second tactical switch interval.");
                brain.LastSeenTime = 13.1;
                var far = Think(13.1);
                Check(Selects(far, longSlot) && !far.Shoot && !far.Reload &&
                    DollSingerWeapons.TryEquip(ref health, far, manager.WeaponRegistry), "The motor did not return to the varied long-range weapon.");
                drop.Invoke(system, new object[] { brain, LocalTransform.FromPosition(origin), 14d, 2 });
                int bag = RaidLootContainers.FirstDeathBagId + i;
                Check(loot.Containers[bag].Find(primary.Id)?.Code == primary.Code &&
                    loot.Containers[bag].Find(secondary.Id)?.Code == secondary.Code && graph.Find(primary.Id) == null,
                    "The actual corpse entry point lost or replaced a spawned weapon.");
            }
            Check(seen.Count == 10, "The real spawn entry point did not cover the full weapon bag.");
            return new { checks, kits, scene = original.path, mode = "Edit Mode isolated server entities; no transport or Play mode" };
        }
        finally
        {
            instance.SetValue(null, oldManager);
            pendingField.SetValue(null, oldPending);
            retired.Clear(); retired.UnionWith(oldRetired);
            UnityEngine.Object.DestroyImmediate(actorObject); UnityEngine.Object.DestroyImmediate(holder);
            SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
