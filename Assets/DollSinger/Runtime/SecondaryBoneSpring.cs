using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>
/// Lightweight secondary motion for the MMD hair and skirt bones. The Animator
/// drives the body; this component follows it in LateUpdate using spring tips.
/// Tips live in world space so body motion creates natural lag, while the bone
/// itself stays rigid: tip velocity is kept tangential and the bend angle is
/// limited so a spike can never flip a chain through the body.
/// </summary>
[DefaultExecutionOrder(130)]
public sealed class SecondaryBoneSpring : MonoBehaviour {
    [System.Serializable]
    public struct BoneSphere {
        public Transform bone;
        [Min(0f)] public float radius;
    }

    [System.Serializable]
    public struct BoneCapsule {
        public Transform start;
        public Transform end;
        [Min(0f)] public float radius;
    }

    public Transform[] hairRoots;
    public Transform[] skirtRoots;
    [Tooltip("Hand and finger spheres that keep the skirt bones outside the animated hands.")]
    public BoneSphere[] skirtColliders;
    [Tooltip("Extra hand clearance during the free-fall cover pose, accounting for cloth between skirt bones.")]
    [Min(0f)] public float coverHandCollisionPadding = 0.015f;
    [Tooltip("Torso and leg capsules that keep the hair tips outside the body.")]
    public BoneCapsule[] hairBodyColliders;
    [Tooltip("Torso and leg capsules that keep the skirt tips outside the body.")]
    public BoneCapsule[] skirtBodyColliders;
    [Tooltip("Additional pelvis clearance for the front skirt only. Keep its radius below the front skirt's rest-pose clearance.")]
    public BoneCapsule[] frontSkirtBodyColliders;
    [Tooltip("Bones whose rest pose already lies inside a body capsule. They keep spring motion but skip that capsule correction.")]
    public Transform[] bodyCollisionExemptBones;
    [Tooltip("Keep the inner skirt chains inside the outer skirt ring. Requires SkirtHandAvoidance outerSkirtRoots.")]
    public bool separateInnerSkirt;
    [Min(0f)] public float innerSkirtClearance = 0.012f;
    [Tooltip("Additional space between inner and outer skirt while covering it with the hands.")]
    [Min(0f)] public float coverInnerSkirtExtraClearance = 0.04f;
    [Range(1f, 45f)] public float innerSkirtMaxCorrectionAngle = 12f;

    [Header("Chest")]
    [Tooltip("Chest bones to give soft-body weight. Leave empty to auto-find 胸.L / 胸.R, the standard MMD breast bones.")]
    public Transform[] chestRoots;
    [Tooltip("Virtual mass in character-local metres. A forward offset lets vertical footsteps bend the chest bones up and down.")]
    public Vector3 chestTipOffset = new Vector3(0f, 0f, 0.08f);
    [Range(0f, 1f)] public float chestDamping = 0.86f;
    public float chestStiffness = 60f;
    [Tooltip("Downward pull on the mass. Keep this small: it is a constant droop from the animated pose, so a large value changes the idle silhouette. Walking jiggle comes from inertia, not from here.")]
    public float chestGravity = 0.15f;
    [Tooltip("Maximum swing away from the animated pose, in degrees. Keep this small — realistic weight is subtle.")]
    [Range(2f, 45f)] public float chestMaxBendAngle = 12f;
    [Tooltip("Fraction of chest motion kept at walking speed. Lower this for a calmer walk.")]
    [Range(0f, 1f)] public float chestWalkAmplitude = 0.35f;
    [Tooltip("Fraction of chest motion kept at running speed. Keep above Walk Amplitude for a stronger run.")]
    [Range(0f, 1f)] public float chestRunAmplitude = 0.7f;
    [Min(0f)] public float chestWalkSpeed = 2f;
    [Min(0f)] public float chestRunSpeed = 5.335f;
    [Tooltip("Response to changes in body velocity. Steady translation is carried fully, so walking cannot hold the chest at its bend limit.")]
    [Range(0f, 1f)] public float chestRootInertia = 0.35f;

