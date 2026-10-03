using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MoonkovBallisticsChecks
{
    public static void ConfigureWeapons()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before editing weapon assets.");
        foreach (var path in new[] { "Assets/DollSinger/HaloWeapon.asset", "Assets/DollSinger/RevolverWeapon.asset" })
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            weapon.Type = WeaponType.Projectile;
            weapon.ProjectileGhostPrefab = new GhostSpawner.GhostReference();
            weapon.ProjectileGhostPrefab.SetAssetReference(new UnityEngine.AddressableAssets.AssetReferenceGameObject(
                AssetDatabase.AssetPathToGUID("Assets/Prefabs/ActorGhosts/Projectile.prefab")));
            weapon.ProjectileSpeed = path.Contains("Halo") ? 300f : 330f;
            weapon.ProjectileGravity = 1.62f;
            weapon.ProjectileRadius = .005f;
            weapon.ProjectileLifetime = 3f;
            weapon.ShowProjectileBody = true;
            weapon.HitscanRange = 1000f;
            EditorUtility.SetDirty(weapon);
        }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Moonkov/Checks/Ballistics")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying, "Run these isolated checks outside Play.");
        var position = Ballistics.Position(Vector3.zero, Vector3.forward * 300f, 1.62f, 1f);
        Require(Mathf.Abs(position.z - 300f) < .001f && Mathf.Abs(position.y + .81f) < .001f,
            "Bullet speed or lunar gravity is wrong.");
        var partial = Ballistics.Position(Vector3.zero, Vector3.forward * 300f, 1.62f, .4f);
        var continued = Ballistics.Position(partial, Ballistics.Velocity(Vector3.forward * 300f, 1.62f, .4f), 1.62f, .6f);
        Require(Vector3.Distance(position, continued) < .001f, "Trajectory depends on frame partitioning.");

        var scene = EditorSceneManager.NewPreviewScene();
        var holder = new GameObject("Ballistic sweep fixture");
        SceneManager.MoveGameObjectToScene(holder, scene);
        try
        {
            Vector3 start = new Vector3(20000, 20000, 20000);
            var shooter = new GameObject("Ignored shooter");shooter.transform.SetParent(holder.transform);
            var self = GameObject.CreatePrimitive(PrimitiveType.Cube);self.transform.SetParent(shooter.transform);
            self.transform.position = start + Vector3.forward * .15f;self.transform.localScale = Vector3.one * .1f;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(holder.transform);
            wall.transform.position = start + Vector3.forward;wall.transform.localScale = new Vector3(1, 1, .02f);
            Physics.SyncTransforms();
            var hits = new RaycastHit[2];
            Require(Ballistics.Sweep(scene.GetPhysicsScene(), start, start + Vector3.forward * 10, .005f, ~0,
                shooter.transform, ref hits, out var hit) && hit.collider.gameObject == wall,
                "Fast bullet skipped its first segment or the owner's collider hid the wall.");
            wall.transform.position = start + Vector3.forward * 15;Physics.SyncTransforms();
            Require(!Ballistics.Sweep(scene.GetPhysicsScene(), start, start + Vector3.forward * 10, .005f, ~0,
                shooter.transform, ref hits, out _), "Bullet hit ahead of its actual flight.");
        }
        finally { UnityEngine.Object.DestroyImmediate(holder);EditorSceneManager.ClosePreviewScene(scene);Physics.SyncTransforms(); }

        foreach (var path in new[] { "Assets/DollSinger/HaloWeapon.asset", "Assets/DollSinger/RevolverWeapon.asset" })
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            Require(weapon.Type == WeaponType.Projectile && weapon.ProjectileGhostPrefab.GhostGuid.IsValid &&
                weapon.ProjectileGravity > 0 && weapon.ShowProjectileBody && weapon.ProjectileRadius < .01f,
                "Gameplay weapon still uses hitscan or has an invalid ballistic configuration.");
        }
        Debug.Log("Ballistics checks passed: flight/drop, frame partitioning, swept near-wall collision, owner filtering, no look-ahead hit and weapon configuration.");
    }

    public static void CheckPredictedLifetime()
    {
        Require(!EditorApplication.isPlaying, "Run outside Play.");
        var instance = new GameObject("Predicted lifetime fixture").AddComponent<GhostGameObject>();
        try
        {
            using var world = new World("Predicted lifetime checks");
            var lifetime = world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
            var manager = world.EntityManager;
            var entity = manager.CreateEntity(typeof(GhostGameObjectGuid), typeof(GhostGameObjectPrefabReference));
            var guid = new Unity.Entities.Hash128("a01234567890abcdef01234567890abcd");
            manager.SetComponentData(entity, new GhostGameObjectGuid { Guid = guid });
            manager.SetComponentData(entity, new GhostGameObjectPrefabReference { PrefabGuid = guid, PrefabRootGuid = guid });
            lifetime.AdoptPredictedGhost(entity, instance);
            Require(instance.Guid == guid && lifetime.TryGetGhostGameObjectByGuid(guid, out var found) && found == instance &&
                lifetime.GhostEntityList.Length == 1 && lifetime.GhostEntityList[0] == entity,
                "Reconciled projectile is not tracked by ghost lifetime cleanup.");
            lifetime.OnGhostGameObjectDestroyed(guid);
            Require(!lifetime.TryGetGhostGameObjectByGuid(guid, out _), "Destroyed predicted projectile remained in the ghost registry.");
        }
        finally { UnityEngine.Object.DestroyImmediate(instance.gameObject); }
        Debug.Log("Predicted projectile lifetime check passed: authoritative identity, registry, entity list and registry removal.");
    }

    private static void Require(bool result, string message)
    { if (!result) throw new InvalidOperationException(message); }
}
