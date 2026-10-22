using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class TacticalItemClientStateChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Moonkov/Check Tactical Item Client State")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run outside Play mode.");
        using (var world = new World("Tactical managed-state regression"))
        {
            world.GetOrCreateSystemManaged<TacticalItemsClientSystem>();
            using var query = world.EntityManager.CreateEntityQuery(typeof(TacticalItemClientState));
            var owner = world.EntityManager.GetComponentObject<TacticalItemClientState>(query.GetSingletonEntity());
            Require(owner != null && owner.Apply(new TacticalItemOwnerRpc { RaidId = 1, RequestId = 2 }), "Tactical owner state was never constructed.");
            Require(!owner.Apply(new TacticalItemOwnerRpc { RaidId = 1, RequestId = 1 }), "Older owner RPC replaced the accepted snapshot.");
        }
        Debug.Log("Tactical managed state construction and RPC ordering passed.");
    }
}
