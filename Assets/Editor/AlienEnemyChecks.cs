using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using BodyPart = Unity.MP_FPS.BodyPart;

/// <summary>Focused integration fixtures against the loaded formal MC00 colliders.
/// Uses isolated ECS worlds and real Ghost links / projectile damage code. It does
/// not authenticate accounts, alter saves, or claim a transport/late-join test.</summary>
public static partial class AlienEnemyChecks
{
    [Serializable] private sealed class Result
    {
        public string species;
        public int hitboxes, particles, nonFinite;
        public bool humanDamaged, pmcDamaged, alienIgnored, projectileHit, diedAndDespawned, scaleReplicated;
        public float smallestRadius, largestRadius;
    }
    [Serializable] private sealed class Report
    { public string scene, capturedAt; public List<Result> cases = new List<Result>(); public List<string> errors = new List<string>(); }
    [MenuItem("Tools/Moonkov/Aliens/Check enemy integration and Scale")]
    public static void Run() { RunFrom(0); }
    public static void RunFrom(int first)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run the bounded fixtures in Edit Mode.");
        var settings = Object.FindAnyObjectByType<AlienEncounterSettings>();
        if (!settings) throw new InvalidOperationException("Open the formal map with alien encounters first.");
        var report = new Report { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, capturedAt = DateTime.UtcNow.ToString("O") };
        var originalScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var fixtureScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var holder = new GameObject("Temporary alien integration fixtures");
        var logs = new Application.LogCallback((message, stack, type) => { if ((type == LogType.Error || type == LogType.Exception) && report.errors.Count < 8) report.errors.Add(message); });
        Application.logMessageReceived += logs;
        using var server = new World("Alien authority fixtures", WorldFlags.GameServer);
        using var client = new World("Alien cosmetic fixtures", WorldFlags.GameClient);
        server.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        client.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var manager = server.EntityManager;
        var tick = manager.CreateEntity(typeof(NetworkTime));
        manager.SetComponentData(tick, new NetworkTime { ServerTick = new NetworkTick(1) });
        try
        {
            for (int i = first; i < settings.Count; i++)
            {
                var profile = Object.Instantiate(settings[i].Profile);
                profile.SightRange = 12; profile.Damage = 10; profile.AttackCooldown = .55f;
                profile.Windup = .15f; profile.Recovery = .15f; profile.AttackReach = 3; profile.PatrolRadius = 0;
                // Compare target classes on the same known-clear floor. Authored
                // encounter placement is checked separately by setup/spawn queries.
                Vector3 point = settings[0].Position;
                if (!Physics.Raycast(point + Vector3.up * 2, Vector3.down, out var floor, 6, LayerMask.GetMask("Default", "Ground")))
                    throw new InvalidOperationException("No authored spawn floor for " + profile.Species);
                Vector3 start = floor.point + floor.normal * SurfaceMotor.Height * profile.Scale;
                var result = new Result { species = profile.Species.ToString() }; report.cases.Add(result);
                var actor = Alien(holder.transform, server, profile, start, MultiplayerRole.Server, -11000 - i);
                var human = Humanoid(holder.transform, server, floor.point + Vector3.forward * .75f, false, 42);
                var pmc = Humanoid(holder.transform, server, floor.point + Vector3.right * .8f, true, -1000);
                SetHealth(manager, pmc.LinkedEntity, 0);
                Require(ProceduralAlienEnemy.IsLivingHumanoid(manager, human.LinkedEntity), "Human target rejected.");
                Step(actor, 40);
                result.humanDamaged = manager.GetComponentData<PredictedPlayerGhost>(human.LinkedEntity).CurrentHealth < 100;
                SetHealth(manager, human.LinkedEntity, 0); SetHealth(manager, pmc.LinkedEntity, 100);
                Step(actor, 45);
                result.pmcDamaged = manager.GetComponentData<PredictedPlayerGhost>(pmc.LinkedEntity).CurrentHealth < 100;
                result.alienIgnored = !ProceduralAlienEnemy.IsLivingHumanoid(manager, actor.GhostGameObject.LinkedEntity);
                result.hitboxes = actor.Body.HitboxCount;
                result.projectileHit = ProjectileDamage(server, holder.transform, actor);
                foreach (float scale in new[] { .35f, 1.4f })
                {
                    profile.Scale = scale; actor.UpdateServer(.016f); Physics.SyncTransforms();
                    var sphere = actor.GetComponentInChildren<SphereCollider>();
                    if (scale < 1) result.smallestRadius = sphere.bounds.extents.x;
                    else result.largestRadius = sphere.bounds.extents.x;
                }
                Require(Mathf.Abs(result.largestRadius / result.smallestRadius - 4) < .02f, "Scale did not resize world-space hitboxes.");
                var proxy = Alien(holder.transform, client, profile, actor.transform.position, MultiplayerRole.ClientProxy, -11000 - i);
                profile.Scale = .7f; actor.UpdateServer(.016f);
                var snapshot = manager.GetComponentData<ProceduralAlienEnemy.State>(actor.GhostGameObject.LinkedEntity);
                client.EntityManager.SetComponentData(proxy.GhostGameObject.LinkedEntity, snapshot);
                proxy.transform.SetPositionAndRotation(actor.transform.position, actor.transform.rotation);
                proxy.UpdateClient(.016f);
                result.scaleReplicated = Mathf.Abs(proxy.Body.Scale - .7f) < .001f;
                proxy.Body.Observe(new SurfaceMotor.Pose { Position = proxy.transform.position, Rotation = proxy.transform.rotation },
                    snapshot.Velocity, snapshot.Detached, snapshot.Phase, .016f, 0, true);
                var sample = proxy.Body.Fluid.SampleParticles(); result.particles = sample.particles; result.nonFinite = sample.nonFinite;
                SetHealth(manager, actor.GhostGameObject.LinkedEntity, 0);
                for (int frame = 0; frame < 75 && actor.GhostGameObject.GhostEntityExists(); frame++)
                {
                    using var commands = new EntityCommandBuffer(Allocator.Temp);
                    GhostGameObject.UpdateEntityCommandBuffer = commands;
                    actor.UpdateServer(1f / 30); commands.Playback(manager); GhostGameObject.UpdateEntityCommandBuffer = default;
                }
                result.diedAndDespawned = !actor.GhostGameObject.GhostEntityExists();
                Require(result.humanDamaged && result.pmcDamaged && result.alienIgnored && result.projectileHit && result.scaleReplicated &&
                    result.diedAndDespawned && result.nonFinite == 0, "Integration fixture failed: " + result.species);
                Object.DestroyImmediate(actor.gameObject); Object.DestroyImmediate(proxy.gameObject);
                Object.DestroyImmediate(human.gameObject); Object.DestroyImmediate(pmc.gameObject); Object.DestroyImmediate(profile);
            }
        }
        finally
        {
            GhostGameObject.UpdateEntityCommandBuffer = default;
            Object.DestroyImmediate(holder); Application.logMessageReceived -= logs;
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(originalScene); EditorSceneManager.CloseScene(fixtureScene, true);
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllText("Library/BuildValidation/AlienIntegration/" + (first == 0 ? "report.json" : "remaining-report.json"), JsonUtility.ToJson(report, true));
        }
        Require(report.errors.Count == 0, "New errors during integration fixtures: " + string.Join("; ", report.errors));
        Debug.Log("Alien integration fixtures passed: humans and PMCs, alien exclusion, projectile damage, death, scaled hitboxes and client reconstruction.");
    }
    private static ProceduralAlienEnemy Alien(Transform holder, World world, AlienCreatureProfile profile, Vector3 position, MultiplayerRole role, int id, bool ambush = false)
    {
        var go = new GameObject("Fixture " + profile.Species + " " + role); go.transform.SetParent(holder, false); go.transform.position = position;
        var ghost = go.AddComponent<GhostGameObject>(); var actor = go.AddComponent<ProceduralAlienEnemy>();
        var serialized = new SerializedObject(actor); serialized.FindProperty("profile").objectReferenceValue = profile; serialized.ApplyModifiedPropertiesWithoutUndo();
        var manager = world.EntityManager;
        var entity = manager.CreateEntity(typeof(GhostGameObjectGuid), typeof(ProceduralAlienEnemy.State), typeof(PredictedPlayerGhost), typeof(GhostOwner), typeof(LocalTransform));
        manager.SetComponentData(entity, new ProceduralAlienEnemy.State { Scale = profile.Scale, AmbushEnabled = ambush,
            Action = ambush ? AlienAction.Ambush : AlienAction.Roam });
        manager.SetComponentData(entity, new PredictedPlayerGhost { MaxHealth = 100, CurrentHealth = 100 });
        manager.SetComponentData(entity, new GhostOwner { NetworkId = id }); manager.SetComponentData(entity, LocalTransform.FromPosition(position));
        manager.AddComponentObject(entity, new GhostGameObjectLink { LinkedInstance = ghost });
        ghost.LinkGhost(world, entity, role); return actor;
    }
    private static GhostGameObject Humanoid(Transform holder, World world, Vector3 position, bool pmc, int owner)
    {
        var go = new GameObject(pmc ? "Fixture PMC" : "Fixture human"); go.transform.SetParent(holder, false); go.transform.position = position;
        go.layer = LayerMask.NameToLayer("ServerPlayer");
        var capsule = go.AddComponent<CapsuleCollider>(); capsule.height = 1.8f; capsule.radius = .3f; capsule.center = Vector3.up * .9f;
        var ghost = go.AddComponent<GhostGameObject>(); var manager = world.EntityManager;
        var entity = manager.CreateEntity(typeof(GhostGameObjectGuid), typeof(PredictedPlayerGhost), typeof(GhostOwner), typeof(LocalTransform));
        manager.SetComponentData(entity, new PredictedPlayerGhost { MaxHealth = 100, CurrentHealth = 100 });
        manager.SetComponentData(entity, new GhostOwner { NetworkId = owner }); manager.SetComponentData(entity, LocalTransform.FromPosition(position));
        manager.AddComponentObject(entity, new GhostGameObjectLink { LinkedInstance = ghost });
        if (pmc) manager.AddComponent<DollSingerEnemy>(entity);
        ghost.LinkGhost(world, entity, MultiplayerRole.Server);
        // In Edit Mode Awake/Start do not run. Target eligibility uses the real type;
        // its controller is deliberately not active in an isolated authority fixture.
        go.AddComponent<PlayerGhost>().SetGhostGameObject(ghost);
        return ghost;
    }
    private static void Step(ProceduralAlienEnemy actor, int count)
    { for (int i = 0; i < count; i++) { Physics.SyncTransforms(); actor.UpdateServer(1f / 30); } }
    private static void SetHealth(EntityManager manager, Entity entity, float value)
    { var health = manager.GetComponentData<PredictedPlayerGhost>(entity); health.CurrentHealth = value; manager.SetComponentData(entity, health); }
    private static bool ProjectileDamage(World server, Transform holder, ProceduralAlienEnemy target)
    {
        var go = new GameObject("Fixture projectile"); go.transform.SetParent(holder, false);
        var ghost = go.AddComponent<GhostGameObject>(); var manager = server.EntityManager;
        var entity = manager.CreateEntity(typeof(GhostGameObjectGuid), typeof(GhostOwner));
        manager.SetComponentData(entity, new GhostOwner { NetworkId = 42 }); ghost.LinkGhost(server, entity, MultiplayerRole.Server);
        var projectile = go.AddComponent<Projectile>(); projectile.SetGhostGameObject(ghost);
        var weapon = ScriptableObject.CreateInstance<WeaponData>(); weapon.Damage = 17;
        typeof(Projectile).GetField("m_Weapon", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(projectile, weapon);
        typeof(Projectile).GetField("m_Data", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(projectile, new Projectile.ProjectileData { OwnerNetworkId = 42 });
        var movement = server.GetOrCreateSystemManaged<ServerPlayerMovementSystem>();
        float before = manager.GetComponentData<PredictedPlayerGhost>(target.GhostGameObject.LinkedEntity).CurrentHealth;
        typeof(Projectile).GetMethod("Damage", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(projectile,
            new object[] { target.GhostGameObject, movement.GetComponentLookup<PredictedPlayerGhost>(), movement.GetComponentLookup<GhostOwner>(), BodyPart.Chest });
        bool damaged = manager.GetComponentData<PredictedPlayerGhost>(target.GhostGameObject.LinkedEntity).CurrentHealth == before - 17;
        Object.DestroyImmediate(weapon); Object.DestroyImmediate(go); return damaged;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
