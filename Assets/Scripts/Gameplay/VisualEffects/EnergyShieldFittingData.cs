using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS
{
    // Immutable source geometry shared by actors using the same skin. Bone matrices
    // and the final fit remain per actor, so animation and imported scale still apply.
    internal sealed class EnergyShieldFittingData
    {
        private static readonly Dictionary<(Mesh, string), EnergyShieldFittingData> Cache = new();
        private readonly Dictionary<(int, int), int[]> neckSamples = new();
        public readonly Vector3[] Vertices;
        public readonly BoneWeight[] Weights;
        public readonly Matrix4x4[] Bindposes;
        public readonly int[] Skin, Torso;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear() => Cache.Clear();

        public static EnergyShieldFittingData For(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            var materials = renderer.sharedMaterials;
            var flags = new char[Mathf.Min(mesh.subMeshCount, materials.Length)];
            for (int i = 0; i < flags.Length; i++)
            {
                string name = materials[i] ? materials[i].name : "";
                bool body = name.Equals("Body", StringComparison.OrdinalIgnoreCase);
                flags[i] = (char)((body || name.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0) |
                    (body || name.IndexOf("Dress", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 0));
            }
            var key = (mesh, new string(flags));
#if UNITY_EDITOR
            // Meshes and material assignments can change in live authoring previews.
            if (!Application.isPlaying) return new EnergyShieldFittingData(mesh, flags);
#endif
            if (!Cache.TryGetValue(key, out var data)) Cache.Add(key, data = new EnergyShieldFittingData(mesh, flags));
            return data;
        }

        private EnergyShieldFittingData(Mesh mesh, char[] flags)
        {
            Vertices = mesh.vertices;
            Weights = mesh.boneWeights;
            Bindposes = mesh.bindposes;
            var skin = new bool[Vertices.Length];
            var torso = new bool[Vertices.Length];
            bool hasSkin = false;
            for (int sub = 0; sub < flags.Length; sub++)
            {
                int flag = flags[sub];
                hasSkin |= (flag & 1) != 0;
                if (flag == 0) continue;
                foreach (int index in mesh.GetTriangles(sub))
                {
                    skin[index] |= (flag & 1) != 0;
                    torso[index] |= (flag & 2) != 0;
                }
            }
            Skin = Indices(skin, !hasSkin);
            Torso = Indices(torso, false);
        }

        private static int[] Indices(bool[] selected, bool all)
        {
            var result = new List<int>();
            for (int i = 0; i < selected.Length; i++) if (all || selected[i]) result.Add(i);
            return result.ToArray();
        }

        public int[] Neck(int neck, int head)
        {
            if (neckSamples.TryGetValue((neck, head), out var samples)) return samples;
            var selected = new List<int>();
            if (Weights.Length == Vertices.Length)
                foreach (int i in Skin)
                {
                    var w = Weights[i];
                    float influence = Weight(w.boneIndex0, w.weight0, neck, head) + Weight(w.boneIndex1, w.weight1, neck, head) +
                        Weight(w.boneIndex2, w.weight2, neck, head) + Weight(w.boneIndex3, w.weight3, neck, head);
                    if (influence < .7f) continue;
                    selected.Add(i);
                }
            samples = selected.ToArray();
            neckSamples.Add((neck, head), samples);
            return samples;
        }

        private static float Weight(int index, float weight, int neck, int head) => index == neck || index == head ? weight : 0;
    }
}
