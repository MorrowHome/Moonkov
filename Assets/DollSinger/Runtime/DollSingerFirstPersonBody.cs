using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
/// <summary>Closes the owner-only neckline; the world mesh and its shadow stay intact.</summary>
internal static class DollSingerFirstPersonBody
{
    public static void CloseNeck(Mesh owner, Mesh source, Transform[] bones, Animator animator)
    {
        if (!owner || !source || !animator || !animator.isHuman) return;
        int neck = Array.IndexOf(bones, animator.GetBoneTransform(HumanBodyBones.Neck));
        int head = Array.IndexOf(bones, animator.GetBoneTransform(HumanBodyBones.Head));
        int chest = Array.IndexOf(bones, animator.GetBoneTransform(HumanBodyBones.UpperChest)
            ?? animator.GetBoneTransform(HumanBodyBones.Chest));
        if (neck < 0 || head < 0 || chest < 0) return;
        // Bind poses make this independent of the current animation and MMD bone scales.
        var bind = source.bindposes;
        Vector3 origin = bind[neck].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 axis = bind[head].inverse.MultiplyPoint3x4(Vector3.zero) - origin;
        float length = axis.magnitude;
        if (length < .000001f) return;
        axis /= length;
        Vector3 center = origin - axis * (length * .08f);
        var vertices = owner.vertices;
        var normals = owner.normals;
        var tangents = owner.tangents;
        var weights = owner.boneWeights;
        var changed = new bool[vertices.Length];
        var used = new bool[vertices.Length];
        foreach (int index in owner.triangles) used[index] = true;
        bool any = false;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!used[i]) continue;
            var w = weights[i];
            float influence = Weight(w.boneIndex0, w.weight0) + Weight(w.boneIndex1, w.weight1)
                + Weight(w.boneIndex2, w.weight2) + Weight(w.boneIndex3, w.weight3);
            float height = Vector3.Dot(vertices[i] - origin, axis);
            if (influence < .25f || height <= -length * .08f) continue;
            // The upper boundary converges on a chest-skinned point, sealing the neck.
            float collapse = Mathf.InverseLerp(-length * .08f, length * .35f, height);
            vertices[i] = Vector3.Lerp(vertices[i] - axis * (height + length * .08f), center, collapse);
            weights[i] = new BoneWeight { boneIndex0 = chest, weight0 = 1f };
            if (normals.Length == vertices.Length) normals[i] = axis;
            if (tangents.Length == vertices.Length)
            {
                Vector3 tangent = Vector3.ProjectOnPlane((Vector3)tangents[i], axis).normalized;
                if (tangent.sqrMagnitude < .01f) tangent = Vector3.Cross(axis, Vector3.right).normalized;
                tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
            }
            changed[i] = any = true;
        }
        if (!any) return;
        owner.vertices = vertices; owner.boneWeights = weights;
        if (normals.Length == vertices.Length) owner.normals = normals;
        if (tangents.Length == vertices.Length) owner.tangents = tangents;
        // Facial morphs must not open the sealed boundary. Preserve every shape index.
        if (source.blendShapeCount > 0)
        {
            var delta = new Vector3[vertices.Length];
            var deltaNormal = new Vector3[vertices.Length];
            var deltaTangent = new Vector3[vertices.Length];
            owner.ClearBlendShapes();
            for (int s = 0; s < source.blendShapeCount; s++)
            for (int f = 0; f < source.GetBlendShapeFrameCount(s); f++)
            {
                source.GetBlendShapeFrameVertices(s, f, delta, deltaNormal, deltaTangent);
                for (int v = 0; v < changed.Length; v++)
                    if (changed[v]) delta[v] = deltaNormal[v] = deltaTangent[v] = Vector3.zero;
                owner.AddBlendShapeFrame(source.GetBlendShapeName(s), source.GetBlendShapeFrameWeight(s, f), delta, deltaNormal, deltaTangent);
            }
        }
        var kept = new List<int>();
        for (int s = 0; s < owner.subMeshCount; s++)
        {
            kept.Clear();
            var triangles = owner.GetTriangles(s);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if ((changed[a] || changed[b] || changed[c]) &&
                    Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude < length * length * length * length * 1e-12f) continue;
                kept.Add(a); kept.Add(b); kept.Add(c);
            }
            owner.SetTriangles(kept, s, false);
        }
        float Weight(int index, float weight) => index == neck || index == head ? weight : 0f;
    }
}
}
