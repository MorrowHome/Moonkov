using Unity.MP_FPS.DollSinger;
using UnityEngine;
using Unity.Mathematics;

namespace Unity.MP_FPS
{
    /// <summary>Client presentation for the template's predicted player, with no independent movement simulation.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class DollSingerNetworkPresentation : GhostMonoBehaviour
    {
        [SerializeField] private DollSingerMovement m_Model;
        [SerializeField] private DollSingerInput m_Input;
        [SerializeField] private DollSingerView m_View;
        [SerializeField] private DollSingerHaloAim m_Halo;
        private bool m_Linked;
        private bool m_ViewActivated;
        private uint m_LastJumpTick;
        private float2 m_FreeLookOffset;
        private Quaternion m_OwnedViewRotation = Quaternion.identity;
        private float m_OwnedLean;
        private bool m_OwnedLightEnabled = true;
        private RaycastHit[] m_AimHits = new RaycastHit[32];
        private const int AimMask = ~(1 << (int)LayerIndex.ServerPlayer);
        private bool m_OwnedAiming, m_HasOwnedAimPoint;
        private Vector3 m_OwnedAimPoint;
        private MoonkovPlayerAudio m_Audio;
        private uint m_LastShotEffectTick;
        private SoundSystem.SoundInfo m_RifleBodySound, m_RifleSnapSound;
        private SoundSystem.SoundInfo m_WeaponTailSound;
        private RifleFeedbackLibrary m_WeaponFeedback;
        private float m_LastShotSoundTime = float.NegativeInfinity, m_ShotSoundPressure;
        private uint m_LastSoundWeapon;
        private bool m_AudioAiming;
        private float m_DryFireAt;

        public DollSingerInput OwnedInput => m_Linked && Role == MultiplayerRole.ClientOwned ? m_Input : null;
        public bool IsThirdPerson => m_View != null && !m_View.IsFirstPerson;
        public float OwnedLeanAmount => m_OwnedLean;
        public float OwnedReticleScreenRoll => m_View ? -m_View.SightLeanRoll : 0f;

        public bool ReadOwnedLight(bool canRead)
        {
            if (canRead && m_Input.LightPressed)
            {
                m_OwnedLightEnabled = !m_OwnedLightEnabled;
                MoonkovAudio.Play(MoonkovAudio.Library?.Light, transform.position);
            }
            return m_OwnedLightEnabled;
        }

        public Vector3 GetGameplayLeanOffset(float lean, Quaternion bodyRotation, bool thirdPerson)
        {
            return thirdPerson ? Vector3.zero : m_Model.GetLeanOffset(lean, bodyRotation);
        }

        public bool UpdateOwnedLook(ref float2 look, out float2 freeLookOffset, bool blocked)
        {
            bool freeLooking = DollSingerLookController.Update(ref look, ref m_FreeLookOffset,
                ref m_OwnedLean, out m_OwnedViewRotation, m_Input, m_Model, m_View, blocked, Time.deltaTime);
            m_OwnedAiming = !blocked && m_Input.AimHeld && !freeLooking;
            freeLookOffset = m_FreeLookOffset;
            return freeLooking;
        }

        public Vector3 CaptureAimPoint(float2 look, float range)
        {
            var viewLook = look + m_FreeLookOffset;
            var ray = m_View.CaptureAimRay(Quaternion.Euler(viewLook.y, viewLook.x, 0f),
                Quaternion.Euler(0f, look.x, 0f), m_OwnedLean);
            var target = ray.GetPoint(range);
            UnityEngine.Physics.SyncTransforms();
            // Host server colliders duplicate the client avatars; ignore that world here.
            int count;
            var physicsScene = gameObject.scene.GetPhysicsScene();
            while ((count = physicsScene.Raycast(ray.origin, ray.direction, m_AimHits, range,
                AimMask, QueryTriggerInteraction.Ignore)) == m_AimHits.Length)
            {
                // Unordered saturated queries can omit the closest wall or target.
                // Grow only on saturation and reuse the buffer for subsequent frames.
                if (m_AimHits.Length >= 4096)
                {
                    m_HasOwnedAimPoint = true;
                    return m_OwnedAimPoint = ray.GetPoint(.05f);
                }
                System.Array.Resize(ref m_AimHits, m_AimHits.Length * 2);
            }
            float nearest = range;
            for (int i = 0; i < count; i++)
            {
                var hit = m_AimHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                target = hit.point;
            }
            m_HasOwnedAimPoint = true;
            return m_OwnedAimPoint = target;
        }

        public override void OnGhostLinked()
        {
            m_Linked = true;
            m_OwnedAiming = m_HasOwnedAimPoint = false;
            m_OwnedLightEnabled = true;
            m_AudioAiming = false; m_DryFireAt = 0;
            m_Audio?.Stop();
            m_Audio = Role == MultiplayerRole.Server ? null : new MoonkovPlayerAudio();
            m_Model.SetNetworkViewPresentation(false, Quaternion.identity, 0f);
            m_Model.enabled = Role != MultiplayerRole.Server;
            m_Model.GetComponent<UnityEngine.CharacterController>().enabled = false;
            m_Model.gameObject.SetActive(Role != MultiplayerRole.Server);
            if (Role == MultiplayerRole.ClientOwned)
                ActivateOwnedView();
            else
            {
                m_Input.enabled = false;
                m_View.gameObject.SetActive(false);
            }
        }

        public Camera ActivateOwnedView()
        {
            if (m_ViewActivated) return m_View.camera;
            m_ViewActivated = true;
            m_Input.GameplayInputBlocked = () => GameSettings.Instance.IsPauseMenuOpen || RaidHUD.PointerRequested;
            m_Model.gameObject.SetActive(true);
            m_View.gameObject.SetActive(true);
            m_Input.enabled = true;
            m_View.BindPlayer(m_Model.gameObject, true);
            m_View.camera.tag = "MainCamera";
            m_View.camera.enabled = true;
            return m_View.camera;
        }

        private void Update()
        {
            if (!m_Linked || Role == MultiplayerRole.Server || GhostGameObject.IsPendingDestroy)
                return;

            var ghost = ReadGhostComponentData<PredictedPlayerGhost>();
            var state = ghost.ControllerState;
            m_Audio?.Update(ghost, transform, Role == MultiplayerRole.ClientOwned, Time.deltaTime);
            bool jumped = ghost.LastJumpTick != 0 &&
                (m_LastJumpTick == 0 || (int)(ghost.LastJumpTick - m_LastJumpTick) > 0);
            if (jumped) m_LastJumpTick = ghost.LastJumpTick;
            m_Model.ApplyNetworkPresentation(state.MovementSpeed, state.JumpFallSpeed,
                state.MovementType == FirstPersonController.MovementType.Standing,
                state.Sprinting, jumped, Time.deltaTime, (Vector3)state.AnimatorMotion,
                ((Quaternion)state.CurrentRotation).eulerAngles.y);
            var viewRotation = Quaternion.Euler(state.PitchDegrees, state.YawDegrees, 0f);
            var headLook = new float2(state.YawDegrees, state.PitchDegrees) + state.FreeLookOffset;
            var headRotation = Quaternion.Euler(headLook.y, headLook.x, 0f);
            bool owned = Role == MultiplayerRole.ClientOwned;
            m_Model.SetNetworkViewPresentation(owned ? m_View.IsFirstPerson : state.FirstPersonView,
                owned ? m_OwnedViewRotation : headRotation, owned ? m_OwnedLean : state.Lean);
            m_Halo.SetNetworkPresentation(owned ? m_OwnedAiming : state.Aiming,
                (owned ? m_OwnedViewRotation : viewRotation) * Vector3.forward,
                owned && m_HasOwnedAimPoint ? m_OwnedAimPoint : (Vector3)ghost.AimPoint,
                owned ? m_OwnedLightEnabled : state.HaloLightEnabled);
            var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghost.EquippedWeaponID, ghost.EquippedModules);
            var haloWeapon = ghost.EquippedWeaponID == DollSingerWeapons.Halo ? DollSingerHaloAim.HaloWeapon.Rifle :
                ghost.EquippedWeaponID == DollSingerWeapons.Revolver ? DollSingerHaloAim.HaloWeapon.Revolver :
                ghost.EquippedWeaponID == DollSingerWeapons.Shotgun ? DollSingerHaloAim.HaloWeapon.Shotgun :
                ghost.EquippedWeaponID == DollSingerWeapons.Sniper ? DollSingerHaloAim.HaloWeapon.Sniper : DollSingerHaloAim.HaloWeapon.None;
            m_Halo.SetNetworkWeapon(haloWeapon, ghost.CurrentAmmo,
                state.IsReloadingState, weapon != null ? 1f - ghost.ReloadTimer / Mathf.Max(0.01f, weapon.ReloadTime * RaidHealth.HandlingMultiplier(ghost)) : 0f,
                ghost.LastShotTick, ghost.LastReloadTick, ghost.ReloadTargetAmmo, ghost.EquippedModules, weapon?.MagazineSize ?? 4, ghost.BurstSequence);
            if (Role == MultiplayerRole.ClientOwned)
            {
                bool audibleAim = m_OwnedAiming && ghost.CurrentHealth > 0;
                if (audibleAim != m_AudioAiming) MoonkovAudio.Play(MoonkovAudio.Library?.Aim, transform.position, .65f);
                m_AudioAiming = audibleAim;
                if (m_Input.FirePressed && ghost.CurrentHealth > 0 && ghost.CurrentAmmo <= 0 &&
                    !state.IsReloadingState && ghost.WeaponEquipRemaining <= 0 && Time.unscaledTime >= m_DryFireAt &&
                    !GameSettings.Instance.IsPauseMenuOpen && !RaidHUD.PointerRequested)
                {
                    MoonkovAudio.Play(MoonkovAudio.Library?.DryFire, transform.position);
                    m_DryFireAt = Time.unscaledTime + .35f;
                }
                m_View.SetNetworkLookRotation(m_OwnedViewRotation);
                m_View.SetAimBlend(m_Halo.AimBlend);
                m_View.SetWeaponAimMagnification(m_Halo.ScopeMagnification);
            }
        }

