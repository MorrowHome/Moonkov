using System;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using BodyPart = Unity.MP_FPS.BodyPart;

/// <summary>Bounded, scene-independent checks of PMC decisions and action lifecycles.
/// These exercise production methods without spawning raid actors, baking navigation,
/// opening a connection, touching a profile, or claiming gameplay acceptance.</summary>
public static class DollSingerHumanlikeChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Moonkov/AI/Check Humanlike Decisions")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run PMC decision checks in Edit Mode.");
        var holder = new GameObject("Temporary PMC decision checks") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false); // Do not register a second active raid map.
        var tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
        tuning.name = "Temporary PMC check tuning";
        try
        {
            var map = holder.AddComponent<MoonRaidMap>();
            using var world = new World("PMC decision checks", WorldFlags.GameServer);
            var system = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            typeof(DollSingerEnemySystem).GetField("m_Tuning", PrivateInstance).SetValue(system, tuning);
            CheckCoverLifecycle(system);
            CheckMedicalLifecycle(system);
            CheckDecisions(system, map, tuning);
        }
        finally
        {
            Object.DestroyImmediate(holder);
            Object.DestroyImmediate(tuning);
        }
        Debug.Log("PMC checks passed: cover arrival/peek/interrupt, timed medical consumption/interrupt, danger interrupts commitment, stale-contact extraction and bounded angle holding. Gameplay and network acceptance remain separate.");
    }

    private static DollSingerEnemyBrain Brain() => new DollSingerEnemyBrain
    {
        Inventory = InventoryGraph.Create(stash: false, starter: false),
        Random = new Unity.Mathematics.Random(137),
        Seed = 1,
        Caution = .5f,
        Aggression = .4f,
        ActionStarted = 10,
        TacticalGoal = Vector3.zero
    };

    private static PredictedPlayerGhost Health()
    {
        var health = default(PredictedPlayerGhost);
        RaidHealth.Initialize(ref health);
        health.CurrentAmmo = 15;
        health.PrimaryAmmo = 15;
        health.EquippedWeaponID = DollSingerWeapons.Halo;
        health.EquippedWeaponSlot = 1;
        return health;
    }

    private static void CheckCoverLifecycle(DollSingerEnemySystem system)
    {
        var brain = Brain();
        brain.Action = PmcAction.Cover;
        brain.HasCover = true;
        brain.CoverPosition = Vector3.zero;
        brain.PeekPosition = Vector3.right * 2;
        brain.CoverPhase = PmcCoverPhase.Approaching;
        brain.CoverPhaseUntil = 30;
        Cover(system, brain, Vector3.left * 5, 10);
        Require(brain.HasCover && brain.CoverPhase == PmcCoverPhase.Approaching && !brain.IsPeeking &&
            brain.TacticalGoal == brain.CoverPosition, "A travelling PMC started peeking before reaching cover.");

        Cover(system, brain, brain.CoverPosition, 10.1);
        double ready = brain.CoverPhaseUntil;
        Require(brain.CoverPhase == PmcCoverPhase.Settling && ready > 10.1 && !brain.IsPeeking,
            "Cover arrival did not start a concealed settling interval.");
        Cover(system, brain, brain.CoverPosition, ready - .01);
        Require(brain.CoverPhase == PmcCoverPhase.Settling && !brain.IsPeeking,
            "The PMC exposed itself before finishing the settling interval.");
        Cover(system, brain, brain.CoverPosition, ready + .01);
        Require(brain.CoverPhase == PmcCoverPhase.Advancing && brain.TacticalGoal == brain.PeekPosition,
            "The PMC did not travel to its peek position after settling.");
        Cover(system, brain, brain.PeekPosition + Vector3.right * .72f, ready + .1);
        Require(brain.CoverPhase == PmcCoverPhase.Holding && brain.IsPeeking,
            "Peek arrival did not start the bounded holding phase.");

        brain.LastDamage = ready + .15;
        brain.BurstUntil = ready + 10;
        Cover(system, brain, brain.PeekPosition, ready + .2);
        Require(brain.CoverPhase == PmcCoverPhase.Returning && !brain.IsPeeking &&
            brain.TacticalGoal == brain.CoverPosition && brain.BurstUntil <= ready + .2,
            "Taking damage did not stop the burst and return the PMC to cover.");
    }

    private static void CheckMedicalLifecycle(DollSingerEnemySystem system)
    {
        var brain = Brain();
        brain.Action = PmcAction.Heal;
        brain.TacticalGoal = Vector3.right * .72f; // Must accept the motor's stopping radius.
        Require(brain.Inventory.AddSupply("medkit", 2, InventoryCatalog.Pockets) == InventoryError.None,
            "Could not prepare accessible medical supplies.");
        var health = Health();
        RaidHealth.Damage(ref health, BodyPart.Chest, 50, 1);
        float injured = health.CurrentHealth;
        int version = brain.Inventory.Version;
        Heal(system, brain, ref health, 10);
        double completedAt = brain.HealUntil;
        Require(completedAt > 10 && health.CurrentHealth == injured && brain.Inventory.Count("medkit") == 2 &&
            brain.Inventory.Version == version, "Starting treatment changed health or consumed medicine immediately.");
        Heal(system, brain, ref health, completedAt - .01);
        Require(health.CurrentHealth == injured && brain.Inventory.Count("medkit") == 2,
            "Treatment completed before its action timer.");
        Heal(system, brain, ref health, completedAt);
        Heal(system, brain, ref health, completedAt);
        Require(Mathf.Approximately(health.CurrentHealth, injured + 40) && brain.Inventory.Count("medkit") == 1 &&
            brain.Inventory.Version == version + 1 && brain.HealUntil == 0 && brain.NextHeal > completedAt,
            "Treatment failed to heal once and atomically consume one carried injector.");

        brain = Brain();
        brain.Action = PmcAction.Heal;
        Require(brain.Inventory.AddSupply("medkit", 2, InventoryCatalog.Pockets) == InventoryError.None,
            "Could not prepare interrupted-treatment supplies.");
        health = Health();
        RaidHealth.Damage(ref health, BodyPart.Chest, 50, 1);
        injured = health.CurrentHealth;
        version = brain.Inventory.Version;
        Heal(system, brain, ref health, 10);
        brain.LastDamage = 10.2;
        Heal(system, brain, ref health, 10.2);
        Require(brain.HealUntil == 0 && brain.HealItemId == null && health.CurrentHealth == injured &&
            brain.Inventory.Count("medkit") == 2 && brain.Inventory.Version == version,
            "A damaged PMC completed treatment or consumed medicine instead of interrupting it.");
    }

    private static void CheckDecisions(DollSingerEnemySystem system, MoonRaidMap map, DollSingerAITuning tuning)
    {
        var brain = Brain();
        var health = Health();
        brain.Action = PmcAction.Scavenge;
        brain.CommitUntil = 40;
        brain.Target = new Entity { Index = 1, Version = 1 };
        brain.LastSeen = Vector3.forward * 5;
        brain.LastSeenTime = brain.VisibleSince = 10;
        brain.Visible = true;
        brain.Confidence = 1;
        brain.Contacts.Add(brain.Target, new PmcContact { Confirmed = true, Visible = true, Confidence = 1 });
        var decision = Decide(system, brain, health, map, 10);
        Require(decision.action == PmcAction.Engage && decision.emergency && brain.CommitUntil > 10,
            "A newly confirmed close opponent failed to interrupt a looting commitment.");

        brain.Action = PmcAction.Extract;
        brain.Leaving = true;
        brain.Visible = false;
        brain.LastSeenTime = brain.LastHeardTime = 3;
        brain.Confidence = .6f;
        decision = Decide(system, brain, health, map, 10);
        Require(decision.action == PmcAction.Extract,
            "A departing PMC pursued a stale remembered contact instead of extracting.");

        brain.Leaving = false;
        brain.Action = PmcAction.Engage;
        brain.LastSeenTime = 10;
        decision = Decide(system, brain, health, map, 10.1);
        Require(decision.action == PmcAction.HoldAngle, "Losing sight skipped the short last-exit hold.");
        brain.Action = PmcAction.HoldAngle;
        decision = Decide(system, brain, health, map, 10 + tuning.HoldAngleSeconds + .1);
        Require(decision.action == PmcAction.Search && decision.emergency,
            "An expired angle hold prevented deliberate search from starting.");
    }

    private static void Cover(DollSingerEnemySystem system, DollSingerEnemyBrain brain, Vector3 position, double now)
        => Call(system, "AdvanceCover", new object[] { brain, position, now });

    private static void Heal(DollSingerEnemySystem system, DollSingerEnemyBrain brain, ref PredictedPlayerGhost health, double now)
    {
        var args = new object[] { brain, health, Vector3.zero, now };
        Call(system, "AdvanceHealing", args);
        health = (PredictedPlayerGhost)args[1];
    }

    private static (PmcAction action, bool emergency) Decide(DollSingerEnemySystem system, DollSingerEnemyBrain brain,
        PredictedPlayerGhost health, MoonRaidMap map, double now)
    {
        var args = new object[] { brain, Vector3.zero, health, map, now, PmcAction.Scavenge, 0f, null, false };
        Call(system, "EvaluateDecision", args);
        return ((PmcAction)args[5], (bool)args[8]);
    }

    private static void Call(DollSingerEnemySystem system, string name, object[] args)
    {
        var method = typeof(DollSingerEnemySystem).GetMethod(name, PrivateInstance);
        Require(method != null, "Missing production action entry: " + name);
        try { method.Invoke(system, args); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { throw new InvalidOperationException("PMC action check failed in " + name, error.InnerException); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("PMC check: " + message); }
}
