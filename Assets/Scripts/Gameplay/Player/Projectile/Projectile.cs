// 一枚网络弹体的生命周期。本地预测先显示飞行，收到服务器对象后可由它接管。
// 双方根据起点、速度和时间算弹道。服务端负责结算实际命中、直接伤害或范围爆炸。
using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    // 每发生成对应的 Ghost 弹体。客户端计算飞行轨迹，实际伤害由服务器结算。
    public class Projectile : GhostMonoBehaviour, IUpdateServer, IPhysicsUpdateServer, IUpdateClient {
        // 快照保留弹道起点和初速度，以及用于匹配的武器、发射拍和弹丸序号。服务器另外保存这发的伤害归属。
        public struct ProjectileData : IComponentData {
            [GhostField] public int OwnerNetworkId;
            public ClashDamageSource DamageSource; // 只在服务器保存发射时的伤害归属。
            [GhostField] public uint SpawnTick;
            [GhostField] public uint WeaponID;
            [GhostField] public uint FireTick;
            [GhostField] public int PelletIndex;
            [GhostField] public ushort ModuleMask;
            [GhostField] public float3 Origin;
            [GhostField] public float3 InitialVelocity;
        }

        public class PredictedProjectileInfo {
            public GameObject Instance;
            public uint SpawnTick;
            public uint WeaponID;
            public int PelletIndex;
            public float CreatedAt;
        }

        public static List<PredictedProjectileInfo> PredictedProjectiles = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() {
            PredictedProjectiles.Clear();
            s_NearMissHits = null;
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

        // Ghost 的物理回调在 Unity 主线程依次运行。
        // 共用有限的查询缓冲，连发时不必给每发另外分配数组。
        private static Collider[] s_NearMissHits;
        private int m_HitMask;
        private int m_NearMissPlayerMask, m_NearMissWorldMask;
        private Transform m_Shooter;
        private Entity m_ShooterEntity;
        private MeshRenderer m_Body;
        private HaloProjectileVisual m_HaloVisual;
        private float m_HaloTrailSeconds;
        private static Material s_BulletMaterial;

        // 本地先以当前玩家信息补齐一份暂用的弹道数据，等实际 Ghost 出现再接到它身上。
        public void InitializePrediction(
            uint weaponId,
            Vector3 origin,
            Quaternion rotation,
            Transform shooter,
            float elapsed = 0f,
            uint shotTick = 0,
            int pelletIndex = 0,
            ushort modules = 0) {
            var player = shooter ? shooter.GetComponent<PlayerGhost>() : null;
            m_Data = new ProjectileData {
                WeaponID = weaponId, Origin = origin, SpawnTick = shotTick,
                PelletIndex = pelletIndex, ModuleMask = modules,
                OwnerNetworkId = player && player.GhostGameObject ? player.GhostGameObject.Owner : 0,
                InitialVelocity = rotation * Vector3.forward *
                                  WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId).ProjectileSpeed
            };
            m_Shooter = shooter;
            Initialize(false);
            Advance(elapsed, false);
            UpdateHaloVisual();
        }

        private void Initialize(bool server) {
            m_Weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(m_Data.WeaponID, m_Data.ModuleMask);
            m_HitMask = LayerMask.GetMask(server ? "ServerPlayer" : "ClientPlayer", "Ground", "Default");
            if (server) {
                m_NearMissPlayerMask = LayerMask.GetMask("ServerPlayer");
                m_NearMissWorldMask = LayerMask.GetMask("Ground", "Default");
            }
            m_Initialized = m_Weapon != null;
            if (!server && m_Initialized && m_Weapon.ShowProjectileBody && m_Body == null) {
                var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                body.name = "Ballistic bullet";
                var collider = body.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                body.transform.SetParent(transform, false);
                var scale = transform.lossyScale;
                body.transform.localScale = new Vector3(.035f / scale.x, .035f / scale.y, .12f / scale.z);
                if (s_BulletMaterial == null) {
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

        private void TryCreateHaloVisual() {
            if (m_HaloVisual != null || !m_Initialized || m_Stopped || !m_Weapon.ShowProjectileBody ||
                !DollSingerWeapons.IsHalo(m_Data.WeaponID))
                return;
            // 远端观察者可能先收到这颗弹体，再收到开枪的人。
            if (m_Shooter == null) ResolveShooter(false);
            if (m_Shooter == null) return;
            var source = m_Shooter.GetComponentInChildren<DollSingerHaloAim>(true);
            if (source == null) return;
            var visuals = new GameObject("Halo projectile visuals");
            visuals.transform.SetParent(transform, false);
            m_HaloVisual = visuals.AddComponent<HaloProjectileVisual>();
            m_HaloVisual.Configure(source, m_Data.WeaponID == DollSingerWeapons.Revolver,
                m_Data.WeaponID == DollSingerWeapons.Shotgun, m_Data.WeaponID == DollSingerWeapons.Sniper,
                m_Data.PelletIndex == 0);
            float limit = HaloProjectileVisual.TrailLengthLimit(m_Data.WeaponID == DollSingerWeapons.Revolver,
                m_Data.WeaponID == DollSingerWeapons.Shotgun, m_Data.WeaponID == DollSingerWeapons.Sniper);
            m_HaloTrailSeconds = Mathf.Min(Mathf.Max(.001f, source.networkBoltTrailSeconds),
                limit / Mathf.Max(1, ((Vector3)m_Data.InitialVelocity).magnitude));
            if (m_Body != null) m_Body.enabled = false;
            UpdateHaloVisual();
        }

        private void UpdateHaloVisual() {
            if (m_HaloVisual == null || m_Stopped) return;
            // 让发光尾迹别贴着手或眼睛，碰撞仍从真实开火位置开始。
            float emergence = .55f / Mathf.Max(1, ((Vector3)m_Data.InitialVelocity).magnitude);
            float tailAge = Mathf.Min(m_Age, Mathf.Max(emergence, m_Age - m_HaloTrailSeconds));
            var tail = Ballistics.Position(m_Data.Origin, m_Data.InitialVelocity, m_Weapon.ProjectileGravity, tailAge);
            // 生成或合并预测对象时，场景上的 Transform 可能暂时属于另一拍。
            // 用同一个经过时间算尾迹两端，避免拉出一条从射出点到快照位置的长线。
            var head = Ballistics.Position(m_Data.Origin, m_Data.InitialVelocity, m_Weapon.ProjectileGravity, m_Age);
            m_HaloVisual.SetFlight(tail, head);
        }

        // 用当前 World 查这发属于哪个角色。命中查询跳过它的碰撞体，服务端压制判断也需要知道发射者。
        private void ResolveShooter(bool server) {
            if (m_Shooter != null || GhostGameObject == null || !GhostGameObject.IsGhostLinked()) return;
            var manager = server ? PlayerGhostManager.ServerInstance : PlayerGhostManager.ClientInstance;
            if (manager != null &&
                manager.TryGetPlayersByRole(server ? MultiplayerRole.Server : MultiplayerRole.ClientAll,
                    out var players))
                foreach (var player in players) {
                    if (player.GhostGameObject.World != GhostGameObject.World) continue;
                    bool captured = server && (m_Data.DamageSource.ParticipantId > 0 ||
                                               BountyClashDamage.HasCapturedOnlineActor(m_Data.DamageSource) ||
                                               m_Data.DamageSource.ExpeditionActor != Entity.Null);
                    bool same = captured
                        ? BountyClashDamage.IsSelf(GhostGameObject.World, player.GhostGameObject.LinkedEntity,
                            m_Data.DamageSource)
                        : player.GhostGameObject.Owner == m_Data.OwnerNetworkId;
                    if (same) {
                        m_Shooter = player.transform;
                        m_ShooterEntity = player.GhostGameObject.LinkedEntity;
                        break;
                    }
                }
        }

        public override void OnGhostLinked() {
            m_Data = GhostGameObject.ReadGhostComponentData<ProjectileData>();
            if (GhostGameObject.TryReadSingleton<ClientServerTickRate>(out var rates)) {
                rates.ResolveDefaults();
                m_TickDuration = 1f / rates.SimulationTickRate;
            }
            bool server = Role == MultiplayerRole.Server;
            m_Age = 0f;
            ResolveShooter(server);
            Initialize(server);
        }

        private void Update() {
            if (m_Initialized && (GhostGameObject == null || !GhostGameObject.IsGhostLinked())) {
                TryCreateHaloVisual();
                Advance(Mathf.Min(m_Weapon.ProjectileLifetime, m_Age + Time.deltaTime), false);
                UpdateHaloVisual();
            }
        }

        // 本机预测和服务器对齐服务器拍，观察者走自己的插值拍。从开火拍算经过的时间，使不同帧率能走同一弹道。
        private float NetworkAge(bool server) {
            if (!GhostGameObject.TryReadSingleton<NetworkTime>(out var time)) return m_Age;
            bool predicted = !server && Role == MultiplayerRole.ClientOwned;
            var tick = server || predicted ? time.ServerTick : time.InterpolationTick;
            float fraction = server || predicted ? time.ServerTickFraction : time.InterpolationTickFraction;
            if (!tick.IsValid) return m_Age;
            int ticks = unchecked((int)(tick.TickIndexForValidTick - m_Data.FireTick));
            return Mathf.Max(0f, (ticks + fraction - 1f) * m_TickDuration);
        }

        public void UpdateServer(float deltaTime) {
            // GhostBridge 在它的 UpdateServer 生命周期阶段销毁实体。
            if (m_Stopped) GhostGameObject.DestroyEntity();
        }

        // 物理回调推进真正的弹体，命中或到时间就结束。更新后把位姿写回 Ghost。
        public void PhysicsUpdateServer(float deltaTime) {
            if (!m_Initialized || m_Stopped) return;
            Advance(Mathf.Min(m_Weapon.ProjectileLifetime, NetworkAge(true)), true);
            if (m_Age >= m_Weapon.ProjectileLifetime) Stop();
            var pose = GhostGameObject.ReadGhostComponentData<LocalTransform>();
            pose.Position = transform.position;
            pose.Rotation = transform.rotation;
            GhostGameObject.WriteGhostComponentData(pose);
        }

        public void UpdateClient(float deltaTime) {
            if (!m_Initialized) return;
            TryCreateHaloVisual();
            Advance(Mathf.Min(m_Weapon.ProjectileLifetime, NetworkAge(false)), false);
            UpdateHaloVisual();
        }

        private void Advance(float targetAge, bool server) {
            if (m_Stopped || targetAge <= m_Age) return;
            var scene = gameObject.scene.GetPhysicsScene();
            var threatFrom =
                Ballistics.Position(m_Data.Origin, m_Data.InitialVelocity, m_Weapon.ProjectileGravity, m_Age);
            float threatAge = m_Age;
            // 扫过这段时间的整条已飞曲线，卡顿期间经过的路径也要算。
            // 补算这一帧之间的整条曲线，每小段扫一次碰撞。只检查终点会漏掉途中穿过的薄墙。
            while (m_Age < targetAge) {
                float nextAge = Mathf.Min(targetAge, m_Age + 1f / 120f);
                var from = Ballistics.Position(m_Data.Origin, m_Data.InitialVelocity, m_Weapon.ProjectileGravity,
                    m_Age);
                var to = Ballistics.Position(m_Data.Origin, m_Data.InitialVelocity, m_Weapon.ProjectileGravity,
                    nextAge);
                if (Ballistics.Sweep(scene, from, to, m_Weapon.ProjectileRadius, m_HitMask, m_Shooter, ref m_Hits,
                        out var hit)) {
                    if (server) ObserveNearMiss(scene, threatFrom, hit.point, hit.collider);
                    transform.position = hit.point;
                    Stop();
                    // 两边都能让弹体飞到碰撞点。只有服务器结算命中，拥有者的本机碰撞仅能先播视觉反馈。
                    if (server) { Impact(hit); } else if (DollSingerWeapons.IsHalo(m_Data.WeaponID)) {
                        // 只有发射者自己的客户端先预测碰撞表现，观察者用服务端确认的命中事件。
                        bool owned = GhostGameObject == null || !GhostGameObject.IsGhostLinked() ||
                                     Role == MultiplayerRole.ClientOwned;
                        if (owned && VisualEffectManager.ClientInstance != null)
                            VisualEffectManager.ClientInstance.RifleImpacts.Present(new ClientRifleImpactRpc {
                                OwnerNetworkId = m_Data.OwnerNetworkId, ShotTick = m_Data.SpawnTick,
                                WeaponId = m_Data.WeaponID, PelletIndex = m_Data.PelletIndex,
                                Position = hit.point, Normal = hit.normal,
                                Kind = (byte)RifleImpactSurface.Classify(hit.collider)
                            }, false, true);
                    } else { MoonkovAudio.Play(m_Weapon.WeaponImpactSfx, hit.point); }
                    return;
                }
                m_Age = nextAge;
                transform.position = to;
                // 已经飞过的路线攒一小段再发擦弹线索，避免每个很短的弹道子步重复扫附近 AI。
                if (server && m_Age - threatAge >= .1f) {
                    ObserveNearMiss(scene, threatFrom, to, null);
                    threatFrom = to;
                    threatAge = m_Age;
                }
                var velocity = Ballistics.Velocity(m_Data.InitialVelocity, m_Weapon.ProjectileGravity, m_Age);
                if (velocity.sqrMagnitude > .00001f) transform.rotation = Quaternion.LookRotation(velocity);
            }
            if (server && m_Age > threatAge) ObserveNearMiss(scene, threatFrom, transform.position, null);
            if (!server && m_Age >= m_Weapon.ProjectileLifetime) Stop();
        }

        private void ObserveNearMiss(PhysicsScene scene, Vector3 from, Vector3 to, Collider impact) {
            if (m_ShooterEntity == Entity.Null) ResolveShooter(true);
            if (m_Weapon.Behavior != ProjectileBehavior.DirectDamage || m_ShooterEntity == Entity.Null ||
                GhostGameObject == null || !GhostGameObject.IsGhostLinked())
                return;
            ProjectileThreat.Observe(scene, GhostGameObject.World, m_ShooterEntity, m_Data.DamageSource, from, to,
                impact, GhostGameObject.World.Time.ElapsedTime, m_NearMissPlayerMask, m_NearMissWorldMask,
                ref s_NearMissHits);
        }

        // 停止时先隐藏弹体和通知尾迹结束。服务器的实体销毁留到后续生命周期回调。
        private void Stop() {
            m_Stopped = true;
            if (m_Body != null) m_Body.enabled = false;
            if (m_HaloVisual != null) m_HaloVisual.Stop();
        }

        // 确认命中后在这里分流。直接命中按距离和身体部位算伤害，范围武器交给爆炸规则。
        private void Impact(RaycastHit hit) {
            var system = GhostGameObject.World.GetExistingSystemManaged<ServerPlayerMovementSystem>();
            var players = system.GetComponentLookup<PredictedPlayerGhost>();
            var owners = system.GetComponentLookup<GhostOwner>();
            byte damageFlags = 0;
            if (m_Weapon.Behavior == ProjectileBehavior.AreaOfEffect) {
                var scene = gameObject.scene.GetPhysicsScene();
                var blast = hit.point + hit.normal * .03f;
                // 范围武器在实际命中点稍向表面外算爆炸，随后发出爆炸声线索与已确认的表现事件。
                ProjectileBlast.Apply(GhostGameObject.World, scene, blast, m_Weapon.AoeRadius, m_Weapon.Damage,
                    m_Data.DamageSource, GhostGameObject.GetCurrentTick(), out damageFlags);
                RaidEnvironmentalNoise.EmitExplosion(GhostGameObject.World.EntityManager, scene, blast,
                    RaidEnvironmentalNoise.ProjectileExplosionId(GhostGameObject.LinkedEntity),
                    GhostGameObject.World.Time.ElapsedTime);
            } else if (GhostGameObject.TryFindGhostGameObject(hit.collider.gameObject, out var target)) {
                damageFlags = Damage(target, players, owners,
                    RaidHealth.ResolveHit(target.GetComponent<PlayerGhost>(), hit.collider, hit.point), hit.point);
            }
            if (damageFlags != 0 && ((HaloModule)m_Data.ModuleMask & HaloModule.HitMarker) != 0)
                VisualEffectManager.ServerInstance?.Server_RequestHaloMark(m_Data.OwnerNetworkId, m_Data.SpawnTick,
                    hit.point);
            if (DollSingerWeapons.IsHalo(m_Data.WeaponID)) {
                VisualEffectManager.ServerInstance?.Server_RequestRifleImpact(new ClientRifleImpactRpc {
                    OwnerNetworkId = m_Data.OwnerNetworkId, ShotTick = m_Data.SpawnTick,
                    WeaponId = m_Data.WeaponID, PelletIndex = m_Data.PelletIndex,
                    Position = hit.point, Normal = hit.normal, Kind = (byte)RifleImpactSurface.Classify(hit.collider),
                    DamageFlags = damageFlags
                });
                return;
            }
            if (m_Weapon.ProjectileHitVfxPrefab != null && m_Weapon.ProjectileHitVfxPrefab.GhostGuid.IsValid)
                GhostSpawner.SpawnGhostPrefab(m_Weapon.ProjectileHitVfxPrefab, hit.point,
                    Quaternion.LookRotation(hit.normal.sqrMagnitude > .001f ? hit.normal : Vector3.up),
                    GhostGameObject.GenerateRandomHash());
        }

        private byte Damage(
            GhostGameObject target,
            ComponentLookup<PredictedPlayerGhost> players,
            ComponentLookup<GhostOwner> owners,
            BodyPart part,
            Vector3 impactPoint) {
            if (target.World != GhostGameObject.World || !players.HasComponent(target.LinkedEntity)) return 0;
            bool hasOwner = owners.HasComponent(target.LinkedEntity);
            int owner = hasOwner ? owners[target.LinkedEntity].NetworkId : int.MinValue;
            if (BountyClashDamage.IsSelf(GhostGameObject.World, target.LinkedEntity, m_Data.DamageSource)) return 0;
            var player = players.GetRefRW(target.LinkedEntity);
            if (player.ValueRO.CurrentHealth <= 0 || m_Weapon.Damage <= 0) return 0;
            var before = player.ValueRO;
            float damage = Ballistics.DamageAtDistance(m_Weapon, Vector3.Distance(m_Data.Origin, impactPoint));
            BountyClashDamage.Apply(GhostGameObject.World, target.LinkedEntity, m_Data.DamageSource, part, damage,
                GhostGameObject.GetCurrentTick(), RaidDamageKind.Bullet, (float3)impactPoint);
            return RaidHealth.ImpactFlags(before, player.ValueRO);
        }
    }
}