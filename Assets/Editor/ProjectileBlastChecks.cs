using System;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ProjectileBlastChecks
{
    [MenuItem("Tools/Moonkov/Check Projectile Blast")]
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run outside Play.");
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new InvalidOperationException(message);
        }
        void Near(float actual, float expected, string message) =>
            Check(Mathf.Abs(actual - expected) < .02f, message + ": " + actual + " expected " + expected);

        Near(ProjectileBlast.DamageAt(300, 4, 0), 300, "Blast centre");
        Near(ProjectileBlast.DamageAt(300, 4, 1), 300, "Full damage core boundary");
        Near(ProjectileBlast.DamageAt(300, 4, 2.5f), 150, "Half damage between core and rim");
        Near(ProjectileBlast.DamageAt(300, 4, 3.25f), 75, "Quarter damage near rim");
        Near(ProjectileBlast.DamageAt(300, 4, 4), 0, "No damage at rim");
        Near(ProjectileBlast.DamageAt(300, 4, 5), 0, "No damage outside radius");
        Near(ProjectileBlast.DamageAt(300, 4, -1), 0, "Negative distance rejected");
        Near(ProjectileBlast.DamageAt(float.NaN, 4, 1), 0, "Invalid damage rejected");
        Near(ProjectileBlast.DamageAt(300, float.PositiveInfinity, 1), 0, "Invalid radius rejected");
        Near(ProjectileBlast.DamageAt(300, 4, float.NaN), 0, "Invalid distance rejected");

        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewPreviewScene();
        var other = EditorSceneManager.NewPreviewScene();
        using var world = new World("Projectile blast authority contract", WorldFlags.GameServer);
        var manager = world.EntityManager;
        var physics = scene.GetPhysicsScene();
        try
        {
            (Entity entity, GameObject root) Victim(string name, Vector3 point, int owner = 0, bool body = false, bool separate = false)
            {
                var root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, separate ? other : scene);
                root.transform.position = point;
                root.layer = LayerMask.NameToLayer("ServerPlayer");
                root.AddComponent<SphereCollider>().radius = .2f;
                var limb = new GameObject("Additional limb");
                limb.transform.SetParent(root.transform, false); limb.layer = root.layer;
                limb.AddComponent<SphereCollider>().radius = .12f;
                var entity = manager.CreateEntity(typeof(PredictedPlayerGhost));
                var health = new PredictedPlayerGhost { CurrentHealth = 1000, MaxHealth = 1000 };
                if (body) RaidHealth.Initialize(ref health);
                manager.SetComponentData(entity, health);
                if (owner != 0) manager.AddComponentData(entity, new GhostOwner { NetworkId = owner });
                manager.AddComponentObject(entity, new GhostGameObjectLink { LinkedInstance = root.AddComponent<GhostGameObject>() });
                return (entity, root);
            }
            PredictedPlayerGhost Health(Entity entity) => manager.GetComponentData<PredictedPlayerGhost>(entity);
            void Sync()
            {
                Physics.SyncTransforms();
                physics.Simulate(.02f);
                other.GetPhysicsScene().Simulate(.02f);
            }
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
            foreach (uint id in new[] { 11u, DollSingerWeapons.Tempest })
            {
                var weapon = registry.GetWeaponData(id);
                Check(weapon && weapon.Behavior == ProjectileBehavior.AreaOfEffect, "Production explosive weapon " + id);
                float radius = weapon.AoeRadius, damage = weapon.Damage;
                Vector3 origin = new Vector3(id * 20, 0, 0);
                var near = Victim("Core target", origin + Vector3.right * (.2f + radius * .125f));
                var mid = Victim("Splash target", origin + Vector3.forward * (.2f + radius * .625f));
                // Keep the physical surface outside by 1 cm; exact rim math is
                // checked above without large-coordinate float roundoff.
                var edge = Victim("Rim target", origin + Vector3.left * (.21f + radius));
                var outside = Victim("Outside target", origin + Vector3.back * (.2f + radius + 1));
                var self = Victim("Original shooter", origin + Vector3.up * (.2f + radius * .125f), (int)id);
                var isolated = Victim("Other physics world", near.root.transform.position, separate: true);
                // Sensors and disabled hitboxes must not extend the damage core.
                foreach (bool trigger in new[] { true, false })
                {
                    var ignored = new GameObject("Ignored collider");
                    ignored.transform.SetParent(mid.root.transform, false); ignored.transform.position = origin;
                    var collider = ignored.AddComponent<SphereCollider>(); collider.radius = .1f;
                    collider.isTrigger = trigger; collider.enabled = trigger;
                }
                Sync();
                int count = ProjectileBlast.Apply(world, physics, origin, radius, damage, (int)id, 100, out byte flags);
                Check(count == 3 && flags == 1, "One impact per body, including self: " + id + "; count=" + count + "; flags=" + flags);
                Near(Health(near.entity).CurrentHealth, 1000 - damage, "Direct/core impact keeps full damage: " + id);
                Near(Health(mid.entity).CurrentHealth, 1000 - damage * .5f, "Splash uses nearest body surface: " + id);
                Near(Health(self.entity).CurrentHealth, 1000 - damage, "Shooter receives own blast: " + id);
                Near(Health(edge.entity).CurrentHealth, 1000, "Rim not damaged: " + id);
                Near(Health(outside.entity).CurrentHealth, 1000, "Outside not damaged: " + id);
                Near(Health(isolated.entity).CurrentHealth, 1000, "Different physics world not damaged: " + id);

                self.root.transform.position = origin + Vector3.right * 10;
                var wounded = Health(self.entity); wounded.CurrentHealth = 10; manager.SetComponentData(self.entity, wounded);
                Sync();
                count = ProjectileBlast.Apply(world, physics, self.root.transform.position, radius, damage, (int)id, 101, out flags);
                Check(count == 1 && flags == 0 && Health(self.entity).CurrentHealth == 0,
                    "Self kill cannot confirm an opponent kill: " + id);
            }

            Vector3 shieldOrigin = new Vector3(400, 0, 0);
            var shielded = Victim("Shielded body", shieldOrigin + Vector3.right * .5f, body: true);
            var shield = Health(shielded.entity); shield.ShieldEquipped = true; shield.ShieldCharge = 100; shield.ShieldEnergy = 100;
            manager.SetComponentData(shielded.entity, shield);
            var covered = Victim("Covered body", shieldOrigin + Vector3.forward * 2);
            var wall = new GameObject("Blast cover"); SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = shieldOrigin + Vector3.forward;
            wall.AddComponent<BoxCollider>().size = new Vector3(2, 3, .1f);
            Sync();
            Check(ProjectileBlast.Apply(world, physics, shieldOrigin, 4, 110, 90, 102, out byte shieldFlags) == 1 && shieldFlags == 1,
                "Shield and tissue form one real impact");
            Near(Health(shielded.entity).ShieldCharge, 0, "Shield absorbs once");
            Near(Health(shielded.entity).CurrentHealth, 430, "Only residual blast reaches tissue");
            Near(Health(covered.entity).CurrentHealth, 1000, "Solid wall blocks blast");

            Vector3 limbOrigin = new Vector3(420, 0, 0);
            var large = Victim("Large rig with distant root collider", limbOrigin + Vector3.right * 10);
            var exposed = large.root.transform.GetChild(0);
            exposed.position = limbOrigin + Vector3.forward * .3f;
            Sync();
            Check(ProjectileBlast.Apply(world, physics, limbOrigin, 2, 80, 90, 103, out _) == 1,
                "Nearby limb is reachable when the root collider is outside the blast");
            Near(Health(large.entity).CurrentHealth, 920, "Exposed limb grants one core impact");

            Vector3 teamOrigin = new Vector3(440, 0, 0);
            var actor = Victim("Captured online actor", teamOrigin + Vector3.right * .5f, 7);
            var friend = Victim("Protected squadmate", teamOrigin + Vector3.forward * .5f, 8);
            manager.AddComponentData(actor.entity, new OnlineRaidMember { Team = 42 });
            manager.AddComponentData(friend.entity, new OnlineRaidMember { Team = 42 });
            var oldHit = Health(friend.entity); oldHit.LastHitTick = 20; oldHit.LastDamageAmount = 12;
            manager.SetComponentData(friend.entity, oldHit);
            var source = BountyClashDamage.Capture(world, actor.entity);
            // A delayed explosion keeps the release-time actor even after its connection changes.
            manager.SetComponentData(actor.entity, new GhostOwner { NetworkId = 9 });
            manager.SetComponentData(friend.entity, new GhostOwner { NetworkId = 7 });
            Sync();
            Check(ProjectileBlast.Apply(world, physics, teamOrigin, 3, 80, source, 104, out byte teamFlags) == 1 && teamFlags == 0,
                "Captured self takes damage without a hit marker; reused teammate ID stays protected");
            Near(Health(actor.entity).CurrentHealth, 920, "Captured actor receives self damage");
            Check(Health(friend.entity).CurrentHealth == 1000 && Health(friend.entity).LastHitTick == 20 &&
                Health(friend.entity).LastDamageAmount == 12, "Protected teammate preserves health and hit history");
            Check(ProjectileBlast.Apply(world, physics, teamOrigin, 3, float.NaN, source, 105, out byte invalidFlags) == 0 && invalidFlags == 0,
                "Invalid blast cannot mutate health or confirm a hit");
            return "PASS " + checks + " projectile blast checks: production Sundown/Tempest, falloff, body surfaces, self damage, shields, cover and captured team identity.";
        }
        finally
        {
            SceneManager.SetActiveScene(original);
            EditorSceneManager.ClosePreviewScene(other);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
