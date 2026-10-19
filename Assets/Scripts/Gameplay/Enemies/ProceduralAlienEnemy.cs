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
    public enum AlienAction : byte { Roam, Chase, Search, Windup, Strike, Recover, Dead, Ambush, Reposition, Attached, Dominance }

    /// <summary>Server-only intent, perception and damage; interpolated clients derive
    /// cosmetic rigs from snapshots. Target eligibility requires a real PlayerGhost:
    /// humans and PMC bots are eligible, other aliens never are.</summary>
    [DisallowMultipleComponent]
    public sealed partial class ProceduralAlienEnemy : GhostMonoBehaviour, IUpdateServer, IUpdateClient
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
            [GhostField] public bool AmbushEnabled;
            [GhostField] public AlienAttackKind AttackKind;
            [GhostField(Quantization = 100)] public float3 AttackPoint;
            [GhostField(Quantization = 100)] public float3 AttackOrigin;
            [GhostField(Quantization = 1000)] public float3 AttackDirection;
            [GhostField] public byte BossPhase;
            [GhostField] public Entity AttachedTarget;
            [GhostField] public bool MucusInFlight;
        }
        [SerializeField] private AlienCreatureProfile profile;
        [SerializeField, Min(0), Tooltip("Full-rate client rig distance at 45-degree FOV. Distant rigs update at 30/15 Hz; zero disables the budget. Server simulation is unchanged.")]
        private float presentationDistance = 60;
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
        private float m_PresentationElapsed, m_NextPresentation;
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
            m_AmbushEnabled = profile.Species == AlienSpecies.Spider && (state.AmbushEnabled || Role == MultiplayerRole.Server);
            m_Action = state.Action;
            m_LastHealth = profile.Health;
            float scale = state.Scale > 0 ? state.Scale : profile.Scale;
            m_Body = new AlienCreatureBody(transform, profile, scale, Role != MultiplayerRole.Server,
                LayerMask.NameToLayer(Role == MultiplayerRole.Server ? "ServerPlayer" : "ClientPlayer"));
            if (Role != MultiplayerRole.Server)
            {
                s_ClientAliens.Add(this);
                // Spread far-away rigs across frames instead of solving every
                // creature on the same frame after a snapshot/spawn batch.
                m_NextPresentation = Time.unscaledTime + (unchecked((uint)GhostGameObject.Owner) % 16) * .004f;
            }
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
                if (m_Action != AlienAction.Dead)
                {
                    if (m_AttachedTarget != Entity.Null) ReleaseFace();
                    ChangeAction(AlienAction.Dead);
                    DropAlienLoot();
                }
                UpdateCombatPresentation(health.CurrentHealth);
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
            if (m_AmbushEnabled)
            {
                UpdateAmbush(dt, health.CurrentHealth);
                WriteState();
                return;
            }
            UpdateSpeciesCombat(dt, health);
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
                ActionTime = m_Clock - m_ActionStarted, Velocity = m_Body.Motor.Velocity, AttackSequence = m_Attacks,
                Detached = m_Body.Motor.Detached, AmbushEnabled = m_AmbushEnabled, MucusInFlight = m_MucusInFlight && m_Action != AlienAction.Dead,
                AttackKind = m_Pattern.Kind, AttackPoint = m_CombatPoint, AttackOrigin = m_CombatOrigin,
                AttackDirection = m_CombatDirection, BossPhase = m_BossPhase, AttachedTarget = m_AttachedTarget });
        }

        public void UpdateClient(float deltaTime)
        {
            if (m_Body == null || !GhostGameObject.GhostEntityExists() || !GhostHasComponent<State>()) return;
            var state = ReadGhostComponentData<State>();
            bool immediate = m_Action != state.Action;
            if (Mathf.Abs(state.Scale - m_Body.Scale) > .002f && state.Scale > 0)
            {
                m_Body.Dispose(); m_Body = new AlienCreatureBody(transform, profile, state.Scale, true, LayerMask.NameToLayer("ClientPlayer"));
                immediate = true;
            }
            m_Action = state.Action;
            m_AmbushEnabled = state.AmbushEnabled;
            ObserveSpeciesAttack(state);
            if (state.Action == AlienAction.Attached && IsLivingHumanoid(GhostGameObject.EntityManager, state.AttachedTarget))
            {
                var head = TargetHead(state.AttachedTarget);
                transform.SetPositionAndRotation(head.position, head.rotation);
            }
            float health = GhostHasComponent<PredictedPlayerGhost>() ? ReadGhostComponentData<PredictedPlayerGhost>().CurrentHealth : profile.Health;
            m_Body.SetCombatPresentation(state.Action, state.ActionTime, state.AttackSequence, health,
                m_AmbushEnabled ? profile.SpiderWindup : m_Pattern.Windup,
                m_AmbushEnabled ? profile.SpiderRecovery : m_Pattern.Recovery,
                m_AmbushEnabled ? profile.SpiderStrikeSeconds : m_Pattern.Active);
            if (Time.unscaledTime >= m_NextLod)
            { m_Liquid = WantsLiquid(); m_NextLod = Time.unscaledTime + .35f; }
            m_PresentationElapsed += Mathf.Clamp(deltaTime, 0, .06f);
            float interval = !Application.isPlaying || m_Liquid ? 0 : PresentationInterval(Camera.main, transform.position,
                presentationDistance * Mathf.Max(1, m_Body.Scale));
            if (!immediate && interval > 0 && Time.unscaledTime < m_NextPresentation) return;
            m_NextPresentation = Time.unscaledTime + interval;
            float presentationDt = Mathf.Min(m_PresentationElapsed, .1f);
            m_PresentationElapsed = 0;
            float windup = m_AmbushEnabled ? profile.SpiderWindup : Mathf.Max(.01f, m_Pattern.Windup);
            float recovery = m_AmbushEnabled ? profile.SpiderRecovery : Mathf.Max(.01f, m_Pattern.Recovery);
            float pulse = state.Action == AlienAction.Windup ? Mathf.Clamp01(state.ActionTime / windup) :
                state.Action == AlienAction.Strike || state.Action == AlienAction.Attached ? 1 :
                state.Action == AlienAction.Dominance ? .65f * Mathf.Sin(Mathf.Clamp01(state.ActionTime / profile.BossDisplaySeconds) * Mathf.PI) :
                state.Action == AlienAction.Recover ? Mathf.Clamp01(1 - state.ActionTime / recovery) : 0;
            m_Body.Observe(new SurfaceMotor.Pose { Position = transform.position, Rotation = transform.rotation }, state.Velocity,
                state.Detached, state.Phase, presentationDt, pulse, m_Liquid);
        }

        private static float PresentationInterval(Camera viewer, Vector3 position, float fullRateDistance)
        {
            if (fullRateDistance <= 0 || !viewer || !viewer.isActiveAndEnabled || viewer.orthographic) return 0;
            float zoom = Mathf.Max(1, Mathf.Tan(22.5f * Mathf.Deg2Rad) /
                Mathf.Tan(Mathf.Max(1, viewer.fieldOfView) * .5f * Mathf.Deg2Rad));
            float near = fullRateDistance * zoom;
            float squared = (position - viewer.transform.position).sqrMagnitude;
            return squared <= near * near ? 0 : squared <= near * near * 4 ? 1f / 30 : 1f / 15;
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
        private void Release() { s_ClientAliens.Remove(this); m_AttackPresentation?.Dispose(); m_AttackPresentation = null; m_Body?.Dispose(); m_Body = null; }
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
