using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.SpiderLab;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    public static void CheckSpiderBossCombatAndCapture()
    {
        if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var passed = new List<string>();
            CheckTacticalBoss("SpiderBoss", 3, AlienAttackKind.SpiderRake, true, passed);
            CheckTacticalBoss("SpiderBoss", 9, AlienAttackKind.SpiderRush, false, passed);
            CheckTacticalBoss("SpiderBoss", 30, AlienAttackKind.MucusSpit, false, passed);
            foreach (string mode in new[] { "hit", "dodge", "wall", "high", "hidden" }) CheckRangedBoss("SpiderBoss", mode, passed);
            CheckSpiderBlockedRush(false, passed); CheckSpiderBlockedRush(true, passed);
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllLines("Library/BuildValidation/AlienIntegration/spider-boss-tactics.txt", passed);
        }
        finally { SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
        RunTacticsContract(); RunCounterplayContract(); RunPressureContract(); RunRefinementContract();
        AlienHorrorPreview.Capture();
        Debug.Log("SPIDER_BOSS_COMBAT_AND_PREVIEW_PASSED");
    }

    private static void CheckSpiderBlockedRush(bool hole, List<string> passed)
    {
        using var world = CounterplayWorld("Blocked spider rush");
        var holder = new GameObject("Temporary blocked spider lane");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(SpiderBossSetup.ProfilePath));
        p.BossPursuitSpeed = p.BossRepositionSpeed = 0; p.BossDisplaySeconds = .6f; p.PatrolRadius = 0;
        Vector3 origin = new Vector3(-11500, 1000, -11500);
        try
        {
            if (hole)
            {
                CounterplayBox(holder.transform, "Near bank", origin + new Vector3(0, -.25f, -8), new Vector3(40, .5f, 24));
                CounterplayBox(holder.transform, "Far bank", origin + new Vector3(0, -.25f, 18), new Vector3(40, .5f, 24));
            }
            else
            {
                CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(40, .5f, 40));
                CounterplayBox(holder.transform, "Low barrier below sight line", origin + new Vector3(0, .4f, 4.5f), new Vector3(16, .8f, .4f));
            }
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * SurfaceMotor.Height * p.Scale, MultiplayerRole.Server, -32003);
            Humanoid(holder.transform, world, origin + Vector3.forward * 9, false, 42);
            for (int i = 0; i < 120 && actor.AttackCount == 0; i++) Step(actor, 1);
            Require(actor.AttackCount == 1 && actor.AttackKind == AlienAttackKind.MucusSpit,
                "Blocked rush did not choose a safe ranged alternative: " + hole);
            passed.Add((hole ? "Missing floor" : "Low barrier") + ": refuses rush and selects a legal projectile");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    public static void InstallAndCheckSpiderBoss()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        SpiderBossSetup.Install();
        CheckSpiderBossIntegration();
    }

    public static void CheckSpiderBossIntegration()
    {
        if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        RunSpiderBossContract();
        RunAmbushContract();
        RunHorrorContract();
        RunFormalHorrorPlacement();
        Debug.Log("SPIDER_BOSS_VALIDATION_PASSED");
    }

    [MenuItem("Tools/Moonkov/Aliens/Check giant spider boss")]
    public static void RunSpiderBossContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var passed = new List<string>();
        try
        {
            var boss = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(SpiderBossSetup.ProfilePath);
            var ceiling = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Spider.asset");
            Require(boss && boss.IsSpiderBoss && !boss.IsCeilingSpider && boss.Tier == AlienThreatTier.Boss, "Missing independent spider boss role.");
            Require(ceiling && ceiling.IsCeilingSpider && ceiling.Scale == 1 && ceiling.Health == 180, "Original ceiling spider changed.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpiderBossSetup.PrefabPath);
            Require(prefab && prefab.GetComponent<ProceduralAlienEnemy>().Profile == boss, "Boss prefab points to the ordinary spider.");
            foreach (string name in new[] { "Tentacle", "Roots" })
            {
                var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset");
                Require(!p.Boss && p.Tier == AlienThreatTier.Common && p.Scale < 1.5f && p.Health < 500, name + " is still an important boss.");
            }
            foreach (byte phase in new byte[] { 1, 2 }) foreach (var kind in new[] { AlienAttackKind.SpiderRake, AlienAttackKind.SpiderRush, AlienAttackKind.MucusSpit })
            {
                var pattern = AlienCombatPattern.For(boss, phase, 1, kind);
                Require(pattern.Kind == kind && pattern.Recovery >= .9f && pattern.Windup >= (kind == AlienAttackKind.MucusSpit ? .85f : 1.15f), "Unreadable spider boss attack.");
            }
            passed.Add("Independent profile/prefab, preserved ceiling spider, demoted former bosses, readable phase timings");
            foreach (var kind in new[] { AlienAttackKind.SpiderRake, AlienAttackKind.SpiderRush })
                foreach (string mode in new[] { "hit", "dodge", "wall", "death", "upper-floor" })
                    CheckSpiderAttack(kind, mode, passed);
            CheckSpiderPhaseAndLoot(passed);
            CheckSpiderWarnings(passed);
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllLines("Library/BuildValidation/AlienIntegration/spider-boss-contract.txt", passed);
            Debug.Log("Spider boss contracts passed: " + string.Join("; ", passed));
        }
        finally { SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
    }

    private static void CheckSpiderAttack(AlienAttackKind kind, string mode, List<string> passed)
    {
        using var world = CounterplayWorld("Spider " + kind + mode);
        var manager = world.EntityManager;
        var holder = new GameObject("Temporary spider boss attack");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(SpiderBossSetup.ProfilePath));
        p.PatrolRadius = 0; p.BossPursuitSpeed = p.BossRepositionSpeed = 0; p.BossDisplaySeconds = .6f; p.AttackCooldown = 60;
        Vector3 origin = new Vector3(-11000, 1000, -11000);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(80, .5f, 80));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * SurfaceMotor.Height * p.Scale, MultiplayerRole.Server, -32000);
            Require(!actor.AmbushEnabled, "Boss entered the ceiling ambush controller.");
            var target = Humanoid(holder.transform, world, origin + Vector3.forward * (kind == AlienAttackKind.SpiderRush ? 9 : 3), false, 42);
            bool changed = false, struck = false; float warning = 0;
            Vector3 committed = default, start = actor.transform.position;
            for (int i = 0; i < 210; i++)
            {
                Step(actor, 1);
                var state = manager.GetComponentData<ProceduralAlienEnemy.State>(actor.GhostGameObject.LinkedEntity);
                if (state.Action == AlienAction.Windup)
                {
                    warning += 1f / 30;
                    Require(actor.AttackKind == kind, "Expected " + kind + ", got " + actor.AttackKind);
                    if (!changed)
                    {
                        changed = true; committed = state.AttackPoint;
                        if (mode == "dodge") target.transform.position += Vector3.right * 7;
                        if (mode == "upper-floor") target.transform.position += Vector3.up * 5;
                        manager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(target.transform.position));
                        if (mode == "death") SetHealth(manager, target.LinkedEntity, 0);
                        if (mode == "wall") CounterplayBox(holder.transform, "Closing barrier", origin + Vector3.forward *
                            (kind == AlienAttackKind.SpiderRush ? 4.5f : 1.8f) + Vector3.up * 3, new Vector3(20, 6, .4f));
                    }
                    Require(Vector3.Distance(committed, state.AttackPoint) < .001f, "Committed spider attack followed the dodging target.");
                }
                if (state.Action == AlienAction.Strike)
                {
                    struck = true;
                    Require(warning >= 1.14f, "Spider attack skipped its warning.");
                    Require(Vector3.Angle(actor.transform.forward, state.AttackDirection) < 1, "Spider turned its strike after commitment.");
                }
            }
            Require(changed && actor.AttackCount == 1, "Spider did not commit exactly one attack.");
            Require(actor.DamageCount == (mode == "hit" ? 1u : 0u), kind + " " + mode + " damage mismatch: " + actor.DamageCount);
            Require(struck == (mode != "death"), "Target death did not cancel the spider attack.");
            if (kind == AlienAttackKind.SpiderRush && mode != "death")
            {
                float travel = actor.transform.position.z - start.z;
                Require(mode == "wall" ? travel < 4 : travel > 7 && travel <= 9.1f, "Spider rush failed physical movement/obstruction: " + travel);
            }
            Require(actor.Body.HitboxCount == 17 && actor.GetComponentsInChildren<Renderer>().Length == 0, "Server lost the eight-leg hit rig or allocated visuals.");
            passed.Add(kind + " " + mode + ": fixed warning, actual movement, " + actor.DamageCount + " hit(s)");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckSpiderPhaseAndLoot(List<string> passed)
    {
        using var world = CounterplayWorld("Spider phase and loot");
        var manager = world.EntityManager;
        var holder = new GameObject("Temporary spider phase and loot");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(SpiderBossSetup.ProfilePath));
        p.Damage = 1; p.PatrolRadius = 0; p.BossRepositionSpeed = p.BossPursuitSpeed = 0;
        Vector3 origin = new Vector3(-11200, 1000, -11200);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(80, .5f, 80));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * SurfaceMotor.Height * p.Scale, MultiplayerRole.Server, -32001);
            Humanoid(holder.transform, world, origin + Vector3.forward * 3, false, 42);
            bool lowered = false, display = false, sawPhaseTwo = false; int displays = 0;
            for (int i = 0; i < 600; i++)
            {
                Step(actor, 1);
                bool now = actor.Action == AlienAction.Dominance;
                if (now && !display) displays++;
                display = now;
                if (now) Require(actor.DamageCount <= 1, "Display unexpectedly dealt damage.");
                if (!lowered && actor.Action == AlienAction.Windup)
                { lowered = true; SetHealth(manager, actor.GhostGameObject.LinkedEntity, 40); }
                if (lowered && actor.AttackCount == 1 && (actor.Action == AlienAction.Windup || actor.Action == AlienAction.Strike))
                    Require(actor.BossPhase == 1, "Half health changed a committed attack.");
                sawPhaseTwo |= actor.BossPhase == 2;
            }
            Require(displays == 2 && sawPhaseTwo, "Expected one introduction and one half-health display.");
            Require(ProjectileDamage(world, holder.transform, actor), "Spider cannot be shot.");
            var lootEntity = manager.CreateEntity(typeof(RaidLootWorld)); var loot = new RaidLootContainers(); loot.Initialize(1);
            manager.AddComponentObject(lootEntity, loot);
            SetHealth(manager, actor.GhostGameObject.LinkedEntity, 0); Step(actor, 2);
            Require(loot.DeathBags.Count == 1, "Spider boss reward did not drop exactly once.");
            foreach (var bag in loot.DeathBags)
            {
                var inventory = loot.Containers[bag.Key];
                Require(inventory.Validate() == InventoryError.None && inventory.Count("alien_core") == 1 && inventory.Count("alien_tissue") == 5,
                    "Spider boss reward is invalid.");
                var decoded = RaidInventoryTransport.Decode(RaidInventoryTransport.Encode(inventory));
                Require(decoded.Count("alien_core") == 1, "Spider reward transport lost the core.");
            }
            passed.Add("One harmless introduction, deferred half-health phase, shootable body, one searchable boss reward");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }

    private static void CheckSpiderWarnings(List<string> passed)
    {
        var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(SpiderBossSetup.ProfilePath);
        var holder = new GameObject("Temporary spider warning");
        try
        {
            using var presentation = new AlienAttackPresentation(holder.transform, p);
            var state = new ProceduralAlienEnemy.State { Action = AlienAction.Windup, AttackSequence = 1, Scale = p.Scale,
                AttackKind = AlienAttackKind.SpiderRush, AttackOrigin = new Vector3(-11400, 1000 + .56f * p.Scale, -11400),
                AttackPoint = new Vector3(-11400, 1000, -11391), AttackDirection = Vector3.forward, BossPhase = 1 };
            var pattern = AlienCombatPattern.For(p, 1, 1, state.AttackKind);
            presentation.Observe(state, pattern);
            var lines = holder.GetComponentsInChildren<LineRenderer>(); LineRenderer lane = null;
            foreach (var line in lines) if (line.name == "Boss committed attack warning" && line.enabled) lane = line;
            Require(lane && lane.positionCount == 27, "Missing capsule rush warning.");
            Vector3 point = lane.GetPosition(0);
            holder.transform.position += Vector3.right * 5;
            state.Action = AlienAction.Strike; state.ActionTime = .3f; presentation.Observe(state, pattern);
            Require(lane.enabled && lane.GetPosition(0) == point, "Warning moved with the rushing spider.");
            state.Action = AlienAction.Recover; presentation.Observe(state, pattern);
            Require(!lane.enabled, "Rush warning survived recovery.");
            passed.Add("Client warning stays in world space through strike and clears on recovery");
        }
        finally { Object.DestroyImmediate(holder); }
    }
}
