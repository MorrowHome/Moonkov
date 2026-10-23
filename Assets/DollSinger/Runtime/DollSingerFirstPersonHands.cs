using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Owner-only arm projection. World optics retain their gameplay magnification.</summary>
    public sealed class DollSingerFirstPersonHands : IDisposable
    {
        public Camera Camera { get; }
        public Mesh BodyWithoutArms { get; }
        public bool Active { get; private set; }
        public bool HideOwnerBody => Active;
        private readonly SkinnedMeshRenderer renderer;
        private readonly SkinnedMeshRenderer source;
        private readonly Mesh armMesh;
        private readonly List<Camera> stack;
        private readonly Transform[] rightHandBones, leftHandBones;
        private readonly Transform leftIndexTip, rightIndexTip, leftThumbTip, rightThumbTip;
        private bool reframe = true, wasViewfinder, meleeMode;
        private float lastAspect, lastFieldOfView, poseBlend, lastVerticalOffset;
        private Quaternion framedOwnerRotation;
        private Rect safeFrame = Rect.MinMaxRect(.07f, .04f, .93f, .34f);

        public DollSingerFirstPersonHands(SkinnedMeshRenderer source, Mesh body, Camera owner, Animator animator)
        {
            this.source = source;
            int layer = LayerMask.NameToLayer("FirstPersonOverlay");
            if (layer < 0 || !owner || !animator || !animator.isHuman) return;
            var data = owner.GetUniversalAdditionalCameraData();
            stack = data.cameraStack;
            if (stack == null) return;
            var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (!left || !right || !rightHand || !leftHand) return;
            var armBones = new bool[source.bones.Length];
            for (int i = 0; i < armBones.Length; i++)
                armBones[i] = source.bones[i] &&
                    ((left && source.bones[i].IsChildOf(left)) || (right && source.bones[i].IsChildOf(right)));
            // Attached melee props and their inactive trail anchors are not hand joints.
            // Fit only skinned joints so hidden props cannot alter the hand projection.
            rightHandBones = Array.FindAll(source.bones, bone => bone && bone.IsChildOf(rightHand));
            leftHandBones = Array.FindAll(source.bones, bone => bone && bone.IsChildOf(leftHand));
            foreach (var bone in rightHandBones)
            {
                if (bone.name == "人指先.R") rightIndexTip = bone;
                if (bone.name == "親指先.R") rightThumbTip = bone;
            }
            foreach (var bone in leftHandBones)
            {
                if (bone.name == "人指先.L") leftIndexTip = bone;
                if (bone.name == "親指先.L") leftThumbTip = bone;
            }
            var weights = body.boneWeights;
            var armVertices = new bool[body.vertexCount];
            for (int i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                float sum = (armBones[w.boneIndex0] ? w.weight0 : 0) + (armBones[w.boneIndex1] ? w.weight1 : 0) +
                    (armBones[w.boneIndex2] ? w.weight2 : 0) + (armBones[w.boneIndex3] ? w.weight3 : 0);
                armVertices[i] = sum >= .5f;
            }
            armMesh = UnityEngine.Object.Instantiate(body);
            armMesh.name = "DollSinger gesture arms (owner only)";
            BodyWithoutArms = UnityEngine.Object.Instantiate(body);
            BodyWithoutArms.name = "DollSinger first person body without gesture arms";
            var arms = new List<int>();
            var torso = new List<int>();
            for (int s = 0; s < body.subMeshCount; s++)
            {
                arms.Clear(); torso.Clear();
                var indices = body.GetTriangles(s);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var destination = armVertices[indices[i]] || armVertices[indices[i + 1]] || armVertices[indices[i + 2]] ? arms : torso;
                    destination.Add(indices[i]); destination.Add(indices[i + 1]); destination.Add(indices[i + 2]);
                }
                armMesh.SetTriangles(arms, s, false);
                BodyWithoutArms.SetTriangles(torso, s, false);
            }
            var armObject = new GameObject("FirstPersonGestureArms (Runtime)") { hideFlags = HideFlags.DontSave, layer = layer };
            armObject.transform.SetParent(source.transform, false);
            renderer = armObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = armMesh;
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.bones = source.bones;
            renderer.rootBone = source.rootBone;
            renderer.localBounds = source.localBounds;
            renderer.quality = source.quality;
            renderer.renderingLayerMask = source.renderingLayerMask;
            renderer.updateWhenOffscreen = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = source.receiveShadows;
            renderer.forceRenderingOff = true;
            var cameraObject = new GameObject("FirstPersonGestureCamera (Runtime)") { hideFlags = HideFlags.DontSave };
            cameraObject.transform.SetParent(owner.transform, false);
            Camera = cameraObject.AddComponent<Camera>();
            Camera.enabled = false;
            Camera.cullingMask = 1 << layer;
            Camera.nearClipPlane = .015f;
            Camera.farClipPlane = 3f;
            Camera.allowHDR = owner.allowHDR;
            Camera.allowMSAA = owner.allowMSAA;
            var overlay = Camera.GetUniversalAdditionalCameraData();
            overlay.renderType = CameraRenderType.Overlay;
            overlay.renderPostProcessing = false;
            overlay.volumeLayerMask = 0;
        }

        public bool StableFraming { get; set; }

        public void Present(bool active, float fieldOfView, Camera owner, bool viewfinder, float poseBlend, float verticalOffset = 0)
        {
            if (!Camera) return;
            if (meleeMode) reframe = true;
            meleeMode = false;
            float presentationBlend = Mathf.SmoothStep(0f, 1f, poseBlend);
            if (!Mathf.Approximately(this.poseBlend, presentationBlend)) reframe = true;
            this.poseBlend = presentationBlend;
            if (active && (!Active || viewfinder != wasViewfinder ||
                !Mathf.Approximately(lastAspect, owner.aspect) || !Mathf.Approximately(lastFieldOfView, fieldOfView) ||
                !Mathf.Approximately(lastVerticalOffset, verticalOffset)))
            {
                reframe = true;
                Camera.ResetProjectionMatrix();
            }
            wasViewfinder = viewfinder;
            lastAspect = owner.aspect; lastFieldOfView = fieldOfView;
            lastVerticalOffset = verticalOffset;
            Active = active;
            safeFrame = Rect.MinMaxRect(.07f, viewfinder ? .08f : .04f-verticalOffset, .93f, viewfinder ? .90f : .34f-verticalOffset);
            Camera.fieldOfView = fieldOfView;
            Camera.rect = owner.rect;
            Camera.aspect = owner.aspect;
            Camera.enabled = active;
            if (active && !stack.Contains(Camera)) stack.Add(Camera);
            else if (!active) stack.Remove(Camera);
            renderer.forceRenderingOff = true;
            if (active)
                for (int i = 0; i < armMesh.blendShapeCount; i++) renderer.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
        }

        public void FrameHands(bool force = false)
        {
            if (!Active || !Camera) return;
            if (meleeMode) return;
            if (StableFraming)
            {
                // The expansion signs have hands at different depths. Re-fitting their
                // animated bounds every frame changes perspective during a shot and makes
                // fingers appear to stretch. Keep one ordinary perspective and fixed eye.
                Camera.ResetProjectionMatrix();
                Camera.transform.localPosition = new Vector3(0, -.03f, -.14f) * poseBlend;
                reframe = false;
                return;
            }
            if (wasViewfinder)
            {
                // Pose first, then zoom with the world. Do not move backwards or
                // refit the hands to cancel the owner's optical magnification.
                Camera.ResetProjectionMatrix();
                Camera.transform.localPosition = Vector3.zero;
                CenterViewfinder();
                return;
            }
            // Head pitch moves the real eye as well as rotating it. Reusing a fit
            // from another viewing angle makes the hands drift, then snap at the
            // viewport boundary. Track looking, while leaving shot recoil intact.
            if (!wasViewfinder && Quaternion.Angle(framedOwnerRotation, Camera.transform.parent.rotation) > .05f) force = true;
            // Leave room for recoil and charging. Reframe only when the final hands
            // would leave their safe area; tracking every movement would cancel the animation.
            if (!force && !reframe && InsideFrame(rightHandBones) && InsideFrame(leftHandBones))
            {
                return;
            }
            reframe = false;
            framedOwnerRotation = Camera.transform.parent.rotation;
            var root = Camera.transform.parent;
            var min = Vector3.one * float.PositiveInfinity;
            var max = Vector3.one * float.NegativeInfinity;
            IncludeBounds(root, rightHandBones, ref min, ref max);
            IncludeBounds(root, leftHandBones, ref min, ref max);
            if (!wasViewfinder)
            {
                // Keep a centred principal point: a finger parallel to the aim ray
                // must converge on the actual crosshair. Uniform scale preserves
                // angles; camera translation places the hands in the lower strip.
                Camera.transform.localPosition = new Vector3(0, 0, Mathf.Min(-.10f, min.z - .15f));
                Camera.ResetProjectionMatrix();
                var low = Vector2.one * float.PositiveInfinity;
                var high = Vector2.one * float.NegativeInfinity;
                IncludeViewport(rightHandBones, ref low, ref high);
                IncludeViewport(leftHandBones, ref low, ref high);
                var size = high - low;
                var middle = (low + high) * .5f;
                float scale = Mathf.Min(1f, Mathf.Min(safeFrame.width / (size.x + .06f),
                    safeFrame.height / (size.y + .06f))) * .88f;
                var fit = Matrix4x4.identity;
                var projection = Camera.projectionMatrix;
                fit.m00 = fit.m11 = scale;
                Camera.projectionMatrix = fit * projection;
                float handDepth = (min.z + max.z) * .5f - Camera.transform.localPosition.z;
                float tangent = Mathf.Tan(Camera.fieldOfView * .5f * Mathf.Deg2Rad);
                // Re-evaluate after translation for the two hands' different depths.
                for (int i = 0; i < 12; i++)
                {
                    low = Vector2.one * float.PositiveInfinity; high = Vector2.one * float.NegativeInfinity;
                    IncludeViewport(rightHandBones, ref low, ref high);
                    IncludeViewport(leftHandBones, ref low, ref high);
                    // Translating a perspective camera changes the apparent span
                    // of hands at different depths. Fit again after translation;
                    // a single scale computed at the real eye can crop a thumb.
                    size = high - low;
                    float shrink = Mathf.Min(1f, Mathf.Min((safeFrame.width - .02f) / Mathf.Max(.001f, size.x),
                        (safeFrame.height - .02f) / Mathf.Max(.001f, size.y)));
                    if (shrink < .99f)
                    {
                        scale *= shrink * .96f;
                        fit.m00 = fit.m11 = scale;
                        Camera.projectionMatrix = fit * projection;
                        low = Vector2.one * float.PositiveInfinity; high = Vector2.one * float.NegativeInfinity;
                        IncludeViewport(rightHandBones, ref low, ref high);
                        IncludeViewport(leftHandBones, ref low, ref high);
                    }
                    middle = (low + high) * .5f;
                    var shift = middle - safeFrame.center;
                    Camera.transform.localPosition += new Vector3(shift.x * 2f * handDepth * tangent * Camera.aspect / scale,
                        shift.y * 2f * handDepth * tangent / scale, 0);
                }
                // Let resting arms enter/leave naturally at the eye. Fitting
                // waist-level hands immediately pulls detached upper arms into
                // view, and reusing that fit makes cancellation snap between sizes.
                var fittedProjection = Camera.projectionMatrix;
                Camera.transform.localPosition *= poseBlend;
                Camera.ResetProjectionMatrix();
                var blendedProjection = Camera.projectionMatrix;
                blendedProjection.m00 = Mathf.Lerp(blendedProjection.m00, fittedProjection.m00, poseBlend);
                blendedProjection.m11 = Mathf.Lerp(blendedProjection.m11, fittedProjection.m11, poseBlend);
                Camera.projectionMatrix = blendedProjection;
                return;
            }
        }

        public void PresentMelee(bool active, Camera owner, float blend, Vector3 offset)
        {
            if (!Camera || !owner) return;
            Present(active, 75, owner, false, blend);
            meleeMode = true;
            Camera.ResetProjectionMatrix();
            Camera.transform.localRotation = Quaternion.identity;
            Camera.transform.localPosition = offset * Mathf.SmoothStep(0, 1, blend);
        }

        private void CenterViewfinder()
        {
            if (!leftIndexTip || !rightIndexTip || !leftThumbTip || !rightThumbTip) return;
            // The two pairs of touching fingertips form opposite frame corners.
            // Centre the opening, rather than the asymmetric curled fingers/palms.
            // Update even while it fits on screen: the eye moves as the head turns.
            // Keep depth so the authored recoil and opening animation retain their size change.
            Vector3 center = Camera.transform.parent.InverseTransformPoint(
                (leftIndexTip.position + rightThumbTip.position + rightIndexTip.position + leftThumbTip.position) * .25f);
            var position = Camera.transform.localPosition;
            // Resting hands are beside the hips. Tracking that centre immediately
            // teleports the lens down the body and magnifies near-plane fragments.
            Camera.transform.localPosition = new Vector3(center.x * poseBlend, center.y * poseBlend, position.z);
        }

        private void IncludeViewport(Transform[] bones, ref Vector2 min, ref Vector2 max)
        {
            foreach (var bone in bones)
            {
                Vector2 point = Camera.WorldToViewportPoint(bone.position);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
        }

        private bool InsideFrame(Transform[] bones)
        {
            foreach (var bone in bones)
            {
                var p = Camera.WorldToViewportPoint(bone.position);
                if (p.z < .04f || !safeFrame.Contains(new Vector2(p.x, p.y))) return false;
            }
            return true;
        }

        private static void IncludeBounds(Transform cameraRoot, Transform[] bones, ref Vector3 min, ref Vector3 max)
        {
            foreach (var bone in bones)
            {
                var p = cameraRoot.InverseTransformPoint(bone.position);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
        }

        public void BeforeCamera(Camera camera)
        {
            if (renderer) renderer.forceRenderingOff = !Active || camera != Camera;
        }

        public void AfterCamera(Camera camera)
        {
            if (renderer) renderer.forceRenderingOff = true;
        }

        public void Dispose()
        {
            Active = false;
            if (Camera) { stack.Remove(Camera); Camera.enabled = false; Destroy(Camera.gameObject); }
            if (renderer) { renderer.forceRenderingOff = true; Destroy(renderer.gameObject); }
            Destroy(armMesh); Destroy(BodyWithoutArms);
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
