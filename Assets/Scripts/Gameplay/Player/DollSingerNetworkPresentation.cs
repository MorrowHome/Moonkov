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
        private FirstPersonController m_Motor;
        private bool m_ViewActivated;
        private uint m_LastJumpTick;
        private float2 m_FreeLookOffset;
        private Quaternion m_OwnedViewRotation = Quaternion.identity;
        private float m_OwnedLean;
        private bool m_OwnedLightEnabled = true;
        private Client.HaloLightPanel m_LightPanel;
        private RaycastHit[] m_AimHits = new RaycastHit[32];
        private const int AimMask = ~(1 << (int)LayerIndex.ServerPlayer);
        private bool m_OwnedAiming, m_OwnedRaised, m_HasOwnedAimPoint;
        private Vector3 m_OwnedAimPoint;
        private MoonkovPlayerAudio m_Audio;
        private DollSingerMeleePresentation m_Melee;
        private EnergyShieldPresentation m_Protection;
        private Animator m_PresentationAnimator;
        public DollSingerMeleePresentation MeleePresentation => m_Melee;
        public Animator PresentationAnimator => m_PresentationAnimator;
        private uint m_LastShotEffectTick;
        private SoundSystem.SoundInfo m_RifleBodySound, m_RifleSnapSound;
        private SoundSystem.SoundInfo m_WeaponTailSound;
        private RifleFeedbackLibrary m_WeaponFeedback;
        private float m_LastShotSoundTime = float.NegativeInfinity, m_ShotSoundPressure;
        private uint m_LastSoundWeapon;
        private bool m_AudioAiming;
        private float m_DryFireAt;
        private PlayerGhost m_PlayerGhost;
        private bool m_OwnedAlive;
        private HaloRecoilResponse m_Recoil;
        private uint m_RecoilWeapon;
        private float2 m_OwnedLook;
        private Client.HaloObstructionView m_ObstructionView;
        public bool OwnedMuzzleObstructed { get; private set; }
        private const int CoverMask = (1 << (int)LayerIndex.Default) | (1 << (int)LayerIndex.Ground);

        public DollSingerInput OwnedInput => m_Linked && Role == MultiplayerRole.ClientOwned ? m_Input : null;
        public Camera OwnedCamera => m_Linked && m_ViewActivated && Role == MultiplayerRole.ClientOwned ? m_View.camera : null;
        public bool IsThirdPerson => m_View != null && !m_View.IsFirstPerson;
        public bool WeaponHandsAvailable => m_Halo && !m_Halo.ExternalHandPoseActive;
        public void PrepareDeploymentView(Camera transitionCamera)
        {
            if (Role != MultiplayerRole.ClientOwned || !m_View) return;
            m_View.TransitionCamera = transitionCamera;
            m_View.SetFirstPerson(true);
        }
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
            return thirdPerson || m_Model.Stance == DollSingerStance.Prone ? Vector3.zero : m_Model.GetLeanOffset(lean, bodyRotation);
        }

        public bool UpdateOwnedLook(ref float2 look, out float2 freeLookOffset, bool blocked)
        {
            if (blocked || !m_OwnedAlive || !WeaponHandsAvailable || m_Input.LightAdjusting || RaidHUD.DarkSightRequested || RaidHUD.QuickSlotToolSelected ||
                (m_View.IsFirstPerson && m_Input.AltHeld)) m_Recoil.Reset();
            else m_Recoil.Apply(ref look.y, -m_Input.Look.y * m_View.AimLookSensitivity, Time.deltaTime);
            if (m_Input.LightAdjusting)
            {
                m_OwnedAiming = m_OwnedAlive && !blocked && !(m_Melee && m_Melee.WantsPresentation) && !RaidHUD.DarkSightRequested && !RaidHUD.QuickSlotToolSelected && m_Input.LightPreviewAiming;
                freeLookOffset = m_FreeLookOffset;
                return false;
            }
            bool freeLooking = DollSingerLookController.Update(ref look, ref m_FreeLookOffset,
                ref m_OwnedLean, out m_OwnedViewRotation, m_Input, m_Model, m_View, blocked, Time.deltaTime);
            m_OwnedAiming = m_OwnedAlive && !blocked && !(m_Melee && m_Melee.WantsPresentation) && !RaidHUD.DarkSightRequested && !RaidHUD.QuickSlotToolSelected && m_Input.AimHeld && !freeLooking;
            freeLookOffset = m_FreeLookOffset;
            return freeLooking;
        }

        public Vector3 CaptureAimPoint(float2 look, float range)
        {
            m_OwnedLook = look;
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
                    return PresentOwnedAimPoint(ray.GetPoint(.05f));
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
            return PresentOwnedAimPoint(target);
        }

        private Vector3 PresentOwnedAimPoint(Vector3 target)
        {
            m_HasOwnedAimPoint = true;
            m_OwnedAimPoint = target;
            // The ECS input pass follows MonoBehaviour.Update. Forward its freshly
            // sampled target before Halo.LateUpdate, without waiting another frame.
            m_Halo.SetNetworkPresentation(m_OwnedAiming,
                m_OwnedViewRotation * Vector3.forward, target, m_OwnedLightEnabled, ReadOwnedReadyIntent());
            PresentMuzzleObstruction(target);
            return target;
        }

        public bool HasMuzzleObstruction(float2 look, Vector3 target, float lean, bool thirdPerson)
        {
            if (!m_PlayerGhost) m_PlayerGhost = GetComponent<PlayerGhost>();
            if (!m_PlayerGhost) return false;
            var command = new PlayerInput { LookYawPitchDegrees = look, AimPoint = target, Lean = lean };
            command.SetFlag(PlayerInput.InputFlag.ThirdPerson, thirdPerson);
            var ray = m_PlayerGhost.GetShotRay(command, 10000, out var requested);
            float distance = Vector3.Distance(ray.origin, requested);
            // A surface at the intended target is not an obstruction. Ignore player
            // copies entirely here: this cue describes cover, never damage or a hit.
            return distance > .1f && m_PlayerGhost.RaycastShot(ray, distance - .08f, CoverMask, out _);
        }

        private void PresentMuzzleObstruction(Vector3 target)
        {
            OwnedMuzzleObstructed = !m_Input.LightAdjusting &&
                m_Halo.OwnsHandPose && m_Halo.AimBlend > .45f &&
                HasMuzzleObstruction(m_OwnedLook, target, m_OwnedLean, IsThirdPerson);
            if (OwnedMuzzleObstructed && m_ObstructionView == null)
            {
                var hud = FindAnyObjectByType<RaidHUD>();
                if (hud) m_ObstructionView = new Client.HaloObstructionView(
                    hud.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement);
            }
            m_ObstructionView?.Present(OwnedMuzzleObstructed);
        }

        public bool ReadOwnedAimIntent()
        {
            bool blocked = !m_OwnedAlive || !WeaponHandsAvailable || m_Melee && m_Melee.WantsPresentation || GameSettings.Instance.IsPauseMenuOpen || RaidHUD.GameplayActionsBlocked || RaidHUD.DarkSightRequested || RaidHUD.QuickSlotToolSelected;
            var returning = DollSingerLookController.ReturnFreeLook(m_FreeLookOffset, m_Model.m_FreeLookReturnSpeed * Time.deltaTime);
            bool freeLooking = m_View.IsFirstPerson && (m_Input.AltHeld || math.lengthsq(returning) > .01f);
            return m_OwnedAiming = !blocked && (m_Input.LightAdjusting ? m_Input.LightPreviewAiming :
                m_Input.CanReadPlayerInput && m_Input.AimHeld && !freeLooking);
        }

        public bool ReadOwnedReadyIntent()
        {
            bool blocked = !m_OwnedAlive || !WeaponHandsAvailable || m_Melee && m_Melee.WantsPresentation ||
                GameSettings.Instance.IsPauseMenuOpen || RaidHUD.GameplayActionsBlocked || RaidHUD.DarkSightRequested ||
                RaidHUD.QuickSlotToolSelected || m_Input.LightAdjusting || !m_Input.CanReadPlayerInput;
            return !blocked && !(m_View.IsFirstPerson && (m_Input.AltHeld || math.lengthsq(m_FreeLookOffset) > .01f)) &&
                (m_OwnedRaised || m_Input.FireHeld || m_Input.FirePressed);
        }

        public override void OnGhostLinked()
        {
            m_Linked = true;
            m_Motor = GetComponent<FirstPersonController>();
            m_OwnedAiming = m_OwnedRaised = m_HasOwnedAimPoint = false;
            m_Recoil.Reset(); m_RecoilWeapon = 0;
            m_LightPanel?.Dispose(); m_LightPanel = null;
            m_OwnedLightEnabled = true;
            m_AudioAiming = false; m_DryFireAt = 0;
            m_Audio?.Stop();
            m_Audio = Role == MultiplayerRole.Server ? null : new MoonkovPlayerAudio();
            m_Model.SetNetworkViewPresentation(false, Quaternion.identity, 0f);
            m_ObstructionView?.Dispose(); m_ObstructionView = null;
            OwnedMuzzleObstructed = false;
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
            if (Role != MultiplayerRole.Server)
            {
                m_PresentationAnimator = m_Model.GetComponent<Animator>();
                m_Melee = m_Model.GetComponent<DollSingerMeleePresentation>();
                if (!m_Melee) m_Melee = m_Model.gameObject.AddComponent<DollSingerMeleePresentation>();
                m_Melee.Configure(m_PresentationAnimator, m_View, m_Halo);
                m_Melee.ResetPresentation();
                m_Protection = m_Model.GetComponent<EnergyShieldPresentation>();
                if (!m_Protection) m_Protection = m_Model.gameObject.AddComponent<EnergyShieldPresentation>();
                m_Protection.ResetPresentation();
            }
        }

        // Also available to the owning prediction adapter; confirmation uses the same sequence and timeline.
        public void PresentMelee(in MeleeViewState state)
        {
            if (m_Linked && Role != MultiplayerRole.Server && m_Melee) m_Melee.SetPresentation(state);
        }

        public Camera ActivateOwnedView()
        {
            if (m_ViewActivated) return m_View.camera;
            m_ViewActivated = true;
            m_Input.GameplayInputBlocked = () => GameSettings.Instance.IsPauseMenuOpen || RaidHUD.GameplayActionsBlocked;
            RaidQuickSlotInput.Bind(m_Input);
            m_Input.TimedActionInputActive = () => RaidHUD.TimedActionInputActive;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            m_Input.DevelopmentStyleToggle = Moon.MoonSilverPrint.ToggleDevelopmentStyle;
#endif
            m_Halo.OwnedAimIntent = ReadOwnedAimIntent;
            m_Halo.OwnedReadyIntent = ReadOwnedReadyIntent;
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
            bool downed = ghost.ClashEnabled && ghost.ClashLife == ClashLife.Downed;
            var presentedStance = downed ? DollSingerStance.Prone : state.Stance;
            m_OwnedAlive = ghost.CurrentHealth > 0;
            m_OwnedRaised = ghost.HipFireHoldRemaining > 0 || ghost.HaloPendingShot;
            m_Halo.HandlingMultiplier = RaidHealth.HandlingMultiplier(ghost);
            if (!m_OwnedAlive || downed || m_RecoilWeapon != ghost.EquippedWeaponID) m_Recoil.Reset();
            m_RecoilWeapon = ghost.EquippedWeaponID;
            if (Role == MultiplayerRole.ClientOwned)
            {
                if (ghost.CurrentHealth <= 0) m_Input.CancelLightAdjustment();
                if (m_Input.LightAdjusting && m_LightPanel == null)
                {
                    var hud = FindAnyObjectByType<RaidHUD>();
                    if (hud) m_LightPanel = new Client.HaloLightPanel(hud.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement, m_Input, m_Halo);
                }
                m_LightPanel?.Refresh(m_OwnedLightEnabled);
            }
            m_Halo.LightingSettings = Role == MultiplayerRole.ClientOwned ? m_Input.LightingSettings : state.HaloLightingSettings;
            m_Motor.ApplyStanceCapsule(presentedStance);
            m_Audio?.Update(ghost, transform, Role == MultiplayerRole.ClientOwned, Time.deltaTime);
            bool jumped = ghost.LastJumpTick != 0 &&
                (m_LastJumpTick == 0 || (int)(ghost.LastJumpTick - m_LastJumpTick) > 0);
            if (jumped) m_LastJumpTick = ghost.LastJumpTick;
            m_Model.ApplyNetworkPresentation(downed ? 0 : state.MovementSpeed, downed ? 0 : state.JumpFallSpeed,
                downed || state.MovementType == FirstPersonController.MovementType.Standing,
                !downed && state.Sprinting, !downed && jumped, Time.deltaTime, downed ? Vector3.zero : (Vector3)state.AnimatorMotion,
                ((Quaternion)state.CurrentRotation).eulerAngles.y, presentedStance);
            var viewRotation = Quaternion.Euler(state.PitchDegrees, state.YawDegrees, 0f);
            var headLook = new float2(state.YawDegrees, state.PitchDegrees) + state.FreeLookOffset;
            var headRotation = Quaternion.Euler(headLook.y, headLook.x, 0f);
            bool owned = Role == MultiplayerRole.ClientOwned;
            if (m_Protection) m_Protection.Present(ghost, owned, owned && m_View.IsFirstPerson, owned ? m_View.camera : null);
            PresentMelee(new MeleeViewState
            {
                Weapon = (MeleeWeaponKind)ghost.MeleeWeapon, AttackWeapon = (MeleeWeaponKind)ghost.MeleeAttackWeapon,
                Attack = (MeleeAttackKind)ghost.MeleeAttack, Phase = (MeleePhase)ghost.MeleePhase,
                AttackSequence = ghost.MeleeAttackSequence, CancelSequence = ghost.MeleeCancelSequence,
                HitSequence = ghost.MeleeHitSequence, ComboIndex = ghost.MeleeCombo, Elapsed = ghost.MeleeElapsed,
                Epoch = ghost.MeleeEpoch,
                Alive = ghost.CurrentHealth > 0 && !downed, IsOwner = owned,
                FirstPerson = owned && m_View.IsFirstPerson, AttackDirection = viewRotation * Vector3.forward,
                Suppressed = ghost.ClashDarkSight || owned && (RaidHUD.DarkSightRequested || RaidHUD.QuickSlotToolSelected ||
                    GameSettings.Instance.IsPauseMenuOpen || RaidHUD.GameplayActionsBlocked),
                HitPoint = (Vector3)ghost.MeleeHitPoint, HitBlocked = ghost.MeleeHitBlocked, Interpolate = true
            });
            bool hideWeapon = downed || ghost.ClashDarkSight || m_Melee && m_Melee.OwnsHandPose || owned && (RaidHUD.DarkSightRequested || RaidHUD.QuickSlotToolSelected);
            m_Model.SetNetworkViewPresentation(owned ? m_View.IsFirstPerson : state.FirstPersonView,
                owned ? m_OwnedViewRotation : headRotation, owned ? m_OwnedLean : state.Lean);
            m_Halo.SetNetworkPresentation(!hideWeapon && (owned ? m_OwnedAiming : state.Aiming),
                (owned ? m_OwnedViewRotation : viewRotation) * Vector3.forward,
                owned && m_HasOwnedAimPoint ? m_OwnedAimPoint : (Vector3)ghost.AimPoint,
                owned ? m_OwnedLightEnabled : state.HaloLightEnabled,
                !hideWeapon && (owned ? ReadOwnedReadyIntent() : ghost.HipFireHoldRemaining > 0 || ghost.HaloPendingShot));
            var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghost.EquippedWeaponID, ghost.EquippedModules);
            var haloWeapon = DollSingerWeapons.IsHalo(ghost.EquippedWeaponID)
                ? (DollSingerHaloAim.HaloWeapon)(ghost.EquippedWeaponID - 1) : DollSingerHaloAim.HaloWeapon.None;
            if (hideWeapon) haloWeapon = DollSingerHaloAim.HaloWeapon.None;
            m_Halo.SetNetworkWeapon(haloWeapon, ghost.CurrentAmmo,
                state.IsReloadingState, HaloReload.Progress(ghost, weapon),
                ghost.LastShotTick, ghost.LastReloadTick, ghost.ReloadTargetAmmo, ghost.EquippedModules, weapon?.MagazineSize ?? 4, ghost.BurstSequence);
            if (Role == MultiplayerRole.ClientOwned)
            {
                bool audibleAim = m_OwnedAiming && ghost.CurrentHealth > 0;
                if (audibleAim != m_AudioAiming) MoonkovAudio.Play(MoonkovAudio.Library?.Aim, transform.position, .65f);
                m_AudioAiming = audibleAim;
                if (m_Input.FirePressed && ghost.CurrentHealth > 0 && ghost.CurrentAmmo <= 0 &&
                    !state.IsReloadingState && ghost.WeaponEquipRemaining <= 0 && Time.unscaledTime >= m_DryFireAt &&
                    !hideWeapon && !GameSettings.Instance.IsPauseMenuOpen && !RaidHUD.GameplayActionsBlocked)
                {
                    MoonkovAudio.Play(MoonkovAudio.Library?.DryFire, transform.position);
                    m_DryFireAt = Time.unscaledTime + .35f;
                }
                m_View.SetNetworkLookRotation(m_OwnedViewRotation);
                m_View.SetAimBlend(m_Halo.AdsBlend);
                m_View.SetWeaponPoseBlend(m_Halo.AimBlend);
                m_View.SetWeaponAimMagnification(m_Halo.ScopeMagnification);
            }
        }

        public void PlayShot(Vector3 aimPoint, uint weaponId, uint shotTick = 0)
        {
            if (m_Linked && Role != MultiplayerRole.Server)
            {
                // The shot RPC may precede the equipment snapshot on an observer.
                var shotState = ReadGhostComponentData<PredictedPlayerGhost>();
                if (shotState.EquippedWeaponID != weaponId) return;
                if (shotTick != 0 && !MoonkovPlayerAudio.NewTick(shotTick, ref m_LastShotEffectTick)) return;
                var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId);
                if (Role == MultiplayerRole.ClientOwned && m_OwnedAlive &&
                    m_Input.CanReadPlayerInput && WeaponHandsAvailable && !m_Input.LightAdjusting)
                {
                    float interval = weapon == null ? .35f :
                        (weapon.BurstCount > 1 ? weapon.BurstInterval : weapon.CooldownInMs) * RaidHealth.HandlingMultiplier(shotState);
                    m_Recoil.Kick(weaponId, m_View.CurrentAimMagnification, interval, m_OwnedAiming);
                }
                if (!m_WeaponFeedback) m_WeaponFeedback = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
                var profile = m_WeaponFeedback?.ForWeapon(weaponId);
                if (weaponId == DollSingerWeapons.Halo || profile != null)
                {
                    bool sustained = m_LastSoundWeapon == weaponId && Time.unscaledTime - m_LastShotSoundTime < Mathf.Max(.15f, (weapon?.CooldownInMs ?? .1f) * 1.5f);
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
                    m_RifleSnapSound = MoonkovAudio.PlayWeaponLayer(profile != null ? profile.FireSnap : weapon?.WeaponFireLayerSfx,
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
            m_LightPanel?.Dispose(); m_LightPanel = null;
            m_Audio?.Stop();
            GameManager.Instance?.SoundSystem?.Stop(m_RifleBodySound, .025f);
            GameManager.Instance?.SoundSystem?.Stop(m_RifleSnapSound, .015f);
            GameManager.Instance?.SoundSystem?.Stop(m_WeaponTailSound, .03f);
            m_RifleBodySound = m_RifleSnapSound = m_WeaponTailSound = null;
            m_Linked = false;
            m_Recoil.Reset();
            if (m_Melee) m_Melee.ResetPresentation();
            if (m_Protection) m_Protection.DisposePresentation();
            m_Halo.OwnedAimIntent = null;
            m_Halo.OwnedReadyIntent = null;
            RaidQuickSlotInput.Unbind(m_Input);
            m_ObstructionView?.Dispose(); m_ObstructionView = null;
            m_Input.enabled = false;
            m_Input.GameplayInputBlocked = null;
            m_Input.TimedActionInputActive = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            m_Input.DevelopmentStyleToggle = null;
#endif
            m_View.gameObject.SetActive(false);
        }
    }
}
