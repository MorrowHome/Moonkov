using UnityEngine;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS
{
    // Observes rendered client state, outside the prediction replay loop.
    public sealed class MoonkovPlayerAudio
    {
        private bool m_Initialized;
        private uint m_ReloadTick, m_JumpTick, m_LandTick, m_HitTick, m_Weapon;
        private float m_Stride, m_SurfaceTimer;
        private Vector3 m_Position;
        private SoundDef m_Surface;
        private DollSingerStance m_Stance;
        private float m_TurnTravel, m_Yaw, m_GearAt;
        private readonly MoonkovSurfaceFoley m_Wall = new MoonkovSurfaceFoley();
        private UnityEngine.CharacterController m_Capsule;
        private SoundSystem.SoundInfo m_WallSound, m_MovementSound, m_StanceSound;
        private float m_LastStepTime = -1, m_BreathAt, m_HeartAt, m_Health, m_Oxygen, m_Energy, m_Hydration;
        private bool m_Reloading, m_WasSealed, m_BreathEffort;
        private int m_Ammo;
        private SoundSystem.SoundInfo m_BreathSound, m_HeartSound;
        private float m_FallSpeed;
        private readonly RaycastHit[] m_SurfaceHits = new RaycastHit[8];

        public static bool NewTick(uint tick, ref uint last)
        {
            if (tick == 0 || last != 0 && unchecked((int)(tick - last)) <= 0) return false;
            last = tick;
            return true;
        }

        public void Update(in PredictedPlayerGhost ghost, Transform player, bool owned, float deltaTime)
        {
            var library = MoonkovAudio.Library;
            if (library == null) return;
            Vector3 position = player.position;
            if (!m_Initialized)
            {
                m_Initialized = true;
                m_ReloadTick = ghost.LastReloadTick; m_JumpTick = ghost.LastJumpTick;
                m_LandTick = ghost.LastLandTick; m_HitTick = ghost.LastHitTick;
                m_Weapon = ghost.EquippedWeaponID; m_Position = position;
                Remember(ghost); m_BreathAt = Time.unscaledTime + .75f; m_HeartAt = Time.unscaledTime + 1;
                m_WasSealed = !ghost.BreathableAir;
                m_Stance = ghost.ControllerState.Stance; m_Yaw = ((Quaternion)ghost.ControllerState.CurrentRotation).eulerAngles.y;
                m_Capsule = player.GetComponent<UnityEngine.CharacterController>();
                return; // A late join must not replay historical events.
            }
            if (NewTick(ghost.LastReloadTick, ref m_ReloadTick))
            {
                var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghost.EquippedWeaponID);
                LocalOrWorld(weapon?.WeaponReloadSfx, position, owned);
            }
            if (NewTick(ghost.LastJumpTick, ref m_JumpTick)) LocalOrWorld(library.Jump, position, owned, .7f);
            if (NewTick(ghost.LastLandTick, ref m_LandTick))
            {
                SampleSurface(player, library);
                LocalOrWorld(m_Surface, position, owned, Mathf.Lerp(.65f, 1.2f, Mathf.Clamp01(m_FallSpeed / 10)));
                LocalOrWorld(library.Land, position, owned, Mathf.Lerp(.3f, .8f, Mathf.Clamp01(m_FallSpeed / 10)));
                m_Stride = 0f; m_LastStepTime = Time.unscaledTime; m_FallSpeed = 0;
            }
            if (NewTick(ghost.LastHitTick, ref m_HitTick))
                LocalOrWorld(library.Hit, position, owned, owned ? 1f : .65f);
            if (owned && m_Weapon != ghost.EquippedWeaponID) MoonkovAudio.Play(library.Equipment, position);
            if (m_Weapon == ghost.EquippedWeaponID && m_Reloading && !ghost.ControllerState.IsReloadingState &&
                ghost.CurrentAmmo > m_Ammo) LocalOrWorld(library.ReloadReady, position, owned, .7f);
            if (owned && ghost.CurrentHealth > 0)
            {
                if (m_Health > 0 && ghost.CurrentHealth > m_Health + 2) LocalOrWorld(library.Medical, position, true);
                if (m_Health > 0 && ghost.Oxygen > m_Oxygen + 5) LocalOrWorld(library.OxygenRefill, position, true);
                if (m_Health > 0 && (ghost.Energy > m_Energy + 2 || ghost.Hydration > m_Hydration + 2))
                    LocalOrWorld(library.Cloth, position, true, .8f);
                bool sealedSuit = !ghost.BreathableAir;
                bool effort = ghost.ControllerState.Sprinting || ghost.CurrentHealth < ghost.MaxHealth * .35f ||
                    sealedSuit && ghost.Oxygen <= 10;
                if (sealedSuit != m_WasSealed || effort != m_BreathEffort)
                {
                    GameManager.Instance?.SoundSystem?.Stop(m_BreathSound, .3f);
                    m_BreathSound = null;
                    m_BreathAt = Time.unscaledTime + .45f;
                    m_WasSealed = sealedSuit; m_BreathEffort = effort;
                }
                if (Time.unscaledTime >= m_BreathAt)
                {
                    if (sealedSuit || effort)
                        m_BreathSound = MoonkovAudio.PlayWeaponLayer(library.SuitBreath, position,
                            effort ? 1f : .65f, 0, effort ? 180 : -15);
                    m_BreathAt = Time.unscaledTime + (effort ? 3.2f : 4.1f);
                }
                if (ghost.CurrentHealth < ghost.MaxHealth * .28f && Time.unscaledTime >= m_HeartAt)
                {
                    m_HeartSound = LocalOrWorld(library.Heartbeat, position, true, .6f);
                    m_HeartAt = Time.unscaledTime + 1.25f;
                }
            }
            else if (owned) Stop();
            if (ghost.CurrentHealth > 0 && m_Stance != ghost.ControllerState.Stance)
            {
                m_StanceSound = LocalOrWorld(library.StanceChange, position, owned,
                    ghost.ControllerState.Stance == DollSingerStance.Prone || m_Stance == DollSingerStance.Prone ? .85f : .5f);
                m_Stride = 0; m_TurnTravel = 0;
            }
            m_Stance = ghost.ControllerState.Stance;
            Remember(ghost);
            m_Weapon = ghost.EquippedWeaponID;
            m_FallSpeed = Mathf.Max(m_FallSpeed, -ghost.ControllerState.JumpFallSpeed);

            var travel = position - m_Position;
            travel.y = 0f;
            m_Position = position;
            bool grounded = ghost.ControllerState.MovementType == FirstPersonController.MovementType.Standing;
            if (!grounded || ghost.CurrentHealth <= 0f || deltaTime <= 0f || deltaTime > .25f || travel.magnitude > 2f)
            {
                m_Stride = 0f; m_TurnTravel = 0; m_Wall.Reset();
                return;
            }
            float yaw = ((Quaternion)ghost.ControllerState.CurrentRotation).eulerAngles.y;
            float turn = Mathf.Abs(Mathf.DeltaAngle(m_Yaw, yaw)); m_Yaw = yaw;
            if (travel.sqrMagnitude < .000001f)
            {
                m_Stride = 0; m_Wall.Reset();
                if (turn < 90) m_TurnTravel += turn;
                if (m_TurnTravel >= 50 && Time.time >= m_GearAt && m_Stance != DollSingerStance.Prone)
                {
                    m_MovementSound = LocalOrWorld(library.GearMotion, position, owned, .4f);
                    m_TurnTravel = 0; m_GearAt = Time.time + .9f;
                }
                return;
            }
            m_TurnTravel = 0;
            if ((owned || MoonkovAudio.CanHearDetail(position, 12)) && ghost.ControllerState.MovementSpeed > .15f)
            {
                float height = m_Capsule ? m_Capsule.height : DollSingerStances.Height(m_Stance, 1.7f, .25f);
                Vector3 center = m_Capsule ? player.TransformPoint(m_Capsule.center) : position + Vector3.up * (height * .55f);
                float reach = (m_Capsule ? m_Capsule.radius : .25f) + .12f;
                if (m_Wall.TrySlide(player, center, travel, reach, deltaTime, out var wall))
                {
                    bool metal = RifleImpactSurface.Classify(wall.collider) == RifleImpactKind.Metal;
                    m_WallSound = MoonkovAudio.PlayWeaponLayer(metal ? library.SuitWallMetal : library.SuitWallBrush,
                        wall.point, ghost.ControllerState.Sprinting ? .8f : .55f, owned ? .65f : 1);
                }
            }
            m_SurfaceTimer -= deltaTime;
            if (m_SurfaceTimer <= 0f)
            {
                m_SurfaceTimer = .2f;
                SampleSurface(player, library);
            }
            m_Stride += travel.magnitude;
            float stride = m_Stance == DollSingerStance.Prone ? .55f :
                m_Stance == DollSingerStance.Crouching || m_Stance == DollSingerStance.Tiptoe ? .6f :
                ghost.ControllerState.Sprinting ? 1.35f : .9f;
            if (m_Stride < stride || Time.unscaledTime - m_LastStepTime < .18f) return;
            m_Stride %= stride; // At most one step after a stalled frame or reconciliation.
            m_LastStepTime = Time.unscaledTime;
            if (m_Stance == DollSingerStance.Prone)
            {
                m_MovementSound = LocalOrWorld(library.Crawl, position, owned, .65f);
                return;
            }
            float volume = m_Stance == DollSingerStance.Tiptoe ? .2f :
                m_Stance == DollSingerStance.Crouching ? .38f : ghost.ControllerState.Sprinting ? 1f : .72f;
            LocalOrWorld(m_Surface ?? library.RegolithFootsteps, position, owned, volume);
            LocalOrWorld(library.Cloth, position, owned, .2f * volume);
        }
        private void Remember(in PredictedPlayerGhost ghost)
        {
            m_Reloading = ghost.ControllerState.IsReloadingState; m_Ammo = ghost.CurrentAmmo;
            m_Health = ghost.CurrentHealth; m_Oxygen = ghost.Oxygen; m_Energy = ghost.Energy; m_Hydration = ghost.Hydration;
        }
        public void Stop()
        {
            var system = GameManager.Instance?.SoundSystem;
            system?.Stop(m_BreathSound, .12f); system?.Stop(m_HeartSound, .12f);
            system?.Stop(m_WallSound, .08f); system?.Stop(m_MovementSound, .08f); system?.Stop(m_StanceSound, .08f);
            m_BreathSound = m_HeartSound = m_WallSound = m_MovementSound = m_StanceSound = null;
            m_Wall.Reset();
        }
        private static SoundSystem.SoundInfo LocalOrWorld(SoundDef sound, Vector3 position, bool owned, float gain = 1)
            => MoonkovAudio.PlayWeaponLayer(sound, position, gain, owned ? .08f : 1);

        private void SampleSurface(Transform player, MoonkovAudioLibrary library)
        {
            m_Surface = library.RegolithFootsteps;
            int count = player.gameObject.scene.GetPhysicsScene().Raycast(player.position + Vector3.up * .4f,
                Vector3.down, m_SurfaceHits, 1.7f, LayerMask.GetMask("Ground", "Default"), QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var hit = m_SurfaceHits[i];
                if (hit.collider.transform.IsChildOf(player) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                m_Surface = ClassifySurface(hit.collider, library);
            }
        }
        public static SoundDef ClassifySurface(Collider collider, MoonkovAudioLibrary library)
        {
            if (!collider || collider is TerrainCollider) return library.RegolithFootsteps;
            if (RifleImpactSurface.Classify(collider) == RifleImpactKind.Metal) return library.MetalFootsteps;
            string name = collider.name;
            var renderer = collider.GetComponent<Renderer>();
            string material = renderer && renderer.sharedMaterial ? renderer.sharedMaterial.name : "";
            if (material.StartsWith("R27S_", System.StringComparison.OrdinalIgnoreCase) ||
                material.IndexOf("regolith", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("rock", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("regolith", System.StringComparison.OrdinalIgnoreCase) >= 0) return library.RegolithFootsteps;
            return library.HardFootsteps ?? library.RegolithFootsteps;
        }
    }
}
