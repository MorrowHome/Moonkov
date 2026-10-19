using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    [MenuItem("Tools/Moonkov/Aliens/Check giant boss formal map placement")]
    public static void RunFormalHorrorPlacement()
    {
        const string path = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            AlienEncounterSettings settings = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                settings = root.GetComponentInChildren<AlienEncounterSettings>(true);
                if (settings) break;
            }
            Require(settings, "Formal map has no alien encounters.");
            using var world = new World("Giant formal spawn fixture", WorldFlags.GameServer);
            var system = world.GetOrCreateSystemManaged<AlienEnemySpawnSystem>();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var observers = (List<RaidSpawnObserver>)typeof(AlienEnemySpawnSystem).GetField("m_Observers", flags).GetValue(system);
            var stateType = typeof(AlienEnemySpawnSystem).GetNestedType("EncounterState", BindingFlags.NonPublic);
            var method = typeof(AlienEnemySpawnSystem).GetMethod("TrySelectPosition", flags);
            var report = new List<string> { "Formal slots=" + settings.Count + ", simultaneous bosses=" + settings.MaximumBosses };
            var bosses = new HashSet<AlienSpecies>();
            for (int i = 0; i < settings.Count; i++)
            {
                var spawn = settings[i]; if (!spawn.Profile || !spawn.Profile.Boss) continue;
                observers.Clear(); observers.Add(new RaidSpawnObserver { Position = spawn.Position + Vector3.forward * 150,
                    Forward = Vector3.forward, CheckVisibility = true });
                var state = Activator.CreateInstance(stateType, true);
                object[] args = { settings, spawn, state, Vector3.zero, Quaternion.identity };
                bool found = false;
                for (int attempt = 0; attempt < 8 && !found; attempt++) found = (bool)method.Invoke(system, args);
                report.Add(spawn.Profile.CreatureName + ": valid giant spawn=" + found + " at " + args[3]);
                Require(found, "No valid formal-map giant spawn for " + spawn.Profile.CreatureName);
                bosses.Add(spawn.Profile.Species);
            }
            Require(bosses.Count == 2, "Formal map must reference both giant bosses.");
            var states = (System.Collections.IList)typeof(AlienEnemySpawnSystem).GetField("m_Encounters", flags).GetValue(system);
            for (int i = 0; i < settings.Count; i++) states.Add(Activator.CreateInstance(stateType, true));
            for (int i = 0; i < settings.Count; i++)
            {
                if (!settings[i].Profile || !settings[i].Profile.Boss) continue;
                stateType.GetField("SpawnCount").SetValue(states[i], 1);
                var spent = typeof(AlienEnemySpawnSystem).GetMethod("HasSpawnedBoss", flags);
                Require((bool)spent.Invoke(system, new object[] { settings, settings[i].Profile }), "Boss candidates lost their shared spent budget.");
                report.Add("Shared one-spawn boss budget retained for " + settings[i].Profile.CreatureName);
                break;
            }
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllLines("Library/BuildValidation/AlienIntegration/horror-formal-spawns.txt", report);
            Debug.Log(string.Join("; ", report));
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    [MenuItem("Tools/Moonkov/Aliens/Check horror tiers and giant bosses")]
    public static void RunHorrorContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var passed = new List<string>();
        try
        {
            foreach (string name in new[] { "Tentacle", "Roots" }) CheckGiantBoss(name, passed);
            CheckHunt("StiltWalker", passed);
            CheckHunt("Centipede", passed);
            CheckHunt("Caterpillar", passed);
            CheckHunt("Tentacle", passed);
            CheckGiantSlope(passed);
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllLines("Library/BuildValidation/AlienIntegration/horror-contract.txt", passed);
            Debug.Log("Horror contracts passed: " + string.Join("; ", passed));
        }
        finally
        {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void CheckGiantBoss(string name, List<string> passed)
    {
        using var world = new World("Horror " + name, WorldFlags.GameServer);
        world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var manager = world.EntityManager;
        var time = manager.CreateEntity(typeof(NetworkTime));
        manager.SetComponentData(time, new NetworkTime { ServerTick = new NetworkTick(1) });
        var holder = new GameObject("Temporary horror boss fixture");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset"));
        p.Liquid = false; p.Damage = 1; p.PatrolRadius = 0;
        Vector3 origin = new Vector3(-9400, 1000, -9400);
        try
        {
            var floor = new GameObject("Large boss fixture floor"); floor.transform.SetParent(holder.transform);
            floor.transform.position = origin - Vector3.up * .25f;
            floor.AddComponent<BoxCollider>().size = new Vector3(120, .5f, 120);
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * SurfaceMotor.Height * p.Scale, MultiplayerRole.Server, -19000);
            var health = manager.GetComponentData<PredictedPlayerGhost>(actor.GhostGameObject.LinkedEntity);
            health.MaxHealth = health.CurrentHealth = p.Health; manager.SetComponentData(actor.GhostGameObject.LinkedEntity, health);
            Humanoid(holder.transform, world, origin + Vector3.forward * 30, false, 42);
            Step(actor, 1);
            Require(actor.Action == AlienAction.Dominance, name + " did not introduce itself.");
            Step(actor, 30);
            Require(actor.Action == AlienAction.Dominance && actor.AttackCount == 0 && actor.DamageCount == 0, "Boss display caused damage or skipped its warning.");
            Step(actor, 150);
            Require(Vector3.Distance(actor.transform.position, origin + Vector3.up * SurfaceMotor.Height * p.Scale) > .5f, name + " cannot move at giant scale.");
            health.CurrentHealth = health.MaxHealth * .4f; manager.SetComponentData(actor.GhostGameObject.LinkedEntity, health);
            bool display = false; int edges = 0;
            for (int i = 0; i < 720; i++)
            {
                Step(actor, 1);
                bool now = actor.Action == AlienAction.Dominance;
                if (now && !display) edges++;
                display = now;
            }
            Require(actor.BossPhase == 2 && edges == 1, name + " enrage display did not run exactly once.");
            Require(actor.GetComponentsInChildren<Renderer>().Length == 0, "Server allocated boss renderers.");
            var pattern = AlienCombatPattern.For(p, 2, 1);
            Require(pattern.Windup >= 1.14f, "Enraged giant lost readable warning.");
            Require(p.Branch.reach * p.Scale * 2 >= 24, "Boss silhouette is not gigantic.");
            Require(RaidSpawnSafety.TryGroundPosition(origin, p.GroundBodyRadius(p.Scale), p.GroundBodyHeight(p.Scale), 35, out _, out _), "Giant rejected open floor.");
            var ceiling = new GameObject("Low ceiling"); ceiling.transform.SetParent(holder.transform);
            ceiling.transform.position = origin + Vector3.up * 4;
            ceiling.AddComponent<BoxCollider>().size = new Vector3(12, .3f, 12); Physics.SyncTransforms();
            Require(!RaidSpawnSafety.TryGroundPosition(origin, p.GroundBodyRadius(p.Scale), p.GroundBodyHeight(p.Scale), 35, out _, out _), "Giant fits under a low ceiling.");
            passed.Add(name + ": giant movement, harmless intro, one enrage, open floor/low ceiling, server zero renderers");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckHunt(string name, List<string> passed)
    {
        using var world = new World("Hunt " + name, WorldFlags.GameServer);
        world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var manager = world.EntityManager;
        manager.SetComponentData(manager.CreateEntity(typeof(NetworkTime)), new NetworkTime { ServerTick = new NetworkTick(1) });
        var holder = new GameObject("Temporary hunt fixture");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset"));
        p.Damage = 1; p.PatrolRadius = 0;
        Vector3 origin = new Vector3(-9500, 1000, -9500);
        try
        {
            PressureFloor(holder.transform, origin);
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * SurfaceMotor.Height * p.Scale, MultiplayerRole.Server, -19002);
            float range = name == "Caterpillar" ? 2 : 12;
            var target = Humanoid(holder.transform, world, origin + Vector3.forward * (p.Boss ? -8 : range), false, 42);
            var health = manager.GetComponentData<PredictedPlayerGhost>(target.LinkedEntity);
            health.ControllerState.YawDegrees = 180; manager.SetComponentData(target.LinkedEntity, health);
            if (!p.Boss) typeof(ProceduralAlienEnemy).GetField("m_NextAttack", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(actor, 100f);
            else p.BossDisplaySeconds = .6f;
            Vector3 start = actor.transform.position;
            Step(actor, 30);
            if (p.Boss)
            {
                Require(actor.AttackCount == 0, "Boss warned forward while attacking behind itself.");
                Step(actor, 120);
                Require(actor.AttackCount > 0, "Boss did not turn and engage a rear target.");
            }
            else if (p.Approach == AlienApproach.Stalk)
            {
                Require(Vector3.Distance(start, actor.transform.position) < .05f, "Watched stalker did not pause.");
                Step(actor, 120);
                Require(Vector3.Distance(start, actor.transform.position) > .6f, "Stalker can be frozen indefinitely.");
            }
            else if (p.Approach == AlienApproach.Flank)
                Require(Mathf.Abs(actor.transform.position.x - start.x) > .15f, "Flanker still pursues in a straight line.");
            else Require(actor.transform.position.z < start.z - .3f, "Ranged creature did not retreat during cooldown.");
            passed.Add(name + ": " + p.Approach + " actual server movement");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckGiantSlope(List<string> passed)
    {
        var floor = new GameObject("Temporary sloping boss floor");
        Vector3 origin = new Vector3(-9600, 1000, -9600);
        try
        {
            floor.transform.position = origin; floor.transform.rotation = Quaternion.Euler(-10, 0, 0);
            floor.AddComponent<BoxCollider>().size = new Vector3(100, .5f, 100); Physics.SyncTransforms();
            var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Tentacle.asset");
            Require(RaidSpawnSafety.TryGroundPosition(origin + Vector3.up, p.GroundBodyRadius(p.Scale), p.GroundBodyHeight(p.Scale), 35, out var ground, out _), "No valid sloping giant spawn.");
            var motor = new SurfaceMotor(ground + Vector3.up * SurfaceMotor.Height * p.Scale, Quaternion.identity, LayerMask.GetMask("Default", "Ground"), p.Scale);
            Vector3 start = motor.Current.Position;
            for (int i = 0; i < 60; i++) motor.TickGroundedCapsule(Vector2.up, 4, 0, 1f / 60, p.GroundBodyHeight(p.Scale), p.GroundBodyRadius(p.Scale));
            Require(motor.Current.Position.z > start.z + 2, "Giant capsule is stuck on valid lunar slope.");
            passed.Add("Giant capsule traverses 10-degree slope");
        }
        finally { Object.DestroyImmediate(floor); }
    }
}
