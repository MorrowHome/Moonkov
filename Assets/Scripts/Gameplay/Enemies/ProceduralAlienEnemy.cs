using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS.SpiderLab;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public enum AlienAction : byte { Roam, Chase, Search, Windup, Strike, Recover, Dead }

    /// <summary>Server-only intent, perception and damage; interpolated clients derive
    /// cosmetic rigs from snapshots. Target eligibility requires a real PlayerGhost:
    /// humans and PMC bots are eligible, other aliens never are.</summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralAlienEnemy : GhostMonoBehaviour, IUpdateServer, IUpdateClient
    {
        public struct State : IComponentData
        {
            [GhostField] public AlienAction Action;
            [GhostField(Quantization = 1000)] public float Scale;
            [GhostField(Quantization = 100)] public float Phase;
            [GhostField(Quantization = 100)] public float ActionTime;
            [GhostField(Quantization = 1000)] public float3 Velocity;
            [GhostField] public uint AttackSequence;
            [GhostField] public bool Detached;
        }
        [SerializeField] private AlienCreatureProfile profile;
        private AlienCreatureBody m_Body;
        private Entity m_Target;
        private Vector3 m_Spawn, m_LastSeen, m_RoamGoal;
        private float m_Clock, m_NextSense, m_LastSeenAt = -100, m_ActionStarted, m_NextAttack, m_NextPatrol, m_Stuck;
        private AlienAction m_Action;
        private bool m_Visible;
        private uint m_Attacks, m_Hits;
        private readonly RaycastHit[] m_HitsBuffer = new RaycastHit[32];
        private readonly Dictionary<Entity, uint> m_ObservedShots = new Dictionary<Entity, uint>();
        public override bool AllowAutoRegisterForUpdates() => Application.isPlaying;
        private static readonly List<ProceduralAlienEnemy> s_ClientAliens = new List<ProceduralAlienEnemy>();
        private float m_NextLod;
        private bool m_Liquid;
        public AlienCreatureProfile Profile => profile;
        public AlienCreatureBody Body => m_Body;
        public Vector3 AimPoint => m_Body != null ? m_Body.AimPoint : transform.position;
        public Entity Target => m_Target;
        public AlienAction Action => m_Action;
        public uint AttackCount => m_Attacks;
        public uint DamageCount => m_Hits;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_ClientAliens.Clear(); }

        public override void OnGhostLinked()
        {
            if (!profile) { Debug.LogError("Alien profile is missing.", this); return; }
            var state = ReadGhostComponentData<State>();
            m_Spawn = transform.position; m_RoamGoal = m_Spawn;
            float scale = state.Scale > 0 ? state.Scale : profile.Scale;
            m_Body = new AlienCreatureBody(transform, profile, scale, Role != MultiplayerRole.Server,
                LayerMask.NameToLayer(Role == MultiplayerRole.Server ? "ServerPlayer" : "ClientPlayer"));
            if (Role != MultiplayerRole.Server) s_ClientAliens.Add(this);
        }
        public static bool IsLivingHumanoid(EntityManager manager, Entity entity)
        {
            if (entity == Entity.Null || !manager.Exists(entity) || manager.HasComponent<State>(entity) ||
                !manager.HasComponent<PredictedPlayerGhost>(entity) || !manager.HasComponent<GhostGameObjectLink>(entity) ||
                manager.GetComponentData<PredictedPlayerGhost>(entity).CurrentHealth <= 0) return false;
            var link = manager.GetComponentObject<GhostGameObjectLink>(entity);
            return link.LinkedInstance && link.LinkedInstance.GetComponent<PlayerGhost>();
        }

        public void UpdateServer(float deltaTime)
        {
            // Destruction can retain a cleanup entity for one frame. The callback
            // registration is removed by GhostBridge cleanup; never read stripped data.
            if (m_Body == null || !GhostGameObject.GhostEntityExists() || !GhostHasComponent<PredictedPlayerGhost>()) return;
            float dt = Mathf.Clamp(deltaTime, 0, .06f); m_Clock += dt;
            var manager = GhostGameObject.EntityManager;
            var health = ReadGhostComponentData<PredictedPlayerGhost>();
            if (health.CurrentHealth <= 0)
            {
                if (m_Action != AlienAction.Dead) ChangeAction(AlienAction.Dead);
                m_Body.Tick(Vector3.zero, 0, 0, dt, 0, false); WriteState();
                if (m_Clock - m_ActionStarted > 2)
                {
                    LeaderboardManager.RetireRaidActor(GhostGameObject.Owner);
                    GhostGameObject.UpdateEntityCommandBuffer.DestroyEntity(GhostGameObject.LinkedEntity);
                }
                return;
            }
            float scale = Mathf.Clamp(profile.Scale, .1f, 4);
            if (Mathf.Abs(scale - m_Body.Scale) > .0001f)
            {
                var pose = m_Body.Motor.Current;
                transform.position = pose.Position + pose.Up * SurfaceMotor.Height * (scale - m_Body.Scale);
                m_Body.Dispose();
                m_Body = new AlienCreatureBody(transform, profile, scale, false, LayerMask.NameToLayer("ServerPlayer"));
            }
            if (m_Clock >= m_NextSense && m_Action != AlienAction.Windup && m_Action != AlienAction.Strike)
            { Sense(); m_NextSense = m_Clock + .15f; }
            if (!IsLivingHumanoid(manager, m_Target)) { m_Target = Entity.Null; m_Visible = false; }
            bool remembered = m_Target != Entity.Null && m_Clock - m_LastSeenAt <= profile.MemorySeconds;
            Vector3 goal = remembered ? m_LastSeen : PatrolGoal();
            float distance = Vector3.Distance(AimPoint, goal);
            if (m_Visible && IsLivingHumanoid(manager, m_Target)) distance = Vector3.Distance(AimPoint, ContactPoint(m_Target));
            float reach = profile.AttackReach * m_Body.Scale + .35f;
            if (m_Action == AlienAction.Windup && m_Clock - m_ActionStarted >= profile.Windup)
            { ChangeAction(AlienAction.Strike); TryStrike(); }
            else if (m_Action == AlienAction.Strike && m_Clock - m_ActionStarted >= .16f) ChangeAction(AlienAction.Recover);
            else if (m_Action == AlienAction.Recover && m_Clock - m_ActionStarted >= profile.Recovery)
                ChangeAction(remembered ? AlienAction.Chase : AlienAction.Roam);
            else if (m_Action != AlienAction.Windup && m_Action != AlienAction.Strike && m_Action != AlienAction.Recover)
            {
                if (remembered && m_Visible && distance <= reach && m_Clock >= m_NextAttack)
                { ChangeAction(AlienAction.Windup); m_Attacks++; m_NextAttack = m_Clock + profile.AttackCooldown; }
                else ChangeAction(remembered ? m_Visible ? AlienAction.Chase : AlienAction.Search : AlienAction.Roam);
            }
            Vector3 tangent = Vector3.ProjectOnPlane(goal - m_Body.Motor.Current.Position, m_Body.Motor.Current.Up);
            Vector3 intent = tangent.sqrMagnitude > .1f ? tangent.normalized : Vector3.zero;
            float speed = (m_Action == AlienAction.Roam ? profile.Speed * .45f : profile.Speed) * m_Body.Scale;
            if (m_Action == AlienAction.Windup || m_Action == AlienAction.Recover) speed = 0;
            if (m_Action == AlienAction.Strike) speed = profile.LungeSpeed * m_Body.Scale;
            if (distance < .6f && m_Action == AlienAction.Roam) speed = 0;
            float yaw = intent.sqrMagnitude > .01f ? Mathf.Clamp(Vector3.SignedAngle(m_Body.Motor.Current.Forward, intent,
                m_Body.Motor.Current.Up) / Mathf.Max(.001f, dt), -profile.TurnSpeed, profile.TurnSpeed) : 0;
            float before = m_Body.Motor.Travel;
            m_Body.Tick(intent, speed, yaw, dt, ActionPulse(), false);
            m_Stuck = speed > .01f && intent.sqrMagnitude > .01f && m_Body.Motor.Travel - before < .002f ? m_Stuck + dt : 0;
            if (m_Stuck > .85f && !m_Body.Motor.Detached) { m_Body.Escape(intent); m_Stuck = 0; }
            WriteState();
        }

        private void ChangeAction(AlienAction action)
        {
            if (m_Action == action) return;
            m_Action = action; m_ActionStarted = m_Clock;
        }
        private Vector3 PatrolGoal()
        {
            if (m_Clock >= m_NextPatrol || Vector3.Distance(transform.position, m_RoamGoal) < .7f)
            {
                float angle = (GhostGameObject.Owner * 137.5f + m_Clock * 43) * Mathf.Deg2Rad;
                m_RoamGoal = m_Spawn + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * profile.PatrolRadius;
                m_NextPatrol = m_Clock + 4;
            }
            return m_RoamGoal;
        }
        private void Sense()
        {
            UnityEngine.Physics.SyncTransforms();
            var manager = GhostGameObject.EntityManager; m_Visible = false;
            using var query = manager.CreateEntityQuery(typeof(PredictedPlayerGhost), typeof(LocalTransform), typeof(GhostGameObjectLink));
            using var entities = query.ToEntityArray(Allocator.Temp);
            float best = float.MaxValue;
            foreach (var entity in entities)
            {
                if (!IsLivingHumanoid(manager, entity)) continue;
                var root = manager.GetComponentObject<GhostGameObjectLink>(entity).LinkedInstance;
                Vector3 point = (Vector3)manager.GetComponentData<LocalTransform>(entity).Position + Vector3.up * 1.0f;
                float distance = Vector3.Distance(AimPoint, point);
                uint shot = manager.GetComponentData<PredictedPlayerGhost>(entity).LastShotTick;
                bool fired = m_ObservedShots.TryGetValue(entity, out uint previousShot) && previousShot != shot;
                m_ObservedShots[entity] = shot;
                bool visible = distance <= profile.SightRange && ClearSight(AimPoint + m_Body.Motor.Current.Up * .12f, point, root.transform);
                bool heard = fired && distance <= profile.HearingRange;
                if (!visible && !heard) continue;
                float score = distance - (entity == m_Target ? 2 : 0) + (visible ? 0 : profile.SightRange);
                if (score >= best) continue;
                best = score; m_Target = entity;
                m_LastSeen = visible ? point : point + new Vector3(Mathf.Sin(shot * 1.7f), 0, Mathf.Cos(shot * 2.3f)) * 1.3f;
                m_LastSeenAt = m_Clock; m_Visible = visible;
            }
        }
        private bool ClearSight(Vector3 from, Vector3 to, Transform target)
        {
            var targetCollider = target.GetComponent<Collider>();
            if (targetCollider && (targetCollider.ClosestPoint(from) - from).sqrMagnitude < .000001f &&
                !UnityEngine.Physics.Linecast(from, to, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) return true;
            Vector3 delta = to - from;
            int count = UnityEngine.Physics.RaycastNonAlloc(from, delta.normalized, m_HitsBuffer, delta.magnitude + .1f,
                LayerMask.GetMask("Default", "Ground", "ServerPlayer"), QueryTriggerInteraction.Ignore);
            if (count == m_HitsBuffer.Length) return false;
            float nearest = float.MaxValue; Transform hit = null;
            for (int i = 0; i < count; i++)
            {
                var entry = m_HitsBuffer[i];
                if (entry.transform.IsChildOf(transform) || entry.distance >= nearest) continue;
                nearest = entry.distance; hit = entry.transform;
            }
            return hit && hit.IsChildOf(target);
        }
        private void TryStrike()
        {
            UnityEngine.Physics.SyncTransforms();
            var manager = GhostGameObject.EntityManager;
            if (!IsLivingHumanoid(manager, m_Target)) return;
            var root = manager.GetComponentObject<GhostGameObjectLink>(m_Target).LinkedInstance;
            Vector3 point = ContactPoint(m_Target);
            if (Vector3.Distance(AimPoint, point) > profile.AttackReach * m_Body.Scale + .5f ||
                !ClearSight(AimPoint + m_Body.Motor.Current.Up * .12f, point, root.transform)) return;
            var health = manager.GetComponentData<PredictedPlayerGhost>(m_Target);
            float damaged = RaidHealth.Damage(ref health, BodyPart.Chest, profile.Damage, GhostGameObject.GetCurrentTick());
            manager.SetComponentData(m_Target, health);
            if (damaged > 0) m_Hits++;
            if (health.CurrentHealth <= 0 && manager.HasComponent<GhostOwner>(m_Target))
                LeaderboardManager.Instance?.AddKill(GhostGameObject.Owner, manager.GetComponentData<GhostOwner>(m_Target).NetworkId);
        }
        private Vector3 ContactPoint(Entity target)
        {
            var manager = GhostGameObject.EntityManager;
            var root = manager.GetComponentObject<GhostGameObjectLink>(target).LinkedInstance;
            var player = root.GetComponent<PlayerGhost>();
            return player && player.Controller && player.Controller.CharacterController ? player.Controller.CharacterController.ClosestPoint(AimPoint) :
                (Vector3)manager.GetComponentData<LocalTransform>(target).Position + Vector3.up * .5f;
        }
        private float ActionPulse() => m_Action == AlienAction.Windup ? Mathf.Clamp01((m_Clock - m_ActionStarted) / profile.Windup) :
            m_Action == AlienAction.Strike ? 1 : m_Action == AlienAction.Recover ? Mathf.Clamp01(1 - (m_Clock - m_ActionStarted) / profile.Recovery) : 0;
        private void WriteState()
        {
            WriteGhostComponentData(new State { Action = m_Action, Scale = m_Body.Scale, Phase = m_Body.Phase,
                ActionTime = m_Clock - m_ActionStarted, Velocity = m_Body.Motor.Velocity, AttackSequence = m_Attacks, Detached = m_Body.Motor.Detached });
        }

        public void UpdateClient(float deltaTime)
        {
            if (m_Body == null || !GhostGameObject.GhostEntityExists() || !GhostHasComponent<State>()) return;
            var state = ReadGhostComponentData<State>();
            if (Mathf.Abs(state.Scale - m_Body.Scale) > .002f && state.Scale > 0)
            {
                m_Body.Dispose(); m_Body = new AlienCreatureBody(transform, profile, state.Scale, true, LayerMask.NameToLayer("ClientPlayer"));
            }
            m_Action = state.Action;
            if (Time.unscaledTime >= m_NextLod)
            { m_Liquid = WantsLiquid(); m_NextLod = Time.unscaledTime + .35f; }
            float pulse = state.Action == AlienAction.Windup ? Mathf.Clamp01(state.ActionTime / profile.Windup) : state.Action == AlienAction.Strike ? 1 :
                state.Action == AlienAction.Recover ? Mathf.Clamp01(1 - state.ActionTime / profile.Recovery) : 0;
            m_Body.Observe(new SurfaceMotor.Pose { Position = transform.position, Rotation = transform.rotation }, state.Velocity,
                state.Detached, state.Phase, Mathf.Clamp(deltaTime, 0, .06f), pulse, m_Liquid);
        }
        private bool WantsLiquid()
        {
            if (!profile.Liquid || profile.MaximumLiquidEnemies <= 0 || !SystemInfo.supportsComputeShaders) return false;
            var camera = Camera.main;
            if (!camera) return false;
            float distance = (camera.transform.position - transform.position).sqrMagnitude;
            if (distance > profile.LiquidDistance * profile.LiquidDistance) return false;
            int nearer = 0, selfIndex = s_ClientAliens.IndexOf(this);
            for (int i = 0; i < s_ClientAliens.Count; i++)
            {
                var other = s_ClientAliens[i];
                if (!other || other == this || other.World != World || !other.profile.Liquid) continue;
                float otherDistance = (camera.transform.position - other.transform.position).sqrMagnitude;
                if (otherDistance < distance || Mathf.Approximately(otherDistance, distance) && i < selfIndex) nearer++;
            }
            return nearer < profile.MaximumLiquidEnemies;
        }
        public override void OnGhostPreDestroy() { Release(); }
        private void OnDestroy() { Release(); }
        private void Release() { s_ClientAliens.Remove(this); m_Body?.Dispose(); m_Body = null; }
    }

    public sealed class ProceduralAlienEnemyBaker : Baker<ProceduralAlienEnemy>
    {
        public override void Bake(ProceduralAlienEnemy authoring)
        {
            if (!authoring.Profile) return;
            DependsOn(authoring.Profile);
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PredictedPlayerGhost { MaxHealth = authoring.Profile.Health, CurrentHealth = authoring.Profile.Health,
                EquippedWeaponID = DollSingerWeapons.None });
            // State itself is added by the existing GhostGameObjectBaker's nested-component
            // discovery. No PlayerInput or predicted movement components are added here.
        }
    }
}
