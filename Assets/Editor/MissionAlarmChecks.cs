using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Campaign;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Missions;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    private delegate void AlarmHandler(ref ServerGameSystem system, ref SystemState state, RaidMissionWorld context, double now);

    public static string RunMissionAlarmContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var holder = new GameObject("Temporary machine alarm checks");
        var siteObject = new GameObject("Temporary mission site");
        siteObject.transform.SetParent(holder.transform); siteObject.SetActive(false);
        var site = siteObject.AddComponent<RaidMissionSite>();
        Vector3 origin = new Vector3(-9200, 1000, -9200);
        site.Author(new[] { new RaidMissionAnchor { Id = "sampler", Approach = origin, Position = origin + Vector3.up } },
            Array.Empty<RaidMissionDevice>(), null, null);
        var profile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Snake.asset"));
        profile.Liquid = false; profile.PatrolRadius = 0;
        World world = null;
        var fixtureEnemies = new List<Entity>();
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(120, .5f, 120));
            world = CounterplayWorld("machine alarm");
            var manager = world.EntityManager;
            var handle = world.GetOrCreateSystem<ServerGameSystem>();
            ref var server = ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
            ref var state = ref world.Unmanaged.ResolveSystemStateRef(handle);
            var emit = (AlarmHandler)typeof(ServerGameSystem).GetMethod("EmitCutterAlarm", BindingFlags.Instance | BindingFlags.NonPublic)
                .CreateDelegate(typeof(AlarmHandler));
            var definition = MissionCatalog.Mooncore();
            var operation = new MissionOperation(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), 1, definition, 10);
            var context = new RaidMissionWorld { Site = site, Operation = operation };
            var step = typeof(RaidHUD).GetMethod("MissionStepCopy", BindingFlags.Static | BindingFlags.NonPublic);
            string warning = (string)step.Invoke(null, new object[] { definition,
                new RaidMissionSnapshotRpc { Phase = MissionPhase.Active, CompletedMask = 3 }, 0 });
            Check(warning.Contains("附近敌人"), "The regular mission does not warn about the cutter before activation.");
            Check(CampaignCatalog.Find("CM03").Nodes[0].Briefing.Contains("附近敌人"),
                "The first chapter does not warn about the cutter before activation.");

            DollSingerEnemyBrain MakeBrain(Vector3 position, bool leaving = false, bool clash = false)
            {
                var brain = new DollSingerEnemyBrain { Inventory = InventoryGraph.Create(false, false),
                    Random = new Unity.Mathematics.Random(137), Seed = 1, Leaving = leaving };
                var entity = manager.CreateEntity(typeof(DollSingerEnemy), typeof(Simulate), typeof(LocalTransform), typeof(PredictedPlayerGhost));
                fixtureEnemies.Add(entity);
                manager.SetComponentData(entity, LocalTransform.FromPosition(position));
                var health = default(PredictedPlayerGhost); RaidHealth.Initialize(ref health); health.CurrentAmmo = 15; health.PrimaryAmmo = 15;
                manager.SetComponentData(entity, health);
                if (clash) { manager.AddComponent<ClashBot>(entity); brain.ClashParticipantId = 1; }
                manager.AddComponentObject(entity, brain);
                return brain;
            }
            var nearby = MakeBrain(origin + Vector3.forward * 8);
            var far = MakeBrain(origin + Vector3.forward * 55);
            var leaving = MakeBrain(origin + Vector3.forward * 8, leaving: true);
            var clash = MakeBrain(origin + Vector3.forward * 8, clash: true);
            var alien = Alien(holder.transform, world, profile, origin + Vector3.forward * 8 + Vector3.up * (.56f * profile.Scale),
                MultiplayerRole.Server, -29000);
            manager.AddComponent<Simulate>(alien.GhostGameObject.LinkedEntity);
            emit(ref server, ref state, context, 10);
            Check(nearby.MissionAlarmId == null && alien.Action == AlienAction.Roam, "The sampler attracted enemies before cutting started.");
            Check(operation.RestoreCompleted(3, 10), "Could not restore the powered test facility.");
            Check(operation.Begin("actor", 1, 2, 1, 10) == MissionActionResult.Started, "Cutting start did not begin.");
            Check(operation.Renew("actor", 1, 11.5) == MissionActionResult.Running, "Cutting start ended too early.");
            Check(operation.Renew("actor", 1, 13) == MissionActionResult.Completed && operation.IsProcessing("cutting"),
                "The real facility did not enter its automatic cutting phase.");
            emit(ref server, ref state, context, 13);
            Check(nearby.MissionAlarmId == operation.Id && nearby.Target == Entity.Null && !nearby.Visible,
                "A nearby PMC did not receive an anonymous machine location.");
            Check(far.MissionAlarmId == null && leaving.MissionAlarmId == null && clash.MissionAlarmId == null,
                "Out-of-range, departing or bounty actors responded to the alarm.");
            double rememberedUntil = nearby.MissionAlarmUntil;
            emit(ref server, ref state, context, 15);
            Check(nearby.MissionAlarmUntil == rememberedUntil, "Repeated sound restarted the same investigation.");
            Vector3 start = alien.transform.position;
            Step(alien, 45);
            Check(alien.Action == AlienAction.Search && alien.Target == Entity.Null && alien.AttackCount == 0,
                "The alien failed to investigate without a fabricated target.");
            Check(Vector3.Distance(alien.transform.position, origin) < Vector3.Distance(start, origin) - .1f,
                "The alien did not move toward the machine.");

            var tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
            var ai = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            var mapObject = new GameObject("Temporary inactive raid map"); mapObject.transform.SetParent(holder.transform); mapObject.SetActive(false);
            var map = mapObject.AddComponent<MoonRaidMap>();
            NavMeshData nav = null;
            NavMeshDataInstance navInstance = default;
            try
            {
                typeof(DollSingerEnemySystem).GetField("m_Tuning", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ai, tuning);
                var settings = NavMesh.GetSettingsByIndex(0);
                settings.agentRadius = .35f; settings.agentHeight = 1.8f; settings.agentClimb = .35f;
                settings.overrideVoxelSize = true; settings.voxelSize = .25f;
                var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, area = 0,
                    transform = Matrix4x4.Translate(origin - Vector3.up * .25f), size = new Vector3(80, .5f, 80) } };
                nav = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(origin, new Vector3(82, 8, 82)), Vector3.zero, Quaternion.identity);
                Check(nav != null, "The isolated PMC navigation could not be built.");
                navInstance = NavMesh.AddNavMeshData(nav);
                typeof(DollSingerEnemySystem).GetField("m_Filter", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(ai, new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas });
                var health = default(PredictedPlayerGhost); RaidHealth.Initialize(ref health); health.CurrentAmmo = 15; health.PrimaryAmmo = 15;
                var evaluate = typeof(DollSingerEnemySystem).GetMethod("EvaluateDecision", BindingFlags.Instance | BindingFlags.NonPublic);
                object[] args = { nearby, origin, health, map, 14d, PmcAction.Scavenge, 0f, null, false };
                evaluate.Invoke(ai, args);
                Check((PmcAction)args[5] == PmcAction.Investigate, "The PMC ignored the machine sound in its real decision logic.");
                Vector3 pmcPosition = origin + Vector3.forward * 8;
                typeof(DollSingerEnemySystem).GetMethod("Decide", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(ai, new object[] { nearby, pmcPosition, health, map, 14d });
                typeof(DollSingerEnemySystem).GetMethod("UpdatePath", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(ai, new object[] { nearby, pmcPosition, nearby.TacticalGoal, 14d });
                var direction = (Vector3)typeof(DollSingerEnemySystem).GetMethod("FollowPath", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { nearby, pmcPosition });
                Check(nearby.Action == PmcAction.Investigate && !nearby.PathFailed &&
                    Vector3.Dot(direction, origin - pmcPosition) > .5f, "The PMC did not choose a valid route toward the machine.");
                var target = manager.CreateEntity(); nearby.Target = target; nearby.Visible = true; nearby.Confidence = 1;
                nearby.Contacts[target] = new PmcContact { Visible = true, Confirmed = true, Confidence = 1, LastSeen = 14 };
                evaluate.Invoke(ai, args);
                Check((PmcAction)args[5] == PmcAction.Engage, "A machine sound overrode a visible enemy.");
                nearby.Target = Entity.Null; nearby.Visible = false;
                args[4] = 44d; evaluate.Invoke(ai, args);
                Check((PmcAction)args[5] == PmcAction.Scavenge, "A stale sound kept the PMC investigating forever.");
            }
            finally { if (navInstance.valid) navInstance.Remove(); if (nav) Object.DestroyImmediate(nav); Object.DestroyImmediate(tuning); }

            Vector3 source = origin + Vector3.up;
            Check(MissionAlarm.Audible(source, source + Vector3.right * 30), "A nearby unblocked alarm was not audible.");
            CounterplayBox(holder.transform, "Sound wall", source + Vector3.right * 15, new Vector3(.5f, 4, 8));
            Check(!MissionAlarm.Audible(source, source + Vector3.right * 30), "A wall did not reduce alarm hearing range.");
            Check(MissionAlarm.Audible(source, source + Vector3.right * 20), "A nearby obstructed alarm became completely silent.");
            Check(!MissionAlarm.Audible(source, source + Vector3.right * 41), "The alarm exceeded its local range.");
            operation.Tick(58.1);
            var late = MakeBrain(origin + Vector3.forward * 8);
            emit(ref server, ref state, context, 58.1);
            Check(!operation.IsProcessing("cutting") && late.MissionAlarmId == null, "The alarm continued after cutting finished.");
            return "PASS " + checks + " machine alarm checks: real process, server dispatch, NPC decisions, movement, occlusion and stop.";
        }
        finally
        {
            if (world != null && world.IsCreated)
            {
                foreach (var entity in fixtureEnemies)
                    if (world.EntityManager.Exists(entity)) world.EntityManager.DestroyEntity(entity);
                world.Dispose();
            }
            Object.DestroyImmediate(holder); Object.DestroyImmediate(profile);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
