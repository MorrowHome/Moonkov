using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    [MenuItem("Tools/Moonkov/Aliens/Check boss movement and mucus counterplay")]
    public static void RunPressureContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var passed = new List<string>();
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            foreach (var species in new[] { AlienSpecies.Tentacle, AlienSpecies.Roots })
            {
                CheckBossMovement(species, false, false, passed);
                CheckBossMovement(species, true, false, passed);
            }
            CheckBossMovement(AlienSpecies.Roots, false, true, passed);
            foreach (string mode in new[] { "head", "dodge", "wall", "body" }) CheckMucus(mode, passed);
            var state = new PredictedPlayerGhost { CurrentHealth = 100, MucusBlindSeconds = 2.6f };
            RaidNutrition.Tick(ref state, 3, 600, 600, 600, 30, 0, 0, 1);
            Require(state.MucusBlindSeconds == 0, "Mucus did not expire.");
            state.MucusBlindSeconds = 2; state.CurrentHealth = 0;
            RaidNutrition.Tick(ref state, .1f, 600, 600, 600, 30, 0, 0, 1);
            Require(state.MucusBlindSeconds == 0, "Mucus survived death.");
            state.MucusBlindSeconds = 2; RaidNutrition.Initialize(ref state);
            Require(state.MucusBlindSeconds == 0, "Mucus survived deployment initialization.");
            passed.Add("Timer: expiry, death and deployment reset");
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllLines("Library/BuildValidation/AlienIntegration/pressure-contract.txt", passed);
            Debug.Log("Boss/mucus contracts passed: " + string.Join("; ", passed));
        }
        finally
        {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void CheckBossMovement(AlienSpecies species, bool wall, bool afterAttack, List<string> passed)
    {
        using var world = new World("Boss movement " + species, WorldFlags.GameServer);
        world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var manager = world.EntityManager;
        var time = manager.CreateEntity(typeof(NetworkTime));
        manager.SetComponentData(time, new NetworkTime { ServerTick = new NetworkTick(1) });
        var holder = new GameObject("Temporary boss pressure fixture");
        var profile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + species + ".asset"));
        profile.Liquid = false; profile.Damage = 1; profile.PatrolRadius = 0;
        Vector3 origin = new Vector3(-9200, 1000, -9200);
        try
        {
            PressureFloor(holder.transform, origin);
            var actor = Alien(holder.transform, world, profile, origin + Vector3.up * .56f * profile.Scale, MultiplayerRole.Server, -18000);
            Humanoid(holder.transform, world, origin + Vector3.forward * (afterAttack ? 6 : 12), false, 42);
            Vector3 start = actor.transform.position, previous = start;
            bool strike = false;
            Step(actor, 1);
            previous = actor.transform.position;
            if (wall)
            {
                var blocker = new GameObject("Dash wall"); blocker.transform.SetParent(holder.transform);
                blocker.transform.position = origin + new Vector3(0, 2.5f, 2);
                blocker.AddComponent<BoxCollider>().size = new Vector3(12, 5, .2f);
            }
            for (int frame = 0; frame < (afterAttack ? 180 : 40); frame++)
            {
                Step(actor, 1); strike |= actor.Action == AlienAction.Strike;
                Require(Vector3.Distance(previous, actor.transform.position) < .5f, "Boss teleported during reposition.");
                previous = actor.transform.position;
            }
            Require(actor.BossRepositions > 0, species + " did not reposition.");
            if (wall) Require(actor.transform.position.z < origin.z + 2, species + " crossed a wall.");
            else Require(Vector3.Distance(start, actor.transform.position) > 1, species + " remained stationary.");
            if (afterAttack) Require(strike, "Root relocation did not follow an attack.");
            passed.Add(species + (wall ? " swept wall stop" : afterAttack ? " attack then relocation" : " pursuit/reposition") +
                " displacement=" + Vector3.Distance(start, actor.transform.position).ToString("F2"));
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(profile); }
    }

    private static void CheckMucus(string mode, List<string> passed)
    {
        using var world = new World("Mucus " + mode, WorldFlags.GameServer);
        world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var manager = world.EntityManager;
        var time = manager.CreateEntity(typeof(NetworkTime));
        manager.SetComponentData(time, new NetworkTime { ServerTick = new NetworkTick(1) });
        var holder = new GameObject("Temporary mucus fixture");
        var profile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Slime.asset"));
        profile.Liquid = false; profile.MucusEveryAttacks = 1; profile.AttackCooldown = 30; profile.PatrolRadius = 0;
        Vector3 origin = new Vector3(-9300, 1000, -9300);
        try
        {
            PressureFloor(holder.transform, origin);
            var actor = Alien(holder.transform, world, profile, origin + Vector3.up * .56f * profile.Scale, MultiplayerRole.Server, -18001);
            var target = Humanoid(holder.transform, world, origin + Vector3.forward * 7, false, 42);
            bool launched = false;
            for (int frame = 0; frame < 100; frame++)
            {
                Step(actor, 1);
                if (actor.Action == AlienAction.Windup)
                    Require(manager.GetComponentData<PredictedPlayerGhost>(target.LinkedEntity).CurrentHealth == 100, "Windup damaged target.");
                if (!launched && actor.Action == AlienAction.Strike)
                {
                    launched = true;
                    Require(actor.AttackKind == AlienAttackKind.MucusSpit && actor.MucusHits == 0, "Mucus was instant or not selected.");
                    if (mode == "wall")
                    {
                        var blocker = new GameObject("Mucus obstruction"); blocker.transform.SetParent(holder.transform);
                        blocker.transform.position = origin + new Vector3(0, 2.5f, 3.5f);
                        blocker.AddComponent<BoxCollider>().size = new Vector3(12, 5, .2f);
                    }
                    else if (mode == "dodge" || mode == "body")
                    {
                        Vector3 moved = target.transform.position + (mode == "dodge" ? Vector3.right * 3 : Vector3.up * .9f);
                        target.transform.position = moved;
                        manager.SetComponentData(target.LinkedEntity, LocalTransform.FromPosition(moved));
                    }
                }
            }
            var health = manager.GetComponentData<PredictedPlayerGhost>(target.LinkedEntity);
            Require(launched, "No mucus launched.");
            if (mode == "head") Require(health.MucusBlindSeconds > 2 && actor.MucusHits == 1 && health.CurrentHealth < 100, "Head hit did not apply mucus.");
            else if (mode == "body") Require(health.MucusBlindSeconds == 0 && actor.MucusHits == 1 && health.CurrentHealth < 100, "Body hit incorrectly blinded or missed.");
            else Require(health.MucusBlindSeconds == 0 && actor.MucusHits == 0 && health.CurrentHealth == 100, "Mucus ignored counterplay: " + mode);
            passed.Add("Mucus " + mode);
        }
        finally { Object.DestroyImmediate(holder); Object.DestroyImmediate(profile); }
    }

    private static void PressureFloor(Transform holder, Vector3 origin)
    {
        var floor = new GameObject("Temporary pressure floor"); floor.transform.SetParent(holder);
        floor.transform.position = origin - Vector3.up * .25f;
        floor.AddComponent<BoxCollider>().size = new Vector3(40, .5f, 40);
        Physics.SyncTransforms();
    }
}
