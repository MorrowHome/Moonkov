// 让使用道具的手势真的对上角色身体。算嘴、前臂等接触点，再调整进食和针剂动作中的道具接触姿势。
// 不同角色的脸和手臂比例不同，位置从实际模型测量。
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using System.Collections.Generic;

namespace Unity.MP_FPS {
    /// <summary>Surface calibration and complete-chain alignment. Never solves a wrist at runtime.</summary>
    public sealed class RaidActionContact {
        private struct Measurements {
            public Vector3 MouthOffset;
            public float Radius;
        }

        // 同一个角色骨架的脸部和前臂尺寸可共用。测一次后缓存，局内不每次拿道具都重新求模型位置。
        private static readonly Dictionary<Avatar, Measurements> MeasurementsByAvatar = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMeasurements() { MeasurementsByAvatar.Clear(); }

        private readonly Transform head, body, rightRoot, leftRoot, rightHand, leftLower, leftHand;
        private readonly Vector3 mouthOffset;
        private readonly float injectionRadius;
        public Vector3 Mouth => head.TransformPoint(mouthOffset);

        // 从动作角色上找到接触计算需要的骨骼和测量值。模型不同也能用同一套修正方法。
        public RaidActionContact(Animator animator) {
            body = animator.transform;
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            foreach (var node in animator.GetComponentsInChildren<Transform>(true))
                if (node.name == "肩P.R")
                    rightRoot = node;
                else if (node.name == "肩P.L") leftRoot = node;
            if (animator.avatar && MeasurementsByAvatar.TryGetValue(animator.avatar, out var cached)) {
                mouthOffset = cached.MouthOffset;
                injectionRadius = cached.Radius;
                return;
            }
            Transform leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye),
                rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (!leftEye || !rightEye)
                foreach (var node in animator.GetComponentsInChildren<Transform>(true))
                    if (node.name == "目.L")
                        leftEye = node;
                    else if (node.name == "目.R") rightEye = node;
            var eye = leftEye && rightEye
                ? (leftEye.position + rightEye.position) * .5f
                : head.position + body.up * .08f + body.forward * .05f;
            injectionRadius = MeasureForearm(animator, out var measuredMouth);
            var mouth = float.IsFinite(measuredMouth.x) ? measuredMouth : eye - body.up * .045f + body.forward * .012f;
            mouthOffset = head.InverseTransformPoint(mouth + body.forward * .003f);
            if (animator.avatar)
                MeasurementsByAvatar[animator.avatar] = new Measurements
                    { MouthOffset = mouthOffset, Radius = injectionRadius };
        }

