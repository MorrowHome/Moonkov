using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>Keeps the hands outside the skirt and places them on its front during a fall.</summary>
[DefaultExecutionOrder(120)]
public sealed class SkirtHandAvoidance : MonoBehaviour {
    public Animator animator;
    public Transform skirtCenter;
    public Transform leftHand;
    public Transform rightHand;
    public Transform[] outerSkirtRoots;
    public DollSingerHaloAim haloAim;
    [Header("Free-fall skirt cover")]
    public SecondaryBoneSpring spring;
    [Tooltip("Wrist targets relative to the skirt center, in character-local metres. Fixed to the waist so lifting skirt bones cannot pull the hands beyond arm reach.")]
    public Vector3 leftCoverTargetOffset = new Vector3(-0.015f, -0.025f, 0.115f);
    public Vector3 rightCoverTargetOffset = new Vector3(-0.015f, -0.005f, 0.16f);
    [Tooltip("0 lets the FreeFall Cover animation control both hands for manual keyframing. Raise this only when you want procedural wrist targets and palm correction.")]
    [Range(0f, 1f)] public float coverHandIKWeight = 0f;
    [Tooltip("Palm roll used only while Cover Hand IK Weight is above zero.")]
    [Range(0f, 1f)] public float coverPalmFacingWeight = 1f;
    [Min(0.01f)] public float coverBlendSpeed = 4f;
    [Range(0f, 1f)] public float coverAirflowThreshold = 0.15f;
    public string coverLayerName = "FreeFall Cover";

    public float CoverBlend => m_CoverBlend;

    [Min(0f)] public float handClearance = 0.04f;
    [Min(0f)] public float maxHandCorrection = 0.18f;
    [Tooltip("Minimum direction alignment (cosine) for a skirt chain sample to count as the hand's facing. Lower for skirts with few chains.")]
    [Range(0f, 1f)] public float minChainAlignment = 0.85f;
    [Tooltip("Time for a hand to follow changes in the skirt contour. Smooths contact and release instead of snapping the IK on/off.")]
    [Min(0.01f)] public float handAvoidanceSmoothTime = 0.08f;

    private Vector3 m_LeftOffsetLocal;
    private Vector3 m_RightOffsetLocal;
    private Vector3 m_LeftOffsetVelocity;
    private Vector3 m_RightOffsetVelocity;
    private float m_CoverBlend;
    private float m_HandCoverBlend;
    private int m_CoverLayerIndex = -1;
    private bool m_PresentationDriven;
    private float m_PresentationDeltaTime, m_PresentationAim;

    public void PreparePresentation(float deltaTime, float aimWeight) {
        m_PresentationDriven = true;
        m_PresentationDeltaTime = deltaTime;
        m_PresentationAim = aimWeight;
        if (!animator) animator = GetComponent<Animator>();
    }

    private void Awake() {
        if (!spring) spring = GetComponent<SecondaryBoneSpring>();
        if (!animator) animator = GetComponent<Animator>();
        m_CoverLayerIndex = animator ? animator.GetLayerIndex(coverLayerName) : -1;
    }

    private void Update() {
        if (m_PresentationDriven) return;
        if (!animator || m_CoverLayerIndex < 0) return;
        bool falling = spring && skirtCenter &&
            spring.AirflowBlend > coverAirflowThreshold;
        m_CoverBlend = Mathf.MoveTowards(m_CoverBlend, falling ? 1f : 0f,
            Time.deltaTime * coverBlendSpeed);
        m_HandCoverBlend = m_CoverBlend * (1f - (haloAim ? haloAim.AimBlend : 0f));
        animator.SetLayerWeight(m_CoverLayerIndex, m_HandCoverBlend);
    }

    private void LateUpdate() {
        // The humanoid position IK changes wrist orientation after OnAnimatorIK.
        // Roll the final hands around their finger axes so their palms face the
        // body without bending the fingers or moving the wrist targets.
        if (!animator || !animator.isHuman || m_HandCoverBlend <= 0.001f ||
            coverHandIKWeight <= 0f ||
            coverPalmFacingWeight <= 0f) return;
        FaceCoverPalm(HumanBodyBones.LeftHand, HumanBodyBones.LeftIndexProximal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftLittleProximal);
        FaceCoverPalm(HumanBodyBones.RightHand, HumanBodyBones.RightIndexProximal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightLittleProximal);
    }

    private void OnAnimatorIK(int layerIndex) {
        if (!animator || !animator.isHuman) return;
        if (m_PresentationDriven) {
            if (layerIndex != 0) return;
            if (m_PresentationAim > 0.001f) {
                ResetOffsets();
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            } else if (skirtCenter && outerSkirtRoots != null) {
                SetHandGoal(AvatarIKGoal.LeftHand, leftHand, ref m_LeftOffsetLocal, ref m_LeftOffsetVelocity);
                SetHandGoal(AvatarIKGoal.RightHand, rightHand, ref m_RightOffsetLocal, ref m_RightOffsetVelocity);
            }
            return;
        }
        if (haloAim && layerIndex == haloAim.AimLayerIndex) {
            haloAim.TryApplyHandIK(AvatarIKGoal.LeftHand);
            haloAim.TryApplyHandIK(AvatarIKGoal.RightHand);
            return;
        }
        if (layerIndex == m_CoverLayerIndex) {
            if (m_HandCoverBlend <= 0.001f) return;
            SetCoverGoal(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow,
                leftCoverTargetOffset, -1f);
            SetCoverGoal(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow,
                rightCoverTargetOffset, 1f);
            return;
        }
        if (layerIndex != 0) return;
        if (m_HandCoverBlend > 0.001f || (haloAim && haloAim.AimBlend > 0.001f)) {
            ResetOffsets();
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
            return;
        }
        if (!skirtCenter || outerSkirtRoots == null) return;
        SetHandGoal(AvatarIKGoal.LeftHand, leftHand,
            ref m_LeftOffsetLocal, ref m_LeftOffsetVelocity);
        SetHandGoal(AvatarIKGoal.RightHand, rightHand,
            ref m_RightOffsetLocal, ref m_RightOffsetVelocity);
    }

