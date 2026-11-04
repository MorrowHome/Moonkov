using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>Constrains the near plane against the visible upper body, without baking or self colliders.</summary>
internal sealed class DollSingerTorsoClearance
{
    private readonly Vector3[] vertices, projected;
    private readonly BoneWeight[] weights;
    private readonly int[] triangles;
    private readonly bool[] armFaces;
    private readonly Transform[] bones;
    private readonly Matrix4x4[] bindPoses, skinMatrices;
    private readonly bool[] usedBones;
    private readonly Bounds[] boneBounds;
    private readonly float minWeightSum, maxWeightSum;

    public DollSingerTorsoClearance(SkinnedMeshRenderer renderer, Animator animator, Mesh visibleMesh = null)
    {
        bones = renderer.bones;
        var mesh = visibleMesh ? visibleMesh : renderer.sharedMesh;
        bindPoses = mesh.bindposes;
        skinMatrices = new Matrix4x4[bones.Length];
        usedBones = new bool[bones.Length];
        boneBounds = new Bounds[bones.Length];
        float minimumSum = float.PositiveInfinity, maximumSum = 0f;
        var torso = new HashSet<Transform> {
            animator.GetBoneTransform(HumanBodyBones.Chest),
            animator.GetBoneTransform(HumanBodyBones.UpperChest),
            animator.GetBoneTransform(HumanBodyBones.Neck),
            animator.GetBoneTransform(HumanBodyBones.LeftShoulder),
            animator.GetBoneTransform(HumanBodyBones.RightShoulder)
        };
        torso.Remove(null);
        var sourceVertices = mesh.vertices;
        var sourceWeights = mesh.boneWeights;
        var leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var armBones = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++) armBones[i] = bones[i] &&
            ((leftArm && bones[i].IsChildOf(leftArm)) || (rightArm && bones[i].IsChildOf(rightArm)));
        var armVertices = new bool[sourceVertices.Length];
        var relevant = new bool[sourceVertices.Length];
        var remap = new int[sourceVertices.Length];
        for (int i = 0; i < relevant.Length; i++)
        {
            var w = sourceWeights[i];
            armVertices[i] = ArmWeight(w.boneIndex0, w.weight0) + ArmWeight(w.boneIndex1, w.weight1)
                + ArmWeight(w.boneIndex2, w.weight2) + ArmWeight(w.boneIndex3, w.weight3) >= .5f;
            relevant[i] = TorsoWeight(w.boneIndex0, w.weight0) + TorsoWeight(w.boneIndex1, w.weight1)
                + TorsoWeight(w.boneIndex2, w.weight2) + TorsoWeight(w.boneIndex3, w.weight3) >= .5f || armVertices[i];
            remap[i] = -1;
        }
        var points = new List<Vector3>();
        var influences = new List<BoneWeight>();
        var faces = new List<int>();
        var arms = new List<bool>();
        var indices = mesh.triangles;
        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            if (!relevant[a] && !relevant[b] && !relevant[c]) continue;
            Add(a); Add(b); Add(c);
            arms.Add(armVertices[a] || armVertices[b] || armVertices[c]);
        }
        vertices = points.ToArray();
        projected = new Vector3[vertices.Length];
        weights = influences.ToArray();
        triangles = faces.ToArray();
        armFaces = arms.ToArray();
        minWeightSum = minimumSum;
        maxWeightSum = maximumSum;

        float TorsoWeight(int index, float weight) => weight > 0f && torso.Contains(bones[index]) ? weight : 0f;
        float ArmWeight(int index, float weight) => armBones[index] ? weight : 0f;
        void Add(int index)
        {
            if (remap[index] < 0)
            {
                remap[index] = points.Count;
                points.Add(sourceVertices[index]); influences.Add(sourceWeights[index]);
                var w = sourceWeights[index];
                float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                minimumSum = Mathf.Min(minimumSum, sum); maximumSum = Mathf.Max(maximumSum, sum);
                Use(w.boneIndex0, w.weight0, sourceVertices[index]); Use(w.boneIndex1, w.weight1, sourceVertices[index]);
                Use(w.boneIndex2, w.weight2, sourceVertices[index]); Use(w.boneIndex3, w.weight3, sourceVertices[index]);
            }
            faces.Add(remap[index]);
        }
        void Use(int index, float weight, Vector3 point)
        {
            if (weight <= 0f) return;
            if (usedBones[index]) boneBounds[index].Encapsulate(point);
            else { boneBounds[index] = new Bounds(point, Vector3.zero); usedBones[index] = true; }
        }
    }

    public Vector3 KeepAboveTorso(Vector3 eye, Vector3 up, float nearClip, float verticalFov, float aspect, Quaternion look, bool includeArms = true)
    {
        if (vertices.Length == 0) return eye;
        Vector3 forward = Vector3.ProjectOnPlane(look * Vector3.forward, up).normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(look * Vector3.up, up).normalized;
        Vector3 right = Vector3.Cross(up, forward);
        float halfHeight = nearClip * Mathf.Tan(verticalFov * .5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * aspect;
        float minX = 0, maxX = 0, minZ = 0, maxZ = 0, nearBottom = 0;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        {
            Vector3 p = Project(look * new Vector3(x * halfWidth, y * halfHeight, nearClip));
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            nearBottom = Mathf.Min(nearBottom, p.y);
        }
        const float padding = .008f;
        minX -= padding; maxX += padding; minZ -= padding; maxZ += padding;
        for (int i = 0; i < bones.Length; i++)
            if (usedBones[i]) skinMatrices[i] = bones[i].localToWorldMatrix * bindPoses[i];
        // A skinned point is a positive weighted sum of bone-transformed points.
        // Their bounds enclose it, including small deviations from a unit weight sum.
        // Usually the whole upper body is below the near plane: avoid skinning every
        // vertex in that case. Ambiguous poses still use the original triangle test.
        if (UpperBodyTop(up) + padding + .002f <= Vector3.Dot(eye, up) + nearBottom) return eye;
        for (int i = 0; i < vertices.Length; i++)
        {
            var w = weights[i];
            Vector3 p = Skin(w.boneIndex0, w.weight0, vertices[i]) + Skin(w.boneIndex1, w.weight1, vertices[i])
                + Skin(w.boneIndex2, w.weight2, vertices[i]) + Skin(w.boneIndex3, w.weight3, vertices[i]);
            projected[i] = Project(p - eye);
        }
        float top = float.NegativeInfinity;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            // Hands may enter the view, but must not lift the eye as they move.
            if (!includeArms && armFaces[i / 3]) continue;
            var a = projected[triangles[i]]; var b = projected[triangles[i + 1]]; var c = projected[triangles[i + 2]];
            if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < minX || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > maxX ||
                Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < minZ || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > maxZ) continue;
            top = Mathf.Max(top, Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
        }
        return eye + up * Mathf.Max(0f, top + padding - nearBottom);

        Vector3 Project(Vector3 p) => new Vector3(Vector3.Dot(p, right), Vector3.Dot(p, up), Vector3.Dot(p, forward));
    }

    private Vector3 Skin(int index, float weight, Vector3 point) => weight > 0f
        ? skinMatrices[index].MultiplyPoint3x4(point) * weight : Vector3.zero;

    private float UpperBodyTop(Vector3 up)
    {
        float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
        for (int i = 0; i < bones.Length; i++)
        {
            if (!usedBones[i]) continue;
            var matrix = skinMatrices[i];
            var bounds = boneBounds[i];
            float centre = Vector3.Dot(matrix.MultiplyPoint3x4(bounds.center), up);
            var extents = bounds.extents;
            float radius = Mathf.Abs(up.x * matrix.m00 + up.y * matrix.m10 + up.z * matrix.m20) * extents.x
                + Mathf.Abs(up.x * matrix.m01 + up.y * matrix.m11 + up.z * matrix.m21) * extents.y
                + Mathf.Abs(up.x * matrix.m02 + up.y * matrix.m12 + up.z * matrix.m22) * extents.z;
            minimum = Mathf.Min(minimum, centre - radius); maximum = Mathf.Max(maximum, centre + radius);
        }
        return Mathf.Max(Mathf.Max(minimum * minWeightSum, minimum * maxWeightSum),
            Mathf.Max(maximum * minWeightSum, maximum * maxWeightSum));
    }
}
}