    [Header("Chest landing")]
    [Min(0f)] public float chestLandingMinFallSpeed = 2f;
    [Min(0f)] public float chestLandingImpulse = 0.03f;
    [Tooltip("Velocity retained per 60 Hz step briefly after landing. Higher values give more fading bounces.")]
    [Range(0f, 1f)] public float chestLandingDamping = 0.94f;
    [Min(0.1f)] public float chestLandingDuration = 1.1f;

    [Range(0f, 1f)] public float hairDamping = 0.82f;
    [Range(0f, 1f)] public float skirtDamping = 0.72f;
    public float hairStiffness = 100f;
    public float skirtStiffness = 130f;
    public float hairGravity = 0.12f;
    public float skirtGravity = 0.04f;
    [Tooltip("Maximum hair spring bend away from the rest pose, in degrees.")]
    [Range(5f, 90f)] public float maxBendAngle = 55f;
    [Tooltip("Maximum skirt spring bend away from the rest pose. Keep this shallow so running cannot fold the front panels into the body. Hand collision may bend farther.")]
    [Range(5f, 90f)] public float skirtMaxBendAngle = 20f;
    [Tooltip("Absolute skirt bend limit after collision correction. Prevents a proxy from folding a panel across the body.")]
    [Range(5f, 90f)] public float skirtCollisionMaxBendAngle = 32f;
    [Tooltip("Hard cap on tip speed in m/s; one bad frame cannot blow up the chain.")]
    public float maxTipSpeed = 8f;
    [Tooltip("Fraction of body translation the tips carry along (0 = pure world-space lag, 1 = tips glued to the body). Raise it if the tails stream out dead-straight at sprint.")]
    [Range(0f, 1f)] public float rootInertia = 0.7f;
    [Tooltip("Spring stiffening per m/s of body speed; keeps the lag shallow while sprinting.")]
    public float stiffnessSpeedGain = 0.25f;

    [Header("Airborne hair and skirt")]
    [Tooltip("Target speed that hair and skirt tips lag behind a falling body once airborne. Higher values lift them farther.")]
    [Min(0f)] public float airLagSpeed = 0.45f;
    [Tooltip("Smooth air disturbance on the hair in m/s²; zero disables hair flutter.")]
    [Min(0f)] public float hairAirFlutter = 5f;
    [Tooltip("Smooth air disturbance on the skirt in m/s²; zero disables skirt flutter.")]
    [Min(0f)] public float skirtAirFlutter = 3.5f;
    [Tooltip("How quickly the airborne disturbance changes; higher values make the flutter busier.")]
    [Min(0.1f)] public float airFlutterFrequency = 2.5f;
    [Tooltip("How fully the hair follows its upward airborne pose.")]
    [Range(0f, 1f)] public float hairAirLift = 1f;
    [Tooltip("How fully the skirt follows its outward and upward airborne pose.")]
    [Range(0f, 1f)] public float skirtAirLift = 0.95f;
    [Tooltip("Air-only hair bend limit; the ground limit stays unchanged.")]
    [Range(55f, 175f)] public float hairAirMaxBendAngle = 145f;
    [Tooltip("Air-only skirt bend limit; the ground limit stays unchanged.")]
    [Range(32f, 160f)] public float skirtAirMaxBendAngle = 120f;

    private readonly List<SpringJoint> m_Hair = new();
    private readonly List<SpringJoint> m_Skirt = new();
    private readonly List<SpringJoint> m_Chest = new();
    private readonly HashSet<Transform> m_BoundBones = new();
    private readonly HashSet<Transform> m_InnerSkirtBones = new();
    private readonly HashSet<Transform> m_FrontSkirtBones = new();
    private readonly HashSet<Transform> m_BodyCollisionExemptBones = new();
    private Transform m_SkirtCenter;
    private Transform[] m_OuterSkirtRoots;
    private SkirtHandAvoidance m_HandAvoidance;
    private Vector3 m_LastRootPosition;
    private Vector3 m_LastRootVelocity;
    private float m_ChestAmplitude;
    private float m_ChestLandingTimer;
    private float m_AirflowBlend;
    private CharacterController m_CharacterController;
    public float AirflowBlend => m_AirflowBlend;

