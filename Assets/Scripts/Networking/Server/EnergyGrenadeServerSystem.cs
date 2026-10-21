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
            public Transform Shooter;
            public PhysicsScene Scene;
            public Vector3 Position, Velocity;
            public float Age, SnapshotAge;
            public bool Resting;
        }

        private readonly List<Grenade> m_Active = new();
        private readonly Dictionary<Entity, double> m_NextThrow = new();
        private readonly List<Entity> m_ExpiredCooldowns = new();
        private RaycastHit[] m_Hits = new RaycastHit[16];
        private MoonRaidMap m_Map;
        private uint m_NextId;

        protected override void OnCreate() => RequireForUpdate<NetworkTime>();

        protected override void OnUpdate()
        {
            var map = MoonRaidMap.Active;
            if (map != m_Map)
            { m_Active.Clear(); m_NextThrow.Clear(); m_Map = map; }
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
                            m_Active.Count < 64 && (!m_NextThrow.TryGetValue(connection, out var next) || now >= next) &&
                            EntityManager.HasComponent<PredictedPlayerGhost>(player) && EntityManager.HasComponent<GhostGameObjectLink>(player))
                        {
                            var health = EntityManager.GetComponentData<PredictedPlayerGhost>(player);
                            var root = EntityManager.GetComponentObject<GhostGameObjectLink>(player).LinkedInstance;
                            if (health.CurrentHealth > 0 && root && TryPose(root, health, out var position, out var velocity))
                            {
                                inventory.Error = EnergyGrenadeRules.Take(inventory.Graph, r.ItemId.ToString(), r.ExpectedVersion);
                                if (inventory.Error == InventoryError.None)
                                {
                                    var grenade = new Grenade { Id = ++m_NextId, Owner = EntityManager.GetComponentData<GhostOwner>(player).NetworkId,
                                        Shooter = root.transform, Scene = root.gameObject.scene.GetPhysicsScene(), Position = position, Velocity = velocity };
                                    m_Active.Add(grenade);
                                    m_NextThrow[connection] = now + EnergyGrenadeRules.Cooldown;
                                    Broadcast(ecb, grenade, tick, false);
                                }
                            }
                        }
                        inventory.LastSentVersion = -1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            if (map)
                for (int i = m_Active.Count - 1; i >= 0; i--)
                {
                    var g = m_Active[i];
                    if (!g.Scene.IsValid()) { m_Active.RemoveAt(i); continue; }
                    float dt = Mathf.Min(SystemAPI.Time.DeltaTime, EnergyGrenadeRules.Fuse - g.Age);
                    EnergyGrenadePhysics.Step(g.Scene, ref g.Position, ref g.Velocity, ref g.Resting, dt,
                        g.Age < .25f ? g.Shooter : null, ref m_Hits);
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

        private bool TryPose(GhostGameObject root, in PredictedPlayerGhost player, out Vector3 position, out Vector3 velocity)
        {
            var controller = root.GetComponent<PlayerGhost>()?.Controller?.CharacterController;
            float height = controller ? controller.height : 1.8f;
            Vector3 eye = root.transform.position + Vector3.up * Mathf.Max(.25f, height - .2f);
            var state = player.ControllerState;
            Vector3 direction = Quaternion.Euler(state.PitchDegrees, state.YawDegrees, 0) * Vector3.forward;
            position = eye;
            velocity = direction * EnergyGrenadeRules.Speed + Vector3.up * 2f;
            if (!float.IsFinite(velocity.x) || !float.IsFinite(velocity.y) || !float.IsFinite(velocity.z)) return false;
            // Sweep from the body to the release point: aiming against a wall cannot spawn through it.
            var scene = root.gameObject.scene.GetPhysicsScene();
            if (Ballistics.Sweep(scene, eye, eye + direction * .55f, EnergyGrenadeRules.BodyRadius,
                EnergyGrenadePhysics.HitMask, root.transform, ref m_Hits, out var hit))
                position = eye + direction * Mathf.Max(0, hit.distance - .02f);
            else position += direction * .55f;
            // Casts do not detect an overlapping starting sphere. Reject it without spending an item.
            var overlaps = new Collider[16];
            int count = scene.OverlapSphere(position, EnergyGrenadeRules.BodyRadius, overlaps,
                EnergyGrenadePhysics.HitMask, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (!overlaps[i].transform.IsChildOf(root.transform)) return false;
            return true;
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
                float before = health.ValueRO.CurrentHealth;
                if (!health.ValueRO.BodyHealthInitialized) BountyClashDamage.Apply(World,entity,grenade.Owner,BodyPart.Chest,damage,tick,RaidDamageKind.Explosion);
                else
                {
                    // Spread blast damage over living tissue instead of treating every blast as one chest bullet.
                    for (int p = 0; p < RaidHealth.PartCount; p++)
                    {
                        var part = (BodyPart)p;
                        float share = damage * RaidHealth.Get(health.ValueRO, part) / before;
                        BountyClashDamage.Apply(World,entity,grenade.Owner,part,share,tick,RaidDamageKind.Explosion);
                    }
                }
                health.ValueRW.LastDamageAmount = before - health.ValueRO.CurrentHealth;
                health.ValueRW.LastHitPart = BodyPart.Auto;
            }
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