        public void PlayShot(Vector3 aimPoint, uint weaponId, uint shotTick = 0)
        {
            if (m_Linked && Role != MultiplayerRole.Server)
            {
                // The shot RPC may precede the equipment snapshot on an observer.
                if (ReadGhostComponentData<PredictedPlayerGhost>().EquippedWeaponID != weaponId) return;
                if (shotTick != 0 && !MoonkovPlayerAudio.NewTick(shotTick, ref m_LastShotEffectTick)) return;
                var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId);
                if (!m_WeaponFeedback) m_WeaponFeedback = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
                var profile = m_WeaponFeedback?.ForWeapon(weaponId);
                if (weaponId == DollSingerWeapons.Shotgun && profile != null)
                {
                    // Preserve the shotgun mix the player already likes.
                    GameManager.Instance.SoundSystem?.Stop(m_RifleBodySound, .025f);
                    GameManager.Instance.SoundSystem?.Stop(m_RifleSnapSound, .015f);
                    GameManager.Instance.SoundSystem?.Stop(m_WeaponTailSound, .03f);
                    m_RifleBodySound = MoonkovAudio.Play(profile.FireBody, transform.position);
                    m_RifleSnapSound = MoonkovAudio.Play(profile.FireSnap, transform.position);
                    m_WeaponTailSound = MoonkovAudio.Play(profile.FireTail, transform.position);
                }
                else if (weaponId == DollSingerWeapons.Halo || profile != null)
                {
                    bool sustained = m_LastSoundWeapon == weaponId && Time.unscaledTime - m_LastShotSoundTime < .22f;
                    m_ShotSoundPressure = sustained ? Mathf.Min(1, m_ShotSoundPressure + .18f) : 0;
                    m_LastSoundWeapon = weaponId; m_LastShotSoundTime = Time.unscaledTime;
                    // The new attack remains crisp; only the preceding body and resonance crossfade.
                    GameManager.Instance.SoundSystem?.Stop(m_RifleBodySound, profile?.BodyCrossfade ?? .045f);
                    GameManager.Instance.SoundSystem?.Stop(m_RifleSnapSound, .018f);
                    GameManager.Instance.SoundSystem?.Stop(m_WeaponTailSound, profile?.TailCrossfade ?? .09f);
                    bool owned = Role == MultiplayerRole.ClientOwned;
                    Vector3 origin = m_Halo.haloVisual ? m_Halo.haloVisual.position : transform.position;
                    float bodyGain = Mathf.Lerp(1, profile?.SustainedBodyGain ?? .9f, m_ShotSoundPressure);
                    m_RifleBodySound = MoonkovAudio.PlayWeaponLayer(profile?.FireBody ?? weapon?.WeaponFireSfx,
                        origin, bodyGain, owned ? .08f : 1f);
                    m_RifleSnapSound = MoonkovAudio.PlayWeaponLayer(profile?.FireSnap ?? weapon?.WeaponFireLayerSfx,
                        origin, .85f, owned ? 0f : 1f, m_ShotSoundPressure * (profile?.BurstPitchRise ?? 35f));
                    m_WeaponTailSound = MoonkovAudio.PlayWeaponLayer(profile?.FireTail, origin, .85f, owned ? .2f : 1f);
                }
                else
                {
                    MoonkovAudio.Play(weapon?.WeaponFireSfx, transform.position);
                    MoonkovAudio.Play(weapon?.WeaponFireLayerSfx, transform.position);
                }
                m_Halo.PlayNetworkShot(aimPoint, weapon != null && weapon.Type == WeaponType.Hitscan, shotTick);
            }
        }

        public override void OnGhostPreDestroy()
        {
            m_Audio?.Stop();
            GameManager.Instance?.SoundSystem?.Stop(m_RifleBodySound, .025f);
            GameManager.Instance?.SoundSystem?.Stop(m_RifleSnapSound, .015f);
            GameManager.Instance?.SoundSystem?.Stop(m_WeaponTailSound, .03f);
            m_RifleBodySound = m_RifleSnapSound = m_WeaponTailSound = null;
            m_Linked = false;
            m_Input.enabled = false;
            m_Input.GameplayInputBlocked = null;
            m_View.gameObject.SetActive(false);
        }
    }
}
