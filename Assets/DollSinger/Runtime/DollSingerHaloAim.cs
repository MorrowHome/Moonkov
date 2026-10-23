using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>
/// Presents the equipped halo weapon. Network equipment and ammo come from the server item tree.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class DollSingerHaloAim : MonoBehaviour {
    public enum HaloWeapon { None, Rifle, Revolver, Shotgun, Sniper, Cicada, Trine, Aegis, Vesper, Thorn, Sundown, Rondo, Tempest, Nocturne }
    [Header("Standalone demo equipment (network equipment comes from inventory)")]
    public HaloWeapon localPrimary = HaloWeapon.Rifle;
    public HaloWeapon localSecondary = HaloWeapon.Shotgun;
    public HaloWeapon localPistol = HaloWeapon.Revolver;
    [Header("Prefab visuals")]
    public Transform haloVisual;
    public RevolverHaloVisual revolverVisual;
    public RifleHaloVisual rifleVisual;
    public SniperHaloVisual sniperVisual;
    public ShotgunHaloVisual shotgunVisual;
    public ArsenalHaloVisual[] arsenalVisuals = System.Array.Empty<ArsenalHaloVisual>();
    public ArsenalHaloVisual ArsenalVisual { get { int i = (int)visualWeapon - 5; return i >= 0 && i < arsenalVisuals.Length ? arsenalVisuals[i] : null; } }
    public LineRenderer[] haloStrokes;
    public LineRenderer[] haloGlowStrokes;
    public Light haloLight;
    public Light haloFaceLightFront;
    public Light haloFaceLightBack;
    public LineRenderer beamGlow;
    public LineRenderer beamCore;

    [Header("Timing")]
    [Min(0.01f)] public float aimInSeconds = 0.28f;
    [Min(0.01f)] public float aimOutSeconds = 0.24f;
    [Min(1f)] public float boltSpeed = 38f;
    [Min(0.05f)] public float boltLength = 0.30f;
    [Tooltip("Laser trail duration for network projectiles; length follows the actual ballistic speed.")]
    [Min(0.001f)] public float networkBoltTrailSeconds = 0.02f;
    [Min(1f)] public float boltMaxDistance = 35f;
    [Tooltip("Standalone demo fire interval; network fire rate comes from HaloWeapon.")]
    [Min(0.02f)] public float localShotInterval = 0.1f;
    [Min(0.02f)] public float localRevolverShotInterval = 0.75f;
    [Min(0.02f)] public float localRevolverReloadTime = 0.75f;
    [Min(0.02f)] public float localRifleReloadTime = 2f;
    [Min(0.02f)] public float localSniperReloadTime = 3f;

    [Header("Halo layout")]
    [Min(0.1f)] public float idleHaloScale = 0.72f;
    [Min(0.1f)] public float aimedHaloScale = 0.45f;
    [Tooltip("Maximum camera-to-halo distance in metres; automatically limited by arm reach.")]
    [Range(0.25f, 0.8f)] public float firstPersonAimDistance = 0.45f;
    [Tooltip("Gap between the index fingertip and the halo along the aiming direction.")]
    [Range(0.02f, 0.15f)] public float fingertipHaloGap = 0.075f;
    [Tooltip("Thumb gap used by the reach-constrained halo layout.")]
    [Range(0.02f, 0.2f)] public float firstPersonThumbClearance = 0.035f;
    [Range(0f, 0.1f)] public float firstPersonThumbDepth = 0.02f;
    // Retained for prefab serialization; hand placement now uses viewport palm anchors.
    [HideInInspector]
    public Vector3 firstPersonSupportOffset = new Vector3(-0.12f, -0.08f, -0.04f);
    [Tooltip("Maximum viewport-height fraction for ordinary first-person halos, including their release envelope.")]
    [Range(.25f, .75f)] public float firstPersonHaloViewportFraction = .54f;
    [Header("Halo reach layout")]
    [HideInInspector]
    public Vector3 revolverSupportOffset = new Vector3(-0.19f, -0.18f, -0.10f);
    [HideInInspector]
    public Vector3 shotgunSupportOffset = new Vector3(-0.09f, 0.015f, -0.025f);
    [HideInInspector]
    public Vector3 sniperSupportOffset = new Vector3(-0.09f, -0.085f, -0.055f);
    [Tooltip("Maximum thumb gap as a fraction of viewport height; keeps the reference visible during scope zoom and weapon transitions.")]
    [Range(.03f, .15f)] public float thumbScreenGap = .065f;
    [Range(.01f, .05f)] public float gestureThumbClearance = .022f;
    [Tooltip("Finger direction slope relative to the camera forward ray. Negative Y exposes fingers below the thumb reference.")]
    public Vector2 revolverFingerSlope = new Vector2(.18f, -.12f);
    public Vector2 shotgunFingerSlope = new Vector2(-.12f, -.32f);
    public Vector2 sniperFingerSlope = new Vector2(-.10f, -.06f);
    [Min(.01f)] public float gestureTransitionSeconds = .16f;

    [Header("Laser flight lighting")]
    [Min(0f)] public float boltLightIntensity = 12f;
    [Min(0.1f)] public float boltLightRange = 10f;

    [Header("Appearance")]
    public Color idleHaloColor = new Color(1f, 0.28f, 0.52f, 1f);
    public Color aimedHaloColor = new Color(1f, 0.08f, 0.25f, 1f);

    [Header("Glow & light")]
    // The aura repeats the core strokes wider and softer. LineRenderer colours
    // clamp at 1 (Color32), so the HDR lift that feeds bloom lives in the
    // HaloLine shader's _Gain instead of these colours.
    [Range(0f, 1f)] public float glowAlpha = 0.6f;
    [Min(0f)] public float idleLightIntensity = 2f;
    [Min(0f)] public float aimedLightIntensity = 3.2f;
    [Min(0.1f)] public float lightRange = 3f;
    [Tooltip("Press L to toggle the halo lights on and off.")]
    public bool haloLightEnabled = true;
    [Range(10f, 170f)] public float faceSpotAngle = 110f;
    [Min(0f)] public float faceLightIntensityScale = 0.7f;
    [Header("Flashlight (aim state)")]
    [Min(0.1f)] public float flashlightRange = 80f;
    [Range(10f, 90f)] public float flashlightAngle = 48f;
    [Min(0f)] public float flashlightIntensity = 45f;

    private readonly RaycastHit[] rayHits = new RaycastHit[32];
    [Header("Blender authored whole-arm gestures")]
    [SerializeField] private TextAsset gestureBake;
    private DollSingerBakedGestures bakedGestures;
    private ILocalArsenalDriver localArsenal;
    private float gestureShotTime = float.NegativeInfinity;
    private float gestureReframeUntil;
    private uint gestureShotTick;
    private bool gestureTickInitialized;
    private Animator animator;
    private static readonly int AimBlendId = Animator.StringToHash("AimBlend");
    private bool hasAimBlendParameter;
    private static readonly int HaloGestureId = Animator.StringToHash("HaloGesture");
    private bool hasGestureParameter;
    private Vector2 fingerSlope;
    private float thumbClearance;
    public DollSingerInput input;
    public DollSingerView view;
    [SerializeField] private bool networkControlled;
    private bool networkAiming;
    private Vector3 networkDirection = Vector3.forward;
    private Vector3 networkAimPoint;
    private DollSingerMovement thirdPerson;
    private Camera playerCamera;
    private DollSingerView cameraOwner;
    private Transform head;
    private Transform chest;
    private Transform rightIndexTip;
    private Transform rightThumbTip;
    private Transform rightUpperArm;
    private Transform rightForearm;
    private Transform rightHand;
    public bool ExternalHandPoseActive { get; set; }
    public void PrepareExternalHandPose() => bakedGestures?.RestoreBeforeAnimation();
    public bool OwnsHandPose => isActiveAndEnabled && (ExternalHandPoseActive || visualWeapon != HaloWeapon.None && (wantsAim || aimBlend > .001f));
    private int aimLayerIndex = -1;
    private float aimBlend;
    private HaloBoltPool boltPool;
    private HaloDischargeVisual discharge;
    private float nextLocalShotTime;
    private bool wantsAim;
    private bool revolverEquipped;
    private HaloWeapon visualWeapon = HaloWeapon.Rifle;
    private int localShotgunAmmo = 4;
    private int localRifleAmmo = RifleHaloVisual.Capacity;
    private int localSniperAmmo = SniperHaloVisual.Capacity;
    private int localRevolverAmmo = 6;
    private float localReloadRemaining;
    private int weaponAmmo = 6, gestureBurstIndex;
    private HaloModuleVisual rifleModules, shotgunModules, revolverModules, sniperModules;
    private HaloModuleVisual CreateModuleVisual(Transform parent, bool rifle)
    {
        if (!parent) return null;
        var child = new GameObject("Halo modification layers"); child.layer = parent.gameObject.layer; child.transform.SetParent(parent, false);
        var visual = child.AddComponent<HaloModuleVisual>(); visual.Configure(rifle); return visual;
    }

    public void SetNetworkWeapon(HaloWeapon weapon, int ammo, bool reloading, float reloadProgress, uint shotTick, uint reloadTick, int reloadTargetAmmo = -1, ushort modules = 0, int magazineSize = 4, int burstIndex = 0)
    {
        if (!rifleModules && rifleVisual) rifleModules = CreateModuleVisual(rifleVisual.transform, true);
        if (!shotgunModules && shotgunVisual) shotgunModules = CreateModuleVisual(shotgunVisual.transform, false);
        if (!revolverModules && revolverVisual) revolverModules = CreateModuleVisual(revolverVisual.transform, false);
        if (!sniperModules && sniperVisual) sniperModules = CreateModuleVisual(sniperVisual.transform, true);
        if (rifleVisual) rifleVisual.SetMagazineCapacity(weapon == HaloWeapon.Rifle ? magazineSize : RifleHaloVisual.Capacity);
        if (shotgunVisual) shotgunVisual.SetModuleProfile(weapon == HaloWeapon.Shotgun ? modules : (ushort)0, weapon == HaloWeapon.Shotgun ? magazineSize : 4);
        gestureBurstIndex = burstIndex;
        ObserveGestureShot(shotTick);
        SetWeaponEquipped(weapon, ammo);
        weaponAmmo = ammo;
        if (ArsenalVisual) ArsenalVisual.SetNetworkState(ammo, magazineSize, reloading, reloadProgress, shotTick, reloadTick, reloadTargetAmmo, burstIndex);
        if (weapon == HaloWeapon.Rifle && rifleModules) rifleModules.SetState(modules, ammo, magazineSize, reloading, reloadProgress, reloadTargetAmmo, shotTick, burstIndex);
        if (weapon == HaloWeapon.Shotgun && shotgunModules) shotgunModules.SetState(modules, ammo, magazineSize, reloading, reloadProgress, reloadTargetAmmo, shotTick);
        if (weapon == HaloWeapon.Revolver && revolverModules) revolverModules.SetState(modules, ammo, magazineSize, reloading, reloadProgress, reloadTargetAmmo, shotTick);
        if (weapon == HaloWeapon.Sniper && sniperModules) sniperModules.SetState(modules, ammo, magazineSize, reloading, reloadProgress, reloadTargetAmmo, shotTick);
        if (rifleVisual) rifleVisual.SetNetworkState(ammo, reloading, reloadProgress, shotTick, reloadTick, reloadTargetAmmo);
        if (sniperVisual) sniperVisual.SetNetworkState(ammo, reloading, reloadProgress, shotTick, reloadTick, reloadTargetAmmo);
        if (revolverVisual) revolverVisual.SetNetworkState(ammo, reloading, reloadProgress, shotTick, reloadTick);
        if (shotgunVisual) shotgunVisual.SetNetworkState(ammo, reloading, reloadProgress, shotTick, reloadTick, reloadTargetAmmo);
    }

    private void SetRevolverEquipped(bool revolver, int ammo)
    {
        SetWeaponEquipped(revolver ? HaloWeapon.Revolver : localPrimary, ammo);
    }
    private void SetWeaponEquipped(HaloWeapon weapon, int ammo)
    {
        if (visualWeapon != weapon && discharge) discharge.Clear();
        if (visualWeapon != weapon)
        {
            if (!networkControlled) localArsenal?.CancelBurst();
            gestureShotTime = float.NegativeInfinity;
            gestureReframeUntil = Time.time + gestureTransitionSeconds + .04f;
        }
        visualWeapon = weapon;
        foreach (var visual in arsenalVisuals) if (visual) visual.SetEquipped(visual.weapon == weapon);
        revolverEquipped = weapon == HaloWeapon.Revolver;
        if (haloVisual) haloVisual.gameObject.SetActive(weapon != HaloWeapon.None);
        if (revolverVisual) revolverVisual.SetEquipped(revolverEquipped, ammo);
        if (shotgunVisual) shotgunVisual.SetEquipped(weapon == HaloWeapon.Shotgun, ammo);
        if (rifleVisual) rifleVisual.SetEquipped(weapon == HaloWeapon.Rifle, ammo);
        if (sniperVisual) sniperVisual.SetEquipped(weapon == HaloWeapon.Sniper, ammo);
        if (haloStrokes != null)
            foreach (var stroke in haloStrokes) if (stroke) stroke.enabled = weapon == HaloWeapon.Rifle && !rifleVisual;
        if (haloGlowStrokes != null)
            foreach (var stroke in haloGlowStrokes) if (stroke) stroke.enabled = weapon == HaloWeapon.Rifle && !rifleVisual;
    }

    [SerializeField] private float hhh;

