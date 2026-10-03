using System;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEditor;

public static class MoonkovGhostLifetimeChecks
{
    [MenuItem("Tools/Moonkov/Check Ghost Lifetime")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || ClientServerBootstrap.ServerWorld != null)
            throw new InvalidOperationException("Run in Edit mode with no server world.");

        var holder = new GameObject("Ghost lifetime check");
        using var world = new World("Ghost lifetime check", WorldFlags.GameServer);
        if (!ClientServerBootstrap.ServerWorlds.Contains(world))
            ClientServerBootstrap.ServerWorlds.Insert(0, world);
        try
        {
            var manager = world.EntityManager;
            var lifetime = world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
            var retrieve = world.GetOrCreateSystemManaged<ServerGhostTransformRetrieveSystem>();
            var ghosts = new GhostGameObject[3];
            var entities = new Entity[3];
            for (int i = 0; i < ghosts.Length; i++)
            {
                var root = new GameObject("Deferred ghost " + i);
                root.transform.SetParent(holder.transform, false);
                root.transform.position = new Vector3(i * 10, 0, 0);
                ghosts[i] = root.AddComponent<GhostGameObject>();
                Require(!ghosts[i].GhostEntityExists(), "An unlinked ghost reports a live entity.");
                entities[i] = manager.CreateEntity(typeof(GhostGameObjectGuid), typeof(GhostGameObjectPrefabReference));
                var guid = GhostGameObject.GenerateRandomHash();
                manager.SetComponentData(entities[i], new GhostGameObjectGuid { Guid = guid });
                manager.SetComponentData(entities[i], new GhostGameObjectPrefabReference
                    { PrefabGuid = guid, PrefabRootGuid = guid });
                lifetime.AdoptPredictedGhost(entities[i], ghosts[i]);
            }

            // Start a real Burst transform job, then remove a middle entry before
            // the next capture. The old implementation scheduled 3 transforms into 2 slots.
            retrieve.Update();
            lifetime.OnGhostGameObjectDestroyed(ghosts[1].Guid);
            manager.DestroyEntity(entities[1]);
            lifetime.PostUpdateRemoveStaleGhostGameObjectsFromList();
            Require(lifetime.GhostGameObjectList.Count == 2 && lifetime.GhostEntityList.Length == 2,
                "Compaction discarded deferred objects or kept the removed ghost.");
            Require(lifetime.GhostEntityList[0] == entities[0] && lifetime.GhostEntityList[1] == entities[2],
                "Deferred ghosts lost their stored entity identity.");
            retrieve.Update();
            var poses = retrieve.GhostTransformsArray;
            Require(poses.Length == 2 && lifetime.GhostGameObjectTransformAccessArray.length == 2,
                "Transform job and destination lengths disagree after compaction.");
            for (int i = 0; i < poses.Length; i++)
            {
                var entity = lifetime.GhostEntityList[i];
                Require(manager.GetComponentData<GhostGameObjectGuid>(entity).LocalGhostIndex == i &&
                    Vector3.Distance(poses[i].Position, lifetime.GhostGameObjectList[i].transform.position) < .001f,
                    "Captured transform belongs to a different ghost index.");
            }

            // Empty the registry and exercise the same scheduling path at zero length.
            lifetime.OnGhostGameObjectDestroyed(ghosts[0].Guid);
            lifetime.OnGhostGameObjectDestroyed(ghosts[2].Guid);
            retrieve.Update();
            Require(retrieve.GhostTransformsArray.Length == 0 &&
                lifetime.GhostGameObjectTransformAccessArray.length == 0 && lifetime.GhostEntityList.Length == 0,
                "The last despawn left stale transform slots.");
        }
        finally
        {
            world.GetExistingSystemManaged<ServerGhostTransformRetrieveSystem>()?.CompleteTransformRead();
            ClientServerBootstrap.ServerWorlds.Remove(world);
            UnityEngine.Object.DestroyImmediate(holder);
        }
        Debug.Log("Ghost lifetime checks passed: unlinked objects, deferred identity, " +
            "despawn during transform capture, compacted indices and empty registry.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
