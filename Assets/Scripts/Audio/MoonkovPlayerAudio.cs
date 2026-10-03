using UnityEngine;

namespace Unity.MP_FPS
{
    // Observes rendered client state, outside the prediction replay loop.
    public sealed class MoonkovPlayerAudio
    {
        private bool m_Initialized;
        private uint m_ReloadTick, m_JumpTick, m_LandTick, m_HitTick, m_Weapon;
        private float m_Stride, m_SurfaceTimer;
        private Vector3 m_Position;
        private bool m_OnMetal;
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
                return; // A late join must not replay historical events.
            }
            if (NewTick(ghost.LastReloadTick, ref m_ReloadTick))
            {
                var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghost.EquippedWeaponID);
                MoonkovAudio.Play(weapon?.WeaponReloadSfx, position);
            }
            if (NewTick(ghost.LastJumpTick, ref m_JumpTick)) MoonkovAudio.Play(library.Jump, position);
            if (NewTick(ghost.LastLandTick, ref m_LandTick))
            {
                MoonkovAudio.Play(library.Land, position);
                m_Stride = 0f;
            }
            if (NewTick(ghost.LastHitTick, ref m_HitTick))
                MoonkovAudio.Play(library.Hit, position, owned ? 1f : .65f);
            if (owned && m_Weapon != ghost.EquippedWeaponID) MoonkovAudio.Play(library.Equipment, position);
            m_Weapon = ghost.EquippedWeaponID;

            var travel = position - m_Position;
            travel.y = 0f;
            m_Position = position;
            bool grounded = ghost.ControllerState.MovementType == FirstPersonController.MovementType.Standing;
            if (!grounded || ghost.CurrentHealth <= 0f || deltaTime <= 0f || deltaTime > .25f || travel.magnitude > 2f)
            {
                m_Stride = 0f;
                return;
            }
            if (travel.sqrMagnitude < .000001f) return;
            m_SurfaceTimer -= deltaTime;
            if (m_SurfaceTimer <= 0f)
            {
                m_SurfaceTimer = .2f;
                m_OnMetal = false;
                int count = player.gameObject.scene.GetPhysicsScene().Raycast(position + Vector3.up * .3f,
                    Vector3.down, m_SurfaceHits, 1.2f, LayerMask.GetMask("Ground", "Default"), QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    var hit = m_SurfaceHits[i];
                    if (hit.collider.transform.IsChildOf(player) || hit.distance >= nearest) continue;
                    nearest = hit.distance;
                    // The lunar Terrain is regolith; constructed collider surfaces use boot-on-metal foley.
                    m_OnMetal = hit.collider is not TerrainCollider;
                }
            }
            m_Stride += travel.magnitude;
            float stride = ghost.ControllerState.Sprinting ? 1.5f : 1.05f;
            if (m_Stride < stride) return;
            m_Stride %= stride; // At most one step after a stalled frame or reconciliation.
            float volume = ghost.ControllerState.Sprinting ? 1f : .75f;
            MoonkovAudio.Play(m_OnMetal ? library.MetalFootsteps : library.RegolithFootsteps, position, volume);
            MoonkovAudio.Play(library.Cloth, position, .35f * volume);
        }
    }
}