        // 只有道具真正接近接触动作时才逐渐增加修正权重，动作开始和结束保留原来的手势。
        public static float ContactWeight(float phase) {
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .36f, phase)) *
                   (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.68f, .88f, phase)));
        }

        // 按嘴和道具接触点之间的差旋转骨架局部。食品到了脸旁时才让它贴在唇边。
        public void ToMouth(Vector3 propContact, float weight) {
            if (!rightRoot || weight <= 0) return;
            var correction = Quaternion.FromToRotation(propContact - rightRoot.position, Mouth - rightRoot.position);
            correction = Quaternion.RotateTowards(Quaternion.identity, correction, 12f * weight);
            rightRoot.rotation = correction * rightRoot.rotation;
        }

        public static float InjectionWeight(float phase) {
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.28f, .44f, phase)) *
                   (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.76f, .9f, phase)));
        }

        public Vector3 InjectionNormal {
            get {
                var axis = (leftHand.position - leftLower.position).normalized;
                var normal = Vector3.ProjectOnPlane(body.up, axis);
                return normal.sqrMagnitude > .01f ? normal.normalized : body.forward;
            }
        }

        // 针剂沿前臂段选一个位置，再偏移到皮肤表面，避免针尖对齐到骨骼中心。
        public Vector3 InjectionSite =>
            Vector3.Lerp(leftLower.position, leftHand.position, .48f) + InjectionNormal * injectionRadius;

        // 搜索姿势跟着真实搜刮点改变观察方向，让身体弯向面前的物资。
        public void Search(Vector3 target, float phase, float weight) {
            if (target.sqrMagnitude < .000001f || !rightRoot || !leftRoot) return;
            var pivot = (rightRoot.position + leftRoot.position) * .5f;
            if (Vector3.Distance(pivot, target) > .65f) return;
            float touch = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.08f, .28f, phase)) *
                          (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.68f, .94f, phase))) * weight;
            var correction =
                Quaternion.FromToRotation((rightHand.position + leftHand.position) * .5f - pivot, target - pivot);
            correction = Quaternion.RotateTowards(Quaternion.identity, correction, 15f * touch);
            RotateRoot(leftRoot, pivot, correction);
            RotateRoot(rightRoot, pivot, correction);
        }

        private static void RotateRoot(Transform root, Vector3 pivot, Quaternion correction) {
            root.SetPositionAndRotation(pivot + correction * (root.position - pivot), correction * root.rotation);
        }

        // 用绑定的模型估计唇的位置与手臂表面距离。不同外观的尺寸差不必靠填固定值硬调。
        private float MeasureForearm(Animator animator, out Vector3 mouth) {
            mouth = Vector3.positiveInfinity;
            SkinnedMeshRenderer source = null;
            foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh && (!source || renderer.sharedMesh.vertexCount > source.sharedMesh.vertexCount))
                    source = renderer;
            if (!source) return .02f;
            var palette = source.bones;
            var weights = source.sharedMesh.boneWeights;
            bool[] lower = new bool[palette.Length];
            for (int i = 0; i < palette.Length; i++) lower[i] = palette[i] && palette[i].IsChildOf(leftLower);
            var mesh = new Mesh();
            try {
                source.BakeMesh(mesh);
                var vertices = mesh.vertices;
                int shape = source.sharedMesh.GetBlendShapeIndex("あ");
                if (shape >= 0 && source.sharedMesh.GetBlendShapeFrameCount(shape) > 0) {
                    // The actual mouth morph identifies each model's lip region.
                    // Eye-to-mouth offsets vary substantially between these three faces.
                    var deltas = new Vector3[vertices.Length];
                    source.sharedMesh.GetBlendShapeFrameVertices(shape, 0, deltas, null, null);
                    float maximum = 0;
                    foreach (var delta in deltas) maximum = Mathf.Max(maximum, delta.sqrMagnitude);
                    var sum = Vector3.zero;
                    float total = 0, front = float.NegativeInfinity;
                    for (int i = 0; i < vertices.Length; i++) {
                        float weight = deltas[i].sqrMagnitude;
                        if (weight < maximum * .04f || weight < .000000001f) continue;
                        var point = source.transform.position + source.transform.rotation * vertices[i];
                        weight = Mathf.Sqrt(weight);
                        sum += point * weight;
                        total += weight;
                        front = Mathf.Max(front, Vector3.Dot(point, body.forward));
                    }
                    if (total > 0) {
                        var centre = sum / total;
                        if (Vector3.Distance(centre, head.position) < .3f)
                            mouth = centre + body.forward * (front - Vector3.Dot(centre, body.forward));
                    }
                }
                Vector3 axis = leftHand.position - leftLower.position, normal = InjectionNormal;
                float squared = axis.sqrMagnitude, radius = .008f;
                for (int i = 0; i < vertices.Length && i < weights.Length; i++) {
                    var w = weights[i];
                    float influence = (lower[w.boneIndex0] ? w.weight0 : 0) + (lower[w.boneIndex1] ? w.weight1 : 0) +
                                      (lower[w.boneIndex2] ? w.weight2 : 0) + (lower[w.boneIndex3] ? w.weight3 : 0);
                    if (influence < .35f) continue;
                    var delta = source.transform.position + source.transform.rotation * vertices[i] -
                                leftLower.position;
                    float along = Vector3.Dot(delta, axis) / Mathf.Max(.000001f, squared);
                    if (along < .35f || along > .62f) continue;
                    radius = Mathf.Max(radius, Vector3.Dot(delta - axis * along, normal));
                }
                return Mathf.Clamp(radius, .008f, .09f);
            } finally {
                if (Application.isPlaying)
                    Object.Destroy(mesh);
                else
                    Object.DestroyImmediate(mesh);
            }
        }
    }
}