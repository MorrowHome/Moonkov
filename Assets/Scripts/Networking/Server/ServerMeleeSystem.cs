using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(ServerPlayerMovementSystem))]
    public partial class ServerMeleeSystem : SystemBase
    {
        private sealed class Actor
        {
            public MeleeCombatState State;
            public int RaidId, Epoch;
            public uint LastRequest, StartTick, DamageTick, HitSequence;
            public string ItemId;
            public ClashDamageSource Source;
            public Quaternion Facing;
            public MeleeFeedback Feedback;
            public Vector3 LastHit;
            public bool HitBlocked;
            public float StartHealth;
            public readonly HashSet<Entity> Victims = new HashSet<Entity>();
        }
        private struct Context
        {
            public Entity Player, Connection;
            public InventoryGraph Graph;
            public int RaidId, Epoch;
            public PredictedPlayerGhost Health;
            public PlayerGhost View;
            public PhysicsScene Scene;
            public bool CanAct, Pending;
            public MeleeWeaponKind Mode => Health.InventoryWeapons
                ? MeleeInventory.ActiveKind(Graph, Health.EquippedWeaponSlot, Health.EquippedWeaponID != DollSingerWeapons.None)
                : MeleeWeaponKind.None;
            public string Item => Mode == MeleeWeaponKind.Fists || Mode == MeleeWeaponKind.None ? "" : MeleeInventory.Equipped(Graph)?.Id ?? "";
        }
        private struct Request
        { public Entity Connection; public RaidMeleeActionRpc Value; }
        private readonly Dictionary<Entity, Actor> m_Actors = new Dictionary<Entity, Actor>();
        private readonly List<Entity> m_Removed = new List<Entity>();
        private readonly List<Request> m_Requests = new List<Request>();
        private readonly Dictionary<Entity, Contact> m_Contacts = new Dictionary<Entity, Contact>();
        private struct Contact { public Collider Collider; public Vector3 Point; public float Distance; }
        private Collider[] m_Overlaps = new Collider[32];
        private readonly Collider[] m_CoverOverlaps = new Collider[8];
        private EntityQuery m_TimeQuery, m_ClientsQuery, m_TacticalQuery;
        private uint m_LastAdvancedTick;
        private bool m_HasAdvancedTick;
        private static readonly int VictimMask = 1 << (int)LayerIndex.ServerPlayer;
        private static readonly int CoverMask = (1 << (int)LayerIndex.Default) | (1 << (int)LayerIndex.Ground);

        protected override void OnCreate()
        {
            m_TimeQuery = GetEntityQuery(ComponentType.ReadOnly<NetworkTime>());
            m_ClientsQuery = GetEntityQuery(ComponentType.ReadOnly<ClientsMap>());
            m_TacticalQuery = GetEntityQuery(ComponentType.ReadOnly<TacticalServerWorld>());
            RequireForUpdate(m_TimeQuery);
        }
        protected override void OnDestroy() { m_Actors.Clear(); m_Requests.Clear(); m_Contacts.Clear(); }
        private bool Tick(out uint tick)
        {
            tick = 0;
            if (m_TimeQuery.CalculateEntityCount() != 1) return false;
            var time = m_TimeQuery.GetSingleton<NetworkTime>().ServerTick;
            if (!time.IsValid) return false;
            tick = time.TickIndexForValidTick; return true;
        }
        private bool TryContext(Entity player, out Context context)
        {
            context = default;
            if (!EntityManager.Exists(player) || !EntityManager.HasComponent<PredictedPlayerGhost>(player) ||
                !EntityManager.HasComponent<GhostGameObjectLink>(player)) return false;
            var root = EntityManager.GetComponentObject<GhostGameObjectLink>(player).LinkedInstance;
            if (!root || !root.TryGetComponent<PlayerGhost>(out var view)) return false;
            context.Player = player; context.View = view; context.Scene = root.gameObject.scene.GetPhysicsScene();
            context.Health = EntityManager.GetComponentData<PredictedPlayerGhost>(player);
            if (EntityManager.HasComponent<ClashPlayer>(player)) context.Epoch = EntityManager.GetComponentData<ClashPlayer>(player).Epoch;
            context.CanAct = context.Health.CurrentHealth > 0 && BountyClashDamage.CanAct(EntityManager, player) && !context.Health.ClashDarkSight;
            if (EntityManager.HasComponent<DollSingerEnemyBrain>(player))
                context.Graph = EntityManager.GetComponentObject<DollSingerEnemyBrain>(player).Inventory;
            else
            {
                if (!EntityManager.HasComponent<GhostOwner>(player) || m_ClientsQuery.CalculateEntityCount() != 1) return false;
                int owner = EntityManager.GetComponentData<GhostOwner>(player).NetworkId;
                var clients = EntityManager.GetBuffer<ClientsMap>(m_ClientsQuery.GetSingletonEntity(), true);
                if (owner <= 0 || owner >= clients.Length) return false;
                context.Connection = clients[owner].ConnectionEntity;
                if (!EntityManager.Exists(context.Connection) || !EntityManager.HasComponent<JoinedClient>(context.Connection) ||
                    EntityManager.GetComponentData<JoinedClient>(context.Connection).PlayerEntity != player ||
                    !EntityManager.HasComponent<RaidSession>(context.Connection) || !EntityManager.HasComponent<RaidInventoryState>(context.Connection)) return false;
                var session = EntityManager.GetComponentData<RaidSession>(context.Connection);
                var inventory = EntityManager.GetComponentObject<RaidInventoryState>(context.Connection);
                context.Graph = inventory.Graph; context.RaidId = session.RaidId; context.Pending = inventory.ActionPending;
                context.CanAct &= session.Phase == RaidPhase.Active && inventory.RaidId == session.RaidId &&
                    !EntityManager.HasComponent<NetworkStreamRequestDisconnect>(context.Connection);
            }
            return context.Graph != null && context.Scene.IsValid();
        }
        private Actor Resolve(in Context context)
        {
            if (!m_Actors.TryGetValue(context.Player, out var actor))
            { actor = new Actor(); m_Actors.Add(context.Player, actor); }
            if (!actor.State.Initialized || actor.Epoch != context.Epoch || actor.RaidId != context.RaidId)
            {
                actor.State = default; actor.Epoch = context.Epoch; actor.RaidId = context.RaidId;
                actor.LastRequest = actor.StartTick = actor.HitSequence = 0; actor.Feedback = MeleeFeedback.None;
                actor.ItemId = null; actor.Victims.Clear();
            }
            MeleeRules.Select(ref actor.State, context.Mode);
            return actor;
        }
        private bool Stimulated(Entity player)
        {
            if (m_TacticalQuery.CalculateEntityCount() != 1) return false;
            var tactical = EntityManager.GetComponentObject<TacticalServerWorld>(m_TacticalQuery.GetSingletonEntity());
            return tactical.Statuses.TryGetValue(player, out var status) && status.Stamina > World.Time.ElapsedTime;
        }
        private void Publish(Entity player, Actor actor)
        {
            if (!EntityManager.Exists(player) || !EntityManager.HasComponent<PredictedPlayerGhost>(player)) return;
            // Always re-read: another sweep can already have damaged this actor during the same tick.
            var health = EntityManager.GetComponentData<PredictedPlayerGhost>(player);
            health.MeleeWeapon = (byte)actor.State.SelectedWeapon; health.MeleeAttackWeapon = (byte)actor.State.AttackWeapon;
            health.MeleeAttack = (byte)actor.State.Attack; health.MeleePhase = (byte)actor.State.Phase;
            health.MeleeCombo = (byte)actor.State.ComboIndex; health.MeleeElapsed = actor.State.Elapsed;
            health.MeleeRecoveryRemaining = actor.State.RecoveryRemaining; health.MeleeStamina = actor.State.Stamina;
            health.MeleeAttackSequence = actor.State.Sequence; health.MeleeCancelSequence = actor.State.CancelSequence;
            health.MeleeHitSequence = actor.HitSequence; health.MeleeRequestId = actor.LastRequest;
            health.MeleeStartTick = actor.StartTick; health.MeleeEpoch = actor.Epoch; health.MeleeFeedback = (byte)actor.Feedback;
            health.MeleeHitPoint = actor.LastHit; health.MeleeHitBlocked = actor.HitBlocked;
            EntityManager.SetComponentData(player, health);
        }
        public bool Select(Entity player)
        {
            if (!TryContext(player, out var context) || !context.CanAct || context.Pending) return false;
            var actor = Resolve(context);
            string itemId = MeleeInventory.Equipped(context.Graph)?.Id ?? "";
            if (context.Health.EquippedWeaponSlot == MeleeInventory.EquipmentIndex && context.Item == itemId) return true;
            var health = context.Health;
            if (!DollSingerWeapons.EquipMelee(ref health, context.Graph, WeaponManager.Instance.WeaponRegistry, itemId)) return false;
            EntityManager.SetComponentData(player, health);
            MeleeRules.Cancel(ref actor.State);
            MeleeRules.Select(ref actor.State, MeleeInventory.ActiveKind(context.Graph, health.EquippedWeaponSlot, false));
            actor.Feedback = MeleeFeedback.Equipped; Publish(player, actor); return true;
        }
        public bool TryStart(Entity player, MeleeAttackKind attack)
        {
            if (!TryContext(player, out var context) || !Tick(out uint tick)) return false;
            var actor = Resolve(context);
            if (!context.CanAct || context.Pending || context.Health.ControllerState.IsReloadingState || context.Health.WeaponEquipRemaining > 0)
            { actor.Feedback = context.CanAct ? MeleeFeedback.Busy : MeleeFeedback.Inactive; Publish(player, actor); return false; }
            if (!MeleeRules.TryStart(ref actor.State, attack, true, Stimulated(player), out var error))
            {
                actor.Feedback = error == MeleeStartError.Exhausted ? MeleeFeedback.Exhausted :
                    error == MeleeStartError.Busy ? MeleeFeedback.Busy : MeleeFeedback.Unavailable;
                Publish(player, actor); return false;
            }
            actor.StartTick = tick; actor.DamageTick = context.Health.LastHitTick; actor.StartHealth = context.Health.CurrentHealth;
            actor.ItemId = context.Item; actor.Source = BountyClashDamage.Capture(World, player); actor.Victims.Clear();
            actor.Facing = Quaternion.Euler(context.Health.ControllerState.PitchDegrees, context.Health.ControllerState.YawDegrees, 0);
            actor.Feedback = MeleeFeedback.Accepted; Publish(player, actor); return true;
        }
        private bool SelectFirearm(in Context context, Actor actor, int slot, string itemId)
        {
            string region = slot == 1 ? "Primary" : slot == 2 ? "Secondary" : slot == 3 ? "Pistol" : null;
            var item = region == null ? null : context.Graph.Equipped(region);
            if (item == null || item.Id != itemId || DollSingerWeapons.WeaponId(item.Code) == DollSingerWeapons.None)
                return false;
            var health = EntityManager.GetComponentData<PredictedPlayerGhost>(context.Player);
            if (health.EquippedWeaponSlot != slot || health.EquippedWeaponItem.ToString() != itemId)
            {
                var input = new PlayerInput();
                input.SetFlag(slot == 1 ? PlayerInput.InputFlag.EquipHalo : slot == 2 ? PlayerInput.InputFlag.EquipRevolver : PlayerInput.InputFlag.EquipPistol, true);
                if (!DollSingerWeapons.TryEquip(ref health, input, WeaponManager.Instance.WeaponRegistry)) return false;
                DollSingerWeapons.SyncEquipment(ref health, context.Graph, WeaponManager.Instance.WeaponRegistry);
                EntityManager.SetComponentData(context.Player, health);
            }
            MeleeRules.Select(ref actor.State, MeleeWeaponKind.None);
            actor.Feedback = MeleeFeedback.Equipped; Publish(context.Player, actor); return true;
        }
        private void Cancel(Entity player, Actor actor)
        { MeleeRules.Cancel(ref actor.State); actor.Feedback = MeleeFeedback.Cancelled; Publish(player, actor); }
        private static bool Newer(uint incoming, uint previous) => incoming != 0 && unchecked((int)(incoming - previous)) > 0;
        private void Handle(in Request work)
        {
            if (!EntityManager.Exists(work.Connection) || !EntityManager.HasComponent<JoinedClient>(work.Connection)) return;
            var player = EntityManager.GetComponentData<JoinedClient>(work.Connection).PlayerEntity;
            if (!TryContext(player, out var context) || context.Connection != work.Connection) return;
            var request = work.Value; var actor = Resolve(context);
            if (request.RaidId != context.RaidId || request.Epoch != context.Epoch || !Newer(request.RequestId, actor.LastRequest)) return;
            actor.LastRequest = request.RequestId;
            if (request.Action == MeleeAction.Cancel) { Cancel(player, actor); return; }
            if (!context.CanAct || context.Pending)
            { actor.Feedback = context.CanAct ? MeleeFeedback.Busy : MeleeFeedback.Inactive; Publish(player, actor); return; }
            if (request.Action == MeleeAction.Equip)
            {
                bool selected = request.EquipmentSlot == MeleeInventory.EquipmentIndex
                    ? (MeleeInventory.Equipped(context.Graph)?.Id ?? "") == request.ItemId.ToString() && Select(player)
                    : SelectFirearm(context, actor, request.EquipmentSlot, request.ItemId.ToString());
                if (!selected) { actor.Feedback = MeleeFeedback.Unavailable; Publish(player, actor); }
                return;
            }
            if (request.Action != MeleeAction.Light && request.Action != MeleeAction.Heavy)
            { actor.Feedback = MeleeFeedback.Invalid; Publish(player, actor); return; }
            if (request.ItemId.ToString() != context.Item || context.Mode == MeleeWeaponKind.None)
            { actor.Feedback = MeleeFeedback.Unavailable; Publish(player, actor); return; }
            TryStart(player, request.Action == MeleeAction.Heavy ? MeleeAttackKind.Heavy : MeleeAttackKind.Light);
        }
        protected override void OnUpdate()
        {
            if (!Tick(out uint tick) || m_HasAdvancedTick && tick == m_LastAdvancedTick) return;
            m_LastAdvancedTick = tick; m_HasAdvancedTick = true;
            float dt = SystemAPI.Time.DeltaTime;
            foreach (var (health, entity) in SystemAPI.Query<RefRO<PredictedPlayerGhost>>().WithAll<Simulate, GhostGameObjectLink>().WithEntityAccess())
                if (health.ValueRO.InventoryWeapons && TryContext(entity, out var context)) Resolve(context);
            m_Requests.Clear();
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidMeleeActionRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            { m_Requests.Add(new Request { Connection = received.ValueRO.SourceConnection, Value = request.ValueRO }); ecb.DestroyEntity(entity); }
            m_Requests.Sort((a, b) => a.Connection.Index != b.Connection.Index ? a.Connection.Index.CompareTo(b.Connection.Index) :
                unchecked((int)(a.Value.RequestId - b.Value.RequestId)));
            foreach (var request in m_Requests) Handle(request);
            UnityEngine.Physics.SyncTransforms();
            m_Removed.Clear();
            foreach (var pair in m_Actors)
            {
                var actor = pair.Value;
                if (!TryContext(pair.Key, out var context))
                { Cancel(pair.Key, actor); m_Removed.Add(pair.Key); continue; }
                if (!context.CanAct || context.Pending || actor.Epoch != context.Epoch ||
                    actor.State.Phase != MeleePhase.Idle && (context.Item != actor.ItemId || context.Health.LastHitTick != actor.DamageTick || context.Health.CurrentHealth < actor.StartHealth - .0001f))
                    Cancel(pair.Key, actor);
                MeleeRules.Select(ref actor.State, context.Mode);
                var window = MeleeRules.Tick(ref actor.State, dt, Stimulated(pair.Key));
                if (window.Valid && context.CanAct && !context.Pending && actor.Source.Epoch == context.Epoch)
                    Sweep(context, actor, window, tick);
                Publish(pair.Key, actor);
            }
            foreach (var entity in m_Removed) m_Actors.Remove(entity);
            ecb.Playback(EntityManager); ecb.Dispose();
        }
        private Vector3 Eye(in Context context)
        {
            var capsule = context.View.Controller ? context.View.Controller.CharacterController : null;
            return context.View.transform.position + Vector3.up * (capsule ? capsule.center.y + capsule.height * .5f - .2f : 1.6f);
        }
        private void Sweep(in Context context, Actor actor, in MeleeActiveWindow window, uint tick)
        {
            if (!MeleeRules.TryDefinition(actor.State.AttackWeapon, out var definition)) return;
            Vector3 eye = Eye(context);
            if (context.Scene.OverlapSphere(eye, definition.SweepRadius, m_CoverOverlaps, CoverMask, QueryTriggerInteraction.Ignore) > 0) return;
            // Facing may follow the player's current look only within the original committed arc.
            var look = Quaternion.Euler(context.Health.ControllerState.PitchDegrees, context.Health.ControllerState.YawDegrees, 0);
            var facing = Quaternion.RotateTowards(actor.Facing, look, MeleeRules.MaximumTurnDegrees);
            int steps = Mathf.Clamp(Mathf.CeilToInt((window.To - window.From) * definition.SweepDegrees / 6), 1, 32);
            float sign = actor.State.ComboIndex == 1 ? -1 : 1;
            for (int step = 0; step <= steps; step++)
            {
                float progress = Mathf.Lerp(window.From, window.To, step / (float)steps);
                var direction = facing * (Quaternion.AngleAxis(Mathf.Lerp(-.5f, .5f, progress) * definition.SweepDegrees * sign, Vector3.up) * Vector3.forward);
                var end = eye + direction * definition.Reach;
                int count;
                while ((count = context.Scene.OverlapCapsule(eye, end, definition.SweepRadius, m_Overlaps, VictimMask, QueryTriggerInteraction.Ignore)) == m_Overlaps.Length)
                {
                    if (m_Overlaps.Length >= 4096) return; // Saturated unordered results never grant a partial hit set.
                    Array.Resize(ref m_Overlaps, m_Overlaps.Length * 2);
                }
                m_Contacts.Clear();
                for (int i = 0; i < count; i++)
                {
                    var collider = m_Overlaps[i]; if (!collider || collider.transform.IsChildOf(context.View.transform)) continue;
                    var ghost = collider.GetComponentInParent<GhostGameObject>();
                    if (!ghost || ghost.World != World || ghost.Role != MultiplayerRole.Server || ghost.IsPendingDestroy || !ghost.GhostEntityExists()) continue;
                    var victim = ghost.LinkedEntity;
                    if (victim == context.Player || actor.Victims.Contains(victim) || !EntityManager.HasComponent<PredictedPlayerGhost>(victim)) continue;
                    var victimHealth = EntityManager.GetComponentData<PredictedPlayerGhost>(victim);
                    if (victimHealth.CurrentHealth <= 0 || victimHealth.ClashEnabled && victimHealth.ClashLife != ClashLife.Alive) continue;
                    if (victimHealth.ClashEnabled && (!context.Health.ClashEnabled || !EntityManager.HasComponent<ClashPlayer>(victim) ||
                        EntityManager.GetComponentData<ClashPlayer>(victim).Epoch != actor.Epoch)) continue;
                    Vector3 point = collider.ClosestPoint(eye + direction * (definition.Reach * .5f));
                    var delta = point - eye; float distance = delta.magnitude;
                    if (distance > definition.Reach + definition.SweepRadius || distance > .1f && Vector3.Dot(direction, delta / distance) <= 0) continue;
                    if (distance > .02f && context.Scene.SphereCast(eye, definition.SweepRadius, delta / distance, out _, distance - .01f, CoverMask, QueryTriggerInteraction.Ignore)) continue;
                    if (!m_Contacts.TryGetValue(victim, out var previous) || distance < previous.Distance)
                        m_Contacts[victim] = new Contact { Collider = collider, Point = point, Distance = distance };
                }
                foreach (var pair in m_Contacts)
                {
                    if (!actor.Victims.Add(pair.Key)) continue;
                    var contact = pair.Value;
                    var victimRoot = contact.Collider.GetComponentInParent<PlayerGhost>();
                    float damage = BountyClashDamage.Apply(World, pair.Key, actor.Source,
                        RaidHealth.ResolveHit(victimRoot, contact.Collider, contact.Point), definition.Damage(actor.State.Attack, actor.State.ComboIndex), tick, RaidDamageKind.Melee);
                    actor.LastHit = contact.Point; actor.HitBlocked = damage <= 0;
                    unchecked { actor.HitSequence++; if (actor.HitSequence == 0) actor.HitSequence++; }
                }
            }
        }
    }
}
