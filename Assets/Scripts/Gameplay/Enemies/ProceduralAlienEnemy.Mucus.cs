// 异形喷吐的服务端流程。出招前检查抛物线路径，发射后每步扫过移动区间，命中才写伤害、落点和对应反馈。
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed partial class ProceduralAlienEnemy {
        private bool m_MucusInFlight, m_MucusLowArc;
        private uint m_ImpactSequence;
        private Vector3 m_ImpactPoint, m_ImpactNormal;
        private AlienImpactKind m_ImpactKind;
        public int LastArcQueries { get; private set; }

        // 出招时试可达的抛物线，高弧线被屋顶挡住时也可尝试低弧。实在无路就短暂延后重试。
        private bool PrepareMucusArc(Vector3 target) {
            LastArcQueries = 0;
            var origin = MucusSocket();
            m_MucusHits ??= new RaycastHit[64];
            for (int mode = 0; mode < 2; mode++) {
                if (!MucusArcClear(origin, target, mode == 1)) continue;
                m_MucusLowArc = mode == 1;
                return true;
            }
            // A failed launch is reconsidered at most twice a second, not every server tick.
            m_NextAttack = m_Clock + .5f;
            return false;
        }

        private Vector3 MucusSocket() { return m_Body.AttackSocket + transform.forward * .4f + Vector3.up * .12f; }

        // 用有限数量的分段扫略测轨迹，中间被挡比只看终点更早发现。正式飞行还会再次检查实际碰撞。
        private bool MucusArcClear(Vector3 origin, Vector3 target, bool low) {
            int mask = LayerMask.GetMask("Default", "Ground");
            LastArcQueries++;
            if (UnityEngine.Physics.CheckSphere(origin, .18f, mask, QueryTriggerInteraction.Ignore)) return false;
            float duration = AlienMucusTrajectory.FlightTime(origin, target, profile.MucusSpeed);
            var previous = origin;
            // 24 bounded swept segments; the server motor still resolves every flight step.
            for (int i = 1; i <= 24; i++) {
                var point = AlienMucusTrajectory.Point(origin, target, profile.MucusSpeed, duration * i / 24, low);
                var delta = point - previous;
                LastArcQueries++;
                if (delta.sqrMagnitude > .000001f && UnityEngine.Physics.SphereCast(previous, .22f, delta.normalized,
                        out _, delta.magnitude, mask, QueryTriggerInteraction.Ignore))
                    return false;
                previous = point;
            }
            return true;
        }

        private float m_MucusStarted;
        private Vector3 m_MucusOrigin, m_MucusTarget, m_MucusPrevious;
        private RaycastHit[] m_MucusHits;
        public int MucusHits { get; private set; }

        // 把已经确定的起点、目标和时间保存为一发独立弹体状态。客户端可用同一条曲线画出来。
        private void StartMucusProjectile() {
            m_MucusHits ??= new RaycastHit[64];
            m_MucusInFlight = true;
            m_MucusStarted = m_Clock;
            m_MucusOrigin = m_CombatOrigin;
            m_MucusTarget = m_CombatPoint;
            m_MucusPrevious = m_MucusOrigin;
        }

        private void TickMucusProjectile() {
            if (!m_MucusInFlight) return;
            // 每拍由经过的时间重算这滴黏液的位置，然后扫过上一步与现在之间的路径。
            float age = m_Clock - m_MucusStarted;
            float duration = AlienMucusTrajectory.FlightTime(m_MucusOrigin, m_MucusTarget, profile.MucusSpeed);
            var next = AlienMucusTrajectory.Point(m_MucusOrigin, m_MucusTarget, profile.MucusSpeed,
                Mathf.Min(age, duration + .12f), m_MucusLowArc);
            var delta = next - m_MucusPrevious;
            if (delta.sqrMagnitude > .000001f) {
                UnityEngine.Physics.SyncTransforms();
                int mask = LayerMask.GetMask("Default", "Ground", "ServerPlayer");
                int count = UnityEngine.Physics.SphereCastNonAlloc(m_MucusPrevious, .18f, delta.normalized, m_MucusHits,
                    delta.magnitude, mask, QueryTriggerInteraction.Ignore);
                if (count == m_MucusHits.Length) {
                    m_MucusInFlight = false;
                    return;
                }
                // 碰撞结果不保证按距离排列。近的墙能先挡住后面的目标，需要找到真实第一处接触。
                float nearest = float.MaxValue;
                GhostGameObject victim = null;
                RaycastHit contact = default;
                for (int i = 0; i < count; i++) {
                    var root = m_MucusHits[i].collider.GetComponentInParent<GhostGameObject>();
                    if (root == GhostGameObject || m_MucusHits[i].distance >= nearest) continue;
                    nearest = m_MucusHits[i].distance;
                    victim = root;
                    contact = m_MucusHits[i];
                }
                if (nearest < float.MaxValue) {
                    m_MucusInFlight = false;
                    m_ImpactSequence++;
                    m_ImpactPoint = contact.point;
                    m_ImpactNormal = contact.normal;
                    m_ImpactKind = AlienImpactKind.World;
                    var manager = GhostGameObject.EntityManager;
                    if (victim && victim.World == World && IsLivingHumanoid(manager, victim.LinkedEntity)) {
                        var entity = victim.LinkedEntity;
                        // 打到头附近才添加特殊的黏液眩目。其他身体命中或撞墙的反馈各有单独类型。
                        bool face = Vector3.Distance(contact.point, TargetHead(entity).position) < .7f;
                        m_ImpactKind = face ? AlienImpactKind.Face : AlienImpactKind.Body;
                        ApplyAlienDamage(entity, profile.Damage * .4f, face ? BodyPart.Head : BodyPart.Chest);
                        var health = manager.GetComponentData<PredictedPlayerGhost>(entity);
                        if (face && health.CurrentHealth > 0)
                            health.MucusBlindSeconds = Mathf.Max(health.MucusBlindSeconds, profile.MucusBlindSeconds);
                        manager.SetComponentData(entity, health);
                        MucusHits++;
                    }
                    return;
                }
            }
            m_MucusPrevious = next;
            if (age >= duration + .12f) m_MucusInFlight = false;
        }
    }
}