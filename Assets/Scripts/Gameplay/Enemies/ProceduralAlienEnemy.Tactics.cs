// 异形选招的地方。按可见目标、距离、种族、身位和独立冷却挑一个能落地的攻击。
// CommitAttack 固定预警范围、方向与阶段，后面按 Combat 中的时间轴执行。
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed partial class ProceduralAlienEnemy {
        private Entity m_MotionTarget;
        private Vector3 m_ObservedTargetPosition, m_ObservedTargetVelocity;
        private float m_TargetSampleAt = -100;
        private float m_NextBossSweep, m_NextBossArea, m_NextBossSpit;

        // 只从确认可见的感知结果估算位移，不读藏起来的目标位置。
        private void ObserveTargetMotion() {
            float elapsed = m_Clock - m_TargetSampleAt;
            // 只用真正看见的连续采样估算目标速度，失去视线立即丢掉跟踪速度。
            if (!m_Visible) {
                m_MotionTarget = Entity.Null;
                m_ObservedTargetVelocity = Vector3.zero;
                return;
            }
            if (m_MotionTarget == m_Target && elapsed >= .1f && elapsed <= .6f) {
                var velocity = Vector3.ProjectOnPlane((m_LastSeen - m_ObservedTargetPosition) / elapsed, Vector3.up);
                m_ObservedTargetVelocity = velocity.magnitude <= 12
                    ? Vector3.Lerp(m_ObservedTargetVelocity, velocity, .65f)
                    : Vector3.zero;
            } else { m_ObservedTargetVelocity = Vector3.zero; }
            m_MotionTarget = m_Target;
            m_ObservedTargetPosition = m_LastSeen;
            m_TargetSampleAt = m_Clock;
        }

        private bool TryChooseAttack(byte phase, out AlienCombatPattern next) {
            next = AlienCombatPattern.For(profile, phase, m_Attacks + 1);
            // 普通怪主要按物种轮换招法。当前距离让普通招式碰不到时，部分种类再挑喷吐。
            if (!profile.Boss) {
                // 脉冲超出实际半径时跳过，免得黏液怪一直往空处出招。
                if (profile.Species == AlienSpecies.Slime && !CanBeginSpeciesAttack(next) &&
                    profile.MucusEveryAttacks > 0)
                    next = AlienCombatPattern.For(profile, phase, m_Attacks + 1, AlienAttackKind.MucusSpit);
                return CanBeginSpeciesAttack(next);
            }
            // 巨蛛先试近身扫击。目标离得远且路径合法时才考虑突进或按情况用远程喷吐。
            if (profile.IsSpiderBoss) {
                if (TryBossPattern(AlienAttackKind.SpiderRake, phase, m_NextBossSweep, out next)) return true;
                var target = ContactPoint(m_Target);
                float distance = GroundDistance(target, transform.position);
                bool outsideMelee = distance > profile.AttackReach * m_Body.Scale;
                if (outsideMelee && TryBossPattern(AlienAttackKind.SpiderRush, phase, m_NextBossArea, out next))
                    return true;
                // 先尝试一次远程起手，再沿地面路线接近。
                // 路线堵住或目标在不同楼层时仍能选择远程攻击。
                bool canSpit = !GroundStrikeLevel(target) || (outsideMelee && (m_Clock < m_BlockedGroundUntil ||
                                                                               (m_Attacks == 0 &&
                                                                                   distance > profile
                                                                                       .SpiderAmbushRange)));
                return canSpit && TryBossPattern(AlienAttackKind.MucusSpit, phase, m_NextBossSpit, out next);
            } else if (profile.Species == AlienSpecies.Roots) {
                if (TryBossPattern(AlienAttackKind.SlimePulse, phase, m_NextBossSweep, out next)) return true;
                if (TryBossPattern(AlienAttackKind.RootSpikes, phase, m_NextBossArea, out next)) return true;
            } else {
                if (TryBossPattern(AlienAttackKind.TentacleSweep, phase, m_NextBossSweep, out next)) return true;
                if (TryBossPattern(AlienAttackKind.TentacleSlam, phase, m_NextBossArea, out next)) return true;
            }
            return TryBossPattern(AlienAttackKind.MucusSpit, phase, m_NextBossSpit, out next);
        }

        // 每种 Boss 招式保留自己的恢复时间和空间要求，不能用另一个招式的剩余间隔算它。
        private bool TryBossPattern(AlienAttackKind kind, byte phase, float readyAt, out AlienCombatPattern pattern) {
            pattern = AlienCombatPattern.For(profile, phase, m_Attacks + 1, kind);
            if (m_Clock < readyAt) return false;
            // 地面攻击不能锁定站在高处玩家下方、实际打不到人的另一层地板。
            if (kind != AlienAttackKind.MucusSpit && !GroundStrikeLevel(ContactPoint(m_Target))) return false;
            return CanBeginSpeciesAttack(pattern) && (kind != AlienAttackKind.SpiderRush ||
                                                      TrySpiderRushLanding(transform.position, m_LastSeen - Vector3.up,
                                                          out _));
        }

        private bool GroundStrikeLevel(Vector3 target) {
            return Mathf.Abs(target.y - (transform.position.y - m_Body.Motor.BodyHeight + 1)) <= 1.5f;
        }

        // 突进前沿真实体型扫线路并检查落点与沿途地面，确保出招不会直冲墙或悬崖。
        private bool TrySpiderRushLanding(Vector3 origin, Vector3 target, out Vector3 landing) {
            landing = default;
            var direction = Vector3.ProjectOnPlane(target - origin, Vector3.up).normalized;
            float distance = GroundDistance(target, origin);
            if (distance > profile.SpiderAmbushRange || distance < .1f) return false;
            var foot = origin - Vector3.up * m_Body.Motor.BodyHeight;
            float radius = profile.GroundBodyRadius(m_Body.Scale), height = profile.GroundBodyHeight(m_Body.Scale);
            int mask = LayerMask.GetMask("Default", "Ground");
            // 选择突进前排除被堵的线路，留机会换别的合法技能。
            // 贴地运动真正走起来时还会检查移动的障碍物。
            if (UnityEngine.Physics.CapsuleCast(foot + Vector3.up * (radius + .04f),
                    foot + Vector3.up * (height - radius + .04f), radius, direction, distance, mask,
                    QueryTriggerInteraction.Ignore))
                return false;
            if (!RaidSpawnSafety.TryGroundPosition(target, radius, height, 35, out landing, out _) ||
                Mathf.Abs(landing.y - foot.y) > .4f)
                return false;
            // 用有限采样探地，避免答应跨过地面缺口做突进。
            for (int i = 1; i <= 4; i++)
                if (!UnityEngine.Physics.Raycast(Vector3.Lerp(foot, landing, i / 5f) + Vector3.up * .45f, Vector3.down,
                        out var floor, .9f, mask, QueryTriggerInteraction.Ignore) || floor.normal.y < .75f)
                    return false;
            return true;
        }

        // 先准备攻击特有的参数，再写决定好的目标、朝向与冷却。之后预警与命中依据同一组值。
        private bool CommitAttack(AlienCombatPattern next, byte phase) {
            bool head = next.Kind == AlienAttackKind.FaceLeap || next.Kind == AlienAttackKind.MucusSpit;
            var target = head ? TargetHead(m_Target).position : m_LastSeen - Vector3.up;
            var origin = next.Kind == AlienAttackKind.MucusSpit ? MucusSocket() : transform.position;
            bool aimed = next.Kind == AlienAttackKind.RootSpikes || next.Kind == AlienAttackKind.TentacleSlam ||
                         next.Kind == AlienAttackKind.AcidSpit || next.Kind == AlienAttackKind.MucusSpit ||
                         next.Kind == AlienAttackKind.Charge || next.Kind == AlienAttackKind.Bite ||
                         next.Kind == AlienAttackKind.FaceLeap;
            if (aimed && m_MotionTarget == m_Target && m_Visible && m_Clock - m_TargetSampleAt <= .6f) {
                float leadTime = next.Kind == AlienAttackKind.MucusSpit
                    ? Mathf.Min(.7f,
                        next.Windup * .45f + AlienMucusTrajectory.FlightTime(origin, target, profile.MucusSpeed) * .5f)
                    : Mathf.Min(.6f, next.Windup * .45f);
                var proposed = target +
                               Vector3.ClampMagnitude(m_ObservedTargetVelocity * leadTime, profile.Boss ? 3 : 1.2f);
                int mask = LayerMask.GetMask("Default", "Ground");
                // 预判瞄准的路线要考虑墙，目标还没越过墙时不要把落点算到另一边。
                if (!UnityEngine.Physics.Linecast(target + (head ? Vector3.zero : Vector3.up),
                        proposed + (head ? Vector3.zero : Vector3.up), mask, QueryTriggerInteraction.Ignore))
                    target = proposed;
            }
            if (next.Kind == AlienAttackKind.MucusSpit && !PrepareMucusArc(target)) return false;
            if (!head && UnityEngine.Physics.Raycast(target + Vector3.up * 2, Vector3.down, out var floor, 6,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore))
                target = floor.point + floor.normal * .04f;
            if (next.Kind == AlienAttackKind.SpiderRush && !TrySpiderRushLanding(origin, target, out target))
                return false;
            // 选定招式后同步阶段和新的攻击号。Boss 之后血量变化也不会改变已经提示给玩家的攻击。
            m_Pattern = next;
            m_BossPhase = phase;
            m_Attacks++;
            m_CombatOrigin = origin;
            m_CombatPoint = target;
            m_CombatDirection = Vector3.ProjectOnPlane(target - transform.position, Vector3.up).normalized;
            if (next.Kind == AlienAttackKind.RootSpikes && m_ObservedTargetVelocity.sqrMagnitude > 1 &&
                m_MotionTarget == m_Target)
                m_CombatDirection = m_ObservedTargetVelocity.normalized;
            if (m_CombatDirection.sqrMagnitude < .01f) m_CombatDirection = transform.forward;
            ChangeAction(AlienAction.Windup);
            float cycle = next.Windup + next.Active + next.Recovery + .3f;
            m_NextAttack = m_Clock + Mathf.Max(profile.AttackCooldown, cycle);
            if (profile.Boss) {
                // 每个技能分别记恢复时间。无法出手时，
                // 改用允许的另一招或走位，重复尝试不会跳过冷却。
                float ready = m_Clock + cycle +
                              (next.Kind == AlienAttackKind.SpiderRake ? .45f : phase > 1 ? 1.2f : 2.5f);
                if (next.Kind == AlienAttackKind.MucusSpit)
                    m_NextBossSpit = ready;
                else if (next.Kind == AlienAttackKind.TentacleSweep || next.Kind == AlienAttackKind.SlimePulse ||
                         next.Kind == AlienAttackKind.SpiderRake)
                    m_NextBossSweep = ready;
                else
                    m_NextBossArea = ready;
            }
            return true;
        }
    }
}