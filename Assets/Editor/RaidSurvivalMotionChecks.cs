using System;
using System.Reflection;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

public static class RaidSurvivalMotionChecks
{
    private static int s_Checks;
    private static void Check(bool condition, string message)
    { s_Checks++; if (!condition) throw new InvalidOperationException(message); }
    private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < .001f;

    private static PredictedPlayerGhost Player()
    {
        var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state);
        state.ProtectionInitialized = true; state.HeadDomeEquipped = true;
        state.ControllerState.MovementSpeed = 5; state.ControllerState.Sprinting = true;
        return state;
    }

    private static void Tick(ref PredictedPlayerGhost state, bool? moving = null)
    {
        RaidOxygen.Tick(ref state, default, new OxygenParameters(100, 0, 3, 3, 1.1f, 1.75f),
            1, 1, 0, true, moving);
        RaidNutrition.Tick(ref state, 1, 100, 100, 100, 10, 1, 1, 1, moving);
    }

    public static string Run()
    {
        s_Checks = 0;
        var motion = new RaidSurvivalMotion();
        Check(!motion.Observe(new float3(10, 0, 10), .1f), "first position establishes the movement baseline");
        Check(!motion.Observe(new float3(10, 0, 10), .1f), "pressing against a wall is not movement");
        Check(motion.Observe(new float3(10.2f, 0, 10), .1f), "horizontal walking is movement");
        Check(!motion.Observe(new float3(10.2f, 2, 10), .1f), "vertical movement alone is not horizontal exertion");
        Check(motion.Observe(new float3(10.1f, 2, 9.9f), .1f), "backwards and diagonal movement counts");
        Check(!motion.Observe(new float3(10.101f, 2, 9.9f), .1f), "small position jitter is ignored");
        Check(!motion.Observe(new float3(20, 2, 9.9f), .1f), "a teleport is not a sprint");
        Check(motion.Observe(new float3(20.1f, 2, 9.9f), .1f), "real movement resumes after a teleport");
        Check(!motion.Observe(new float3(float.NaN, 0, 0), .1f), "invalid position cannot count as movement");
        Check(!motion.Observe(new float3(20.2f, 2, 9.9f), .1f), "invalid position clears the movement baseline");
        Check(!motion.Observe(new float3(20.2f, 2, 9.9f), 0), "invalid time cannot count as movement");
        Check(!motion.Observe(new float3(20.3f, 2, 9.9f), .1f), "invalid time also clears the baseline");

        var blocked = Player(); Tick(ref blocked, false);
        Check(Near(blocked.Oxygen, 99) && Near(blocked.Energy, 99) && Near(blocked.Hydration, 99),
            "sprint input against a wall uses resting oxygen and nutrition consumption");
        var sprint = Player(); Tick(ref sprint, true);
        Check(Near(sprint.Oxygen, 98.25f) && Near(sprint.Energy, 98.25f) && Near(sprint.Hydration, 98.6f),
            "actual sprinting applies the intended three resource multipliers");
        var walking = Player(); walking.ControllerState.Sprinting = false; Tick(ref walking, true);
        Check(Near(walking.Oxygen, 98.9f) && Near(walking.Energy, 98.9f) && Near(walking.Hydration, 98.9f),
            "actual walking applies walking consumption");
        var legacy = Player(); Tick(ref legacy);
        Check(Near(legacy.Oxygen, 98.25f) && Near(legacy.Energy, 98.25f) && Near(legacy.Hydration, 98.6f),
            "callers without an observed position retain the existing rule");
        return "PASS " + s_Checks + " actual movement survival checks";
    }

    private delegate void SurvivalHandler(ref ServerGameSystem system, ref SystemState state);
    private static unsafe void UpdateServer(World world, SystemHandle handle, double time)
    {
        world.SetTime(new TimeData(time, 1));
        ref var system = ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
        ref var state = ref world.Unmanaged.ResolveSystemStateRef(handle);
        ((SurvivalHandler)typeof(ServerGameSystem).GetMethod("UpdateSurvival", BindingFlags.Instance | BindingFlags.NonPublic)
            .CreateDelegate(typeof(SurvivalHandler)))(ref system, ref state);
    }

    [MenuItem("Tools/Moonkov/Check Survival Actual Movement")]
    public static void RunServerCheck()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || MoonRaidMap.Active != null)
            throw new InvalidOperationException("Run in idle Edit Mode without an active raid map.");
        string rules = Run();
        var go = new GameObject("Survival movement fixture") { hideFlags = HideFlags.HideAndDontSave };
        go.SetActive(false);
        try
        {
            var map = go.AddComponent<MoonRaidMap>();
            var settings = new SerializedObject(map);
            settings.FindProperty("m_OutdoorOxygenSeconds").floatValue = 100;
            settings.FindProperty("m_EnergyReserveSeconds").floatValue = 100;
            settings.FindProperty("m_HydrationReserveSeconds").floatValue = 100;
            settings.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(true);
            using var world = new World("Survival movement server check", WorldFlags.GameServer);
            var em = world.EntityManager;
            var handle = world.GetOrCreateSystem<ServerGameSystem>();
            em.SetComponentData(em.CreateEntity(typeof(NetworkTime)), new NetworkTime { ServerTick = new NetworkTick(10), ServerTickFraction = 1 });
            var player = em.CreateEntity(typeof(PredictedPlayerGhost), typeof(LocalTransform));
            em.SetComponentData(player, Player());
            var origin = new float3(20000, 0, 20000);
            em.SetComponentData(player, LocalTransform.FromPosition(origin));
            var connection = em.CreateEntity(typeof(RaidSession), typeof(JoinedClient));
            em.SetComponentData(connection, new RaidSession { RaidId = 7, Phase = RaidPhase.Active });
            em.SetComponentData(connection, new JoinedClient { PlayerEntity = player });
            var graph = InventoryGraph.Create(false);
            graph.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "helmet", Parent = "equipment", Region = "Helmet" });
            em.AddComponentObject(connection, new RaidInventoryState { Graph = graph, RaidId = 7 });

            UpdateServer(world, handle, 1);
            UpdateServer(world, handle, 2);
            var blocked = em.GetComponentData<PredictedPlayerGhost>(player);
            Check(Near(blocked.Oxygen, 98) && Near(blocked.Energy, 98) && Near(blocked.Hydration, 98),
                "server treats sprinting against a wall as resting");
            em.SetComponentData(player, LocalTransform.FromPosition(origin + new float3(1, 0, 0)));
            UpdateServer(world, handle, 3);
            var running = em.GetComponentData<PredictedPlayerGhost>(player);
            Check(Near(running.Oxygen, 96.25f) && Near(running.Energy, 96.25f) && Near(running.Hydration, 96.6f),
                "server applies sprint consumption after actual movement");
            running.ControllerState.MovementSpeed = 0;
            em.SetComponentData(player, running);
            em.SetComponentData(player, LocalTransform.FromPosition(origin + new float3(100, 0, 0)));
            UpdateServer(world, handle, 4);
            running = em.GetComponentData<PredictedPlayerGhost>(player);
            running.ControllerState.MovementSpeed = 5;
            em.SetComponentData(player, running);
            em.SetComponentData(player, LocalTransform.FromPosition(origin + new float3(101, 0, 0)));
            UpdateServer(world, handle, 5);
            var resumed = em.GetComponentData<PredictedPlayerGhost>(player);
            Check(Near(resumed.Energy, 93.5f) && Near(resumed.Hydration, 94.2f),
                "server updates the position baseline even while the player is idle");
            Debug.Log(rules + "; server integration checks passed.");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
