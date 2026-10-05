using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    // One ghost per shot. Clients evaluate the trajectory; only the server applies damage.
    public class Projectile : GhostMonoBehaviour, IUpdateServer, IPhysicsUpdateServer, IUpdateClient
    {
        public struct ProjectileData : IComponentData
        {
            [GhostField] public int OwnerNetworkId;
            [GhostField] public uint SpawnTick;
            [GhostField] public uint WeaponID;
            [GhostField] public uint FireTick;
            [GhostField] public int PelletIndex;
            [GhostField] public float3 Origin;
            [GhostField] public float3 InitialVelocity;
        }

        public class PredictedProjectileInfo
        {
            public GameObject Instance;
            public uint SpawnTick;
            public uint WeaponID;
            public int PelletIndex;
            public float CreatedAt;
        }

        public static List<PredictedProjectileInfo> PredictedProjectiles = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            PredictedProjectiles.Clear();
            if (s_BulletMaterial != null) Destroy(s_BulletMaterial);
            s_BulletMaterial = null;
        }

        private ProjectileData m_Data;
        private WeaponData m_Weapon;
        private float m_Age;
        private float m_TickDuration = 1f / 60f;
        private bool m_Initialized;
        private bool m_Stopped;
        private RaycastHit[] m_Hits = new RaycastHit[16];
        private int m_HitMask;
        private Transform m_Shooter;
        private MeshRenderer m_Body;
        private HaloProjectileVisual m_HaloVisual;
        private float m_HaloTrailSeconds;
        private static Material s_BulletMaterial;

        public void InitializePrediction(uint weaponId, Vector3 origin, Quaternion rotation, Transform shooter,
            float elapsed = 0f)
        {
            m_Data = new ProjectileData { WeaponID = weaponId, Origin = origin,
                InitialVelocity = rotation * Vector3.forward * WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId).ProjectileSpeed };
            m_Shooter = shooter;
            Initialize(false);
            Advance(elapsed, false);
            UpdateHaloVisual();
        }

        private void Initialize(bool server)
        {
            m_Weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(m_Data.WeaponID);
            m_HitMask = LayerMask.GetMask(server ? "ServerPlayer" : "ClientPlayer", "Ground", "Default");
            m_Initialized = m_Weapon != null;
            if (!server && m_Initialized && m_Weapon.ShowProjectileBody && m_Body == null)
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = "Ballistic bullet";
                var collider = body.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                body.transform.SetParent(transform, false);
                var scale = transform.lossyScale;
                body.transform.localScale = new Vector3(.035f / scale.x, .035f / scale.y, .12f / scale.z);
                if (s_BulletMaterial == null)
                {
                    s_BulletMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                        { name = "Ballistic bullet (runtime)", hideFlags = HideFlags.DontSave };
                    s_BulletMaterial.SetColor("_BaseColor", new Color(.9f, .7f, .35f));
                    s_BulletMaterial.SetFloat("_Metallic", .7f);
                    s_BulletMaterial.SetColor("_EmissionColor", new Color(1f, .55f, .15f) * 3f);
                    s_BulletMaterial.EnableKeyword("_EMISSION");
                }
                m_Body = body.GetComponent<MeshRenderer>();
                m_Body.sharedMaterial = s_BulletMaterial;
                m_Body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (m_Stopped) m_Body.enabled = false;
            }
            if (!server) TryCreateHaloVisual();
        }

        private void TryCreateHaloVisual()
        {
            if (m_HaloVisual != null || !m_Initialized || m_Stopped || !m_Weapon.ShowProjectileBody ||
                !DollSingerWeapons.IsHalo(m_Data.WeaponID)) return;
            // The projectile can arrive before its shooter ghost on an observer.
            if (m_Shooter == null) ResolveShooter(false);
            if (m_Shooter == null) return;
            var source = m_Shooter.GetComponentInChildren<DollSingerHaloAim>(true);
            if (source == null) return;
            var visuals = new GameObject("Halo projectile visuals");
            visuals.transform.SetParent(transform, false);
            m_HaloVisual = visuals.AddComponent<HaloProjectileVisual>();
            m_HaloVisual.Configure(source, m_Data.WeaponID == DollSingerWeapons.Revolver, m_Data.WeaponID == DollSingerWeapons.Shotgun);
            m_HaloTrailSeconds = Mathf.Max(0.001f, source.networkBoltTrailSeconds);
            if (m_Body != null) m_Body.enabled = false;
            UpdateHaloVisual();
        }

        private void UpdateHaloVisual()
        {
            if (m_HaloVisual == null || m_Stopped) return;
            var tail = Ballistics.Position(m_Data.Origin, m_Data.InitialVelocity, m_Weapon.ProjectileGravity,
                Mathf.Max(0f, m_Age - m_HaloTrailSeconds));
            m_HaloVisual.SetFlight(tail, transform.position);
        }

        private void ResolveShooter(bool server)
        {
            if (m_Shooter != null || GhostGameObject == null || !GhostGameObject.IsGhostLinked()) return;
            var manager = server ? PlayerGhostManager.ServerInstance : PlayerGhostManager.ClientInstance;
            if (manager != null && manager.TryGetPlayersByRole(server ? MultiplayerRole.Server : MultiplayerRole.ClientAll, out var players))
                foreach (var player in players)
                    if (player.GhostGameObject.Owner == m_Data.OwnerNetworkId && player.GhostGameObject.World == GhostGameObject.World)
                    { m_Shooter = player.transform; break; }
        }

        public override void OnGhostLinked()
        {
            m_Data = GhostGameObject.ReadGhostComponentData<ProjectileData>();
            if (GhostGameObject.TryReadSingleton<ClientServerTickRate>(out var rates))
            {
                rates.ResolveDefaults();
                m_TickDuration = 1f / rates.SimulationTickRate;
            }
            bool server = Role == MultiplayerRole.Server;
            m_Age = 0f;
            ResolveShooter(server);
            Initialize(server);
        }

        private void Update()
        {
            if (m_Initialized && (GhostGameObject == null || !GhostGameObject.IsGhostLinked()))
            {
                TryCreateHaloVisual();
                Advance(Mathf.Min(m_Weapon.ProjectileLifetime, m_Age + Time.deltaTime), false);
                UpdateHaloVisual();
            }
        }

        private float NetworkAge(bool server)
        {
            if (!GhostGameObject.TryReadSingleton<NetworkTime>(out var time)) return m_Age;
            bool predicted = !server && Role == MultiplayerRole.ClientOwned;
            var tick = server || predicted ? time.ServerTick : time.InterpolationTick;
            float fraction = server || predicted ? time.ServerTickFraction : time.InterpolationTickFraction;
            if (!tick.IsValid) return m_Age;
            int ticks = unchecked((int)(tick.TickIndexForValidTick - m_Data.FireTick));
            return Mathf.Max(0f, (ticks + fraction - 1f) * m_TickDuration);
        }

        public void UpdateServer(float deltaTime)
        {
            // GhostBridge destroys entities during its UpdateServer lifecycle phase.
            if (m_Stopped) GhostGameObject.DestroyEntity();
        }

        public void PhysicsUpdateServer(float deltaTime)
        {
            if (!m_Initialized || m_Stopped) return;
            Advance(Mathf.Min(m_Weapon.ProjectileLifetime, NetworkAge(true)), true);
            if (m_Age >= m_Weapon.ProjectileLifetime) Stop();
            var pose = GhostGameObject.ReadGhostComponentData<LocalTransform>();
            pose.Position = transform.position;
            pose.Rotation = transform.rotation;
            GhostGameObject.WriteGhostComponentData(pose);
        }

        public void UpdateClient(float deltaTime)
        {
            if (!m_Initialized) return;
            TryCreateHaloVisual();
            Advance(Mathf.Min(m_Weapon.ProjectileLifetime, NetworkAge(false)), false);
            UpdateHaloVisual();
        }

        private void Advance(float targetAge, bool server)
        {
            if (m_Stopped || targetAge <= m_Age) return;
            var scene = gameObject.scene.GetPhysicsScene();
            // Sweep the entire travelled arc, including a stalled frame's path.
            while (m_Age < targetAge)
            {
                float nextAge = Mathf.Min(targetAge, m_Age + 1f / 120f);
                Vector3 from = Ballistics.Position(m_Data.Origin, m_Data.InitialVelocity, m_Weapon.ProjectileGravity, m_Age);
                Vector3 to = Ballistics.Position(m_Data.Origin, m_Data.InitialVelocity, m_Weapon.ProjectileGravity, nextAge);
                if (Ballistics.Sweep(scene, from, to, m_Weapon.ProjectileRadius, m_HitMask, m_Shooter, ref m_Hits, out var hit))
                {
                    transform.position = hit.point;
                    Stop();
                    if (server) Impact(hit);
                    else MoonkovAudio.Play(m_Weapon.WeaponImpactSfx, hit.point);
                    return;
                }
                m_Age = nextAge;
                transform.position = to;
                var velocity = Ballistics.Velocity(m_Data.InitialVelocity, m_Weapon.ProjectileGravity, m_Age);
                if (velocity.sqrMagnitude > .00001f) transform.rotation = Quaternion.LookRotation(velocity);
            }
            if (!server && m_Age >= m_Weapon.ProjectileLifetime) Stop();
        }

        private void Stop()
        {
            m_Stopped = true;
            if (m_Body != null) m_Body.enabled = false;
            if (m_HaloVisual != null) m_HaloVisual.Stop();
        }

        private void Impact(RaycastHit hit)
        {
            var system = GhostGameObject.World.GetExistingSystemManaged<ServerPlayerMovementSystem>();
            var players = system.GetComponentLookup<PredictedPlayerGhost>();
            var owners = system.GetComponentLookup<GhostOwner>();
            if (m_Weapon.Behavior == ProjectileBehavior.AreaOfEffect)
            {
                var damaged = new HashSet<Entity>();
                var colliders = UnityEngine.Physics.OverlapSphere(hit.point, m_Weapon.AoeRadius, LayerMask.GetMask("ServerPlayer"));
                foreach (var collider in colliders)
                    if (GhostGameObject.TryFindGhostGameObject(collider.gameObject, out var target) && damaged.Add(target.LinkedEntity))
                        Damage(target, players, owners);
            }
            else if (GhostGameObject.TryFindGhostGameObject(hit.collider.gameObject, out var target))
                Damage(target, players, owners);
            if (m_Weapon.ProjectileHitVfxPrefab != null && m_Weapon.ProjectileHitVfxPrefab.GhostGuid.IsValid)
                GhostSpawner.SpawnGhostPrefab(m_Weapon.ProjectileHitVfxPrefab, hit.point,
                    Quaternion.LookRotation(hit.normal.sqrMagnitude > .001f ? hit.normal : Vector3.up), GhostGameObject.GenerateRandomHash());
        }

        private void Damage(GhostGameObject target, ComponentLookup<PredictedPlayerGhost> players, ComponentLookup<GhostOwner> owners)
        {
            if (target.World != GhostGameObject.World || !players.HasComponent(target.LinkedEntity) || !owners.HasComponent(target.LinkedEntity)) return;
            int owner = owners[target.LinkedEntity].NetworkId;
            if (owner == m_Data.OwnerNetworkId) return;
            var player = players.GetRefRW(target.LinkedEntity);
            if (player.ValueRO.CurrentHealth <= 0) return;
            player.ValueRW.CurrentHealth -= m_Weapon.Damage;
            player.ValueRW.ControllerState.IsHit = true;
            player.ValueRW.LastDamageAmount = m_Weapon.Damage;
            player.ValueRW.LastHitTick = GhostGameObject.GetCurrentTick();
            if (player.ValueRO.CurrentHealth <= 0 && LeaderboardManager.Instance != null)
                LeaderboardManager.Instance.AddKill(m_Data.OwnerNetworkId, owner);
        }
    }
}
