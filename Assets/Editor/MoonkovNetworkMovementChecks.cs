using System;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;

public static class MoonkovNetworkMovementChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Moonkov/Check Network Movement")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run in Edit mode.");
        if (PlayerGhostManager.ServerInstance != null || PlayerGhostManager.ClientInstance != null ||
            PlayerPredictionSystem.Instance != null)
            throw new InvalidOperationException("Run after the gameplay worlds have been disposed.");

        var scene = EditorSceneManager.NewPreviewScene();
        var holder = new GameObject("Network movement check");
        holder.SetActive(false);
        SceneManager.MoveGameObjectToScene(holder, scene);
        try
        {
            CreateManager(holder.transform, MultiplayerRole.Server);
            CreateManager(holder.transform, MultiplayerRole.ClientProxy);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab");
            var server = CreatePlayer(prefab, holder.transform, MultiplayerRole.Server);
            var proxy = CreatePlayer(prefab, holder.transform, MultiplayerRole.ClientProxy);
            holder.SetActive(true);
            CheckReplicaCollision(server, proxy);
            CheckReplicaCollision(proxy, server);
            CheckSkippedPrediction(server);
        }
        finally
        {
            // These fixture ghosts deliberately skip LinkGhost's callback registration.
            foreach (var manager in holder.GetComponentsInChildren<PlayerGhostManager>(true))
                manager.OnGhostPreDestroy();
            UnityEngine.Object.DestroyImmediate(holder);
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Physics.SyncTransforms();
        }
        Debug.Log("Network movement checks passed: reproduced replica push, both timelines isolated, " +
                  "head/body queries preserved, world collision preserved, skipped prediction left untouched.");
    }

    // Supply only the role and required cached controller to exercise real OnGhostLinked
    // without a connection, account, camera or inventory transaction.
    private static void SetRole(GhostMonoBehaviour behaviour, MultiplayerRole role)
    {
        var ghost = behaviour.GetComponent<GhostGameObject>();
        if (ghost == null) ghost = behaviour.gameObject.AddComponent<GhostGameObject>();
        typeof(GhostGameObject).GetField("m_Role", PrivateInstance).SetValue(ghost, role);
        behaviour.SetGhostGameObject(ghost);
    }

    private static void CreateManager(Transform parent, MultiplayerRole role)
    {
        var root = new GameObject("Fixture manager " + role);
        root.transform.SetParent(parent, false);
        var manager = root.AddComponent<PlayerGhostManager>();
        SetRole(manager, role);
        manager.OnGhostLinked();
    }

    private static PlayerGhost CreatePlayer(GameObject prefab, Transform parent, MultiplayerRole role)
    {
        var player = UnityEngine.Object.Instantiate(prefab, parent).GetComponent<PlayerGhost>();
        foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (var camera in player.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (var collider in player.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var controller = player.GetComponent<FirstPersonController>();
        typeof(FirstPersonController).GetField("m_Controller", PrivateInstance)
            .SetValue(controller, player.GetComponent<UnityEngine.CharacterController>());
        player.Awake();
        SetRole(player, role);
        typeof(PlayerGhost).GetField("m_SpawnSFX", PrivateInstance).SetValue(player, null);
        player.OnGhostLinked();
        return player;
    }

    private static void CheckReplicaCollision(PlayerGhost moving, PlayerGhost replica)
    {
        var origin = new Vector3(100, 100, 100);
        var controller = moving.Controller.CharacterController;
        var excluded = controller.excludeLayers;
        int opposite = 1 << replica.gameObject.layer;
        Require((excluded.value & opposite) != 0, "Movement collides with the other timeline.");
        var replicaBody = replica.transform.Find("Body hitbox").GetComponent<CapsuleCollider>();
        var ownBody = moving.transform.Find("Body hitbox").GetComponent<CapsuleCollider>();
        var ownHead = moving.transform.Find("Head hitbox").GetComponent<SphereCollider>();
        ownBody.enabled = ownHead.enabled = false;
        replicaBody.enabled = true;
        replica.Controller.CharacterController.enabled = false;
        controller.enabled = true;

        moving.transform.position = origin;
        replica.transform.position = origin + Vector3.right * .03f;
        controller.excludeLayers = excluded.value & ~opposite;
        UnityEngine.Physics.SyncTransforms();
        for (int i = 0; i < 30; i++) controller.Move(Vector3.down * .01f);
        Require(Mathf.Abs(moving.transform.position.x - origin.x) > .1f,
            "Fixture did not reproduce the solid replica pushing an idle player.");

        controller.excludeLayers = excluded;
        controller.enabled = false;
        moving.transform.position = origin;
        controller.enabled = true;
        UnityEngine.Physics.SyncTransforms();
        for (int i = 0; i < 30; i++) controller.Move(Vector3.down * .01f);
        Require(Mathf.Abs(moving.transform.position.x - origin.x) < .001f &&
                Mathf.Abs(moving.transform.position.z - origin.z) < .001f,
            "An idle player still drifts against its replica.");

        // Excluding physical contact must not remove the opponent from ballistic queries.
        var ray = new Ray(replica.transform.position + Vector3.up * .85f + Vector3.back * 2f, Vector3.forward);
        UnityEngine.Physics.SyncTransforms();
        Require(moving.RaycastShot(ray, 5, opposite, out var hit) && hit.collider == replicaBody,
            "Replica body is no longer queryable for shots.");

        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.SetParent(moving.transform.parent, false);
        wall.transform.position = moving.transform.position + Vector3.right + Vector3.up * .85f;
        wall.transform.localScale = new Vector3(.1f, 3, 3);
        UnityEngine.Physics.SyncTransforms();
        var before = moving.transform.position;
        controller.Move(Vector3.right * 2f);
        Require(moving.transform.position.x - before.x < 1f, "World walls no longer block movement.");
        UnityEngine.Object.DestroyImmediate(wall);
        ownBody.enabled = ownHead.enabled = true;
    }

    private static void CheckSkippedPrediction(PlayerGhost player)
    {
        using var world = new World("Skipped player prediction check", WorldFlags.GameClient);
        world.SetTime(new Unity.Core.TimeData(1, 1f / 60f));
        var manager = world.EntityManager;
        manager.SetComponentData(manager.CreateEntity(typeof(NetworkTime)), new NetworkTime
            { ServerTick = new NetworkTick(10), ServerTickFraction = 1f });
        var entity = manager.CreateEntity(typeof(PredictedPlayerGhost), typeof(LocalTransform),
            typeof(PredictedPlayerControllerConsts), typeof(Simulate));
        manager.AddBuffer<PredictedPlayerGhostState>(entity);
        manager.AddComponentObject(entity, new PlayerControllerLink { Controller = player.Controller });
        var state = new FirstPersonController.ControllerState();
        state.Init(player.transform.position, quaternion.identity);
        var predicted = new PredictedPlayerGhost
            { ControllerState = state, AccumulatedMovement = new float3(1, 0, 0), RequestApplyMovement = true };
        manager.SetComponentData(entity, predicted);
        manager.SetComponentEnabled<Simulate>(entity, false);
        var before = player.transform.position;
        world.GetOrCreateSystemManaged<PlayerPredictionSystem>().Update();
        Require(player.transform.position == before &&
                math.all(manager.GetComponentData<PredictedPlayerGhost>(entity).AccumulatedMovement == predicted.AccumulatedMovement),
            "Prediction applied movement to a player whose Simulate tag is disabled.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