    private sealed class SpringJoint {
        public Transform bone;
        public Vector3 localTip;
        public Quaternion restLocalRotation;
        public Vector3 tip;
        public Vector3 velocity;
        public float airflowPhase;
    }

    private void OnEnable() {
        m_Hair.Clear();
        m_Skirt.Clear();
        m_Chest.Clear();
        m_BoundBones.Clear();
        m_InnerSkirtBones.Clear();
        m_FrontSkirtBones.Clear();
        m_BodyCollisionExemptBones.Clear();
        if (bodyCollisionExemptBones != null)
            foreach (var bone in bodyCollisionExemptBones)
                if (bone) m_BodyCollisionExemptBones.Add(bone);
        AddChains(hairRoots, m_Hair, m_BoundBones);
        AddChains(skirtRoots, m_Skirt, m_BoundBones);
        BindInnerSkirt();
        BindFrontSkirt();
        AddChestJoints(m_Chest, m_BoundBones);
        m_CharacterController = GetComponentInParent<CharacterController>();
        m_LastRootPosition = transform.position;
        m_LastRootVelocity = Vector3.zero;
        m_ChestAmplitude = chestWalkAmplitude;
        m_ChestLandingTimer = 0f;
        m_AirflowBlend = 0f;
        ResetTips(m_Hair);
        ResetTips(m_Skirt);
        ResetTips(m_Chest);
    }

    private void BindInnerSkirt() {
        m_SkirtCenter = null;
        m_OuterSkirtRoots = null;
        if (!separateInnerSkirt) return;
        var avoidance = GetComponent<SkirtHandAvoidance>();
        if (!avoidance || !avoidance.skirtCenter || avoidance.outerSkirtRoots == null) return;
        m_SkirtCenter = avoidance.skirtCenter;
        m_OuterSkirtRoots = avoidance.outerSkirtRoots;
        foreach (var joint in m_Skirt) {
            bool outer = false;
            foreach (var root in m_OuterSkirtRoots) {
                if (!root) continue;
                if (joint.bone == root || joint.bone.IsChildOf(root)) { outer = true; break; }
            }
            if (!outer) m_InnerSkirtBones.Add(joint.bone);
        }
    }

    private void BindFrontSkirt() {
        m_HandAvoidance = GetComponent<SkirtHandAvoidance>();
        if (!m_HandAvoidance || !m_HandAvoidance.skirtCenter) return;
        Vector3 center = m_HandAvoidance.skirtCenter.position;
        foreach (var joint in m_Skirt) {
            Vector3 radial = Vector3.ProjectOnPlane(joint.bone.position - center, transform.up);
            if (radial.sqrMagnitude > 0.0001f &&
                Vector3.Dot(radial.normalized, transform.forward) > 0.35f)
                m_FrontSkirtBones.Add(joint.bone);
        }
    }

