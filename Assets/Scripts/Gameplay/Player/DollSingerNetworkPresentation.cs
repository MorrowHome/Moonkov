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
        private readonly RaycastHit[] m_AimHits = new RaycastHit[32];
        private MoonkovPlayerAudio m_Audio;

        public DollSingerInput OwnedInput => m_Linked && Role == MultiplayerRole.ClientOwned ? m_Input : null;
        public bool IsThirdPerson => m_View != null && !m_View.IsFirstPerson;
        public float OwnedLeanAmount => m_OwnedLean;
        public float OwnedReticleScreenRoll => m_View ? -m_View.SightLeanRoll : 0f;

        public bool ReadOwnedLight(bool canRead)
        {
            if (canRead && m_Input.LightPressed) m_OwnedLightEnabled = !m_OwnedLightEnabled;
            return m_OwnedLightEnabled;
        }

        public Vector3 GetGameplayLeanOffset(float lean, Quaternion bodyRotation, bool thirdPerson)
        {
            return thirdPerson ? Vector3.zero : m_Model.GetLeanOffset(lean, bodyRotation);
        }

        public bool UpdateOwnedLook(ref float2 look, out float2 freeLookOffset, bool blocked)
        {
            bool freeLooking = !blocked && !IsThirdPerson && m_Input.AltHeld;
            if (freeLooking)
            {
                m_FreeLookOffset += new float2(m_Input.Look.x, -m_Input.Look.y);
                m_FreeLookOffset = math.clamp(m_FreeLookOffset,
                    new float2(-m_Model.m_FreeLookYawLimit, -m_Model.m_FreeLookPitchLimit),
                    new float2(m_Model.m_FreeLookYawLimit, m_Model.m_FreeLookPitchLimit));
            }
            else
            {
                if (!blocked) look += new float2(m_Input.Look.x, -m_Input.Look.y);
                float step = m_Model.m_FreeLookReturnSpeed * Time.deltaTime;
                m_FreeLookOffset.x = Mathf.MoveTowards(m_FreeLookOffset.x, 0f, step);
                m_FreeLookOffset.y = Mathf.MoveTowards(m_FreeLookOffset.y, 0f, step);
            }
            if (IsThirdPerson) m_FreeLookOffset = float2.zero;
            look.y = math.clamp(look.y, -80f, 80f);
            m_FreeLookOffset.y = math.clamp(look.y + m_FreeLookOffset.y, -80f, 80f) - look.y;
            freeLookOffset = m_FreeLookOffset;
            m_OwnedLean = blocked || IsThirdPerson ? 0f :
                Mathf.MoveTowards(m_OwnedLean, m_Input.LeanTarget, Time.deltaTime * m_Model.m_LeanSpeed);
            Quaternion bodyRotation = freeLooking ? m_Model.transform.rotation : Quaternion.Euler(0f, look.x, 0f);
            if (!blocked && !IsThirdPerson) m_OwnedLean = m_View.LimitLeanAmount(m_OwnedLean, bodyRotation);
            m_Model.SetNetworkLeanAmount(m_OwnedLean);
            var viewLook = look + m_FreeLookOffset;
            m_OwnedViewRotation = Quaternion.Euler(viewLook.y, viewLook.x, 0f);
            m_View.SetNetworkLookRotation(m_OwnedViewRotation);
            return freeLooking || math.lengthsq(m_FreeLookOffset) > 0.01f;
        }

        public Vector3 CaptureAimPoint(float2 look, float range)
        {
            var viewLook = look + m_FreeLookOffset;
            var ray = m_View.CaptureAimRay(Quaternion.Euler(viewLook.y, viewLook.x, 0f),
                Quaternion.Euler(0f, look.x, 0f), m_OwnedLean);
            var target = ray.GetPoint(range);
            UnityEngine.Physics.SyncTransforms();
            // Host server colliders duplicate the client avatars; ignore that world here.
            int count = gameObject.scene.GetPhysicsScene().Raycast(ray.origin,ray.direction, m_AimHits, range,
                ~LayerMask.GetMask("ServerPlayer"), QueryTriggerInteraction.Ignore);
            float nearest = range;
            for (int i = 0; i < count; i++)
            {
                var hit = m_AimHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                target = hit.point;
            }
            return target;
        }

        public override void OnGhostLinked()
        {
            m_Linked = true;
            m_OwnedLightEnabled = true;
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
            m_Halo.SetNetworkPresentation(state.Aiming, viewRotation * Vector3.forward, ghost.AimPoint,
                owned ? m_OwnedLightEnabled : state.HaloLightEnabled);
            var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghost.EquippedWeaponID);
            var haloWeapon = ghost.EquippedWeaponID == DollSingerWeapons.Halo ? DollSingerHaloAim.HaloWeapon.Rifle :
                ghost.EquippedWeaponID == DollSingerWeapons.Revolver ? DollSingerHaloAim.HaloWeapon.Revolver :
                ghost.EquippedWeaponID == DollSingerWeapons.Shotgun ? DollSingerHaloAim.HaloWeapon.Shotgun :
                ghost.EquippedWeaponID == DollSingerWeapons.Sniper ? DollSingerHaloAim.HaloWeapon.Sniper : DollSingerHaloAim.HaloWeapon.None;
            m_Halo.SetNetworkWeapon(haloWeapon, ghost.CurrentAmmo,
                state.IsReloadingState, weapon != null ? 1f - ghost.ReloadTimer / Mathf.Max(0.01f, weapon.ReloadTime) : 0f,
                ghost.LastShotTick, ghost.LastReloadTick, ghost.ReloadTargetAmmo);
            if (Role == MultiplayerRole.ClientOwned)
            {
                m_View.SetNetworkLookRotation(m_OwnedViewRotation);
                m_View.SetAimBlend(m_Halo.AimBlend);
                m_View.SetWeaponAimMagnification(m_Halo.ScopeMagnification);
            }
        }

        public void PlayShot(Vector3 aimPoint, uint weaponId)
        {
            if (m_Linked && Role != MultiplayerRole.Server)
            {
                // The shot RPC may precede the equipment snapshot on an observer.
                if (ReadGhostComponentData<PredictedPlayerGhost>().EquippedWeaponID != weaponId) return;
                var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId);
                m_Halo.PlayNetworkShot(aimPoint, weapon != null && weapon.Type == WeaponType.Hitscan);
            }
        }

        public override void OnGhostPreDestroy()
        {
            m_Linked = false;
            m_Input.enabled = false;
            m_Input.GameplayInputBlocked = null;
            m_View.gameObject.SetActive(false);
        }
    }
}