    private void OnDisable() {
        ResetOffsets();
        m_CoverBlend = m_HandCoverBlend = 0f;
        if (animator && m_CoverLayerIndex >= 0)
            animator.SetLayerWeight(m_CoverLayerIndex, 0f);
    }

    private void SetCoverGoal(AvatarIKGoal goal, AvatarIKHint hint,
        Vector3 localOffset, float side) {
        float weight = m_HandCoverBlend * coverHandIKWeight;
        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKHintPositionWeight(hint, weight * 0.7f);
        animator.SetIKRotationWeight(goal, 0f);
        if (weight <= 0.001f) return;
        animator.SetIKPosition(goal, skirtCenter.position + transform.TransformDirection(localOffset));
        var elbow = animator.GetBoneTransform(side < 0f
            ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        if (elbow)
            animator.SetIKHintPosition(hint, elbow.position + transform.right * (side * 0.08f));
    }

    private void FaceCoverPalm(HumanBodyBones handBone, HumanBodyBones indexBone,
        HumanBodyBones middleBone, HumanBodyBones littleBone) {
        var wrist = animator.GetBoneTransform(handBone);
        var index = animator.GetBoneTransform(indexBone);
        var middle = animator.GetBoneTransform(middleBone);
        var little = animator.GetBoneTransform(littleBone);
        if (!wrist || !index || !middle || !little) return;
        Vector3 fingers = (middle.position - wrist.position).normalized;
        Vector3 palm = Vector3.Cross(index.position - little.position, fingers).normalized;
        // The same cross product points toward the palm on the left hand and
        // toward the back of the hand on the mirrored right-hand skeleton.
        if (handBone == HumanBodyBones.RightHand) palm = -palm;
        Vector3 towardBody = Vector3.ProjectOnPlane(-transform.forward, fingers);
        if (fingers.sqrMagnitude < 0.001f || palm.sqrMagnitude < 0.001f ||
            towardBody.sqrMagnitude < 0.001f) return;
        float roll = Vector3.SignedAngle(palm, towardBody.normalized, fingers);
        wrist.rotation = Quaternion.AngleAxis(roll * m_HandCoverBlend * coverHandIKWeight * coverPalmFacingWeight,
            fingers) * wrist.rotation;
    }

    private void ResetOffsets() {
        m_LeftOffsetLocal = m_RightOffsetLocal = Vector3.zero;
        m_LeftOffsetVelocity = m_RightOffsetVelocity = Vector3.zero;
    }

    private void SetHandGoal(AvatarIKGoal goal, Transform hand,
        ref Vector3 offsetLocal, ref Vector3 offsetVelocity) {
        animator.SetIKRotationWeight(goal, 0f);
        if (!hand) {
            offsetLocal = offsetVelocity = Vector3.zero;
            animator.SetIKPositionWeight(goal, 0f);
            return;
        }
        Vector3 position = hand.position;
        Vector3 radial = Vector3.ProjectOnPlane(position - skirtCenter.position, transform.up);
        float handRadius = radial.magnitude;
        Vector3 desiredOffsetLocal = Vector3.zero;
        if (handRadius >= 0.001f) {
            Vector3 outward = radial / handRadius;

            // Each outer root is a short vertical chain. Interpolate it at the
            // hand's height, then use the nearest facing chain.
            float bestAlignment = minChainAlignment;
            float skirtRadius = 0f;
            foreach (var root in outerSkirtRoots) {
                var bone = root;
                while (bone && bone.childCount == 1) {
                    var child = bone.GetChild(0);
                    float top = bone.position.y;
                    float bottom = child.position.y;
                    if (position.y <= Mathf.Max(top, bottom) && position.y >= Mathf.Min(top, bottom)) {
                        float t = Mathf.InverseLerp(top, bottom, position.y);
                        Vector3 sample = Vector3.Lerp(bone.position, child.position, t);
                        Vector3 sampleRadial = Vector3.ProjectOnPlane(sample - skirtCenter.position, transform.up);
                        float radius = sampleRadial.magnitude;
                        float alignment = Vector3.Dot(outward, sampleRadial / Mathf.Max(radius, 0.001f));
                        if (alignment > bestAlignment) {
                            bestAlignment = alignment;
                            skirtRadius = radius;
                        }
                        break;
                    }
                    bone = child;
                }
            }
            float correction = Mathf.Clamp(skirtRadius + handClearance - handRadius,
                0f, maxHandCorrection);
            desiredOffsetLocal = transform.InverseTransformDirection(outward * correction);
        }

        offsetLocal = Vector3.SmoothDamp(offsetLocal, desiredOffsetLocal,
            ref offsetVelocity, handAvoidanceSmoothTime, Mathf.Infinity,
            m_PresentationDriven ? m_PresentationDeltaTime : Time.deltaTime);
        // Blend the IK solver in with the correction. A binary weight made the
        // whole arm suddenly solve toward a nearly identical wrist target on
        // every shallow skirt contact during the walk and run cycles.
        float weight = Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01(offsetLocal.magnitude / 0.04f));
        animator.SetIKPositionWeight(goal, weight);
        if (weight > 0.001f)
            animator.SetIKPosition(goal, position + transform.TransformDirection(offsetLocal));
    }
}

}
