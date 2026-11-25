using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Unity.MP_FPS.Missions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    // 报警只提供位置线索。障碍会缩短能有效听到的距离，
    // 听见声音也不会告诉 AI 是谁触发了装置。
    public static class TacticalAlarmHearing {
        public const float Range = 35;

        private static class PhysicsMask {
            public static readonly int World = LayerMask.GetMask("Default", "Ground");
        }

        public static string EventId(uint epoch, uint id) { return id == 0 ? null : $"trap/{epoch}/{id}"; }

        public static bool WithinRange(float squaredDistance, bool obstructed, float range = Range) {
            return MissionMachineSounds.WithinRange(squaredDistance, range, obstructed);
        }

        public static bool Audible(PhysicsScene scene, Vector3 source, Vector3 listener, float range = Range) {
            if (!scene.IsValid()) return false;
            var delta = listener - source;
            float distanceSquared = delta.sqrMagnitude;
            if (!WithinRange(distanceSquared, false, range)) return false;
            if (WithinRange(distanceSquared, true, range)) return true;
            float distance = Mathf.Sqrt(distanceSquared);
            return !scene.Raycast(source, delta / distance, out _, distance, PhysicsMask.World,
                QueryTriggerInteraction.Ignore);
        }
    }

    // 每次实际爆炸只发一轮限量的声音线索。附近空闲敌人只获得
    // 爆炸位置，不能直接识别投掷者或得到瞄准点。
    public static class RaidEnvironmentalNoise {
        public const float ExplosionRange = 50;

        public static string ExplosionId(bool energy, uint epoch, uint id) {
            return id == 0 ? null : energy ? $"blast/energy/{id}" : $"blast/tactical/{epoch}/{id}";
        }

        // 实体版本让回收复用的投掷体 ID 依然能区分，
        // 而声音线索里无需带射手身份。
        public static string ProjectileExplosionId(Entity projectile) {
            return projectile == Entity.Null ? null : $"blast/projectile/{projectile.Index}/{projectile.Version}";
        }

        public static void EmitExplosion(
            EntityManager manager,
            PhysicsScene scene,
            Vector3 position,
            string clue,
            double now) {
            if (!scene.IsValid() || string.IsNullOrEmpty(clue) || !double.IsFinite(now) ||
                BountyClashRuntime.Enabled(manager))
                return;
            var source = position + Vector3.up;
            using var humans = manager.CreateEntityQuery(new EntityQueryDesc {
                All = new[] {
                    ComponentType.ReadOnly<DollSingerEnemy>(), ComponentType.ReadOnly<Simulate>(),
                    ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostGameObjectLink>()
                },
                None = new[] { ComponentType.ReadOnly<ClashBot>() }
            });
            using var actors = humans.ToEntityArray(Allocator.Temp);
            foreach (var entity in actors) {
                if (manager.GetComponentData<PredictedPlayerGhost>(entity).CurrentHealth <= 0 ||
                    !manager.HasComponent<DollSingerEnemyBrain>(entity))
                    continue;
                var link = manager.GetComponentObject<GhostGameObjectLink>(entity);
                if (!link.LinkedInstance || link.LinkedInstance.gameObject.scene.GetPhysicsScene() != scene) continue;
                var brain = manager.GetComponentObject<DollSingerEnemyBrain>(entity);
                var listener = (Vector3)manager.GetComponentData<LocalTransform>(entity).Position + Vector3.up * 1.2f;
                if (brain.Target != Entity.Null || brain.Leaving ||
                    !TacticalAlarmHearing.Audible(scene, source, listener, ExplosionRange))
                    continue;
                DollSingerEnemySystem.HearMissionAlarm(brain, clue, position, now);
            }
            using var aliens = manager.CreateEntityQuery(new EntityQueryDesc {
                All = new[] {
                    ComponentType.ReadOnly<ProceduralAlienEnemy.State>(), ComponentType.ReadOnly<Simulate>(),
                    ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostGameObjectLink>()
                }
            });
            using var creatures = aliens.ToEntityArray(Allocator.Temp);
            foreach (var entity in creatures) {
                if (manager.GetComponentData<PredictedPlayerGhost>(entity).CurrentHealth <= 0 ||
                    !TacticalAlarmHearing.WithinRange(
                        ((Vector3)manager.GetComponentData<LocalTransform>(entity).Position - source).sqrMagnitude,
                        false, ExplosionRange))
                    continue;
                var link = manager.GetComponentObject<GhostGameObjectLink>(entity);
                if (!link.LinkedInstance || link.LinkedInstance.gameObject.scene.GetPhysicsScene() != scene) continue;
                var alien = link.LinkedInstance.GetComponent<ProceduralAlienEnemy>();
                if (alien && TacticalAlarmHearing.Audible(scene, source, alien.AimPoint, ExplosionRange))
                    alien.HearMissionAlarm(clue, position);
            }
        }
    }
}
