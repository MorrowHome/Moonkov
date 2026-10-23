using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    [MenuItem("Tools/Moonkov/Aliens/Check tactical attack decisions")]
    public static void RunTacticsContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var passed = new List<string>();
        try
        {
            CheckTacticalBoss("Tentacle", 8, AlienAttackKind.TentacleSweep, false, passed);
            CheckTacticalBoss("Tentacle", 20, AlienAttackKind.TentacleSlam, false, passed);
            CheckTacticalBoss("Tentacle", 40, AlienAttackKind.MucusSpit, false, passed);
            CheckTacticalBoss("Roots", 3, AlienAttackKind.SlimePulse, false, passed);
            CheckTacticalBoss("Roots", 16, AlienAttackKind.RootSpikes, false, passed);
            CheckTacticalBoss("Roots", 40, AlienAttackKind.MucusSpit, false, passed);
            CheckTacticalBoss("Tentacle", 8, AlienAttackKind.TentacleSweep, true, passed);
            CheckPredictiveCommit("Tentacle", passed);
            CheckPredictiveCommit("Roots", passed);
            CheckRootWaveWarnings(passed);
            CheckRootClosePulse(false, passed); CheckRootClosePulse(true, passed);
            CheckStationaryRootWave(passed); CheckSlimeRangedResponse(passed);
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllLines("Library/BuildValidation/AlienIntegration/tactics-contract.txt", passed);
            Debug.Log("Tactical attacks passed: " + string.Join("; ", passed));
        }
        finally { UnityEngine.SceneManagement.SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
    }
    private static void CheckTacticalBoss(string name, float distance, AlienAttackKind expected, bool followup, List<string> passed)
    {
        using var world = CounterplayWorld("Tactical " + name);
        var holder = new GameObject("Temporary tactical encounter");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset"));
        p.Liquid = false; p.Damage = .1f; p.BossPursuitSpeed = p.BossRepositionSpeed = 0;
        p.PatrolRadius = 0; p.BossDisplaySeconds = .6f;
        Vector3 origin = new Vector3(-9700, 1000, -9700);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(140, .5f, 140));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * (.56f * p.Scale), MultiplayerRole.Server, -24000);
            Humanoid(holder.transform, world, origin + Vector3.forward * distance, false, 42);
            var kinds = new List<AlienAttackKind>(); uint sequence = 0;
            for (int frame = 0; frame < (followup ? 540 : 180); frame++)
            {
                Step(actor, 1);
                if (actor.AttackCount != sequence)
                {
                    sequence = actor.AttackCount; kinds.Add(actor.AttackKind);
                    if (!followup || kinds.Count == 2) break;
                }
            }
            Require(kinds.Count > 0 && kinds[0] == expected, name + " at " + distance + "m chose " + (kinds.Count > 0 ? kinds[0].ToString() : "nothing"));
            if (followup) Require(kinds.Count == 2 && kinds[1] != kinds[0], "Boss repeated its cooling-down skill instead of another legal attack.");
            passed.Add(name + " " + distance + "m: " + string.Join(" -> ", kinds));
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }
    private static void CheckPredictiveCommit(string name, List<string> passed)
    {
        using var world = CounterplayWorld("Predictive " + name);
        var holder = new GameObject("Temporary visible motion fixture");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset"));
        p.Liquid = false; p.Damage = .1f; p.BossPursuitSpeed = p.BossRepositionSpeed = 0;
        p.PatrolRadius = 0; p.BossDisplaySeconds = .6f;
        Vector3 origin = new Vector3(-9700, 1000, -9700);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(140, .5f, 140));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * (.56f * p.Scale), MultiplayerRole.Server, -24001);
            var target = Humanoid(holder.transform, world, origin + Vector3.forward * 40, false, 42);
            bool locked = false; Vector3 point = default; uint sequence = 0;
            for (int frame = 0; frame < 160; frame++)
            {
                if (!locked)
                {
                    target.transform.position += Vector3.right * .1f;
                    world.EntityManager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(target.transform.position));
                }
                Step(actor, 1);
                var state = world.EntityManager.GetComponentData<ProceduralAlienEnemy.State>(actor.GhostGameObject.LinkedEntity);
                if (!locked && state.Action == AlienAction.Windup)
                {
                    point = state.AttackPoint; sequence = state.AttackSequence;
                    Require(point.x - target.transform.position.x > .5f && point.x - target.transform.position.x <= 3.1f, "Boss failed bounded visible-motion lead.");
                    Require(actor.DamageCount == 0, "Predictive windup dealt damage.");
                    target.transform.position += Vector3.left * 8;
                    world.EntityManager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(target.transform.position));
                    locked = true;
                }
                if (locked && state.AttackSequence == sequence)
                    Require(Vector3.Distance(point, state.AttackPoint) < .02f, "Committed attack tracked the player's dodge.");
            }
            Require(locked && actor.MucusHits == 0, "Changed direction did not evade locked predictive shot.");
            passed.Add(name + ": bounded motion lead, fixed warning, direction change dodges actual projectile");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }
    private static void CheckRootClosePulse(bool dodge, List<string> passed)
    {
        using var world = CounterplayWorld("Root close pulse");
        var holder = new GameObject("Temporary root close response");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Roots.asset"));
        p.Liquid = false; p.Damage = 1; p.AttackCooldown = 60; p.PatrolRadius = 0;
        p.BossPursuitSpeed = p.BossRepositionSpeed = 0; p.BossDisplaySeconds = .6f;
        Vector3 origin = new Vector3(-9700, 1000, -9700);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(100, .5f, 100));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * (.56f * p.Scale), MultiplayerRole.Server, -24002);
            var target = Humanoid(holder.transform, world, origin + Vector3.forward * 3, false, 42);
            bool committed = false;
            for (int frame = 0; frame < 180; frame++)
            {
                Step(actor, 1);
                if (!committed && actor.Action == AlienAction.Windup)
                {
                    committed = true;
                    Require(actor.AttackKind == AlienAttackKind.SlimePulse && actor.DamageCount == 0, "Missing safe root close warning.");
                    if (dodge)
                    {
                        target.transform.position += Vector3.right * 9;
                        world.EntityManager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(target.transform.position));
                    }
                }
            }
            Require(committed && actor.DamageCount == (dodge ? 0u : 1u), "Root close pulse hit/dodge mismatch.");
            passed.Add("Root close pulse: " + (dodge ? "escape during windup avoids damage" : "one real hit on a stationary nearby target"));
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }
    private static void CheckStationaryRootWave(List<string> passed)
    {
        using var world = CounterplayWorld("Root wave stationary target");
        var holder = new GameObject("Temporary root wave damage");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Roots.asset"));
        p.Liquid = false; p.Damage = 1; p.AttackCooldown = 60; p.PatrolRadius = 0;
        p.BossPursuitSpeed = p.BossRepositionSpeed = 0; p.BossDisplaySeconds = .6f;
        Vector3 origin = new Vector3(-9700, 1000, -9700);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(100, .5f, 100));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * (.56f * p.Scale), MultiplayerRole.Server, -24003);
            Humanoid(holder.transform, world, origin + Vector3.forward * 10, false, 42);
            SetHealth(world.EntityManager, actor.GhostGameObject.LinkedEntity, 40);
            Step(actor, 300);
            Require(actor.AttackCount == 1 && actor.AttackKind == AlienAttackKind.RootSpikes && actor.DamageCount == 1,
                "Advancing root rows kept hitting the original stationary patch.");
            passed.Add("Enraged roots: stationary first-row target takes one hit, later rows advance");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }
    private static void CheckSlimeRangedResponse(List<string> passed)
    {
        using var world = CounterplayWorld("Slime distant response");
        var holder = new GameObject("Temporary slime range fixture");
        var p = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Slime.asset"));
        p.Liquid = false; p.Damage = 1; p.AttackCooldown = 60; p.PatrolRadius = 0;
        Vector3 origin = new Vector3(-9700, 1000, -9700);
        try
        {
            CounterplayBox(holder.transform, "Floor", origin - Vector3.up * .25f, new Vector3(100, .5f, 100));
            var actor = Alien(holder.transform, world, p, origin + Vector3.up * (.56f * p.Scale), MultiplayerRole.Server, -24004);
            Humanoid(holder.transform, world, origin + Vector3.forward * 9, false, 42);
            Step(actor, 1);
            Require(actor.Action == AlienAction.Windup && actor.AttackKind == AlienAttackKind.MucusSpit,
                "Slime outside pulse radius failed to select its ranged response.");
            passed.Add("Slime outside real pulse radius selects ranged spit");
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(p); }
    }
    private static void CheckRootWaveWarnings(List<string> passed)
    {
        var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Roots.asset");
        var holder = new GameObject("Temporary root wave warning fixture");
        try
        {
            using var view = new AlienAttackPresentation(holder.transform, p);
            var pattern = AlienCombatPattern.For(p, 2, 1, AlienAttackKind.RootSpikes);
            var state = new ProceduralAlienEnemy.State { Action = AlienAction.Windup, AttackKind = pattern.Kind,
                AttackSequence = 1, BossPhase = 2, Scale = p.Scale, AttackDirection = Vector3.forward,
                AttackOrigin = Vector3.up * .56f * p.Scale, AttackPoint = Vector3.forward * 10 };
            view.Observe(state, pattern, Vector3.up * 3);
            int count = 0;
            foreach (var line in holder.GetComponentsInChildren<LineRenderer>()) if (line.name == "Boss committed attack warning" && line.enabled) count++;
            Require(count == 9, "All three root rows must be warned before the first strike.");
            Require(Vector3.Distance(pattern.PulsePoint(state.AttackPoint, state.AttackDirection, 0),
                pattern.PulsePoint(state.AttackPoint, state.AttackDirection, 2)) > pattern.Radius * 3, "Root wave repeatedly hit the same patch.");
            Require(holder.GetComponentsInChildren<Collider>().Length == 0, "Warning created client damage geometry.");
            passed.Add("Root wave: nine patches pre-warned, three distinct rows, zero client colliders");
        }
        finally { Object.DestroyImmediate(holder); }
    }
}
