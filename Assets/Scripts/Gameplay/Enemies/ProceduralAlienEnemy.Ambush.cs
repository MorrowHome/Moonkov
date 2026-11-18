// 贴顶蜘蛛的伏击循环。等目标、亮出预警、扑向选定位置、落地恢复，再找能重新挂住的地方。
// 追踪和换位沿表面导航，没路就停在可达位置。
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed partial class ProceduralAlienEnemy {
        private bool m_AmbushEnabled, m_StrikeHit;
        private float m_LastHealth, m_RepositionUntil, m_NextPerch;
        private Vector3 m_StrikePoint, m_StrikeOrigin, m_RepositionGoal;
        private AlienSurfaceNavigation m_Navigation;
        private readonly AlienSurfaceNavigation.Route m_Route = new();
        public bool AmbushEnabled => m_AmbushEnabled;
        public string NavigationFailure => m_Route.LastFailure;
        public Vector3 RepositionGoal => m_RepositionGoal;

        private void UpdateCombatPresentation(float health) {
            m_Body.SetCombatPresentation(m_Action, m_Clock - m_ActionStarted, m_Attacks, health,
                m_AmbushEnabled ? profile.SpiderWindup : Mathf.Max(.1f, m_Pattern.Windup),
                m_AmbushEnabled ? profile.SpiderRecovery : Mathf.Max(.1f, m_Pattern.Recovery),
                m_AmbushEnabled ? profile.SpiderStrikeSeconds : Mathf.Max(.1f, m_Pattern.Active), m_Pattern.Kind);
        }

        private void UpdateAmbush(float dt, float health) {
            if (!m_Navigation) m_Navigation = AlienSurfaceNavigation.Find(m_Spawn);
            bool hurt = health < m_LastHealth - .01f;
            m_LastHealth = health;
            var manager = GhostGameObject.EntityManager;
            // 伏击等待时定时寻找目标，已经蓄力或出手时不重选，攻击意图可以预期。
            if (m_Clock >= m_NextSense && m_Action != AlienAction.Windup && m_Action != AlienAction.Strike) {
                Sense();
                m_NextSense = m_Clock + .15f;
            }
            bool living = IsLivingHumanoid(manager, m_Target);
            if (!living) {
                m_Target = Entity.Null;
                m_Visible = false;
            }
            bool remembered = living && m_Clock - m_LastSeenAt <= profile.MemorySeconds;
            // 这类伏击有实际可走的导航边界。
            // 不追到区域外的墙后，也不读取看不见的对象的精确位置。
            // 伏击有活动空间边界，线索跑出这一块可导航区域后不继续隔墙追精确坐标。
            if (remembered && m_Navigation && !m_Navigation.Contains(m_LastSeen)) remembered = false;
            float age = m_Clock - m_ActionStarted;
            float distance = living && m_Visible ? Vector3.Distance(AimPoint, ContactPoint(m_Target)) : float.MaxValue;
            if ((m_Action == AlienAction.Windup || m_Action == AlienAction.Strike) && !living) {
                ChangeAction(AlienAction.Recover);
            } else if (m_Action == AlienAction.Windup && age >= Mathf.Max(.3f, profile.SpiderWindup)) {
                ChangeAction(AlienAction.Strike);
                m_StrikeHit = false;
                m_StrikeOrigin = m_Body.Motor.Current.Position;
                // 用固定飞行时长和预警时记的落点反算起飞速度，再交给身体马达处理真实飞行。
                float flightTime = Mathf.Max(.2f, profile.SpiderStrikeSeconds);
                var velocity = (m_StrikePoint - m_StrikeOrigin) / flightTime + Vector3.up * (10 * flightTime);
                m_Body.Launch(Vector3.ClampMagnitude(velocity, 18));
            } else if (m_Action == AlienAction.Strike && age >= Mathf.Max(.2f, profile.SpiderStrikeSeconds)) {
                ChangeAction(AlienAction.Recover);
            // 收招要等计时结束并重新有表面接触，再开始找新的挂点。
            } else if (m_Action == AlienAction.Recover && age >= Mathf.Max(.3f, profile.SpiderRecovery) &&
                       !m_Body.Motor.Detached) { BeginReposition(remembered ? m_LastSeen : m_Spawn); } else if
                (m_Action != AlienAction.Windup && m_Action != AlienAction.Strike && m_Action != AlienAction.Recover) {
                if (m_Action == AlienAction.Reposition && (m_Body.Motor.Current.Up.y > -.65f ||
                                                           Vector3.Distance(transform.position, m_RepositionGoal) >
                                                           .8f)) {
                    if (m_Clock >= m_RepositionUntil) BeginReposition(remembered ? m_LastSeen : m_Spawn);
                } else if (remembered && m_Visible && distance <= profile.SpiderAmbushRange &&
                           m_Clock >= m_NextAttack) {
                    // 蓄力预警开始时固定扑击落点。
                    // 玩家可以躲开，飞出去后不会转向玩家更新后的位置。
                    // 蓄力开始就记住扑击落点。之后玩家可以跑开，蜘蛛起跳时仍飞向原来的点。
                    m_StrikePoint = ContactPoint(m_Target);
                    m_Pattern = AlienCombatPattern.For(profile, 1, m_Attacks + 1);
                    m_CombatPoint = m_StrikePoint;
                    m_CombatOrigin = transform.position;
                    m_CombatDirection = (m_StrikePoint - transform.position).normalized;
                    ChangeAction(AlienAction.Windup);
                    m_Attacks++;
                    m_NextAttack = m_Clock + Mathf.Max(profile.AttackCooldown,
                        profile.SpiderWindup + profile.SpiderStrikeSeconds + profile.SpiderRecovery + .6f);
                } else if (hurt && remembered) { BeginReposition(m_LastSeen); } else if (remembered) {
                    ChangeAction(m_Visible ? AlienAction.Chase : AlienAction.Search);
                } else if (Vector3.Distance(transform.position, m_Spawn) > .6f) {
                    m_RepositionGoal = m_Spawn;
                    ChangeAction(AlienAction.Reposition);
                } else { ChangeAction(AlienAction.Ambush); }
            }
            var intent = Vector3.zero;
            float speed = 0, yaw = 0;
            if (m_Action == AlienAction.Chase || m_Action == AlienAction.Search || m_Action == AlienAction.Reposition) {
                var destination = m_Action == AlienAction.Reposition ? m_RepositionGoal : m_LastSeen;
                // 追击、搜索和换位都拿表面路的下一点。没有路线时不沿直线硬闯。
                if (m_Navigation &&
                    m_Navigation.TryGetSteering(m_Route, m_Body.Motor, destination, m_Clock, out var waypoint)) {
                    intent = Vector3.ProjectOnPlane(waypoint - m_Body.Motor.Current.Position, m_Body.Motor.Current.Up)
                        .normalized;
                    speed = profile.Speed * m_Body.Scale * (m_Action == AlienAction.Search ? .55f : .85f);
                    yaw = Mathf.Clamp(
                        Vector3.SignedAngle(m_Body.Motor.Current.Forward, intent, m_Body.Motor.Current.Up) /
                        Mathf.Max(.001f, dt), -profile.TurnSpeed, profile.TurnSpeed);
                }
                // 门或墙挡住必要的路线时停止移动，不沿目标方向硬闯。
            }
            UpdateCombatPresentation(health);
            m_Body.Tick(intent, speed, yaw, dt, ActionPulse(), false);
            if (m_Action == AlienAction.Strike && !m_StrikeHit && m_Clock - m_ActionStarted >= .06f && living) {
                var committed = m_StrikePoint - m_StrikeOrigin;
                var current = ContactPoint(m_Target) - m_StrikeOrigin;
                // 玩家已经离开原来的扑击扇区就不给碰撞补偿，留有真实闪避空间。
                if (Vector3.Angle(committed, current) < 50) {
                    uint before = m_Hits;
                    TryStrike();
                    m_StrikeHit = m_Hits != before;
                }
            }
        }

        // 优先挑连得上的天花板挂点。找不到就回出生挂点，过一小段时间再重查。
        private void BeginReposition(Vector3 target) {
            m_RepositionGoal = m_Spawn;
            if (m_Navigation && m_Clock >= m_NextPerch) {
                if (m_Navigation.TryFindPerch(transform.position, target, 12, out var perch, m_Body.Scale))
                    m_RepositionGoal = perch;
                m_NextPerch = m_Clock + 2;
            }
            m_RepositionUntil = m_Clock + 12;
            ChangeAction(AlienAction.Reposition);
        }
    }
}