#if UNITY_EDITOR
    // Used by isolated Editor validation; normal input remains RMB / LMB.
    [System.NonSerialized] public bool editorForceAim;
    public void EditorPreviewFire() => FireCosmeticBolt();
#endif

    public uint LightingSettings { get; set; }
    public float AimBlend => aimBlend;
    // Only the locally owned network presentation installs this. Remote replicas
    // continue to use synchronized state; the blend still advances once per Update.
    public System.Func<bool> OwnedAimIntent { get; set; }
    public float ScopeMagnification => visualWeapon == HaloWeapon.Sniper && sniperVisual ? sniperVisual.scopeMagnification : ArsenalVisual ? ArsenalVisual.magnification : 0;
    private int LocalAmmo(HaloWeapon weapon) => (int)weapon >= 5 ? localArsenal?.Ammo(weapon) ?? 0 : weapon == HaloWeapon.Rifle ? localRifleAmmo : weapon == HaloWeapon.Shotgun ? localShotgunAmmo : weapon == HaloWeapon.Sniper ? localSniperAmmo : localRevolverAmmo;
    public int AimLayerIndex => aimLayerIndex;

    public void SetNetworkPresentation(bool aiming, Vector3 direction, Vector3 aimPoint, bool lightEnabled = true)
    {
        networkControlled = true;
        haloLightEnabled = lightEnabled;
        networkAiming = aiming;
        networkDirection = direction.normalized;
        networkAimPoint = aimPoint;
    }

    // Invoked by the existing predicted/server-confirmed shot effects path.
    // This is a cosmetic bolt; damage still belongs to the authoritative weapon system.
    public void PlayNetworkShot(Vector3 aimPoint, bool cosmeticFlight = true, uint shotTick = 0)
    {
        if (shotTick == 0) gestureShotTime = Time.time;
        else ObserveGestureShot(shotTick);
        if (revolverEquipped && revolverVisual) revolverVisual.PlayShot(shotTick);
        if (visualWeapon == HaloWeapon.Shotgun && shotgunVisual) shotgunVisual.PlayShot(shotTick);
        if (visualWeapon == HaloWeapon.Rifle && rifleVisual) rifleVisual.PlayShot(shotTick);
        if (visualWeapon == HaloWeapon.Sniper && sniperVisual) sniperVisual.PlayShot(shotTick);
        if (ArsenalVisual) ArsenalVisual.PlayShot(shotTick);
        PlayDischarge(shotTick);
        if (cosmeticFlight) FireCosmeticBolt(aimPoint);
    }

    public Color WeaponColor => ArsenalVisual ? ArsenalVisual.lightColor : visualWeapon == HaloWeapon.Sniper && sniperVisual ? sniperVisual.lightColor :
        revolverEquipped && revolverVisual ? revolverVisual.lightColor :
        visualWeapon == HaloWeapon.Shotgun && shotgunVisual ? shotgunVisual.lightColor : aimedHaloColor;

    private void PlayDischarge(uint tick = 0)
    {
        if (!haloVisual || !beamCore || !beamCore.sharedMaterial) return;
        if (!discharge)
        {
            var root = new GameObject("Halo pressure release"); root.layer = haloVisual.gameObject.layer;
            root.transform.SetParent(haloVisual, false);
            discharge = root.AddComponent<HaloDischargeVisual>(); discharge.Configure(beamCore.sharedMaterial);
        }
        discharge.Play(visualWeapon, WeaponColor, tick);
    }

    private void Awake() {
        animator = GetComponent<Animator>();
        thumbClearance = firstPersonThumbClearance;
        foreach (var parameter in animator.parameters)
        {
            if (parameter.nameHash == AimBlendId && parameter.type == AnimatorControllerParameterType.Float)
                hasAimBlendParameter = true;
            if (parameter.nameHash == HaloGestureId && parameter.type == AnimatorControllerParameterType.Int)
                hasGestureParameter = true;
        }
        if (!input) input = GetComponent<DollSingerInput>();
        localArsenal = GetComponent<ILocalArsenalDriver>();
        var skirtAvoidance = GetComponent<SkirtHandAvoidance>();
        if (skirtAvoidance) skirtAvoidance.haloAim = this;
        if (!animator || !animator.isHuman) {
            enabled = false;
            return;
        }
        head = animator.GetBoneTransform(HumanBodyBones.Head);
        chest = animator.GetBoneTransform(HumanBodyBones.Chest) ??
                animator.GetBoneTransform(HumanBodyBones.UpperChest);
        rightIndexTip = FindTip(animator.GetBoneTransform(HumanBodyBones.RightIndexDistal));
        rightThumbTip = FindTip(animator.GetBoneTransform(HumanBodyBones.RightThumbDistal));
        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rightForearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (!head || !chest || !rightIndexTip) {
            Debug.LogWarning("Doll Singer halo prototype: required Humanoid bones are missing.", this);
            enabled = false;
            return;
        }
        if (gestureBake) bakedGestures = new DollSingerBakedGestures(animator, gestureBake);
        aimLayerIndex = animator.GetLayerIndex("Halo Aim");
        if (aimLayerIndex < 0)
            Debug.LogWarning("Doll Singer halo: editable 'Halo Aim' Animator layer is missing.", this);
        EnsureGlowStrokes();
        EnsureHaloLight();
        SetRevolverEquipped(false, LocalAmmo(localPrimary));
        SetBoltVisible(false);
    }

    private void Start() {
        if (!networkControlled && input) FindCamera();
    }

    private void Update() {
        if (ExternalHandPoseActive)
        {
            wantsAim = false; aimBlend = 0;
            if (aimLayerIndex >= 0) animator.SetLayerWeight(aimLayerIndex, 0);
            if (hasAimBlendParameter) animator.SetFloat(AimBlendId, 0);
            if (view) { view.SetAimBlend(0); view.SetWeaponAimMagnification(1); }
            return;
        }
        if (networkControlled && OwnedAimIntent != null) networkAiming = OwnedAimIntent();
        if (aimBlend > 0 || wantsAim) bakedGestures?.RestoreBeforeAnimation();
        if (discharge) discharge.Step(Time.deltaTime);
        if (!networkControlled && !input) return;
        if (!thirdPerson) thirdPerson = GetComponent<DollSingerMovement>();
        if (!networkControlled && !playerCamera) FindCamera();
        // Legacy clips stay available for older prefabs; production uses the exact-rig Blender bake.
        if (hasGestureParameter) animator.SetInteger(HaloGestureId, (int)(visualWeapon == HaloWeapon.None ? HaloWeapon.Rifle : visualWeapon));
        Vector2 nextSlope = visualWeapon == HaloWeapon.Revolver ? revolverFingerSlope :
            visualWeapon == HaloWeapon.Shotgun ? shotgunFingerSlope :
            visualWeapon == HaloWeapon.Sniper ? sniperFingerSlope : Vector2.zero;
        fingerSlope = Vector2.Lerp(fingerSlope, nextSlope,
            1f - Mathf.Exp(-Time.deltaTime * 3f / Mathf.Max(.01f, gestureTransitionSeconds)));
        thumbClearance = Mathf.Lerp(thumbClearance,
            visualWeapon == HaloWeapon.Rifle ? firstPersonThumbClearance : gestureThumbClearance,
            1f - Mathf.Exp(-Time.deltaTime * 3f / Mathf.Max(.01f, gestureTransitionSeconds)));

        bool manualAim = networkControlled ? networkAiming :
            input.AimHeld && !(thirdPerson && thirdPerson.IsFreeLooking);
#if UNITY_EDITOR
        bool aimingInput = manualAim || editorForceAim;
#else
        bool aimingInput = manualAim;
#endif
        bool wasAiming = wantsAim;
        wantsAim = visualWeapon != HaloWeapon.None && (networkControlled ? networkAiming :
            thirdPerson && thirdPerson.enabled && playerCamera && aimingInput);
        if (wantsAim && !wasAiming) gestureReframeUntil = Time.time + aimInSeconds + .04f;
        if (thirdPerson) thirdPerson.IsLocallyAiming = wantsAim;
        if (!networkControlled && input && input.enabled)
        {
            LightingSettings = input.LightingSettings;
            if (input.LightPressed) haloLightEnabled = !haloLightEnabled;
        }
        aimBlend = Mathf.MoveTowards(aimBlend, wantsAim ? 1f : 0f,
            Time.deltaTime / (wantsAim ? aimInSeconds : aimOutSeconds));
        if (aimLayerIndex >= 0) animator.SetLayerWeight(aimLayerIndex, bakedGestures != null ? 0f : aimBlend);
        if (hasAimBlendParameter) animator.SetFloat(AimBlendId, aimBlend);
        // Network presentation runs before this Update and otherwise forwards
        // the previous frame's blend. Commit the newly advanced pose/zoom state
        // before DollSingerView.LateUpdate, including for network-owned players.
        if (view && view.isActiveAndEnabled && view.player == gameObject)
        {
            view.SetAimBlend(aimBlend);
            view.SetWeaponAimMagnification(ScopeMagnification);
        }
        if (ArsenalVisual) ArsenalVisual.SetAimBlend(DollSingerView.AimPoseBlend(aimBlend));
        float poseBlend = DollSingerView.AimPoseBlend(aimBlend);
        if (revolverVisual) revolverVisual.SetAimBlend(poseBlend);
        if (shotgunVisual) shotgunVisual.SetAimBlend(poseBlend);
        if (rifleVisual) rifleVisual.SetAimBlend(poseBlend);
        if (sniperVisual) sniperVisual.SetAimBlend(poseBlend);
        if (rifleModules) rifleModules.SetAimBlend(poseBlend);
        if (shotgunModules) shotgunModules.SetAimBlend(poseBlend);
        if (revolverModules) revolverModules.SetAimBlend(poseBlend);
        if (sniperModules) sniperModules.SetAimBlend(poseBlend);
        if (!networkControlled && input)
        {
            if (localReloadRemaining <= 0f && !(localArsenal?.Reloading ?? false) && input.WeaponSlotPressed != 0)
            {
                var next = input.WeaponSlotPressed == 1 ? localPrimary : input.WeaponSlotPressed == 2 ? localSecondary : localPistol;
                SetWeaponEquipped(next, LocalAmmo(next));
                nextLocalShotTime = Time.time + (revolverEquipped ? localRevolverShotInterval : localShotInterval);
            }
            if (ArsenalVisual)
            {
                if (localArsenal != null && localArsenal.Step(this, visualWeapon, input.FireHeld, input.FirePressed,
                    input.ReloadPressed, wantsAim && aimBlend > .65f, Time.deltaTime))
                { gestureShotTime = Time.time; PlayDischarge(); FireCosmeticBolt(); }
                weaponAmmo = LocalAmmo(visualWeapon);
                return;
            }
            if (localReloadRemaining > 0f)
            {
                localReloadRemaining = Mathf.Max(0f, localReloadRemaining - Time.deltaTime);
                if (localReloadRemaining == 0f) { if (visualWeapon == HaloWeapon.Shotgun) localShotgunAmmo = 4; else if (visualWeapon == HaloWeapon.Rifle) localRifleAmmo = RifleHaloVisual.Capacity; else if (visualWeapon == HaloWeapon.Sniper) localSniperAmmo = SniperHaloVisual.Capacity; else localRevolverAmmo = 6; }
            }
            if (revolverEquipped && localReloadRemaining == 0f && localRevolverAmmo < 6 &&
                (input.ReloadPressed || localRevolverAmmo == 0)) localReloadRemaining = localRevolverReloadTime;
            bool fire = revolverEquipped ? input.FirePressed && localRevolverAmmo > 0 && localReloadRemaining == 0f : input.FireHeld;
            if (visualWeapon == HaloWeapon.Shotgun)
            {
                if (localReloadRemaining == 0f && localShotgunAmmo < 4 && (input.ReloadPressed || localShotgunAmmo == 0)) localReloadRemaining = 2f;
                fire = input.FirePressed && localShotgunAmmo > 0 && localReloadRemaining == 0f;
            }
            if (visualWeapon == HaloWeapon.Rifle)
            {
                if (localReloadRemaining == 0 && localRifleAmmo < RifleHaloVisual.Capacity &&
                    (input.ReloadPressed || localRifleAmmo == 0 && input.FireHeld)) localReloadRemaining = localRifleReloadTime;
                fire = input.FireHeld && localRifleAmmo > 0 && localReloadRemaining == 0;
            }
            if (visualWeapon == HaloWeapon.Sniper)
            {
                if (localReloadRemaining == 0 && localSniperAmmo < SniperHaloVisual.Capacity &&
                    (input.ReloadPressed || localSniperAmmo == 0)) localReloadRemaining = localSniperReloadTime;
                fire = input.FirePressed && localSniperAmmo > 0 && localReloadRemaining == 0;
            }
            if (wantsAim && aimBlend > 0.65f && fire && Time.time >= nextLocalShotTime)
            {
                nextLocalShotTime = Time.time + (revolverEquipped ? localRevolverShotInterval : visualWeapon == HaloWeapon.Shotgun ? .8f : visualWeapon == HaloWeapon.Sniper ? 1.4f : localShotInterval);
                if (revolverEquipped)
                {
                    localRevolverAmmo--;
                    if (revolverVisual) revolverVisual.PlayShot();
                }
                gestureShotTime = Time.time;
                PlayDischarge();
                FireCosmeticBolt();
                if (visualWeapon == HaloWeapon.Shotgun) { localShotgunAmmo--; if (shotgunVisual) shotgunVisual.PlayShot(); }
                if (visualWeapon == HaloWeapon.Rifle) { localRifleAmmo--; if (rifleVisual) rifleVisual.PlayShot(); }
                if (visualWeapon == HaloWeapon.Sniper) { localSniperAmmo--; if (sniperVisual) sniperVisual.PlayShot(); }
            }
            weaponAmmo = LocalAmmo(visualWeapon);
            if (revolverVisual) revolverVisual.SetState(localRevolverAmmo, localReloadRemaining > 0f,
                1f - localReloadRemaining / localRevolverReloadTime);
            if (shotgunVisual) shotgunVisual.SetLocalState(localShotgunAmmo, localReloadRemaining > 0f, 1f - localReloadRemaining / 2f);
            if (rifleVisual && visualWeapon == HaloWeapon.Rifle) rifleVisual.SetLocalState(localRifleAmmo, localReloadRemaining > 0, 1f - localReloadRemaining / localRifleReloadTime);
            if (sniperVisual && visualWeapon == HaloWeapon.Sniper) sniperVisual.SetLocalState(localSniperAmmo, localReloadRemaining > 0, 1f - localReloadRemaining / localSniperReloadTime);
        }
    }

    // Skirt avoidance calls this on the hand layer. The Blender chain owns all joints.
    public bool TryApplyHandIK(AvatarIKGoal goal)
    {
        if (!OwnsHandPose) return false;
        animator.SetIKPositionWeight(goal, 0f);
        animator.SetIKRotationWeight(goal, 0f);
        animator.SetIKHintPositionWeight(goal == AvatarIKGoal.RightHand ? AvatarIKHint.RightElbow : AvatarIKHint.LeftElbow, 0f);
        return true;
    }

    private void ObserveGestureShot(uint tick)
    {
        if (!gestureTickInitialized) { gestureTickInitialized = true; gestureShotTick = tick; return; }
        if (unchecked((int)(tick - gestureShotTick)) <= 0) return;
        gestureShotTick = tick; gestureShotTime = Time.time;
    }

    private void ApplyBakedGestures()
    {
        if (bakedGestures == null) return;
        bool reload = false; float progress = 0;
        switch (visualWeapon)
        {
            case HaloWeapon.Rifle when rifleVisual: reload = rifleVisual.IsReloading; progress = rifleVisual.ReloadProgress; break;
            case HaloWeapon.Revolver when revolverVisual: reload = revolverVisual.IsReloading; progress = revolverVisual.ReloadProgress; break;
            case HaloWeapon.Shotgun when shotgunVisual: reload = shotgunVisual.IsReloading; progress = shotgunVisual.ReloadProgress; break;
            case HaloWeapon.Sniper when sniperVisual: reload = sniperVisual.IsReloading; progress = sniperVisual.ReloadProgress; break;
        }
        if (ArsenalVisual) { reload = ArsenalVisual.IsReloading; progress = ArsenalVisual.ReloadProgress; }
        Vector3 direction = networkControlled ? networkDirection : playerCamera ? playerCamera.transform.forward : transform.forward;
        float poseBlend = DollSingerView.AimPoseBlend(aimBlend);
        bakedGestures.Apply((int)visualWeapon, poseBlend, Time.time - gestureShotTime, reload, progress, gestureTransitionSeconds, chest, direction, burstIndex: gestureBurstIndex);
    }

    private void LateUpdate() {
        // A single complete-chain sampler owns the arms. Skirt IK also observes OwnsHandPose.
        if (ExternalHandPoseActive)
        { if (haloVisual) haloVisual.gameObject.SetActive(false); return; }
        if (haloVisual && visualWeapon != HaloWeapon.None) haloVisual.gameObject.SetActive(true);
        if (!haloVisual || !head || visualWeapon == HaloWeapon.None)
        { if (view) view.SetGestureHandsActive(false); return; }
        ApplyBakedGestures();
        bool centred = TryGetFirstPersonCamera(out var aimingCamera);
        if (view) view.SetGestureHandsActive(centred && aimBlend > .001f, visualWeapon == HaloWeapon.Sniper,
            visualWeapon == HaloWeapon.Cicada ? .03f : visualWeapon == HaloWeapon.Tempest ? .025f : 0,
            ArsenalVisual != null);
        Vector3 centredPosition = centred ? GetFirstPersonHaloPosition(aimingCamera) : Vector3.zero;
        float layoutBlend = DollSingerView.AimPoseBlend(aimBlend);
        float t = layoutBlend * layoutBlend * (3f - 2f * layoutBlend);
        if (centred && view.GestureHandsActive)
        {
            view.FrameGestureHands(visualWeapon != HaloWeapon.Sniper && Time.time <= gestureReframeUntil);
        }
        Vector3 headPosition = head.position + transform.up * 0.27f +
                               transform.right * Mathf.Sin(Time.time * 1.8f) * 0.008f + transform.up * hhh;
        Vector3 aimDirection = networkControlled ? networkDirection :
            playerCamera ? playerCamera.transform.forward : transform.forward;
        if (networkControlled && networkAimPoint != Vector3.zero)
            aimDirection = (networkAimPoint - rightIndexTip.position).normalized;
        Vector3 fingerPosition = rightIndexTip.position + aimDirection * fingertipHaloGap;
        if (centred)
        {
            // DollSingerView has already followed the leaning head this frame. Using its
            // final camera ray keeps the halo centred through pitch, roll and Q/E peeking.
            aimDirection = aimingCamera.transform.forward;
            fingerPosition = centredPosition;
        }
        Vector3 control = headPosition + transform.up * 0.17f + transform.right * 0.16f;
        haloVisual.position = (1f - t) * (1f - t) * headPosition +
                              2f * (1f - t) * t * control + t * t * fingerPosition;
        // Local Z is the ring's hole axis: up over the head, forward at the muzzle.
        Quaternion restingRotation = Quaternion.LookRotation(transform.up, transform.forward);
        Quaternion aimingRotation = centred
            ? Quaternion.AngleAxis(view.SightLeanRoll, aimDirection) * aimingCamera.transform.rotation
            : Quaternion.LookRotation(aimDirection, transform.up);
        haloVisual.rotation = Quaternion.Slerp(restingRotation, aimingRotation, t);
        float haloScale = Mathf.Lerp(idleHaloScale, aimedHaloScale, t);
        if (centred && visualWeapon == HaloWeapon.Sniper && sniperVisual)
            haloScale = Mathf.Lerp(haloScale, Mathf.Min(aimedHaloScale,
                sniperVisual.ScopeScale(Vector3.Distance(centredPosition, aimingCamera.transform.position), aimingCamera.fieldOfView)), t);
        // Reach-limited eye-to-halo distance can be much shorter than the configured distance.
        // Constrain ordinary halo size by the actual distance, preserving its art and central sight.
        if (centred && visualWeapon != HaloWeapon.Sniper)
        {
            float distance = Vector3.Dot(centredPosition - aimingCamera.transform.position, aimingCamera.transform.forward);
            float halfHeight = Mathf.Max(.01f, distance) * Mathf.Tan(aimingCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            haloScale = Mathf.Min(haloScale, firstPersonHaloViewportFraction * halfHeight / FirstPersonVisualRadius());
        }
        Vector3 parentScale = transform.lossyScale;
        haloVisual.localScale = new Vector3(haloScale / Mathf.Max(0.001f, parentScale.x),
            haloScale / Mathf.Max(0.001f, parentScale.y),
            haloScale / Mathf.Max(0.001f, parentScale.z));
        if (haloStrokes != null) {
            Color color = ArsenalVisual ? ArsenalVisual.lightColor : visualWeapon == HaloWeapon.Sniper && sniperVisual ? sniperVisual.lightColor : revolverEquipped && revolverVisual ? revolverVisual.lightColor : visualWeapon == HaloWeapon.Shotgun && shotgunVisual ? shotgunVisual.lightColor : visualWeapon == HaloWeapon.Rifle && rifleVisual ? rifleVisual.lightColor : Color.Lerp(idleHaloColor, aimedHaloColor, t);
            foreach (var stroke in haloStrokes) {
                if (!stroke) continue;
                stroke.startColor = color;
                stroke.endColor = color;
            }
            if (haloGlowStrokes != null) {
                Color glow = new Color(color.r, color.g, color.b, glowAlpha);
                foreach (var stroke in haloGlowStrokes) {
                    if (!stroke) continue;
                    stroke.startColor = glow;
                    stroke.endColor = glow;
                }
            }
            float idleIntensity = idleLightIntensity * HaloLighting.IdlePercent(LightingSettings) / 100f;
            float aimScale = HaloLighting.AimPercent(LightingSettings) / 100f;
            float idleRange = HaloLighting.Range(LightingSettings, lightRange);
            if (haloLight) {
                haloLight.color = new Color(color.r, color.g, color.b, 1f);
                haloLight.intensity = Mathf.Lerp(idleIntensity, aimedLightIntensity * aimScale, t) + (discharge ? discharge.Flash * 5f : 0);
                haloLight.range = Mathf.Lerp(idleRange, lightRange, t);
                haloLight.enabled = haloLightEnabled;
            }
            // The two spot lights approximate a disc-shaped area light on each
            // ring face (local +/-Z is the ring's hole axis). Aiming turns the
            // front face into a long-throw flashlight along the aim direction.
            float faceIntensity = Mathf.Lerp(idleIntensity, aimedLightIntensity * aimScale, t) *
                                  faceLightIntensityScale;
            Color faceColor = new Color(color.r, color.g, color.b, 1f);
            if (haloFaceLightFront) {
                haloFaceLightFront.color = faceColor;
                haloFaceLightFront.intensity = Mathf.Lerp(faceIntensity, flashlightIntensity * aimScale, t);
                haloFaceLightFront.range = Mathf.Lerp(idleRange, HaloLighting.AimRange(LightingSettings, flashlightRange), t);
                haloFaceLightFront.spotAngle = Mathf.Lerp(faceSpotAngle, flashlightAngle, t);
                haloFaceLightFront.innerSpotAngle = haloFaceLightFront.spotAngle * Mathf.Lerp(0.55f, 0.72f, t);
                haloFaceLightFront.enabled = haloLightEnabled;
            }
            if (haloFaceLightBack) {
                haloFaceLightBack.color = faceColor;
                haloFaceLightBack.intensity = faceIntensity + (discharge ? discharge.Flash * 2f : 0);
                haloFaceLightBack.range = Mathf.Lerp(idleRange, lightRange, t);
                haloFaceLightBack.spotAngle = faceSpotAngle;
                haloFaceLightBack.innerSpotAngle = faceSpotAngle * 0.55f;
                haloFaceLightBack.enabled = haloLightEnabled;
            }
        }
    }

    private float FirstPersonVisualRadius()
    {
        if (visualWeapon == HaloWeapon.Rifle && rifleVisual)
            return Mathf.Max(.43f, (rifleVisual.ringRadius + .073f + rifleVisual.releaseExpansion * 1.25f) * rifleVisual.overallScale);
        if (revolverEquipped && revolverVisual)
            return Mathf.Max(.27f, revolverVisual.shotRadius + (revolverVisual.loadedSprite ? revolverVisual.loadedSprite.bounds.extents.magnitude : .1f));
        if (visualWeapon == HaloWeapon.Shotgun && shotgunVisual && shotgunVisual.shell && shotgunVisual.shell.sprite)
            return Mathf.Max(.43f, shotgunVisual.shell.sprite.bounds.extents.magnitude * 1.5f);
        return ArsenalVisual ? .32f : .43f;
    }

    private bool TryGetFirstPersonCamera(out Camera ownedCamera)
    {
        // Never borrow Camera.main: network observers have a disabled view of their own.
        ownedCamera = view && view.isActiveAndEnabled && view.IsFirstPerson && view.player == gameObject
            && view.camera && view.camera.isActiveAndEnabled ? view.camera : null;
        return ownedCamera;
    }

    private Vector3 GetFirstPersonHaloPosition(Camera aimingCamera)
    {
        Transform cameraTransform = aimingCamera.transform;
        float distance = firstPersonAimDistance;
        if (rightUpperArm && rightForearm && rightHand &&
            TryGetFirstPersonWrist(cameraTransform, cameraTransform.position, out var baseWrist, out _))
        {
            float reach = Vector3.Distance(rightUpperArm.position, rightForearm.position) +
                          Vector3.Distance(rightForearm.position, rightHand.position) - 0.005f;
            Vector3 eyeOffset = baseWrist - rightUpperArm.position;
            float along = Vector3.Dot(eyeOffset, cameraTransform.forward);
            float perpendicularSquared = Mathf.Max(0f, eyeOffset.sqrMagnitude - along * along);
            float intersection = reach * reach - perpendicularSquared;
            if (intersection >= 0f)
            {
                // The wrist follows the thumb below the halo, leaving its sight window clear.
                float maxDistance = -along + Mathf.Sqrt(intersection);
                distance = Mathf.Min(distance, maxDistance);
            }
        }
        distance = Mathf.Max(aimingCamera.nearClipPlane + 0.03f, distance);
        return cameraTransform.position + cameraTransform.forward * distance;
    }

    private bool TryGetFirstPersonWrist(Transform cameraTransform, Vector3 haloPosition,
        out Vector3 wristPosition, out Quaternion wristRotation)
    {
        wristPosition = Vector3.zero;
        wristRotation = Quaternion.identity;
        if (!rightHand || !rightIndexTip || !rightThumbTip) return false;
        Vector3 fingerDirection = rightIndexTip.position - rightHand.position;
        if (fingerDirection.sqrMagnitude < 0.000001f) return false;
        Vector3 pointing = cameraTransform.forward + cameraTransform.right * fingerSlope.x + cameraTransform.up * fingerSlope.y;
        Quaternion correction = Quaternion.FromToRotation(fingerDirection, pointing.normalized);
        float clearance = thumbClearance;
        if (view && view.camera)
            clearance = Mathf.Min(clearance, firstPersonAimDistance *
                Mathf.Tan((visualWeapon == HaloWeapon.Sniper ? view.firstPersonFieldOfView : view.camera.fieldOfView) * .5f * Mathf.Deg2Rad) *
                (visualWeapon == HaloWeapon.Rifle ? .10f : thumbScreenGap) * 2f);
        Vector3 thumbPosition = haloPosition - cameraTransform.up * clearance -
                               cameraTransform.forward * firstPersonThumbDepth;
        wristPosition = thumbPosition - correction * (rightThumbTip.position - rightHand.position);
        wristRotation = correction * rightHand.rotation;
        return true;
    }

    private void OnDisable() {
        bakedGestures?.RestoreBeforeAnimation();
        gestureShotTime = float.NegativeInfinity; gestureTickInitialized = false;
        wantsAim = false;
        if (view) view.SetGestureHandsActive(false);
        aimBlend = 0f;
        nextLocalShotTime = 0;
        if (thirdPerson) thirdPerson.IsLocallyAiming = false;
        if (animator && aimLayerIndex >= 0) animator.SetLayerWeight(aimLayerIndex, 0f);
        if (animator && hasAimBlendParameter) animator.SetFloat(AimBlendId, 0f);
        if (view && view.player == gameObject) view.SetAimBlend(0f);
        SetBoltVisible(false);
        if (boltPool) boltPool.ClearActive();
        if (discharge) discharge.Clear();
    }

    private void FindCamera() {
        var owner = view;
        cameraOwner = owner && owner.player == gameObject ? owner : null;
        playerCamera = cameraOwner ? cameraOwner.camera : Camera.main;
    }

    private Vector3 FindAimPoint() {
        if (networkControlled) return networkAimPoint != Vector3.zero ? networkAimPoint :
            transform.position + transform.up * 1.5f + networkDirection * boltMaxDistance;
        if (!playerCamera) return transform.position + transform.forward * 60f;
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        int count = Physics.RaycastNonAlloc(ray, rayHits, boltMaxDistance, ~0,
            QueryTriggerInteraction.Ignore);
        float nearest = boltMaxDistance;
        Vector3 point = ray.GetPoint(nearest);
        for (int i = 0; i < count; i++) {
            var hit = rayHits[i];
            if (!hit.collider || hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            point = hit.point;
        }
        return point;
    }

    private void FireCosmeticBolt(Vector3? shotAimPoint = null) {
        if (!haloVisual || (!networkControlled && !playerCamera)) return;
        Vector3 forward = networkControlled ? networkDirection : playerCamera.transform.forward;
        Vector3 target = shotAimPoint ?? FindAimPoint();
        Vector3 boltStart = haloVisual.position + forward * 0.02f;
        Vector3 travel = target - boltStart;
        if (travel.sqrMagnitude < 0.0001f || (!networkControlled && Vector3.Dot(travel, forward) < 0.5f))
            travel = forward * boltMaxDistance;
        if (!boltPool) {
            var flights = new GameObject("Halo Bolts (Runtime)");
            flights.transform.SetParent(transform, false);
            boltPool = flights.AddComponent<HaloBoltPool>();
            boltPool.Configure(beamCore, beamGlow);
        }
        boltPool.Play(boltStart, travel.normalized, Mathf.Min(travel.magnitude, boltMaxDistance),
            boltSpeed, boltLength, revolverEquipped && revolverVisual ? revolverVisual.lightColor : aimedHaloColor, boltLightIntensity, boltLightRange);
    }

    private void SetBoltVisible(bool visible) {
        if (beamGlow) beamGlow.enabled = visible;
        if (beamCore) beamCore.enabled = visible;
    }

    /// <summary>
    /// Self-heal for clones and older prefabs: the aura strokes and the halo
    /// light are created here when the asset predates them.
    /// </summary>
    private void EnsureGlowStrokes() {
        if (haloStrokes == null || haloStrokes.Length == 0) return;
        bool complete = haloGlowStrokes != null && haloGlowStrokes.Length == haloStrokes.Length;
        if (complete)
            foreach (var stroke in haloGlowStrokes)
                if (!stroke) { complete = false; break; }
        if (complete) return;
        var glow = new LineRenderer[haloStrokes.Length];
        for (int i = 0; i < glow.Length; i++) {
            var core = haloStrokes[i];
            if (!core) continue;
            var lineObject = new GameObject(core.name + "_Glow");
            lineObject.transform.SetParent(core.transform.parent, false);
            lineObject.transform.localPosition = core.transform.localPosition;
            lineObject.transform.localRotation = core.transform.localRotation;
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = core.useWorldSpace;
            line.alignment = core.alignment;
            line.widthMultiplier = core.widthMultiplier * 2.3f;
            line.numCapVertices = core.numCapVertices;
            line.numCornerVertices = core.numCornerVertices;
            line.positionCount = core.positionCount;
            for (int p = 0; p < core.positionCount; p++)
                line.SetPosition(p, core.GetPosition(p));
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.sharedMaterial = GlowMaterial();
            line.sortingOrder = -1;
            glow[i] = line;
        }
        haloGlowStrokes = glow;
    }

    private void EnsureHaloLight() {
        if (!haloVisual) return;
        if (!haloLight) {
            var lightObject = new GameObject("HaloLight");
            lightObject.transform.SetParent(haloVisual, false);
            haloLight = lightObject.AddComponent<Light>();
            haloLight.type = LightType.Point;
            haloLight.range = lightRange;
            haloLight.intensity = idleLightIntensity;
            haloLight.shadows = LightShadows.None;
        }
        if (!haloFaceLightFront)
            haloFaceLightFront = CreateFaceLight("HaloFaceFront", Quaternion.identity);
        if (!haloFaceLightBack)
            haloFaceLightBack = CreateFaceLight("HaloFaceBack", Quaternion.Euler(0f, 180f, 0f));
    }

    private Light CreateFaceLight(string name, Quaternion localRotation) {
        var faceObject = new GameObject(name);
        faceObject.transform.SetParent(haloVisual, false);
        faceObject.transform.localRotation = localRotation;
        var light = faceObject.AddComponent<Light>();
        light.type = LightType.Spot;
        light.spotAngle = faceSpotAngle;
        light.innerSpotAngle = faceSpotAngle * 0.55f;
        light.range = lightRange;
        light.intensity = idleLightIntensity * faceLightIntensityScale;
        light.shadows = LightShadows.None;
        return light;
    }

    // Aura renders one queue earlier so the crisp core composites on top of it.
    // HaloLine applies the HDR gain the LineRenderer colour cannot hold.
    private static Material glowMaterial;
    private static Material GlowMaterial() {
        if (glowMaterial) return glowMaterial;
        var shader = Shader.Find("DollSinger/HaloLine");
        if (!shader) return null;
        glowMaterial = new Material(shader) {
            name = "HaloGlow",
            renderQueue = 2999,
            hideFlags = HideFlags.HideAndDontSave
        };
        glowMaterial.SetColor("_Color", Color.white);
        glowMaterial.SetFloat("_Gain", 4f);
        return glowMaterial;
    }

    private void OnGUI() {
        if (!wantsAim || !input || !input.enabled) return;
        float x = Screen.width * 0.5f;
        float y = Screen.height * 0.5f;
        GUI.color = new Color(1f, 0.38f, 0.52f, 0.95f);
        GUI.DrawTexture(new Rect(x - 1f, y - 1f, 2f, 2f), Texture2D.whiteTexture);
        if (!networkControlled && revolverEquipped)
            GUI.Label(new Rect(x + 24f, y + 18f, 160f, 24f), $"REVOLVER {weaponAmmo}/6" + (localReloadRemaining > 0f ? " RELOAD" : ""));
        GUI.color = Color.white;
    }

    private static Transform FindTip(Transform distal) => distal && distal.childCount > 0
        ? distal.GetChild(0) : distal;
}

}
