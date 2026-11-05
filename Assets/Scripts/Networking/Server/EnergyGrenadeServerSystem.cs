using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Entities;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(ServerGameSystem))]
    public partial class EnergyGrenadeServerSystem : SystemBase
    {
        private sealed class Grenade
        {
            public uint Id;
            public int Owner;
            public ClashDamageSource Source;
            public Transform Shooter;
            public PhysicsScene Scene;
            public Vector3 Position, Velocity;
            public float Age, SnapshotAge, FlightAge;
            public bool Resting;
        }

        private readonly List<Grenade> m_Active = new();
        private sealed class Held
        {
            public Entity Player;
            public RaidInventoryState Inventory;
            public int Raid;
            public uint HoldId;
            public string ItemId, Code;
            public GrenadePhase Phase;
            public double Until, FuseAt = double.PositiveInfinity;
            public bool Consumed, ReleaseQueued, Underhand, Reserved;
            public Transform Root;
            public PhysicsScene Scene;
            public Vector3 Position;
            public ClashDamageSource Source;
            public int Owner;
        }
        private readonly Dictionary<Entity, Held> m_Held = new();
        private readonly List<Entity> m_HeldKeys = new();
        private readonly Dictionary<Entity, double> m_NextThrow = new();
        private readonly List<Entity> m_ExpiredCooldowns = new();
        private RaycastHit[] m_Hits = new RaycastHit[16];
        private EntityQuery m_TacticalQuery;
        private MoonRaidMap m_Map;
        private uint m_NextId;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            m_TacticalQuery = GetEntityQuery(typeof(TacticalServerWorld));
        }
        private TacticalServerWorld Tactical => m_TacticalQuery.IsEmptyIgnoreFilter ? null :
            EntityManager.GetComponentObject<TacticalServerWorld>(m_TacticalQuery.GetSingletonEntity());

        public static InventoryError CommitEquip(RaidInventoryState inventory, ref PredictedPlayerGhost health,
            bool canAct, string itemId, int version, uint tick)
        {
            if (inventory?.Graph == null) return InventoryError.Invalid;
            if (!canAct || !(health.CurrentHealth > 0) || !float.IsFinite(health.CurrentHealth) || health.ClashDarkSight ||
                RaidHealth.HandsBusy(health) || health.MeleePhase != 0) return InventoryError.Inaccessible;
            if (inventory.Graph.Version != version) return InventoryError.Stale;
            if (!GrenadeHandling.Accessible(inventory.Graph, inventory.Graph.Find(itemId))) return InventoryError.Inaccessible;
            inventory.CancelActions(); inventory.GrenadePending = true;
            health.LastGrenadeShotTick = tick;
            return InventoryError.None;
        }

        // Read-only server perception. No trajectory or owner is exposed to NPCs.
        public int ActiveThreatCount => m_Active.Count;
        public bool TryGetLiveThreat(int index, out uint id, out PhysicsScene scene, out Vector3 position, out float remaining)
        {
            id = 0; scene = default; position = default; remaining = 0;
            if (index < 0 || index >= m_Active.Count) return false;
            var grenade = m_Active[index];
            if (grenade == null || grenade.Id == 0 || !grenade.Scene.IsValid() || !float.IsFinite(grenade.Age) ||
                grenade.Age >= EnergyGrenadeRules.Fuse ||
                !float.IsFinite(grenade.Position.x) || !float.IsFinite(grenade.Position.y) || !float.IsFinite(grenade.Position.z)) return false;
            id = grenade.Id; scene = grenade.Scene; position = grenade.Position;
            remaining = EnergyGrenadeRules.Fuse - grenade.Age;
            return true;
        }

        protected override void OnUpdate()
        {
            var map = MoonRaidMap.Active;
            if (map != m_Map)
            {
                foreach (var held in m_Held.Values) Clear(held);
                m_Held.Clear(); m_Active.Clear(); m_NextThrow.Clear(); m_Map = map;
            }
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            double now = SystemAPI.Time.ElapsedTime;
            m_ExpiredCooldowns.Clear();
            foreach (var pair in m_NextThrow)
                if (pair.Value <= now || !EntityManager.Exists(pair.Key)) m_ExpiredCooldowns.Add(pair.Key);
            foreach (var connection in m_ExpiredCooldowns) m_NextThrow.Remove(connection);
            var serverTick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            if (!serverTick.IsValid) { ecb.Dispose(); return; }
            uint tick = serverTick.TickIndexForValidTick;
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidGrenadeThrowRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection = received.ValueRO.SourceConnection;
                var r = request.ValueRO;
                if (EntityManager.HasComponent<RaidInventoryState>(connection) &&
                    EntityManager.HasComponent<RaidSession>(connection) && EntityManager.HasComponent<JoinedClient>(connection) &&
                    !EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    var inventory = EntityManager.GetComponentObject<RaidInventoryState>(connection);
                    if (r.RequestId > inventory.RequestId)
                    {
                        inventory.RequestId = r.RequestId;
                        inventory.Error = InventoryError.Inaccessible;
                        var session = EntityManager.GetComponentData<RaidSession>(connection);
                        var player = EntityManager.GetComponentData<JoinedClient>(connection).PlayerEntity;
                        if (map && session.Phase == RaidPhase.Active && session.RaidId == r.RaidId && inventory.RaidId == r.RaidId &&
                            EntityManager.HasComponent<PredictedPlayerGhost>(player) && EntityManager.HasComponent<GhostGameObjectLink>(player) &&
                            EntityManager.HasComponent<GhostOwner>(player))
                        {
                            var health = EntityManager.GetComponentData<PredictedPlayerGhost>(player);
                            var root = EntityManager.GetComponentObject<GhostGameObjectLink>(player).LinkedInstance;
                            bool canAct = BountyClashRuntime.CanAct(EntityManager, player);
                            if (health.CurrentHealth > 0 && float.IsFinite(health.CurrentHealth) && canAct && !health.ClashDarkSight && root)
                                inventory.Error = Command(connection, player, root, inventory, ref health, r, now, tick, ecb);
                            EntityManager.SetComponentData(player, health);
                        }
                        inventory.LastSentVersion = -1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            m_HeldKeys.Clear(); m_HeldKeys.AddRange(m_Held.Keys);
            foreach (var connection in m_HeldKeys)
            {
                var held = m_Held[connection];
                bool actor = EntityManager.HasComponent<PredictedPlayerGhost>(held.Player);
                var health = actor ? EntityManager.GetComponentData<PredictedPlayerGhost>(held.Player) : default;
                bool valid = actor && held.Root && health.CurrentHealth > 0 && !health.ClashDarkSight &&
                    BountyClashRuntime.CanAct(EntityManager, held.Player) && EntityManager.HasComponent<RaidSession>(connection) &&
                    EntityManager.HasComponent<JoinedClient>(connection) && !EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection) &&
                    EntityManager.GetComponentData<JoinedClient>(connection).PlayerEntity == held.Player &&
                    EntityManager.GetComponentData<RaidSession>(connection).RaidId == held.Raid &&
                    EntityManager.GetComponentData<RaidSession>(connection).Phase == RaidPhase.Active;
                if (held.Root) held.Position = HandPosition(held.Root, health);
                if (!valid || held.Consumed && now >= held.FuseAt)
                {
                    if (held.Consumed) Release(held, health, true, now, tick, ecb);
                    Clear(held); m_Held.Remove(connection); continue;
                }
                // A safe grenade stays in inventory. An external inventory replacement must never duplicate it.
                if (!held.Consumed && held.Phase != GrenadePhase.Recovering &&
                    !GrenadeHandling.Accessible(held.Inventory.Graph, held.Inventory.Graph?.Find(held.ItemId)))
                { Clear(held); m_Held.Remove(connection); continue; }
                if (now >= held.Until)
                {
                    if (held.Phase == GrenadePhase.Drawing) SetPhase(held, GrenadePhase.Safe, now);
                    else if (held.Phase == GrenadePhase.Priming)
                        SetPhase(held, held.ReleaseQueued ? GrenadePhase.Throwing : GrenadePhase.Live, held.Until);
                    if (held.Phase == GrenadePhase.Throwing && now >= held.Until)
                    {
                        double releaseAt = held.Until;
                        Release(held, health, false, now, tick, ecb);
                        SetPhase(held, GrenadePhase.Recovering, releaseAt);
                        m_NextThrow[connection] = now + EnergyGrenadeRules.Cooldown;
                    }
                    else if ((held.Phase == GrenadePhase.Recovering || held.Phase == GrenadePhase.Stowing) && now >= held.Until)
                    { Clear(held); m_Held.Remove(connection); continue; }
                }
                Publish(held, ref health, now);
                EntityManager.SetComponentData(held.Player, health);
            }
            if (map)
                for (int i = m_Active.Count - 1; i >= 0; i--)
                {
                    var g = m_Active[i];
                    if (!g.Scene.IsValid()) { m_Active.RemoveAt(i); continue; }
                    float dt = Mathf.Min(SystemAPI.Time.DeltaTime, EnergyGrenadeRules.Fuse - g.Age);
                    EnergyGrenadePhysics.Step(g.Scene, ref g.Position, ref g.Velocity, ref g.Resting, dt,
                        g.FlightAge < .25f ? g.Shooter : null, ref m_Hits);
                    g.FlightAge += dt;
                    g.Age += dt;
                    if (g.Age >= EnergyGrenadeRules.Fuse)
                    {
                        Explode(g, tick);
                        Broadcast(ecb, g, tick, true);
                        m_Active.RemoveAt(i);
                    }
                    else if (g.Age - g.SnapshotAge >= .2f)
                    { g.SnapshotAge = g.Age; Broadcast(ecb, g, tick, false); }
                }
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        private InventoryError Command(Entity connection, Entity player, GhostGameObject root, RaidInventoryState inventory,
            ref PredictedPlayerGhost health, in RaidGrenadeThrowRpc request, double now, uint tick, EntityCommandBuffer ecb)
        {
            if (request.Command == GrenadeCommand.Equip)
            {
                if (m_Held.ContainsKey(connection) || RaidHealth.HandsBusy(health) || health.MeleePhase != 0 ||
                    m_NextThrow.TryGetValue(connection, out var next) && now < next) return InventoryError.Inaccessible;
                var accepted = CommitEquip(inventory, ref health, true, request.ItemId.ToString(), request.ExpectedVersion, tick);
                if (accepted != InventoryError.None) return accepted;
                var item = inventory.Graph.Find(request.ItemId.ToString());
                var held = new Held { Player = player, Inventory = inventory, Raid = request.RaidId, HoldId = request.RequestId,
                    ItemId = item.Id, Code = item.Code, Root = root.transform, Scene = root.gameObject.scene.GetPhysicsScene(),
                    Position = HandPosition(root.transform, health), Owner = EntityManager.GetComponentData<GhostOwner>(player).NetworkId,
                    Source = BountyClashDamage.Capture(World, player) };
                SetPhase(held, GrenadePhase.Drawing, now); m_Held.Add(connection, held);
                HaloModuleFire.Reset(ref health);
                Publish(held, ref health, now);
                return InventoryError.None;
            }
            if (!m_Held.TryGetValue(connection, out var current) || current.Player != player ||
                request.HoldId != current.HoldId) return InventoryError.Inaccessible;
            switch (request.Command)
            {
                case GrenadeCommand.Prime:
                    if (current.Phase != GrenadePhase.Safe) return InventoryError.Inaccessible;
                    var tactical = Tactical;
                    bool energy = current.Code == EnergyGrenadeRules.Code;
                    int reserved = 0;
                    if (energy) foreach (var held in m_Held.Values) if (held.Consumed && held.Code == EnergyGrenadeRules.Code) reserved++;
                    if (energy ? m_Active.Count + reserved >= 64 : tactical == null || tactical.Objects.Count + tactical.GrenadeReservations >= TacticalItemRules.MaxWorldObjects)
                        return InventoryError.Full;
                    var error = GrenadeHandling.Take(inventory.Graph, current.ItemId, request.ExpectedVersion);
                    if (error != InventoryError.None) return error;
                    current.Consumed = true; current.Underhand = request.Underhand;
                    current.FuseAt = now + GrenadeHandling.PrimeSeconds + GrenadeHandling.Fuse(current.Code);
                    if (!energy) { tactical.GrenadeReservations++; current.Reserved = true; }
                    SetPhase(current, GrenadePhase.Priming, now); health.LastGrenadeShotTick = tick;
                    break;
                case GrenadeCommand.Release:
                    if (current.Phase == GrenadePhase.Priming) current.ReleaseQueued = true;
                    else if (current.Phase == GrenadePhase.Live) SetPhase(current, GrenadePhase.Throwing, now);
                    else return InventoryError.Inaccessible;
                    break;
                case GrenadeCommand.Stow:
                    if (current.Consumed || current.Phase != GrenadePhase.Safe && current.Phase != GrenadePhase.Drawing)
                        return InventoryError.Inaccessible;
                    SetPhase(current, GrenadePhase.Stowing, now);
                    break;
                case GrenadeCommand.Drop:
                    if (!current.Consumed) return InventoryError.Inaccessible;
                    Release(current, health, true, now, tick, ecb); SetPhase(current, GrenadePhase.Recovering, now);
                    break;
                default: return InventoryError.Invalid;
            }
            Publish(current, ref health, now);
            return InventoryError.None;
        }

        private static void SetPhase(Held held, GrenadePhase phase, double now)
        { held.Phase = phase; float seconds = GrenadeHandling.Duration(phase); held.Until = seconds > 0 ? now + seconds : double.PositiveInfinity; }
        private static void Publish(Held held, ref PredictedPlayerGhost health, double now)
        {
            health.GrenadeHoldId = held.HoldId; health.GrenadePhase = (byte)held.Phase;
            health.GrenadeItem = held.Code; health.GrenadeUnderhand = held.Underhand;
            health.GrenadePhaseRemaining = double.IsFinite(held.Until) ? Mathf.Max(0, (float)(held.Until - now)) : 0;
            health.GrenadeFuseRemaining = held.Consumed ? Mathf.Max(0, (float)(held.FuseAt - now)) : 0;
        }
        private void Clear(Held held)
        {
            if (held.Reserved && Tactical != null) Tactical.GrenadeReservations = Mathf.Max(0, Tactical.GrenadeReservations - 1);
            held.Reserved = false; held.Inventory.GrenadePending = false; held.Inventory.LastSentVersion = -1;
            if (!EntityManager.HasComponent<PredictedPlayerGhost>(held.Player)) return;
            var health = EntityManager.GetComponentData<PredictedPlayerGhost>(held.Player);
            health.GrenadePhase = 0; health.GrenadeItem = default; health.GrenadePhaseRemaining = health.GrenadeFuseRemaining = 0;
            EntityManager.SetComponentData(held.Player, health);
        }
        private static Vector3 HandPosition(Transform root, in PredictedPlayerGhost health)
        {
            var controller = root.GetComponent<PlayerGhost>()?.Controller?.CharacterController;
            float height = controller ? controller.height : 1.8f;
            var rotation = Quaternion.Euler(0, health.ControllerState.YawDegrees, 0);
            return root.position + Vector3.up * Mathf.Max(.25f, height - .45f) + rotation * new Vector3(.18f, 0, .2f);
        }
        private void Release(Held held, in PredictedPlayerGhost health, bool drop, double now, uint tick, EntityCommandBuffer ecb)
        {
            if (!held.Consumed || !held.Scene.IsValid()) return;
            var position = held.Position;
            var direction = Quaternion.Euler(health.ControllerState.PitchDegrees, health.ControllerState.YawDegrees, 0) * Vector3.forward;
            var velocity = drop ? Vector3.zero : direction * (held.Underhand ? GrenadeHandling.LowSpeed : EnergyGrenadeRules.Speed) + Vector3.up * (held.Underhand ? .8f : 2f);
            // Sweep from inside the capsule to the hand. A live grenade can never be cancelled by a wall.
            if (held.Root)
            {
                var origin = new Vector3(held.Root.position.x, position.y, held.Root.position.z);
                if (Ballistics.Sweep(held.Scene, origin, position, EnergyGrenadeRules.BodyRadius, EnergyGrenadePhysics.HitMask, held.Root, ref m_Hits, out var hit))
                { position = origin + (position - origin).normalized * Mathf.Max(0, hit.distance - .02f); velocity = Vector3.zero; }
            }
            if (held.Code == EnergyGrenadeRules.Code)
            {
                var grenade = new Grenade { Id = ++m_NextId, Owner = held.Owner, Source = held.Source, Shooter = held.Root,
                    Scene = held.Scene, Position = position, Velocity = velocity,
                    Age = Mathf.Min(EnergyGrenadeRules.Fuse - (float)(held.FuseAt - now), EnergyGrenadeRules.Fuse) };
                grenade.SnapshotAge = grenade.Age; m_Active.Add(grenade); Broadcast(ecb, grenade, tick, false);
            }
            else if (Tactical is { } tactical)
            {
                var obj = new TacticalServerWorld.Object { Id = ++tactical.NextId, Owner = held.Owner, OwnerPlayer = held.Player,
                    Source = held.Source, Definition = TacticalItemRules.Find(held.Code), Phase = TacticalWorldPhase.Flying,
                    Scene = held.Scene, Shooter = held.Root, Position = position, Velocity = velocity, Created = now,
                    Until = held.FuseAt, ArmAt = now };
                tactical.Objects.Add(obj); ServerGameSystem.TacticalBroadcast(ecb, tactical, obj, false);
            }
            if (held.Reserved && Tactical != null) Tactical.GrenadeReservations = Mathf.Max(0, Tactical.GrenadeReservations - 1);
            held.Reserved = held.Consumed = false;
        }

        private void Explode(Grenade grenade, uint tick)
        {
            // One entity per victim, including aliens, PMCs and the thrower; colliders never multiply damage.
            foreach (var (health, link, entity) in SystemAPI.Query<RefRW<PredictedPlayerGhost>, GhostGameObjectLink>().WithEntityAccess())
            {
                var root = link.LinkedInstance;
                if (!root || root.gameObject.scene.GetPhysicsScene() != grenade.Scene || health.ValueRO.CurrentHealth <= 0) continue;
                var collider = root.GetComponentInChildren<Collider>();
                Vector3 point = collider ? collider.bounds.center : root.transform.position + Vector3.up * .8f;
                float damage = EnergyGrenadeRules.DamageAt(Vector3.Distance(grenade.Position, point));
                if (damage <= 0 || !EnergyGrenadePhysics.ClearSight(grenade.Scene, grenade.Position, point, root.transform)) continue;
                // Resolve shielding once, then distribute the residual over living tissue.
                BountyClashDamage.Apply(World, entity, grenade.Source, BodyPart.Auto, damage, tick, RaidDamageKind.Explosion, (Unity.Mathematics.float3)grenade.Position);
            }
            RaidEnvironmentalNoise.EmitExplosion(EntityManager, grenade.Scene, grenade.Position,
                RaidEnvironmentalNoise.ExplosionId(true, 0, grenade.Id), SystemAPI.Time.ElapsedTime);
        }

        private static void Broadcast(EntityCommandBuffer ecb, Grenade grenade, uint tick, bool exploded)
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new EnergyGrenadeStateRpc { Id = grenade.Id, Tick = tick, Position = grenade.Position,
                Velocity = grenade.Velocity, Remaining = EnergyGrenadeRules.Fuse - grenade.Age, Exploded = (byte)(exploded ? 1 : 0) });
            ecb.AddComponent(entity, new SendRpcCommandRequest());
        }
    }
}