    // Chest bones are single bones with no single-child chain to walk, so they are bound
    // directly with an explicit mass offset instead of via AddChains. Convert a
    // character-local metre offset to bone space because the imported armature
    // carries a 100x scale.
    private void AddChestJoints(List<SpringJoint> joints, HashSet<Transform> bound) {
        var roots = chestRoots;
        if (roots == null || roots.Length == 0) {
            // Auto-find the standard MMD breast bones so no prefab wiring is required.
            var found = new List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "胸.L" || t.name == "胸.R") found.Add(t);
            roots = found.ToArray();
        }
        foreach (var root in roots) {
            if (!root || bound.Contains(root)) continue;
            bound.Add(root);
            joints.Add(new SpringJoint {
                bone = root,
                localTip = root.InverseTransformVector(transform.TransformDirection(chestTipOffset)),
                restLocalRotation = root.localRotation
            });
        }
    }

    // Roots may name every bone of one chain (the importer does for the skirt).
    // Walking on childCount==1 would then bind the same bone many times and the
    // copies would fight over one transform. Bind every bone at most once.
    private static void AddChains(Transform[] roots, List<SpringJoint> joints, HashSet<Transform> bound) {
        if (roots == null) return;
        foreach (var root in roots) {
            if (!root || bound.Contains(root)) continue;
            var bone = root;
            while (bone.childCount == 1) {
                if (bound.Contains(bone)) break;
                var child = bone.GetChild(0);
                bound.Add(bone);
                joints.Add(new SpringJoint {
                    bone = bone,
                    localTip = child.localPosition,
                    restLocalRotation = bone.localRotation,
                    airflowPhase = joints.Count * 0.67f
                });
                bone = child;
            }
        }
    }

    private static void ResetTips(List<SpringJoint> joints) {
        foreach (var joint in joints) {
            joint.bone.localRotation = joint.restLocalRotation;
            joint.tip = joint.bone.TransformPoint(joint.localTip);
            joint.velocity = Vector3.zero;
        }
    }

    private void LateUpdate() {
        Step(Time.deltaTime);
    }

    private void Step(float dt) {
        if (dt <= 0f) return;
        Vector3 rootDelta = transform.position - m_LastRootPosition;
        if (rootDelta.sqrMagnitude > 4f || dt > 0.2f) {
            ResetTips(m_Hair);
            ResetTips(m_Skirt);
            ResetTips(m_Chest);
            m_LastRootPosition = transform.position;
            m_LastRootVelocity = Vector3.zero;
            m_ChestAmplitude = chestWalkAmplitude;
            m_ChestLandingTimer = 0f;
            m_AirflowBlend = 0f;
            return;
        }
        m_LastRootPosition = transform.position;
        Vector3 rootVelocity = rootDelta / dt;
        float fallSpeed = Mathf.Max(0f, -rootVelocity.y);
        float targetAirflow = m_CharacterController && m_CharacterController.isGrounded
            ? 0f : Mathf.Clamp01((fallSpeed - 0.5f) / 3f);
        m_AirflowBlend = Mathf.MoveTowards(m_AirflowBlend, targetAirflow, dt * 3f);
        // Carry more of a fast fall onto the tips so constant falling speed cannot
        // hold every chain against its bend limit. Keep a bounded amount of lag.
        float airCarry = fallSpeed > 0.01f
            ? Mathf.Clamp01(1f - airLagSpeed / fallSpeed) : rootInertia;
        float carryFraction = Mathf.Lerp(rootInertia,
            Mathf.Max(rootInertia, airCarry), m_AirflowBlend);
        float cover = m_HandAvoidance ? m_HandAvoidance.CoverBlend : 0f;
        if (rootDelta.sqrMagnitude > 0f) {
            Vector3 carry = rootDelta * carryFraction;
            foreach (var joint in m_Hair) joint.tip += carry;
            foreach (var joint in m_Skirt)
                joint.tip += rootDelta * Mathf.Lerp(carryFraction, 1f,
                    m_FrontSkirtBones.Contains(joint.bone) ? cover : 0f);
        }
        Vector3 velocityChange = rootVelocity - m_LastRootVelocity;
        m_ChestLandingTimer = Mathf.Max(0f, m_ChestLandingTimer - dt);
        // A genuine landing abruptly arrests a downward fall. Ordinary footstep
        // bob stays below the fall-speed threshold, so it does not retrigger this.
        if (m_ChestLandingTimer <= 0f && m_LastRootVelocity.y < -chestLandingMinFallSpeed &&
            velocityChange.y > 1.5f) {
            float impactSpeed = Mathf.Min(-m_LastRootVelocity.y, 8f);
            foreach (var joint in m_Chest)
                joint.velocity += Vector3.down * (impactSpeed * chestLandingImpulse);
            m_ChestLandingTimer = chestLandingDuration;
            m_ChestAmplitude = Mathf.Max(m_ChestAmplitude, chestRunAmplitude);
        }
        m_LastRootVelocity = rootVelocity;
        foreach (var joint in m_Chest) {
            joint.tip += rootDelta;
            joint.velocity -= velocityChange * (chestRootInertia * 0.1f);
        }
        float rootSpeed = rootDelta.magnitude / dt;
        float horizontalSpeed = Vector3.ProjectOnPlane(rootVelocity, Vector3.up).magnitude;
        float runBlend = Mathf.InverseLerp(chestWalkSpeed,
            Mathf.Max(chestWalkSpeed + 0.01f, chestRunSpeed), horizontalSpeed);
        float targetChestAmplitude = Mathf.Lerp(chestWalkAmplitude, chestRunAmplitude, runBlend);
        if (m_ChestLandingTimer > 0f)
            targetChestAmplitude = Mathf.Max(targetChestAmplitude, chestRunAmplitude);
        // Raise the visible amplitude promptly when accelerating, but retain it
        // long enough for the spring to finish oscillating after a sudden stop.
        float amplitudeSpeed = targetChestAmplitude > m_ChestAmplitude ? 3f : 0.5f;
        m_ChestAmplitude = Mathf.MoveTowards(m_ChestAmplitude, targetChestAmplitude,
            dt * amplitudeSpeed);
        float landingDampingBlend = Mathf.Clamp01(m_ChestLandingTimer / 0.25f);
        float effectiveChestDamping = Mathf.Lerp(chestDamping,
            Mathf.Max(chestDamping, chestLandingDamping), landingDampingBlend);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 60f));
        float stepTime = dt / steps;
        for (int i = 0; i < steps; i++) {
            Simulate(m_Hair, stepTime, hairDamping, hairStiffness, hairGravity, rootSpeed,
                maxBendAngle, maxBendAngle, 1f, null, hairBodyColliders, hairAirFlutter,
                hairAirLift, hairAirMaxBendAngle, 0.96f);
            Simulate(m_Skirt, stepTime, skirtDamping, skirtStiffness, skirtGravity, rootSpeed,
                skirtMaxBendAngle, skirtCollisionMaxBendAngle, 1f, skirtColliders,
                skirtBodyColliders, skirtAirFlutter, skirtAirLift, skirtAirMaxBendAngle, 0.45f);
            Simulate(m_Chest, stepTime, effectiveChestDamping, chestStiffness, chestGravity, rootSpeed,
                chestMaxBendAngle, chestMaxBendAngle, m_ChestAmplitude, null, null,
                0f, 0f, chestMaxBendAngle, 0f);
        }
    }

    private void Simulate(List<SpringJoint> joints, float dt, float damping,
        float stiffness, float gravity, float rootSpeed, float bendLimit, float collisionBendLimit,
        float motionAmplitude,
        BoneSphere[] colliders, BoneCapsule[] bodyColliders, float airFlutter,
        float airLift, float airBendLimit, float airVertical) {
        // Inspector damping is calibrated at 60 FPS; convert it to this step's
        // duration so high frame rates do not make the spring overdamped.
        float retainedVelocity = Mathf.Pow(Mathf.Clamp01(damping), dt * 60f);
        // Stiffer while the body moves fast so the lag stays shallow instead of
        // dragging every joint into one straight line behind the sprinter.
        float speedStiffness = stiffness * (1f + Mathf.Max(0f, rootSpeed) * stiffnessSpeedGain);
        foreach (var joint in joints) {
            var bone = joint.bone;
            if (!bone || !bone.parent) continue;
            float handCover = joints == m_Skirt && m_HandAvoidance
                ? m_HandAvoidance.CoverBlend : 0f;
            bool innerSkirtBone = separateInnerSkirt && joints == m_Skirt &&
                m_InnerSkirtBones.Contains(bone);
            float frontCover = joints == m_Skirt && m_FrontSkirtBones.Contains(bone)
                ? handCover : 0f;
            float jointAirflow = m_AirflowBlend *
                (1f - Mathf.Max(frontCover, innerSkirtBone ? handCover : 0f));
            float maxBendRad = Mathf.Lerp(bendLimit, airBendLimit, jointAirflow) * Mathf.Deg2Rad;
            var restRotation = bone.parent.rotation * joint.restLocalRotation;
            bone.rotation = restRotation;
            Vector3 origin = bone.position;
            // FBX armatures can have an imported 100x parent scale. TransformVector
            // includes that scale; rotating localTip alone would shrink the spring.
            Vector3 restDirection = bone.TransformVector(joint.localTip);
            float length = restDirection.magnitude;
            if (length < 0.0001f) continue;
            Vector3 targetDirection = restDirection;
            if (jointAirflow > 0f && airLift > 0f) {
                Vector3 outward = Vector3.ProjectOnPlane(restDirection, transform.up);
                if (outward.sqrMagnitude < 0.000001f)
                    outward = Vector3.ProjectOnPlane(origin - transform.position, transform.up);
                if (outward.sqrMagnitude < 0.000001f) outward = transform.forward;
                float up = Mathf.Clamp01(airVertical);
                Vector3 lifted = (transform.up * up + outward.normalized *
                    Mathf.Sqrt(1f - up * up)) * length;
                targetDirection = Vector3.Slerp(restDirection, lifted,
                    Mathf.Clamp01(jointAirflow * airLift));
            }
            Vector3 acceleration = (origin + targetDirection - joint.tip) *
                (speedStiffness * (1f + frontCover)) +
                Vector3.down * gravity;
            if (jointAirflow > 0f && airFlutter > 0f) {
                float noiseTime = Time.time * airFlutterFrequency;
                Vector3 wind = transform.right * (Mathf.PerlinNoise(noiseTime, joint.airflowPhase) * 2f - 1f) +
                    transform.forward * (Mathf.PerlinNoise(joint.airflowPhase + 17f,
                        noiseTime * 0.73f) * 2f - 1f);
                acceleration += Vector3.ProjectOnPlane(wind, restDirection.normalized) *
                    (airFlutter * jointAirflow);
            }
            Vector3 velocity = (joint.velocity + acceleration * dt) *
                Mathf.Lerp(retainedVelocity, retainedVelocity * 0.6f, frontCover);
            if (velocity.sqrMagnitude > maxTipSpeed * maxTipSpeed)
                velocity = velocity.normalized * maxTipSpeed;
            Vector3 next = joint.tip + velocity * dt;
            Vector3 direction = next - origin;
            if (direction.sqrMagnitude < 0.000001f) direction = restDirection;
            direction = direction.normalized * length;
            // Keep spring motion inside a cone around the animated rest pose;
            // collision avoidance can bend farther to keep the hand clear.
            direction = Vector3.RotateTowards(restDirection, direction, maxBendRad, 0f);
            if (colliders != null && handCover <= 0.001f)
                direction = ResolveCollisions(origin, restDirection, direction, length, colliders, 0f);
            if (bodyColliders != null && !m_BodyCollisionExemptBones.Contains(bone))
                direction = ResolveCapsuleCollisions(origin, direction, length, bodyColliders);
            if (joints == m_Skirt && m_FrontSkirtBones.Contains(bone) &&
                !m_BodyCollisionExemptBones.Contains(bone))
                direction = ResolveCapsuleCollisions(origin, direction, length, frontSkirtBodyColliders);
            direction = Vector3.RotateTowards(restDirection, direction,
                Mathf.Lerp(collisionBendLimit, airBendLimit, jointAirflow) * Mathf.Deg2Rad, 0f);
            // The hands push the outer skirt after its bend limit. The inner
            // skirt follows inside the outer skirt instead of being pushed past it.
            if (colliders != null && handCover > 0.001f && !innerSkirtBone)
                direction = ResolveCollisions(origin, restDirection, direction, length,
                    colliders, handCover * coverHandCollisionPadding);
            if (innerSkirtBone)
                direction = KeepInnerSkirtInsideOuter(origin, direction, length, handCover);
            // Only scale the visible chest rotation. Keep the spring tip and velocity
            // unscaled so walking does not damp the motion away every frame, and a
            // stopped character can still finish its inertial oscillation.
            Vector3 visibleDirection = direction;
            if (motionAmplitude < 1f)
                visibleDirection = Vector3.Slerp(restDirection, direction,
                    Mathf.Clamp01(motionAmplitude));
            Vector3 newTip = origin + direction;
            bone.rotation = Quaternion.FromToRotation(restDirection, visibleDirection) * restRotation;
            // A collision can move the tip outward instantly. Drop velocity
            // aimed back into that obstacle so it does not re-enter next frame.
            Vector3 correction = newTip - next;
            if (correction.sqrMagnitude > 0.00000001f && Vector3.Dot(velocity, correction) < 0f)
                velocity -= Vector3.Project(velocity, correction);
            // The bone is rigid: the tip may only orbit its origin. Dropping the
            // radial velocity keeps the spherical constraint from pumping energy
            // back into the spring every step — that is what made hair fly around.
            velocity -= Vector3.Project(velocity, direction / length);
            joint.velocity = velocity;
            joint.tip = newTip;
        }
    }

    private static Vector3 ResolveCapsuleCollisions(Vector3 origin, Vector3 direction,
        float length, BoneCapsule[] colliders) {
        foreach (var collider in colliders) {
            if (!collider.start || !collider.end || collider.radius <= 0f) continue;
            Vector3 axis = collider.end.position - collider.start.position;
            float axisLengthSq = axis.sqrMagnitude;
            if (axisLengthSq < 0.000001f) continue;
            Vector3 tip = origin + direction;
            float t = Vector3.Dot(tip - collider.start.position, axis) / axisLengthSq;
            // Bone endpoints overlap neighbouring proxies; testing their caps here
            // would push skirt roots away even in the idle pose.
            if (t < 0f || t > 1f) continue;
            Vector3 closest = collider.start.position + axis * t;
            Vector3 outward = tip - closest;
            float safeRadius = collider.radius + 0.003f;
            if (outward.sqrMagnitude >= safeRadius * safeRadius) continue;
            if (outward.sqrMagnitude < 0.000001f)
                outward = Vector3.ProjectOnPlane(tip - origin, axis);
            if (outward.sqrMagnitude < 0.000001f)
                outward = Vector3.Cross(axis, Vector3.up);
            if (outward.sqrMagnitude < 0.000001f)
                outward = Vector3.Cross(axis, Vector3.right);
            Vector3 target = closest + outward.normalized * safeRadius;
            Vector3 corrected = target - origin;
            if (corrected.sqrMagnitude > 0.000001f)
                direction = corrected.normalized * length;
        }
        return direction;
    }

    private Vector3 KeepInnerSkirtInsideOuter(Vector3 origin, Vector3 direction,
        float length, float handCover) {
        if (!m_SkirtCenter || m_OuterSkirtRoots == null) return direction;
        Vector3 center = m_SkirtCenter.position;
        Vector3 up = transform.up;
        Vector3 tip = origin + direction;
        Vector3 radial = Vector3.ProjectOnPlane(tip - center, up);
        float radius = radial.magnitude;
        if (radius < 0.0001f) return direction;
        Vector3 outward = radial / radius;
        float height = Vector3.Dot(tip - center, up);
        float bestAlignment = Mathf.Lerp(0.8f, 0.5f, handCover);
        float outerRadius = 0f;
        foreach (var root in m_OuterSkirtRoots) {
            var bone = root;
            while (bone && bone.childCount == 1) {
                var child = bone.GetChild(0);
                float top = Vector3.Dot(bone.position - center, up);
                float bottom = Vector3.Dot(child.position - center, up);
                bool withinHeight = height <= Mathf.Max(top, bottom) &&
                    height >= Mathf.Min(top, bottom);
                if (withinHeight || handCover > 0.001f) {
                    float t = Mathf.Abs(bottom - top) < 0.0001f ? 0f :
                        Mathf.Clamp01((height - top) / (bottom - top));
                    Vector3 sample = Vector3.ProjectOnPlane(
                        Vector3.Lerp(bone.position, child.position, t) - center, up);
                    float sampleRadius = sample.magnitude;
                    float alignment = Vector3.Dot(outward,
                        sample / Mathf.Max(sampleRadius, 0.0001f));
                    if (!withinHeight) alignment -= Mathf.Abs(height - Mathf.Lerp(top, bottom, t));
                    if (alignment > bestAlignment) {
                        bestAlignment = alignment;
                        outerRadius = sampleRadius;
                    }
                    if (withinHeight) break;
                }
                bone = child;
            }
        }
        float limit = outerRadius - innerSkirtClearance -
            handCover * coverInnerSkirtExtraClearance;
        if (limit <= 0f || radius <= limit) return direction;
        Vector3 inward = Vector3.ProjectOnPlane(center - origin, up);
        if (inward.sqrMagnitude < 0.000001f) return direction;
        Vector3 start = direction.normalized;
        Vector3 end = inward.normalized;
        float smallestReachableRadius = Vector3.ProjectOnPlane(
            origin + end * length - center, up).magnitude;
        if (smallestReachableRadius >= radius) return direction;
        float low = 0f, high = 1f;
        for (int i = 0; i < 6; i++) {
            float mid = (low + high) * 0.5f;
            Vector3 candidate = origin + Vector3.Slerp(start, end, mid) * length;
            float candidateRadius = Vector3.ProjectOnPlane(candidate - center, up).magnitude;
            if (candidateRadius > limit) low = mid;
            else high = mid;
        }
        Vector3 corrected = Vector3.Slerp(start, end, high) * length;
        return Vector3.RotateTowards(direction, corrected,
            Mathf.Lerp(innerSkirtMaxCorrectionAngle,
                Mathf.Max(innerSkirtMaxCorrectionAngle, 45f), handCover) * Mathf.Deg2Rad, 0f);
    }

    private static Vector3 ResolveCollisions(Vector3 origin, Vector3 restDirection,
        Vector3 direction, float length, BoneSphere[] colliders, float padding) {
        foreach (var collider in colliders) {
            if (!collider.bone || collider.radius <= 0f) continue;
            Vector3 toCenter = collider.bone.position - origin;
            float centerDistance = toCenter.magnitude;
            float radius = collider.radius + padding;
            if (centerDistance <= radius + 0.0001f) continue;

            // The cloth follows the entire bone from origin to tip. Checking
            // only the tip missed palms crossing the middle of a skirt panel.
            Vector3 along = direction / length;
            float nearest = Mathf.Clamp(Vector3.Dot(toCenter, along), 0f, length);
            if ((toCenter - along * nearest).sqrMagnitude >= radius * radius) continue;

            // Rotate the fixed-length bone to the tangent of the hand sphere.
            // When the nearest point is the tip, use the tip-on-sphere angle.
            float angle = nearest < length - 0.0001f
                ? Mathf.Asin(Mathf.Clamp01(radius / centerDistance))
                : Mathf.Acos(Mathf.Clamp((length * length + centerDistance * centerDistance -
                    radius * radius) / (2f * length * centerDistance), -1f, 1f));
            Vector3 centerAxis = toCenter / centerDistance;
            Vector3 tangent = Vector3.ProjectOnPlane(direction, centerAxis);
            if (tangent.sqrMagnitude < 0.000001f)
                tangent = Vector3.ProjectOnPlane(restDirection, centerAxis);
            if (tangent.sqrMagnitude < 0.000001f)
                tangent = Vector3.Cross(centerAxis, Vector3.up);
            if (tangent.sqrMagnitude < 0.000001f)
                tangent = Vector3.Cross(centerAxis, Vector3.right);
            angle += 0.005f;
            direction = length * (centerAxis * Mathf.Cos(angle) +
                                  tangent.normalized * Mathf.Sin(angle));
        }
        return direction;
    }
}

}
