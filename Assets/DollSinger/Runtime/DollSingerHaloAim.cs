using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>
/// Local visual prototype: hold RMB to aim a finger gun, click LMB to fire a cosmetic bolt.
/// The ring stays visible on every Doll Singer; only the local player's input drives the pose.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class DollSingerHaloAim : MonoBehaviour {
    [Header("Prefab visuals")]
    public Transform haloVisual;
    public RevolverHaloVisual revolverVisual;
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
    [Min(1f)] public float boltMaxDistance = 35f;
    [Tooltip("Standalone demo fire interval; network fire rate comes from HaloWeapon.")]
    [Min(0.02f)] public float localShotInterval = 0.1f;
    [Min(0.02f)] public float localRevolverShotInterval = 0.75f;
    [Min(0.02f)] public float localRevolverReloadTime = 0.75f;

    [Header("Halo layout")]
    [Min(0.1f)] public float idleHaloScale = 0.72f;
    [Min(0.1f)] public float aimedHaloScale = 0.45f;
    [Tooltip("Maximum camera-to-halo distance in metres; automatically limited by arm reach.")]
    [Range(0.25f, 0.8f)] public float firstPersonAimDistance = 0.45f;
    [Tooltip("Gap between the index fingertip and the halo along the aiming direction.")]
    [Range(0.02f, 0.15f)] public float fingertipHaloGap = 0.075f;
    [Tooltip("Camera-space vertical gap between the right thumb tip and the halo centre.")]
    [Range(0.04f, 0.2f)] public float firstPersonThumbClearance = 0.10f;
    [Range(0f, 0.1f)] public float firstPersonThumbDepth = 0.02f;
    [Tooltip("Camera-space support wrist offset from the firing wrist.")]
    public Vector3 firstPersonSupportOffset = new Vector3(-0.12f, -0.055f, -0.04f);

    [Header("Laser flight lighting")]
    [Min(0f)] public float boltLightIntensity = 4f;
    [Min(0.1f)] public float boltLightRange = 3f;

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
    [Min(0.1f)] public float flashlightRange = 24f;
    [Range(10f, 90f)] public float flashlightAngle = 42f;
    [Min(0f)] public float flashlightIntensity = 4f;

    private readonly RaycastHit[] rayHits = new RaycastHit[32];
    private Animator animator;
    private static readonly int AimBlendId = Animator.StringToHash("AimBlend");
    private bool hasAimBlendParameter;
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
    private Transform leftUpperArm;
    private Transform leftForearm;
    private Transform leftHand;
    private int aimLayerIndex = -1;
    private float aimBlend;
    private HaloBoltPool boltPool;
    private float nextLocalShotTime;
    private bool wantsAim;
    private bool revolverEquipped;
    private int localRevolverAmmo = 6;
    private float localReloadRemaining;
    private int weaponAmmo = 6;

    public void SetNetworkWeapon(bool revolver, int ammo, bool reloading, float reloadProgress, uint shotTick, uint reloadTick)
    {
        SetRevolverEquipped(revolver, ammo);
        weaponAmmo = ammo;
        if (revolverVisual) revolverVisual.SetNetworkState(ammo, reloading, reloadProgress, shotTick, reloadTick);
    }

    private void SetRevolverEquipped(bool revolver, int ammo)
    {
        revolverEquipped = revolver;
        if (revolverVisual) revolverVisual.SetEquipped(revolver, ammo);
        if (haloStrokes != null)
            foreach (var stroke in haloStrokes) if (stroke) stroke.enabled = !revolver;
        if (haloGlowStrokes != null)
            foreach (var stroke in haloGlowStrokes) if (stroke) stroke.enabled = !revolver;
    }

    [SerializeField] private float hhh;

#if UNITY_EDITOR
    // Used by isolated Editor validation; normal input remains RMB / LMB.
    [System.NonSerialized] public bool editorForceAim;
    public void EditorPreviewFire() => FireCosmeticBolt();
