using Unity.MP_FPS.DollSinger;
using UnityEngine;
using System.Collections.Generic;

namespace Unity.MP_FPS
{
    /// <summary>Surface calibration and complete-chain alignment. Never solves a wrist at runtime.</summary>
    public sealed class RaidActionContact
    {
        private struct Measurements { public Vector3 MouthOffset; public float Radius; }
        private static readonly Dictionary<Avatar,Measurements> MeasurementsByAvatar=new Dictionary<Avatar,Measurements>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMeasurements()=>MeasurementsByAvatar.Clear();
        private readonly Transform head, body, rightRoot, leftRoot, rightHand, leftLower, leftHand;
        private readonly Vector3 mouthOffset;
        private readonly float injectionRadius;
        public Vector3 Mouth => head.TransformPoint(mouthOffset);

        public RaidActionContact(Animator animator)
        {
            body = animator.transform; head = animator.GetBoneTransform(HumanBodyBones.Head);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm); leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            foreach (var node in animator.GetComponentsInChildren<Transform>(true))
            { if (node.name == "肩P.R") rightRoot = node; else if (node.name == "肩P.L") leftRoot = node; }
            if(animator.avatar&&MeasurementsByAvatar.TryGetValue(animator.avatar,out var cached))
            {mouthOffset=cached.MouthOffset;injectionRadius=cached.Radius;return;}
            Transform leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye), rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (!leftEye || !rightEye)
                foreach (var node in animator.GetComponentsInChildren<Transform>(true))
                { if (node.name == "目.L") leftEye = node; else if (node.name == "目.R") rightEye = node; }
            Vector3 eye = leftEye && rightEye ? (leftEye.position + rightEye.position) * .5f : head.position + body.up * .08f + body.forward * .05f;
            injectionRadius = MeasureForearm(animator, out var measuredMouth);
            Vector3 mouth = float.IsFinite(measuredMouth.x) ? measuredMouth : eye - body.up * .045f + body.forward * .012f;
            mouthOffset = head.InverseTransformPoint(mouth + body.forward * .003f);
            if(animator.avatar)MeasurementsByAvatar[animator.avatar]=new Measurements{MouthOffset=mouthOffset,Radius=injectionRadius};
        }
        public static float ContactWeight(float phase) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .36f, phase)) *
            (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.68f, .88f, phase)));
        public void ToMouth(Vector3 propContact, float weight)
        {
            if (!rightRoot || weight <= 0) return;
            var correction = Quaternion.FromToRotation(propContact-rightRoot.position, Mouth-rightRoot.position);
            correction = Quaternion.RotateTowards(Quaternion.identity, correction, 12f * weight);
            rightRoot.rotation = correction * rightRoot.rotation;
        }
        public static float InjectionWeight(float phase) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.28f, .44f, phase)) *
            (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.76f, .9f, phase)));
        public Vector3 InjectionNormal
        {
            get
            {
                Vector3 axis = (leftHand.position - leftLower.position).normalized;
                var normal = Vector3.ProjectOnPlane(body.up, axis);
                return normal.sqrMagnitude > .01f ? normal.normalized : body.forward;
            }
        }
        public Vector3 InjectionSite => Vector3.Lerp(leftLower.position, leftHand.position, .48f) + InjectionNormal * injectionRadius;
        public void Search(Vector3 target, float phase, float weight)
        {
            if (target.sqrMagnitude < .000001f || !rightRoot || !leftRoot) return;
            var pivot = (rightRoot.position + leftRoot.position) * .5f;
            if (Vector3.Distance(pivot,target) > .65f) return;
            float touch = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.08f, .28f, phase)) *
                (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.68f, .94f, phase))) * weight;
            var correction = Quaternion.FromToRotation((rightHand.position+leftHand.position)*.5f-pivot,target-pivot);
            correction = Quaternion.RotateTowards(Quaternion.identity,correction,15f*touch);
            RotateRoot(leftRoot,pivot,correction); RotateRoot(rightRoot,pivot,correction);
        }
        private static void RotateRoot(Transform root,Vector3 pivot,Quaternion correction) =>
            root.SetPositionAndRotation(pivot+correction*(root.position-pivot),correction*root.rotation);
        private float MeasureForearm(Animator animator, out Vector3 mouth)
        {
            mouth = Vector3.positiveInfinity;
            SkinnedMeshRenderer source = null;
            foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh && (!source || renderer.sharedMesh.vertexCount > source.sharedMesh.vertexCount)) source = renderer;
            if (!source) return .02f;
            var palette = source.bones; var weights = source.sharedMesh.boneWeights;
            var lower = new bool[palette.Length];
            for (int i = 0; i < palette.Length; i++) lower[i] = palette[i] && palette[i].IsChildOf(leftLower);
            var mesh = new Mesh();
            try
            {
                source.BakeMesh(mesh); var vertices = mesh.vertices;
                int shape = source.sharedMesh.GetBlendShapeIndex("あ");
                if (shape >= 0 && source.sharedMesh.GetBlendShapeFrameCount(shape) > 0)
                {
                    // The actual mouth morph identifies each model's lip region.
                    // Eye-to-mouth offsets vary substantially between these three faces.
                    var deltas = new Vector3[vertices.Length];
                    source.sharedMesh.GetBlendShapeFrameVertices(shape, 0, deltas, null, null);
                    float maximum = 0; foreach (var delta in deltas) maximum = Mathf.Max(maximum, delta.sqrMagnitude);
                    Vector3 sum = Vector3.zero; float total = 0, front = float.NegativeInfinity;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        float weight = deltas[i].sqrMagnitude;
                        if (weight < maximum * .04f || weight < .000000001f) continue;
                        Vector3 point = source.transform.position + source.transform.rotation * vertices[i];
                        weight = Mathf.Sqrt(weight); sum += point * weight; total += weight;
                        front = Mathf.Max(front, Vector3.Dot(point, body.forward));
                    }
                    if (total > 0)
                    {
                        var centre = sum / total;
                        if (Vector3.Distance(centre, head.position) < .3f)
                            mouth = centre + body.forward * (front - Vector3.Dot(centre, body.forward));
                    }
                }
                Vector3 axis = leftHand.position - leftLower.position, normal = InjectionNormal;
                float squared = axis.sqrMagnitude, radius = .008f;
                for (int i = 0; i < vertices.Length && i < weights.Length; i++)
                {
                    var w = weights[i];
                    float influence = (lower[w.boneIndex0] ? w.weight0 : 0) + (lower[w.boneIndex1] ? w.weight1 : 0) +
                        (lower[w.boneIndex2] ? w.weight2 : 0) + (lower[w.boneIndex3] ? w.weight3 : 0);
                    if (influence < .35f) continue;
                    Vector3 delta = source.transform.position + source.transform.rotation * vertices[i] - leftLower.position;
                    float along = Vector3.Dot(delta, axis) / Mathf.Max(.000001f, squared);
                    if (along < .35f || along > .62f) continue;
                    radius = Mathf.Max(radius, Vector3.Dot(delta - axis * along, normal));
                }
                return Mathf.Clamp(radius, .008f, .09f);
            }
            finally { if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
