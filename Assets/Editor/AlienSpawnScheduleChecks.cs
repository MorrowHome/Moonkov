using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Bounded scheduler checks in an isolated World. Does not load a map,
/// spawn gameplay prefabs, enter Play Mode or exercise the network transport.</summary>
public static class AlienSpawnScheduleChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Moonkov/Aliens/Check spawn cooldown and budgets")]
    public static void Run() => Debug.Log(RunChecks());

    public static string RunChecks()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run spawn scheduler checks in Edit Mode.");
        var activeBefore = AlienEncounterSettings.Active;
        var holder = new GameObject("Temporary alien spawn scheduler checks") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false);
        try
        {
            var settings = holder.AddComponent<AlienEncounterSettings>();
            Set(settings, "maximumAliens", 2);
            Set(settings, "maximumTotalSpawns", 5);
            Set(settings, "spawnsPerEncounter", 2);
            Set(settings, "respawnDelay", 30f);
            Set(settings, "respawnDelayJitter", 0f);
            using var world = new World("Alien spawn scheduler fixture", WorldFlags.GameServer);
            var system = world.GetOrCreateSystemManaged<AlienEnemySpawnSystem>();
            Set(system, "m_Settings", settings);
            Set(system, "m_Random", new System.Random(1));
            Set(system, "m_TotalSpawned", 1);
            var stateType = typeof(AlienEnemySpawnSystem).GetNestedType("EncounterState", BindingFlags.NonPublic);
            var state = Activator.CreateInstance(stateType, true);
            var actor = world.EntityManager.CreateEntity(typeof(PredictedPlayerGhost));
            world.EntityManager.SetComponentData(actor, new PredictedPlayerGhost { CurrentHealth = 100 });
            Field(state, "Entity").SetValue(state, actor);
            Field(state, "SpawnCount").SetValue(state, 1);
            ((IList)Get(system, "m_Encounters")).Add(state);

            Call(system, "RefreshPopulation", settings, 10d);
            Require(system.SpawnedCount == 1 && !Ready(system, settings, state, 10), "Living actor must occupy its encounter.");
            world.EntityManager.SetComponentData(actor, new PredictedPlayerGhost { CurrentHealth = 0 });
            Call(system, "RefreshPopulation", settings, 20d);
            Require(system.SpawnedCount == 0 && (double)Field(state, "ReadyAt").GetValue(state) == 50d,
                "Death must free the population cap and start one cooldown.");
            Call(system, "RefreshPopulation", settings, 25d);
            Require((double)Field(state, "ReadyAt").GetValue(state) == 50d,
                "Repeated dead-actor checks must not restart the cooldown.");
            world.EntityManager.DestroyEntity(actor);
            Call(system, "RefreshPopulation", settings, 26d);
            Require(!Ready(system, settings, state, 49) && Ready(system, settings, state, 50),
                "A removed corpse's encounter must reopen only after its cooldown.");
            Require(system.TotalSpawnedCount == 1, "Death or failed eligibility must not consume another spawn.");

            Field(state, "SpawnCount").SetValue(state, 2);
            Require(!Ready(system, settings, state, 60), "Per-encounter budget must stop further respawns.");
            Field(state, "SpawnCount").SetValue(state, 1);
            Set(system, "m_TotalSpawned", 5);
            Require(!Ready(system, settings, state, 60), "Total map budget must stop further respawns.");
            Call(system, "OnStopRunning");
            Require(system.TotalSpawnedCount == 5 && ReferenceEquals(Get(system, "m_Settings"), settings) &&
                !Ready(system, settings, state, 60), "An empty server must retain the current map budget.");
            Set(system, "m_TotalSpawned", 1);
            Set(system, "m_AliveCount", 2);
            Require(!Ready(system, settings, state, 60), "Living population cap must block another encounter.");
            Set(system, "m_AliveCount", 0);
            Require(Ready(system, settings, state, 60), "An eligible encounter must resume when capacity is available.");
        }
        finally { Object.DestroyImmediate(holder); }
        Require(ReferenceEquals(AlienEncounterSettings.Active, activeBefore), "Checks must preserve the loaded scene's settings.");
        return "Alien spawn scheduler: PASS (death, one-shot cooldown, corpse removal, eligibility, slot/total/live caps, empty-server budget preservation).";
    }

    public static string RunPlacementChecks()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run placement checks in Edit Mode.");
        var settings = Object.FindAnyObjectByType<AlienEncounterSettings>();
        if (!settings) throw new InvalidOperationException("Open the configured raid map first.");
        using var world = new World("Alien physical placement fixture", WorldFlags.GameServer);
        var system = world.GetOrCreateSystemManaged<AlienEnemySpawnSystem>();
        var observers = (List<RaidSpawnObserver>)Get(system, "m_Observers");
        var stateType = typeof(AlienEnemySpawnSystem).GetNestedType("EncounterState", BindingFlags.NonPublic);
        var rejected = new List<string>();
        UnityEngine.Physics.SyncTransforms();
        for (int i = 0; i < settings.Count; i++)
        {
            var spawn = settings[i];
            if (spawn == null || !spawn.Profile) { rejected.Add("Missing slot " + i); continue; }
            var state = Activator.CreateInstance(stateType, true);
            observers.Clear();
            // Nearby enough to activate, outside the visibility exclusion radius.
            float distance = Mathf.Min(settings.ActivationDistance - 1,
                Mathf.Max(settings.VisibilityDistance, settings.MinimumPlayerSpawnDistance) + settings.CandidateSearchRadius + 15);
            observers.Add(new RaidSpawnObserver { Position = spawn.Position + Vector3.right * distance, CheckVisibility = true });
            int passes = Mathf.CeilToInt(18f * (1 + (spawn.CandidatePositions?.Length ?? 0)) / settings.CandidateAttempts);
            bool found = false;
            for (int pass = 0; pass < passes && !found; pass++)
            {
                object[] arguments = { settings, spawn, state, Vector3.zero, Quaternion.identity };
                found = (bool)Call(system, "TrySelectPosition", arguments);
            }
            if (!found) rejected.Add(spawn.Profile.Species.ToString());
        }
        return $"Alien physical placement: {settings.Count - rejected.Count}/{settings.Count} species have a safe candidate. " +
            (rejected.Count == 0 ? "No ghosts instantiated; runtime/transport not exercised." : "Rejected: " + string.Join(", ", rejected));
    }

    private static bool Ready(object system, AlienEncounterSettings settings, object state, double now) =>
        (bool)Call(system, "CanSpawnEncounter", settings, state, now);
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static object Get(object target, string name) => Field(target, name).GetValue(target);
    private static object Call(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, PrivateInstance).Invoke(target, arguments);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
