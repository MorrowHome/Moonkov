using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>Tracks the actual upper torso skin without baking a mesh or adding self colliders.</summary>
internal sealed class DollSingerTorsoClearance
{
    private readonly Vector3[] vertices;
    private readonly BoneWeight[] weights;
    private readonly Transform[] bones;
    private readonly Matrix4x4[] bindPoses;
    private readonly Matrix4x4[] skinMatrices;
    private readonly bool[] usedBones;

    public DollSingerTorsoClearance(SkinnedMeshRenderer renderer, Animator animator)
    {
        bones = renderer.bones;
        var mesh = renderer.sharedMesh;
        bindPoses = mesh.bindposes;
        skinMatrices = new Matrix4x4[bones.Length];
        usedBones = new bool[bones.Length];
        var torso = new HashSet<Transform> {
            animator.GetBoneTransform(HumanBodyBones.Chest),
            animator.GetBoneTransform(HumanBodyBones.UpperChest),
            animator.GetBoneTransform(HumanBodyBones.Neck)
        };
        torso.Remove(null);
        var sourceVertices = mesh.vertices;
        var sourceWeights = mesh.boneWeights;
        var points = new List<Vector3>();
        var influences = new List<BoneWeight>();
        for (int i = 0; i < sourceWeights.Length; i++)
        {
            BoneWeight w = sourceWeights[i];
            float influence = TorsoWeight(w.boneIndex0, w.weight0)
                + TorsoWeight(w.boneIndex1, w.weight1)
                + TorsoWeight(w.boneIndex2, w.weight2)
                + TorsoWeight(w.boneIndex3, w.weight3);
            if (influence < 0.5f) continue;
            points.Add(sourceVertices[i]);
            influences.Add(w);
            Use(w.boneIndex0, w.weight0);
            Use(w.boneIndex1, w.weight1);
            Use(w.boneIndex2, w.weight2);
            Use(w.boneIndex3, w.weight3);
        }
        vertices = points.ToArray();
        weights = influences.ToArray();

        float TorsoWeight(int index, float weight) => weight > 0f && torso.Contains(bones[index]) ? weight : 0f;
        void Use(int index, float weight) { if (weight > 0f) usedBones[index] = true; }
    }

    public Vector3 KeepAboveTorso(Vector3 eye, Vector3 up, float nearClip, float verticalFov, float aspect)
    {
        if (vertices.Length == 0) return eye;
        for (int i = 0; i < bones.Length; i++)
            if (usedBones[i]) skinMatrices[i] = bones[i].localToWorldMatrix * bindPoses[i];
        float top = float.NegativeInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            BoneWeight w = weights[i];
            Vector3 p = Skin(w.boneIndex0, w.weight0, vertices[i])
                + Skin(w.boneIndex1, w.weight1, vertices[i])
                + Skin(w.boneIndex2, w.weight2, vertices[i])
                + Skin(w.boneIndex3, w.weight3, vertices[i]);
            top = Mathf.Max(top, Vector3.Dot(p, up));
        }
        // A sphere enclosing all four near-plane corners also covers pitch and lean roll.
        float halfHeight = nearClip * Mathf.Tan(verticalFov * .5f * Mathf.Deg2Rad);
        float radius = Mathf.Sqrt(nearClip * nearClip + halfHeight * halfHeight * (1f + aspect * aspect));
        float lift = top + radius + .015f - Vector3.Dot(eye, up);
        return eye + up * Mathf.Max(0f, lift);
    }

    private Vector3 Skin(int index, float weight, Vector3 point) => weight > 0f
        ? skinMatrices[index].MultiplyPoint3x4(point) * weight : Vector3.zero;
}
}
