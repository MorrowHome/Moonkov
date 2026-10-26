using System;
using System.Reflection;
using Unity.Core;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    [MenuItem("Tools/Moonkov/Check Projectile Near Miss")]
    public static string RunProjectileThreatContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var holder = new GameObject("Temporary projectile threat checks");
        World world = null; WeaponData weapon = null; DollSingerAITuning tuning = null;
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        try
        {
            world = CounterplayWorld("near miss projectile");
            var manager = world.EntityManager;
            var origin = new Vector3(-14000, 1000, -14000);
            var attacker = Humanoid(holder.transform, world, origin, false, 101);
            var target = Humanoid(holder.transform, world, origin + new Vector3(1.2f, 0, 10), true, 102);
            var brain = new DollSingerEnemyBrain { Inventory = InventoryGraph.Create(false, false),
                Random = new Unity.Mathematics.Random(137), Seed = 1, Action = PmcAction.Scavenge };
            manager.AddComponentObject(target.LinkedEntity, brain);
            manager.AddComponent<Simulate>(attacker.LinkedEntity);
            var source = new ClashDamageSource { NetworkId = 101 };
            int players = LayerMask.GetMask("ServerPlayer"), walls = LayerMask.GetMask("Default", "Ground");
            Vector3 from = origin + new Vector3(0, .9f, 1.5f), to = origin + new Vector3(0, .9f, 20);
            Collider[] buffer = null;
            void ResetBrain()
            {
                brain.Target = Entity.Null; brain.Contacts.Clear(); brain.NextNearMiss = 0;
                brain.Suppression = 0; brain.NextDecision = 100; brain.CommitUntil = 100;
            }
            int Observe(Vector3 a, Vector3 b, Collider impact, double time, ClashDamageSource attribution)
                => ProjectileThreat.Observe(scene.GetPhysicsScene(), world, attacker.LinkedEntity, attribution,
                    a, b, impact, time, players, walls, ref buffer);
            Physics.SyncTransforms();
            var beforeTarget = origin + new Vector3(0, .9f, 9.5f);
            Check(Observe(from, beforeTarget, null, 9, source) == 0,
                "A projectile that had not yet passed the body counted as a near miss.");
            Check(Observe(beforeTarget, to, null, 9.1, source) == 1,
                "A projectile did not count after passing the nearby body.");
            ResetBrain();
            Check(Observe(from, to, null, 10, source) == 1 && Mathf.Approximately(brain.Suppression, .24f),
                "A real nearby path did not pressure the PMC.");
            bool cleared = true; foreach (var item in buffer) if (item) cleared = false;
            Check(cleared, "The shared physics buffer retained scene collider references.");
            var contact = brain.Contacts[attacker.LinkedEntity];
            Check(!contact.Visible && !contact.Confirmed && contact.PositionUncertainty >= 12 &&
                Vector3.Distance(contact.Position, attacker.transform.position) > 1 && brain.CommitUntil == 0,
                "The near miss exposed the hidden shooter's exact position or did not interrupt scavenging.");
            Check(Observe(from, to, null, 10.1, source) == 0 && Mathf.Approximately(brain.Suppression, .24f),
                "One projectile repeatedly pressured the same PMC inside the cooldown.");
            Vector3 previousClue = contact.Position;
            Check(Observe(from, to, null, 10.21, source) == 1 && brain.Suppression > .4f,
                "Sustained real near misses did not build pressure.");
            Check(Vector3.Distance(contact.Position, previousClue) < .001f,
                "A burst made the estimated attacker position jump with every round.");

            var ai = world.GetOrCreateSystemManaged<DollSingerEnemySystem>();
            tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
            typeof(DollSingerEnemySystem).GetField("m_Tuning", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ai, tuning);
            brain.LastSenseTime = 10.11;
            var eye = target.transform.position + Vector3.up * 1.4f;
            typeof(DollSingerEnemySystem).GetMethod("Sense", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(ai, new object[] { brain, target.transform, eye, 0f, 10.21d });
            Check(brain.Target == attacker.LinkedEntity && !brain.Visible && !brain.Contacts[attacker.LinkedEntity].Confirmed,
                "A near miss became a visible target or was not retained as an uncertain clue.");
            var mapObject = new GameObject("Near miss test map"); mapObject.SetActive(false); mapObject.transform.SetParent(holder.transform);
            var map = mapObject.AddComponent<MoonRaidMap>();
            var h = manager.GetComponentData<PredictedPlayerGhost>(target.LinkedEntity); h.CurrentAmmo = 10;
            var decision = new object[] { brain, target.transform.position, h, map, 10.21d, PmcAction.Scavenge, 0f, null, false };
            typeof(DollSingerEnemySystem).GetMethod("EvaluateDecision", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ai, decision);
            Check((PmcAction)decision[5] == PmcAction.Cover, "Sustained near misses did not lead the PMC to seek cover.");

            ResetBrain();
            Check(Observe(from + Vector3.left * 4, to + Vector3.left * 4, null, 11, source) == 0 && brain.Suppression == 0,
                "A distant projectile applied near-miss pressure.");
            ResetBrain();
            Check(Observe(from, to, target.GetComponent<Collider>(), 11, source) == 0 && brain.Suppression == 0,
                "A direct hit also counted as a near miss.");
            ResetBrain(); var sameOwner = new ClashDamageSource { NetworkId = 102 };
            Check(Observe(from, to, null, 11, sameOwner) == 0, "An actor's own projectile suppressed it.");
            ResetBrain(); var clash = manager.GetComponentData<PredictedPlayerGhost>(target.LinkedEntity);
            clash.ClashEnabled = true; manager.SetComponentData(target.LinkedEntity, clash);
            Check(Observe(from, to, null, 11, source) == 0, "Ordinary raid suppression changed bounty-clash behavior.");
            clash.ClashEnabled = false; manager.SetComponentData(target.LinkedEntity, clash);
            ResetBrain();
            var sideWall = GameObject.CreatePrimitive(PrimitiveType.Cube); sideWall.transform.SetParent(holder.transform);
            sideWall.transform.position = origin + new Vector3(.55f, 1, 10); sideWall.transform.localScale = new Vector3(.2f, 3, 5);
            Physics.SyncTransforms();
            Check(Observe(from, to, null, 11, source) == 0, "A wall between the path and body did not block pressure.");
            Object.DestroyImmediate(sideWall); Physics.SyncTransforms();
            var frontWall = GameObject.CreatePrimitive(PrimitiveType.Cube); frontWall.transform.SetParent(holder.transform);
            frontWall.transform.position = origin + new Vector3(0, 1, 8.8f); frontWall.transform.localScale = new Vector3(5, 3, .2f);
            Physics.SyncTransforms(); RaycastHit[] hits = new RaycastHit[16];
            Check(Ballistics.Sweep(scene.GetPhysicsScene(), from, to, .005f, players | walls, attacker.transform, ref hits, out var impact),
                "The obstruction fixture did not actually stop the projectile.");
            ResetBrain();
            Check(Observe(from, impact.point, impact.collider, 11, source) == 0,
                "A projectile that hit the wall suppressed a PMC behind it.");
            Object.DestroyImmediate(frontWall); Physics.SyncTransforms();

            ResetBrain(); world.SetTime(new TimeData(20, .02f));
            var bullet = new GameObject("Temporary real projectile"); bullet.transform.SetParent(holder.transform);
            var ghost = bullet.AddComponent<GhostGameObject>();
            var entity = manager.CreateEntity(typeof(GhostGameObjectGuid), typeof(GhostOwner));
            manager.SetComponentData(entity, new GhostOwner { NetworkId = 101 }); ghost.LinkGhost(world, entity, MultiplayerRole.Server);
            var projectile = bullet.AddComponent<Projectile>(); projectile.SetGhostGameObject(ghost);
            weapon = ScriptableObject.CreateInstance<WeaponData>(); weapon.ProjectileGravity = 0; weapon.ProjectileRadius = .005f;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Field(string name, object value) => typeof(Projectile).GetField(name, flags).SetValue(projectile, value);
            Field("m_Weapon", weapon); Field("m_HitMask", players | walls); Field("m_Shooter", attacker.transform);
            Field("m_ShooterEntity", attacker.LinkedEntity); Field("m_NearMissPlayerMask", players); Field("m_NearMissWorldMask", walls);
            Field("m_Data", new Projectile.ProjectileData { Origin = from, InitialVelocity = Vector3.forward * 100,
                OwnerNetworkId = 101, DamageSource = source });
            typeof(Projectile).GetMethod("Advance", flags).Invoke(projectile, new object[] { .18f, true });
            Check(brain.Suppression > 0 && brain.Contacts.ContainsKey(attacker.LinkedEntity),
                "The production projectile sweep did not deliver the near-miss event.");
            ResetBrain(); weapon.Damage = 0;
            world.GetOrCreateSystemManaged<ServerPlayerMovementSystem>();
            var directObject = new GameObject("Temporary direct-hit projectile"); directObject.transform.SetParent(holder.transform);
            var directGhost = directObject.AddComponent<GhostGameObject>();
            var directEntity = manager.CreateEntity(typeof(GhostGameObjectGuid), typeof(GhostOwner));
            manager.SetComponentData(directEntity, new GhostOwner { NetworkId = 101 }); directGhost.LinkGhost(world, directEntity, MultiplayerRole.Server);
            var direct = directObject.AddComponent<Projectile>(); direct.SetGhostGameObject(directGhost);
            void DirectField(string name, object value) => typeof(Projectile).GetField(name, flags).SetValue(direct, value);
            DirectField("m_Weapon", weapon); DirectField("m_HitMask", players | walls); DirectField("m_Shooter", attacker.transform);
            DirectField("m_ShooterEntity", attacker.LinkedEntity); DirectField("m_NearMissPlayerMask", players); DirectField("m_NearMissWorldMask", walls);
            DirectField("m_Data", new Projectile.ProjectileData { Origin = from + Vector3.right * 1.2f,
                InitialVelocity = Vector3.forward * 100, OwnerNetworkId = 101, DamageSource = source });
            var advance = typeof(Projectile).GetMethod("Advance", flags);
            advance.Invoke(direct, new object[] { .08f, true });
            Check(brain.Suppression == 0, "An approaching direct hit caused premature near-miss pressure.");
            advance.Invoke(direct, new object[] { .1f, true });
            Check(brain.Suppression == 0 && (bool)typeof(Projectile).GetField("m_Stopped", flags).GetValue(direct),
                "A real direct hit was also counted as a near miss.");
            Check(buffer != null && buffer.Length <= 64, "Near-miss physics storage exceeded its bound.");
            return $"PASS {checks} projectile threat checks: real swept path, occlusion, source, cooldown and PMC cover decision.";
        }
        finally
        {
            Object.DestroyImmediate(holder); if (weapon) Object.DestroyImmediate(weapon); if (tuning) Object.DestroyImmediate(tuning);
            if (world != null && world.IsCreated) world.Dispose();
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