#endif

    public float AimBlend => aimBlend;
    public int AimLayerIndex => aimLayerIndex;

    public void SetNetworkPresentation(bool aiming, Vector3 direction, Vector3 aimPoint)
    {
        networkControlled = true;
        networkAiming = aiming;
        networkDirection = direction.normalized;
        networkAimPoint = aimPoint;
    }

    // Invoked by the existing predicted/server-confirmed shot effects path.
    // This is a cosmetic bolt; damage still belongs to the authoritative weapon system.
    public void PlayNetworkShot(Vector3 aimPoint)
    {
        if (revolverEquipped && revolverVisual) revolverVisual.PlayShot();
        FireCosmeticBolt(aimPoint);
    }

    private void Awake() {
        animator = GetComponent<Animator>();
        foreach (var parameter in animator.parameters)
            if (parameter.nameHash == AimBlendId && parameter.type == AnimatorControllerParameterType.Float)
                hasAimBlendParameter = true;
        if (!input) input = GetComponent<DollSingerInput>();
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
        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftForearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (!head || !chest || !rightIndexTip) {
            Debug.LogWarning("Doll Singer halo prototype: required Humanoid bones are missing.", this);
            enabled = false;
            return;
        }
        aimLayerIndex = animator.GetLayerIndex("Halo Aim");
        if (aimLayerIndex < 0)
            Debug.LogWarning("Doll Singer halo: editable 'Halo Aim' Animator layer is missing.", this);
        EnsureGlowStrokes();
        EnsureHaloLight();
        SetRevolverEquipped(false, localRevolverAmmo);
        SetBoltVisible(false);
    }

    private void Start() {
        if (!networkControlled && input) FindCamera();
    }

    private void Update() {
        if (!networkControlled && !input) return;
        if (!thirdPerson) thirdPerson = GetComponent<DollSingerMovement>();
        if (!networkControlled && !playerCamera) FindCamera();

        bool manualAim = networkControlled ? networkAiming :
            input.AimHeld && !(thirdPerson && thirdPerson.IsFreeLooking);
#if UNITY_EDITOR
        bool aimingInput = manualAim || editorForceAim;
#else
        bool aimingInput = manualAim;
#endif
        wantsAim = networkControlled ? networkAiming :
            thirdPerson && thirdPerson.enabled && playerCamera && aimingInput;
        if (thirdPerson) thirdPerson.IsLocallyAiming = wantsAim;
        if (input && input.enabled && input.LightPressed)
            haloLightEnabled = !haloLightEnabled;
        aimBlend = Mathf.MoveTowards(aimBlend, wantsAim ? 1f : 0f,
            Time.deltaTime / (wantsAim ? aimInSeconds : aimOutSeconds));
        if (aimLayerIndex >= 0) animator.SetLayerWeight(aimLayerIndex, aimBlend);
        if (hasAimBlendParameter) animator.SetFloat(AimBlendId, aimBlend);
        if (cameraOwner) cameraOwner.SetAimBlend(aimBlend);
        if (revolverVisual) revolverVisual.SetAimBlend(aimBlend);
        if (!networkControlled && input)
        {
            if (localReloadRemaining <= 0f && input.WeaponSlotPressed != 0)
            {
                SetRevolverEquipped(input.WeaponSlotPressed == 2, localRevolverAmmo);
                nextLocalShotTime = Time.time + (revolverEquipped ? localRevolverShotInterval : localShotInterval);
            }
            if (localReloadRemaining > 0f)
            {
                localReloadRemaining = Mathf.Max(0f, localReloadRemaining - Time.deltaTime);
                if (localReloadRemaining == 0f) localRevolverAmmo = 6;
            }
            if (revolverEquipped && localReloadRemaining == 0f && localRevolverAmmo < 6 &&
                (input.ReloadPressed || localRevolverAmmo == 0)) localReloadRemaining = localRevolverReloadTime;
            bool fire = revolverEquipped ? input.FirePressed && localRevolverAmmo > 0 && localReloadRemaining == 0f : input.FireHeld;
            if (wantsAim && aimBlend > 0.65f && fire && Time.time >= nextLocalShotTime)
            {
                nextLocalShotTime = Time.time + (revolverEquipped ? localRevolverShotInterval : localShotInterval);
                if (revolverEquipped)
                {
                    localRevolverAmmo--;
                    if (revolverVisual) revolverVisual.PlayShot();
                }
                FireCosmeticBolt();
            }
            weaponAmmo = localRevolverAmmo;
            if (revolverVisual) revolverVisual.SetState(localRevolverAmmo, localReloadRemaining > 0f,
                1f - localReloadRemaining / localRevolverReloadTime);
        }
    }

    /// <summary>Third-person pitch correction; the final first-person arms solve after camera and body lean.</summary>
    public bool TryApplyHandIK(AvatarIKGoal goal) {
        if (aimBlend <= 0.001f || (!networkControlled && !playerCamera) || !chest) return false;
        if (TryGetFirstPersonCamera(out _))
        {
            animator.SetIKPositionWeight(goal, 0f);
            animator.SetIKRotationWeight(goal, 0f);
            return true;
        }
        Vector3 aimPoint = FindAimPoint();
        Vector3 direction = (aimPoint - chest.position).normalized;
        float elevation = Mathf.Clamp(Vector3.Dot(direction, transform.up), -0.55f, 0.65f);
        Transform hand = animator.GetBoneTransform(goal == AvatarIKGoal.RightHand
            ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        if (!hand) return false;
        animator.SetIKHintPositionWeight(goal == AvatarIKGoal.RightHand
            ? AvatarIKHint.RightElbow : AvatarIKHint.LeftElbow, 0f);
        animator.SetIKPositionWeight(goal, aimBlend);
        animator.SetIKRotationWeight(goal, 0f);
        animator.SetIKPosition(goal, hand.position + transform.up * (elevation * 0.20f));
        return true;
    }

    private void LateUpdate() {
        if (!haloVisual || !head) return;
        bool centred = TryGetFirstPersonCamera(out var aimingCamera);
        Vector3 centredPosition = centred ? GetFirstPersonHaloPosition(aimingCamera) : Vector3.zero;
        float t = aimBlend * aimBlend * (3f - 2f * aimBlend);
        if (centred && t > 0.001f) ApplyFirstPersonHands(aimingCamera.transform, centredPosition, t);
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
        Quaternion aimingRotation = centred ? aimingCamera.transform.rotation : Quaternion.LookRotation(aimDirection, transform.up);
        haloVisual.rotation = Quaternion.Slerp(restingRotation, aimingRotation, t);
        float haloScale = Mathf.Lerp(idleHaloScale, aimedHaloScale, t);
        Vector3 parentScale = transform.lossyScale;
        haloVisual.localScale = new Vector3(haloScale / Mathf.Max(0.001f, parentScale.x),
            haloScale / Mathf.Max(0.001f, parentScale.y),
            haloScale / Mathf.Max(0.001f, parentScale.z));
        if (haloStrokes != null) {
            Color color = revolverEquipped && revolverVisual ? revolverVisual.lightColor : Color.Lerp(idleHaloColor, aimedHaloColor, t);
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
            if (haloLight) {
                haloLight.color = new Color(color.r, color.g, color.b, 1f);
                haloLight.intensity = Mathf.Lerp(idleLightIntensity, aimedLightIntensity, t);
                haloLight.range = lightRange;
                haloLight.enabled = haloLightEnabled;
            }
            // The two spot lights approximate a disc-shaped area light on each
            // ring face (local +/-Z is the ring's hole axis). Aiming turns the
            // front face into a long-throw flashlight along the aim direction.
            float faceIntensity = Mathf.Lerp(idleLightIntensity, aimedLightIntensity, t) *
                                  faceLightIntensityScale;
            Color faceColor = new Color(color.r, color.g, color.b, 1f);
            if (haloFaceLightFront) {
                haloFaceLightFront.color = faceColor;
                haloFaceLightFront.intensity = Mathf.Lerp(faceIntensity, flashlightIntensity, t);
                haloFaceLightFront.range = Mathf.Lerp(lightRange, flashlightRange, t);
                haloFaceLightFront.spotAngle = Mathf.Lerp(faceSpotAngle, flashlightAngle, t);
                haloFaceLightFront.innerSpotAngle = haloFaceLightFront.spotAngle * 0.55f;
                haloFaceLightFront.enabled = haloLightEnabled;
            }
            if (haloFaceLightBack) {
                haloFaceLightBack.color = faceColor;
                haloFaceLightBack.intensity = faceIntensity;
                haloFaceLightBack.range = lightRange;
                haloFaceLightBack.spotAngle = faceSpotAngle;
                haloFaceLightBack.innerSpotAngle = faceSpotAngle * 0.55f;
                haloFaceLightBack.enabled = haloLightEnabled;
            }
        }
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

    private void ApplyFirstPersonHands(Transform cameraTransform, Vector3 haloPosition, float blend)
    {
        if (!rightUpperArm || !rightForearm ||
            !TryGetFirstPersonWrist(cameraTransform, haloPosition, out var desiredWrist, out var wristRotation)) return;
        Vector3 direction = cameraTransform.forward;
        Quaternion correction = wristRotation * Quaternion.Inverse(rightHand.rotation);

        // Keep the finger shape, but lower and separate the hands instead of stacking
        // them in the sight window. Solve after torso lean, without translating bones.
        Vector3 supportPosition = desiredWrist + cameraTransform.rotation * firstPersonSupportOffset;
        Quaternion supportRotation = leftHand ? correction * leftHand.rotation : Quaternion.identity;
        Vector3 elbowBase = cameraTransform.position + direction * 0.12f - cameraTransform.up * 0.30f;
        SolveArm(rightUpperArm, rightForearm, rightHand, desiredWrist, wristRotation,
            elbowBase + cameraTransform.right * 0.24f, blend);
        if (leftUpperArm && leftForearm && leftHand)
            SolveArm(leftUpperArm, leftForearm, leftHand, supportPosition, supportRotation,
                elbowBase - cameraTransform.right * 0.24f, blend);
    }

    private bool TryGetFirstPersonWrist(Transform cameraTransform, Vector3 haloPosition,
        out Vector3 wristPosition, out Quaternion wristRotation)
    {
        wristPosition = Vector3.zero;
        wristRotation = Quaternion.identity;
        if (!rightHand || !rightIndexTip || !rightThumbTip) return false;
        Vector3 fingerDirection = rightIndexTip.position - rightHand.position;
        if (fingerDirection.sqrMagnitude < 0.000001f) return false;
        Quaternion correction = Quaternion.FromToRotation(fingerDirection, cameraTransform.forward);
        Vector3 thumbPosition = haloPosition - cameraTransform.up * firstPersonThumbClearance -
                               cameraTransform.forward * firstPersonThumbDepth;
        wristPosition = thumbPosition - correction * (rightThumbTip.position - rightHand.position);
        wristRotation = correction * rightHand.rotation;
        return true;
    }

    private static void SolveArm(Transform upper, Transform forearm, Transform hand,
        Vector3 target, Quaternion rotation, Vector3 elbowHint, float blend)
    {
        Vector3 origin = upper.position;
        float upperLength = Vector3.Distance(origin, forearm.position);
        float lowerLength = Vector3.Distance(forearm.position, hand.position);
        if (upperLength < 0.0001f || lowerLength < 0.0001f) return;
        target = Vector3.Lerp(hand.position, target, blend);
        Quaternion wristRotation = Quaternion.Slerp(hand.rotation, rotation, blend);
        Vector3 travel = target - origin;
        if (travel.sqrMagnitude < 0.000001f) return;
        Vector3 direction = travel.normalized;
        float distance = Mathf.Clamp(travel.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.0001f,
            upperLength + lowerLength - 0.0001f);
        // Clamp to actual limb reach; change joint rotations only, preserving bone lengths.
        target = origin + direction * distance;
        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 bend = Vector3.ProjectOnPlane(elbowHint - origin, direction);
        Vector3 originalBend = Vector3.ProjectOnPlane(forearm.position - origin, direction);
        bend = Vector3.Lerp(originalBend, bend, blend);
        if (bend.sqrMagnitude < 0.000001f) bend = Vector3.Cross(direction, upper.right);
        if (bend.sqrMagnitude < 0.000001f) bend = Vector3.Cross(direction, upper.up);
        Vector3 elbow = origin + direction * along + bend.normalized * height;
        upper.rotation = Quaternion.FromToRotation(forearm.position - origin, elbow - origin) * upper.rotation;
        forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position, target - forearm.position) * forearm.rotation;
        hand.rotation = wristRotation;
    }

    private void OnDisable() {
        wantsAim = false;
        aimBlend = 0f;
        nextLocalShotTime = 0;
        if (thirdPerson) thirdPerson.IsLocallyAiming = false;
        if (animator && aimLayerIndex >= 0) animator.SetLayerWeight(aimLayerIndex, 0f);
        if (animator && hasAimBlendParameter) animator.SetFloat(AimBlendId, 0f);
        if (cameraOwner) cameraOwner.SetAimBlend(0f);
        SetBoltVisible(false);
        if (boltPool) boltPool.ClearActive();
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
