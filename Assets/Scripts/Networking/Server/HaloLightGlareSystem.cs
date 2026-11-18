// 服务端判断光环灯的直射眩光。结合发光方向、距离、遮挡与烟雾给受影响的角色写入状态，后续移动和表现系统可以读取。
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(ServerPlayerMovementSystem))]
    public partial class HaloLightGlareSystem : SystemBase {
        private struct Actor {
            public Entity Entity;
            public PredictedPlayerGhost State;
            public Vector3 Eye, Forward;
            public bool Human, Emits;
            public PhysicsScene Physics;
        }

        private readonly List<Actor> m_Actors = new();
        private readonly Dictionary<Entity, float> m_Filters = new();
        private EntityQuery m_Smoke;
        private double m_NextSample;

        protected override void OnCreate() {
            RequireForUpdate<NetworkStreamInGame>();
            m_Smoke = GetEntityQuery(typeof(TacticalServerWorld));
        }

        protected override void OnUpdate() {
            // 低频采样所有人的灯光和各自的受影响强度，没到下一采样点不用每帧重新比较所有角色。
            if (SystemAPI.Time.ElapsedTime < m_NextSample) return;
            m_NextSample = SystemAPI.Time.ElapsedTime + HaloLightGlareRules.SampleInterval;
            m_Actors.Clear();
            m_Filters.Clear();
            // 从权威背包查有没有防眩设备。对目标计算前先缓存人物的折减系数。
            foreach (var (joined, connection) in SystemAPI.Query<RefRO<JoinedClient>>().WithAll<RaidInventoryState>()
                         .WithEntityAccess())
                m_Filters[joined.ValueRO.PlayerEntity] =
                    SpecialEquipmentRules.GlareMultiplier(EntityManager
                        .GetComponentObject<RaidInventoryState>(connection).Graph);
            foreach (var (health, pose, entity) in SystemAPI.Query<RefRO<PredictedPlayerGhost>, RefRO<LocalTransform>>()
                         .WithAll<GhostGameObjectLink>().WithEntityAccess()) {
                var root = EntityManager.GetComponentObject<GhostGameObjectLink>(entity).LinkedInstance;
                if (!root) continue;
                root.TryGetComponent<PlayerGhost>(out var player);
                root.TryGetComponent<ProceduralAlienEnemy>(out var alien);
                if (!player && !alien) continue;
                var state = health.ValueRO;
                var control = state.ControllerState;
                var look = control.FreeLookOffset;
                var forward = player
                    ? Quaternion.Euler(control.PitchDegrees + look.y, control.YawDegrees + look.x, 0) * Vector3.forward
                    : root.transform.forward;
                m_Actors.Add(new Actor {
                    Entity = entity, State = state, Forward = forward,
                    Eye = alien ? alien.AimPoint :
                        player.ShotOrigin ? player.ShotOrigin.position :
                        (Vector3)pose.ValueRO.Position + Vector3.up * 1.5f,
                    Human = EntityManager.HasComponent<GhostOwner>(entity) &&
                            EntityManager.GetComponentData<GhostOwner>(entity).NetworkId > 0 &&
                            !EntityManager.HasComponent<DollSingerEnemy>(entity),
                    Emits = player && HaloLightGlareRules.Emits(state),
                    Physics = root.gameObject.scene.GetPhysicsScene()
                });
            }
            var smoke = m_Smoke.IsEmptyIgnoreFilter
                ? null
                : EntityManager.GetComponentObject<TacticalServerWorld>(m_Smoke.GetSingletonEntity());
            UnityEngine.Physics.SyncTransforms();
            // 对每个活目标看有哪些来源可造成有效强光。距离和方向合格后再查实际物理遮挡与烟雾。
            foreach (var target in m_Actors) {
                float strongest = 0;
                var sourcePoint = Vector3.zero;
                if (target.State.CurrentHealth > 0)
                    foreach (var source in m_Actors) {
                        if (!source.Emits || source.Entity == target.Entity || source.Physics != target.Physics ||
                            HaloTransferRules.Teammates(source.State, target.State, source.Human, target.Human))
                            continue;
                        // The light follows weapon aim; free look changes the receiver's gaze only.
                        var control = source.State.ControllerState;
                        var direction = Quaternion.Euler(control.PitchDegrees, control.YawDegrees, 0) * Vector3.forward;
                        float strength = HaloLightGlareRules.Strength(source.State, source.Eye, direction, target.Eye,
                            target.Forward);
                        if (strength <= strongest ||
                            !HaloLightGlareRules.ClearPath(source.Physics, source.Eye, target.Eye) ||
                            SmokeVisibility.Blocked(smoke, source.Physics, source.Eye, target.Eye))
                            continue;
                        strongest = strength;
                        sourcePoint = source.Eye;
                    }
                var state = EntityManager.GetComponentData<PredictedPlayerGhost>(target.Entity);
                // 烟雾里的角色可额外读取战术视线状态。灯光和烟的影响分别存，后续角色判断用对应字段。
                if (state.CurrentHealth > 0 && (!state.ClashEnabled || state.ClashLife == ClashLife.Alive) &&
                    smoke != null && smoke.Statuses.TryGetValue(target.Entity, out var exposure) &&
                    exposure.FlashUntil > SystemAPI.Time.ElapsedTime) {
                    float flash = exposure.FlashStrength *
                                  Mathf.Clamp01((float)(exposure.FlashUntil - SystemAPI.Time.ElapsedTime) / 4);
                    if (flash > strongest) {
                        strongest = flash;
                        sourcePoint = target.Eye + target.Forward * 2;
                    }
                }
                float filter = m_Filters.TryGetValue(target.Entity, out float equipped)
                    ? equipped
                    :
                    EntityManager.HasComponent<DollSingerEnemyBrain>(target.Entity)
                        ?
                        SpecialEquipmentRules.GlareMultiplier(EntityManager
                            .GetComponentObject<DollSingerEnemyBrain>(target.Entity).Inventory)
                        : 1;
                state.HaloLightGlare = strongest * filter;
                state.HaloGlareSource = sourcePoint;
                EntityManager.SetComponentData(target.Entity, state);
            }
        }
    }
}