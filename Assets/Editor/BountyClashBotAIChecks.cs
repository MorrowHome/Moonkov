using System;
using System.Collections;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Bounded checks of production policies. No raid, profile, transport or NavMesh bake.
public static class BountyClashBotAIChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int s_Passed;
    [MenuItem("Tools/Moonkov/Bounty Clash/Check bot decisions and squad memory")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use idle EditMode.");
        s_Passed = 0;
        var holder = new GameObject("Clash AI checks / isolated") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false);
        var tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
        tuning.name = "Temporary Clash AI check tuning";
        try
        {
            using var world = new World("Clash AI policy checks", WorldFlags.GameServer);
            var system = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            Field(system, "m_Tuning", tuning);
            var em = world.EntityManager;
            var self = Actor(em, 1, 1, 1);
            var friendly = Actor(em, 2, 1, 1);
            var enemy = Actor(em, 3, 2, 1);
            var oldEpoch = Actor(em, 4, 2, 2);
            var downed = Actor(em, 5, 2, 1, ClashLife.Downed);
            var source = Brain(1, 1, 0);
            Check((bool)Call(system, "CanObserveOpponent", source, enemy), "current hostile cannot be perceived");
            Check(!(bool)Call(system, "CanObserveOpponent", source, self), "self selected as an opponent");
            Check(!(bool)Call(system, "CanObserveOpponent", source, friendly), "friendly selected as an opponent");
            Check(!(bool)Call(system, "CanObserveOpponent", source, oldEpoch), "old epoch contact retained");
            Check(!(bool)Call(system, "CanObserveOpponent", source, downed), "downed actor treated as an active threat");

            var receiver = Brain(2, 1, 1);
            var unrelated = Brain(6, 3, 0);
            source.Contacts.Add(enemy, new PmcContact { Position = new Vector3(10, 0, 4), LastSeen = 10,
                Confidence = 1, Confirmed = true, Visible = true, HadVisualSample = true, PositionUncertainty = 1 });
            var list = (IList)typeof(DollSingerEnemySystem).GetField("m_ClashAgents", Private).GetValue(system);
            AddAgent(list, self, source, Vector3.zero);
            AddAgent(list, friendly, receiver, Vector3.right * 4);
            AddAgent(list, Actor(em, 6, 3, 1), unrelated, Vector3.right * 5);
            Call(system, "ShareClashObservations", 10d);
            Check(receiver.Contacts.TryGetValue(enemy, out var report), "squad observation was not shared");
            Check(!report.Visible && !report.HadVisualSample && report.LastSeen < 0 && report.LastReported == 10,
                "radio report became a personal sighting / firing solution");
            Check(report.PositionUncertainty >= 3 && report.Confidence <= .55f &&
                Vector3.Distance(report.Position, source.Contacts[enemy].Position) > 1.9f, "radio report has no positional error");
            Check(!unrelated.Contacts.ContainsKey(enemy), "observation crossed team boundary");
            var reportedPosition = report.Position;
            em.AddComponentData(enemy, LocalTransform.FromPosition(new Vector3(1000, 0, 1000)));
            Call(system, "ShareClashObservations", 10.6d);
            Check(report.Position == reportedPosition && report.LastReported == 10, "unseen enemy motion changed a squad report");
            source.Contacts[enemy].LastReported = 20;
            Call(system, "ShareClashObservations", 20d);
            Check(report.LastReported == 10, "report of a report refreshed old evidence");
            Call(system, "Sense", receiver, holder.transform, Vector3.zero, 45f, 30d);
            Check(!receiver.Contacts.ContainsKey(enemy), "radio memory did not expire");

            var arena = new ClashArenaDefinition { Center = new ClashPoint(0, 0, 0), Radius = 130 };
            Field(system, "m_ClashArena", arena);
            Check((bool)Call(system, "SafeClashRoute", new Vector3[] { Vector3.left * 100, Vector3.right * 100 }, 2), "inside route rejected");
            Check(!(bool)Call(system, "SafeClashRoute", new Vector3[] { Vector3.zero, Vector3.right * 140, Vector3.zero }, 3), "route excursions outside circle accepted");
            Check((bool)Call(system, "SafeClashRoute", new Vector3[] { Vector3.right * 140, Vector3.right * 120 }, 2), "inward boundary recovery rejected");
            Check(!(bool)Call(system, "SafeClashRoute", new Vector3[] { Vector3.right * 140, Vector3.right * 150, Vector3.zero }, 3), "outward boundary recovery accepted");

            var mission = Brain(1, 1, 0);
            mission.Inventory = InventoryGraph.Create(stash: false, starter: false);
            mission.LootedCaches = 999;
            mission.Action = PmcAction.ClashObjective;
            var health = default(PredictedPlayerGhost);RaidHealth.Initialize(ref health);
            health.CurrentAmmo = health.PrimaryAmmo = 15;
            var decision = new object[] { mission, Vector3.zero, health, holder.AddComponent<MoonRaidMap>(), 10000d, PmcAction.Scavenge, 0f, null, false };
            CallArray(system, "EvaluateDecision", decision);
            Check(!mission.Leaving && (PmcAction)decision[5] == PmcAction.ClashObjective, "old cache/time quota replaced a Clash objective");

            var registry = new ClashBotRegistry();
            typeof(ClashBotRegistry).GetMethod("Reset", Private).Invoke(registry, new object[] { 1 });
            var request = new ClashBotSpawnRequest { ParticipantId = 1, TeamId = 1, Epoch = 1 };
            registry.PendingSpawns.Add(request);
            Check(registry.Register(request, self, em.CreateEntity(), mission.Inventory), "could not prepare bot registry fixture");
            var view = new ClashBotView { ParticipantId = 1, TeamId = 1, Epoch = 1, Phase = ClashPhase.Active,
                Life = ClashLife.Alive, Target = new Vector3(100000, 0, 100001.4f),
                Teammates = new[] { new ClashBotTeammate { ParticipantId = 2, Life = ClashLife.Downed, Position = new Vector3(100000, 0, 100001.4f) } } };
            ((IDictionary)typeof(ClashBotRegistry).GetField("Views", Private).GetValue(registry)).Add(1, view);
            Field(system, "m_ClashRegistry", registry);
            mission.Mission = ClashBotGoal.Rescue;mission.RescueTargetId = 2;
            var position = new Vector3(100000, 0, 100000);
            var eye = position + Vector3.up * 1.6f;
            Call(system, "AdvanceClashObjective", mission, position, eye, 10d);
            Check(mission.Interacting && LastIntent(registry).Intent == ClashIntent.ReviveBegin, "safe rescue did not begin / stop movement");
            view.ReviveTarget = 2;
            // A normal motor stop can leave the actor 2.2 m from the downed body.
            Call(system, "AdvanceClashObjective", mission, position - Vector3.forward * .8f, eye - Vector3.forward * .8f, 10.21d);
            Check(LastIntent(registry).Intent == ClashIntent.ReviveRenew && LastIntent(registry).Sequence == 2,
                "continuous rescue did not renew a monotonically sequenced lease");
            mission.Visible = true;
            Call(system, "AdvanceClashObjective", mission, position, eye, 10.42d);
            Check(!mission.Interacting && LastIntent(registry).Intent == ClashIntent.ReviveCancel,
                "renewed threat did not cancel rescue");
            uint sequence = mission.IntentSequence;
            view.Life = ClashLife.Downed;
            Call(system, "AdvanceClashObjective", mission, position, eye, 11d);
            Check(!mission.Interacting && mission.IntentSequence == sequence, "downed actor submitted an interaction");
            view.Life = ClashLife.Alive;view.ReviveTarget = -1;view.Bounty = ClashBountyState.Ground;view.BountyPosition = view.Target;
            mission.Mission = ClashBotGoal.GroundBounty;
            Call(system, "AdvanceClashObjective", mission, position, eye, 11.21d);
            Check(LastIntent(registry).Intent == ClashIntent.Pickup && !mission.Extracted,
                "ground bounty did not request authority or bypassed authoritative extraction");
            var capped = new PredictedPlayerGhost { ClashEnabled = true, ClashLife = ClashLife.Alive, ClashMaxHealthFraction = .75f };
            RaidHealth.Initialize(ref capped);
            Check(!(bool)typeof(DollSingerEnemySystem).GetMethod("NeedsHealing", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { capped }),
                "full post-revive health cap causes an endless Heal decision");
            RaidHealth.Damage(ref capped, Unity.MP_FPS.BodyPart.Chest, 10, 1);
            var treating = Brain(1, 1, 0);treating.Action = PmcAction.Heal;
            treating.Inventory = InventoryGraph.Create(stash: false, starter: false);
            treating.Inventory.AddSupply("medkit", 2, InventoryCatalog.Pockets);
            var treatment = new object[] { treating, capped, Vector3.zero, 100d };
            CallArray(system, "AdvanceHealing", treatment);
            Check(treating.HealUntil > 100, "an actual wound below revived cap was not treated");
            treatment = new object[] { treating, capped, Vector3.zero, treating.HealUntil };
            CallArray(system, "AdvanceHealing", treatment);capped = (PredictedPlayerGhost)treatment[1];
            Check(Mathf.Approximately(capped.CurrentHealth, capped.MaxHealth) && treating.Inventory.Count("medkit") == 1,
                "revived actor did not heal the treatable part once to its reduced cap");
            treatment = new object[] { treating, capped, Vector3.zero, 110d };CallArray(system, "AdvanceHealing", treatment);
            Check(treating.HealUntil == 0 && treating.Inventory.Count("medkit") == 1,
                "full reduced-cap actor resumed treatment and wasted medicine");
            Debug.Log("Clash bot AI checks passed: " + s_Passed + "; team/epoch filtering, uncertain expiring radio memory, safe circle routes, Clash objectives and interrupted rescue leases. Actual navigation and combat require the single-Editor raid check.");
        }
        finally { Object.DestroyImmediate(holder);Object.DestroyImmediate(tuning); }
    }
    private static DollSingerEnemyBrain Brain(int id, int team, int slot) => new DollSingerEnemyBrain {
        ClashParticipantId = id, ClashTeamId = team, ClashEpoch = 1, ClashSlot = slot, Seed = id,
        Random = new Unity.Mathematics.Random((uint)(id + 37)), Action = PmcAction.ClashObjective };
    private static Entity Actor(EntityManager em, int id, int team, int epoch, ClashLife life = ClashLife.Alive)
    {
        var actor = em.CreateEntity(typeof(ClashPlayer), typeof(PredictedPlayerGhost));
        em.SetComponentData(actor, new ClashPlayer { ParticipantId = id, TeamId = team, Epoch = epoch });
        em.SetComponentData(actor, new PredictedPlayerGhost { CurrentHealth = 100, ClashLife = life });return actor;
    }
    private static void AddAgent(IList list, Entity entity, DollSingerEnemyBrain brain, Vector3 position)
    {
        var type = typeof(DollSingerEnemySystem).GetNestedType("ClashAgent", BindingFlags.NonPublic);
        var agent = Activator.CreateInstance(type);
        type.GetField("Entity").SetValue(agent, entity);type.GetField("Brain").SetValue(agent, brain);type.GetField("Position").SetValue(agent, position);
        type.GetField("View").SetValue(agent, new ClashBotView { ParticipantId = brain.ClashParticipantId, TeamId = brain.ClashTeamId, Epoch = 1, Life = ClashLife.Alive });
        list.Add(agent);
    }
    private static ClashBotIntent LastIntent(ClashBotRegistry registry)
    {
        var queue = typeof(ClashBotRegistry).GetField("Intents", Private).GetValue(registry);
        return ((ClashBotIntent[])queue.GetType().GetMethod("ToArray").Invoke(queue, null))[(int)queue.GetType().GetProperty("Count").GetValue(queue) - 1];
    }
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) => CallArray(target, name, args);
    private static object CallArray(object target, string name, object[] args)
    {
        try { return target.GetType().GetMethod(name, Private).Invoke(target, args); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Clash bot AI: " + message);s_Passed++; }
}
