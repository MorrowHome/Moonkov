using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private AlienCombatPattern m_Pattern;
        private Vector3 m_CombatPoint, m_CombatOrigin, m_CombatDirection;
        private byte m_BossPhase = 1;
        private int m_Pulse;
        private Entity m_AttachedTarget;
        private readonly HashSet<Entity> m_StrikeVictims = new HashSet<Entity>();
        private AlienAttackPresentation m_AttackPresentation;
        public AlienAttackKind AttackKind => m_Pattern.Kind;
        public byte BossPhase => m_BossPhase;
        public Entity AttachedTarget => m_AttachedTarget;

        private void UpdateSpeciesCombat(float dt, PredictedPlayerGhost health)
        {
            var manager = GhostGameObject.EntityManager;
            TickMucusProjectile();
            if (m_Action == AlienAction.Attached) { TickFaceAttachment(dt, health.CurrentHealth); return; }
            if (m_Clock >= m_NextSense && m_Action != AlienAction.Windup && m_Action != AlienAction.Strike)
            { Sense(); m_NextSense = m_Clock + .2f; }
            bool living = IsLivingHumanoid(manager, m_Target);
            if (!living) { m_Target = Entity.Null; m_Visible = false; }
            bool remembered = living && m_Clock - m_LastSeenAt <= profile.MemorySeconds;
            if (TickBossDisplay(dt, health, remembered)) return;
            if (TickBossMobility(dt, health, remembered)) return;
            float age = m_Clock - m_ActionStarted;
            if ((m_Action == AlienAction.Windup || m_Action == AlienAction.Strike) && !living)
                ChangeAction(AlienAction.Recover);
            else if (m_Action == AlienAction.Windup && age >= m_Pattern.Windup)
            {
                ChangeAction(AlienAction.Strike); m_Pulse = 0; m_StrikeVictims.Clear();
                if (m_Pattern.Kind == AlienAttackKind.MucusSpit) StartMucusProjectile();
                if (m_Pattern.Kind == AlienAttackKind.FaceLeap)
                {
                    Vector3 velocity = (m_CombatPoint - transform.position) / m_Pattern.Active + Vector3.up * (10 * m_Pattern.Active);
                    m_Body.Launch(Vector3.ClampMagnitude(velocity, 16));
                }
            }
            else if (m_Action == AlienAction.Strike && age >= m_Pattern.Active)
                ChangeAction(AlienAction.Recover);
            else if (m_Action == AlienAction.Recover && age >= m_Pattern.Recovery && !m_Body.Motor.Detached)
                ChangeAction(remembered ? AlienAction.Chase : AlienAction.Roam);
            else if (m_Action != AlienAction.Windup && m_Action != AlienAction.Strike && m_Action != AlienAction.Recover)
            {
                byte phase = profile.Boss && health.CurrentHealth <= health.MaxHealth * profile.EnrageHealthFraction ? (byte)2 : (byte)1;
                var next = AlienCombatPattern.For(profile, phase, m_Attacks + 1);
                // A short-range sequence must not prevent an otherwise visible ranged response.
                if (profile.Boss && remembered && m_Visible && m_Clock >= m_NextAttack && !CanBeginSpeciesAttack(next))
                    next = AlienCombatPattern.For(profile, phase, m_Attacks + 1, AlienAttackKind.MucusSpit);
                if (remembered && m_Visible && m_Clock >= m_NextAttack && CanBeginSpeciesAttack(next) &&
                    (next.Kind != AlienAttackKind.MucusSpit || PrepareMucusArc()))
                {
                    m_Pattern = next; m_BossPhase = phase; m_Attacks++;
                    bool headShot = m_Pattern.Kind == AlienAttackKind.FaceLeap || m_Pattern.Kind == AlienAttackKind.MucusSpit;
                    m_CombatOrigin = m_Pattern.Kind == AlienAttackKind.MucusSpit ? MucusSocket() : transform.position;
                    m_CombatPoint = headShot ? TargetHead(m_Target).position : m_LastSeen - Vector3.up;
                    if (!headShot && UnityEngine.Physics.Raycast(m_CombatPoint + Vector3.up * 2, Vector3.down,
                        out var floor, 6, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) m_CombatPoint = floor.point + floor.normal * .04f;
                    m_CombatDirection = Vector3.ProjectOnPlane(m_CombatPoint - transform.position, Vector3.up).normalized;
                    if (m_CombatDirection.sqrMagnitude < .01f) m_CombatDirection = transform.forward;
                    ChangeAction(AlienAction.Windup);
                    m_NextAttack = m_Clock + Mathf.Max(profile.AttackCooldown, next.Windup + next.Active + next.Recovery + .3f);
                }
                else ChangeAction(remembered ? (m_Visible ? AlienAction.Chase : AlienAction.Search) : AlienAction.Roam);
            }

            Vector3 intent = Vector3.zero;
            float speed = 0;
            if (m_Action == AlienAction.Strike && m_Pattern.Speed > 0)
            { intent = Vector3.ProjectOnPlane(m_CombatDirection, m_Body.Motor.Current.Up).normalized; speed = m_Pattern.Speed; }
            else if ((m_Action == AlienAction.Chase || m_Action == AlienAction.Search || m_Action == AlienAction.Roam) && (profile.Species != AlienSpecies.Roots || profile.Boss))
            {
                bool pause = false;
                Vector3 goal = remembered ? HuntGoal(m_Visible ? m_LastSeen : SearchGoal(), out pause) : PatrolGoal();
                if (profile.IsUpright || profile.Boss) goal = UprightWaypoint(goal);
                var graph = profile.IsUpright || profile.Boss ? null : AlienSurfaceNavigation.Find(m_Spawn);
                if (graph && graph.Contains(goal))
                {
                    if (graph.TryGetSteering(m_Route, m_Body.Motor, goal, m_Clock, out var waypoint)) goal = waypoint;
                    else goal = transform.position;
                }
                intent = Vector3.ProjectOnPlane(goal - transform.position, m_Body.Motor.Current.Up).normalized;
                speed = profile.Speed * m_Body.Scale * (m_Action == AlienAction.Roam ? .35f : .8f);
                if (profile.Boss && m_Action != AlienAction.Roam) speed = profile.BossPursuitSpeed * (m_BossPhase > 1 ? 1.15f : 1);
                if (pause || Vector3.Distance(goal, transform.position) < .7f) speed = 0;
            }
            Vector3 facingIntent = m_Action == AlienAction.Windup ? m_CombatDirection :
                remembered && m_Visible && (profile.Boss || speed <= .1f) ?
                    Vector3.ProjectOnPlane(m_LastSeen - transform.position, Vector3.up).normalized : intent;
            float yaw = facingIntent.sqrMagnitude > .01f ? Mathf.Clamp(Vector3.SignedAngle(m_Body.Motor.Current.Forward, facingIntent, m_Body.Motor.Current.Up) /
                Mathf.Max(.001f, dt), -profile.TurnSpeed, profile.TurnSpeed) : 0;
            Vector3 previous = transform.position;
            UpdateCombatPresentation(health.CurrentHealth);
            m_Body.Tick(intent, speed, yaw, dt, CombatPulse(), false);
            ObserveHuntMovement(previous, speed, dt);
            if (m_Action == AlienAction.Strike) ResolveSpeciesAttack(previous);
        }

        private bool CanBeginSpeciesAttack(AlienCombatPattern next)
        {
            Vector3 target = ContactPoint(m_Target);
            if (Vector3.Distance(AimPoint, target) > next.Range) return false;
            var root = GhostGameObject.EntityManager.GetComponentObject<GhostGameObjectLink>(m_Target).LinkedInstance;
            if (!ClearSight(AimPoint, target, root.transform)) return false;
            if (next.Kind == AlienAttackKind.TentacleSweep && !AlienCombatPattern.SweepContains(target,
                transform.position, Vector3.ProjectOnPlane(target - transform.position, Vector3.up).normalized,
                next.Radius, m_Body.Motor.BodyHeight)) return false;
            // A heavy mantle cannot warn forward and then sweep behind itself.
            // Roots erupt remotely and do not require a forward-facing melee arc.
            return !profile.Boss || profile.Species == AlienSpecies.Roots ||
                Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(target - transform.position, Vector3.up)) <= 60;
        }

        private float CombatPulse() => m_Action == AlienAction.Windup ? Mathf.Clamp01((m_Clock - m_ActionStarted) / Mathf.Max(.01f, m_Pattern.Windup)) :
            m_Action == AlienAction.Strike || m_Action == AlienAction.Attached ? 1 :
            m_Action == AlienAction.Recover ? 1 - Mathf.Clamp01((m_Clock - m_ActionStarted) / Mathf.Max(.01f, m_Pattern.Recovery)) : 0;

        private void ResolveSpeciesAttack(Vector3 previous)
        {
            if (m_Pattern.Kind == AlienAttackKind.MucusSpit) return;
            float age = m_Clock - m_ActionStarted;
            bool moving = m_Pattern.Kind == AlienAttackKind.Bite || m_Pattern.Kind == AlienAttackKind.Charge || m_Pattern.Kind == AlienAttackKind.FaceLeap;
            if (!moving && (m_Pulse >= m_Pattern.Pulses || age < m_Pattern.PulseTime(m_Pulse))) return;
            if (!moving) { m_Pulse++; m_StrikeVictims.Clear(); }
            var manager = GhostGameObject.EntityManager;
            using var query = manager.CreateEntityQuery(typeof(PredictedPlayerGhost), typeof(LocalTransform), typeof(GhostGameObjectLink));
            using var entities = query.ToEntityArray(Allocator.Temp);
            UnityEngine.Physics.SyncTransforms();
            foreach (var entity in entities)
            {
                if (!IsLivingHumanoid(manager, entity) || m_StrikeVictims.Contains(entity)) continue;
                Vector3 point = ContactPoint(entity), centre = m_CombatPoint;
                Vector3 sightOrigin = AimPoint;
                bool hit = false;
                switch (m_Pattern.Kind)
                {
                    case AlienAttackKind.StiltRake:
                        Vector3 rakeEnd = m_CombatOrigin + m_CombatDirection * m_Pattern.Range;
                        hit = DistanceToSegment(Vector3.ProjectOnPlane(point, Vector3.up),
                            Vector3.ProjectOnPlane(m_CombatOrigin, Vector3.up), Vector3.ProjectOnPlane(rakeEnd, Vector3.up)) <= m_Pattern.Radius &&
                            Vector3.Dot(point - m_CombatOrigin, m_CombatDirection) >= 0 &&
                            Mathf.Abs(point.y - m_CombatOrigin.y) <= profile.UprightHeight(m_Body.Scale);
                        break;
                    case AlienAttackKind.LeechDrain:
                        hit = entity == m_Target && Vector3.Distance(AimPoint, point) <= m_Pattern.Radius + .35f; break;
                    case AlienAttackKind.Bite:
                    case AlienAttackKind.Charge:
                        hit = DistanceToSegment(point, previous, transform.position) <= m_Pattern.Radius + .35f; break;
                    case AlienAttackKind.FaceLeap:
                        point = TargetHead(entity).position;
                        hit = DistanceToSegment(point, previous, transform.position) <= m_Pattern.Radius; break;
                    case AlienAttackKind.TentacleSweep:
                        hit = AlienCombatPattern.SweepContains(point, m_CombatOrigin, m_CombatDirection,
                            m_Pattern.Radius, m_Body.Motor.BodyHeight); break;
                    case AlienAttackKind.SlimePulse:
                        centre = m_CombatOrigin; sightOrigin = centre + Vector3.up * .25f;
                        hit = GroundDistance(point, centre) <= m_Pattern.Radius && Mathf.Abs(point.y - centre.y) < 2; break;
                    case AlienAttackKind.RootSpikes:
                        // Three marked patches leave open lanes between the eruptions.
                        Vector3 right = Vector3.Cross(Vector3.up, m_CombatDirection);
                        hit = GroundDistance(point, centre) <= m_Pattern.Radius ||
                            GroundDistance(point, centre + right * (m_Pattern.Radius * 2.7f)) <= m_Pattern.Radius ||
                            GroundDistance(point, centre - right * (m_Pattern.Radius * 2.7f)) <= m_Pattern.Radius;
                        hit &= Mathf.Abs(point.y - centre.y) < 2;
                        Vector3 positive = centre + right * (m_Pattern.Radius * 2.7f), negative = centre - right * (m_Pattern.Radius * 2.7f);
                        Vector3 nearest = GroundDistance(point, positive) < GroundDistance(point, centre) ? positive : centre;
                        if (GroundDistance(point, negative) < GroundDistance(point, nearest)) nearest = negative;
                        sightOrigin = nearest + Vector3.up * .2f; break;
                    default:
                        sightOrigin = centre + Vector3.up * .25f;
                        hit = GroundDistance(point, centre) <= m_Pattern.Radius && Mathf.Abs(point.y - centre.y) < 2; break;
                }
                var root = manager.GetComponentObject<GhostGameObjectLink>(entity).LinkedInstance;
                if (!hit || !ClearSight(sightOrigin, point, root.transform)) continue;
                // Remote patches cannot damage a player behind a wall or on a hidden floor.
                if (profile.Boss && !ClearSight(m_CombatOrigin, point, root.transform)) continue;
                m_StrikeVictims.Add(entity);
                if (m_Pattern.Kind == AlienAttackKind.FaceLeap)
                { AttachFace(entity); break; }
                float drained = ApplyAlienDamage(entity, profile.Damage * m_Pattern.DamageScale, BodyPart.Chest);
                if (m_Pattern.Kind == AlienAttackKind.LeechDrain && drained > 0)
                {
                    var own = ReadGhostComponentData<PredictedPlayerGhost>();
                    own.CurrentHealth = Mathf.Min(own.MaxHealth, own.CurrentHealth + drained * .5f);
                    manager.SetComponentData(GhostGameObject.LinkedEntity, own);
                }
            }
        }

        private float ApplyAlienDamage(Entity target, float amount, BodyPart part)
        {
            var manager = GhostGameObject.EntityManager;
            if (!IsLivingHumanoid(manager, target)) return 0;
            var health = manager.GetComponentData<PredictedPlayerGhost>(target);
            float damaged = RaidHealth.CombatDamage(ref health, part, amount, GhostGameObject.GetCurrentTick(), RaidDamageKind.Melee);
            manager.SetComponentData(target, health);
            if (damaged > 0) m_Hits++;
            if (health.CurrentHealth <= 0 && manager.HasComponent<GhostOwner>(target))
                LeaderboardManager.Instance?.AddKill(GhostGameObject.Owner, manager.GetComponentData<GhostOwner>(target).NetworkId);
            return damaged;
        }

        private Pose TargetHead(Entity entity)
        {
            var manager = GhostGameObject.EntityManager;
            var pose = manager.GetComponentData<LocalTransform>(entity);
            Quaternion rotation = pose.Rotation;
            float height = 1.6f;
            var player = manager.GetComponentObject<GhostGameObjectLink>(entity).LinkedInstance.GetComponent<PlayerGhost>();
            if (player && player.Controller && player.Controller.CharacterController)
                height = Mathf.Max(.6f, player.Controller.CharacterController.height - .15f);
            return new Pose((Vector3)pose.Position + Vector3.up * height + rotation * Vector3.forward * .12f, rotation);
        }

        private void AttachFace(Entity target)
        {
            // One face parasite per victim; identity is a remapped Ghost Entity.
            var manager = GhostGameObject.EntityManager;
            using var query = manager.CreateEntityQuery(typeof(State));
            using var states = query.ToComponentDataArray<State>(Allocator.Temp);
            foreach (var state in states) if (state.AttachedTarget == target) return;
            m_AttachedTarget = target; m_Pulse = 0;
            ChangeAction(AlienAction.Attached);
        }

        private void TickFaceAttachment(float dt, float health)
        {
            var manager = GhostGameObject.EntityManager;
            if (!IsLivingHumanoid(manager, m_AttachedTarget) || m_Clock - m_ActionStarted >= profile.FaceAttachSeconds)
            { ReleaseFace(); return; }
            var head = TargetHead(m_AttachedTarget);
            int mask = LayerMask.GetMask("Default", "Ground");
            if (UnityEngine.Physics.CheckSphere(head.position, .14f, mask, QueryTriggerInteraction.Ignore))
            { ReleaseFace(); return; }
            m_Body.SetAttachedPose(head.position, head.rotation);
            UpdateCombatPresentation(health);
            m_Body.Tick(Vector3.zero, 0, 0, dt, 1, false);
            if (m_Clock - m_ActionStarted >= m_Pulse * .8f)
            { m_Pulse++; ApplyAlienDamage(m_AttachedTarget, profile.Damage / 3, BodyPart.Head); }
        }

        private void ReleaseFace()
        {
            m_AttachedTarget = Entity.Null;
            m_Body.Launch(Vector3.up * 2 - transform.forward * 3);
            ChangeAction(AlienAction.Recover);
        }

        private void ObserveSpeciesAttack(State state)
        {
            m_BossPhase = state.BossPhase;
            m_Pattern = AlienCombatPattern.For(profile, state.BossPhase, state.AttackSequence, state.AttackKind);
            m_AttachedTarget = state.AttachedTarget;
            if (Role == MultiplayerRole.Server) return;
            m_AttackPresentation ??= new AlienAttackPresentation(transform, profile);
            m_AttackPresentation.Observe(state, m_Pattern, m_Body.AttackSocket);
        }

        private static float GroundDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 delta = b - a;
            return Vector3.Distance(point, a + delta * Mathf.Clamp01(Vector3.Dot(point - a, delta) / Mathf.Max(.0001f, delta.sqrMagnitude)));
        }
    }
}
