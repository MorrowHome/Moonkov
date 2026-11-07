using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BleedingVoiceReview
{
    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use an idle Editor");
        int checks = 0; var failures = new List<string>();
        void Check(bool value, string label) { checks++; if (!value) failures.Add(label); }
        var scene = EditorSceneManager.NewPreviewScene(); var previousMap = MoonRaidMap.Active;
        using var world = new World("Bleeding voice authority, no transport", WorldFlags.GameServer);
        var em = world.EntityManager;
        try
        {
            var mapRoot = new GameObject("Voice fixture map"); mapRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(mapRoot, scene);
            var map = mapRoot.AddComponent<MoonRaidMap>(); typeof(MoonRaidMap).GetProperty("Active").SetValue(null, map);
            var root = new GameObject("Voice fixture player"); SceneManager.MoveGameObjectToScene(root, scene);
            var link = root.AddComponent<GhostGameObject>();
            var player = em.CreateEntity(typeof(PredictedPlayerGhost), typeof(GhostOwner));
            var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health); health.BreathableAir = true;
            em.SetComponentData(player, health); em.SetComponentData(player, new GhostOwner { NetworkId = 7 });
            em.AddComponentObject(player, new GhostGameObjectLink { LinkedInstance = link });
            var connection = em.CreateEntity(typeof(RaidSession), typeof(JoinedClient));
            em.SetComponentData(connection, new RaidSession { RaidId = 4, Phase = RaidPhase.Active });
            em.SetComponentData(connection, new JoinedClient { PlayerEntity = player });
            var inventory = new RaidInventoryState { Graph = InventoryGraph.Create(false), RaidId = 4 };
            em.AddComponentObject(connection, inventory);
            var server = world.GetOrCreateSystem<ServerGameSystem>();
            var clock = em.CreateEntity(typeof(NetworkTime)); double now = 0; uint tick = 1;
            void Step(float seconds = .1f)
            {
                now += seconds; world.SetTime(new TimeData(now, seconds));
                em.SetComponentData(clock, new NetworkTime { ServerTick = new NetworkTick(++tick) });
                ref var system = ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(server);
                ref var state = ref world.Unmanaged.ResolveSystemStateRef(server);
                using var ecb = new EntityCommandBuffer(Allocator.Temp);
                system.TacticalTestTick(ref state, ecb); ecb.Playback(em);
                using var sent = em.CreateEntityQuery(typeof(SendRpcCommandRequest)); em.DestroyEntity(sent);
            }
            Step(); using var query = em.CreateEntityQuery(typeof(TacticalServerWorld));
            var tactical = query.GetSingleton<TacticalServerWorld>();
            Check(!em.GetComponentData<PredictedPlayerGhost>(player).TacticalBleeding, "Healthy server state is not bleeding");
            tactical.Statuses[player] = new TacticalServerWorld.Status { Bleed = now + 8 };
            Step(); var sentHealth = em.GetComponentData<PredictedPlayerGhost>(player);
            Check(sentHealth.TacticalBleeding && sentHealth.CurrentHealth > sentHealth.MaxHealth * .9f,
                "Real high-health bleeding publishes the voice state");
            Check(MoonkovDistressAudio.PainfulStatus(sentHealth), "Published bleeding reaches distress classifier");

            var serializer = typeof(PredictedPlayerGhost).Assembly.GetType(
                "Assembly_CSharp_Generated.Assembly_CSharp_Generated_PredictedPlayerGhostGhostComponentSerializer", true);
            ref var systemState = ref world.Unmanaged.ResolveSystemStateRef(server);
            foreach (bool bleeding in new[] { true, false })
            {
                sentHealth.TacticalBleeding = bleeding;
                object[] to = { new GhostSerializerState { GhostFromEntity = systemState.GetComponentLookup<GhostInstance>(true) },
                    Activator.CreateInstance(serializer.GetNestedType("Snapshot", BindingFlags.NonPublic)), sentHealth };
                serializer.GetMethod("CopyToSnapshotGenerated", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, to);
                using var mapGhosts = new NativeParallelHashMap<SpawnedGhost, Entity>(1, Allocator.Temp);
                object[] from = { new GhostDeserializerState { GhostMap = mapGhosts.AsReadOnly(), SnapshotTick = new NetworkTick(10) },
                    new PredictedPlayerGhost(), 0f, 0f, to[1], to[1] };
                serializer.GetMethod("CopyFromSnapshotGenerated", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, from);
                Check(((PredictedPlayerGhost)from[1]).TacticalBleeding == bleeding, "Generated ghost round trip: " + bleeding);
            }
            var request = em.CreateEntity();
            em.AddComponentData(request, new RaidTacticalItemActionRpc { RaidId = 4, ExpectedVersion = inventory.Graph.Version,
                RequestId = 1, Action = TacticalItemAction.Extinguish });
            em.AddComponentData(request, new ReceiveRpcCommandRequest { SourceConnection = connection });
            Step(); Check(inventory.TacticalPending, "Real stop-bleeding request starts");
            Step(3.1f);
            health = em.GetComponentData<PredictedPlayerGhost>(player);
            Check(!inventory.TacticalPending && !health.TacticalBleeding && tactical.Statuses[player].Bleed == 0,
                "Completing stop-bleeding clears the published voice state");
            Check(!MoonkovDistressAudio.PainfulStatus(health), "Healed high-health character has no lingering groan");
            tactical.Statuses[player].Bleed = now + .5; Step(); Step(.6f);
            Check(!em.GetComponentData<PredictedPlayerGhost>(player).TacticalBleeding, "Natural expiration clears bleeding");
            tactical.Statuses[player].Bleed = now + 30; Step();
            health = em.GetComponentData<PredictedPlayerGhost>(player); health.CurrentHealth = 0; em.SetComponentData(player, health);
            Step(); Check(!em.GetComponentData<PredictedPlayerGhost>(player).TacticalBleeding, "Death clears bleeding");
            RaidHealth.Initialize(ref health); em.SetComponentData(player, health);
            tactical.Statuses[player].Bleed = now + 30; Step();
            ref var serverSystem = ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(server);
            using var cleanup = new EntityCommandBuffer(Allocator.Temp);
            serverSystem.TacticalTestClear(ref systemState, cleanup); cleanup.Playback(em);
            Check(!em.GetComponentData<PredictedPlayerGhost>(player).TacticalBleeding, "Raid cleanup clears bleeding");
        }
        finally
        {
            typeof(MoonRaidMap).GetProperty("Active").SetValue(null, previousMap);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        MoonkovDistressAudioChecks.Run();
        return new { passed = failures.Count == 0, checks, failures, scope = "Real isolated server, stop-bleeding, expiry, death and generated ghost serialization; no Play or transport." };
    }
}
