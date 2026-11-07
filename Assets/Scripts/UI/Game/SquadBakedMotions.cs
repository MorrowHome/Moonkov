using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.Client
{
    // The squad and home reactions sample the same authored upper-body poses.
    internal sealed class SquadBakedMotions
    {
        [Serializable] private sealed class Motion
        {
            public string name;
            public int frames;
            public float seconds;
            public float[] rotations;
        }
        [Serializable] private sealed class Library
        {
            public int version;
            public string rig;
            public string[] bones;
            public Motion[] motions;
        }
        private sealed class Cached { public int Hash; public Library Data; }
        private static readonly Dictionary<int, Cached> Libraries = new Dictionary<int, Cached>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Libraries.Clear();

        private readonly Library m_Library;
        private readonly Transform[] m_Bones;
        private readonly Quaternion[] m_Bind, m_From, m_Current;
        private readonly List<Expression> m_Expressions = new List<Expression>();
        private int m_Pose = -1;
        private float m_Blend;
        private struct Expression
        {
            public SkinnedMeshRenderer Renderer;
            public int Index, Kind;
            public float Weight, Baseline;
        }

        public SquadBakedMotions(Animator animator, int character)
        {
            string rig = character == 3 ? "HatsuneMiku" : character == 4 ? "Shian" : "DollSinger";
            var asset = Resources.Load<TextAsset>("Moonkov/SquadMotions/" + rig);
            if (asset == null) throw new InvalidOperationException("Missing squad motions: " + rig);
            string text = asset.text;
            int hash = text.GetHashCode();
            if (!Libraries.TryGetValue(character, out var cached) || cached.Hash != hash)
            {
                var data = JsonUtility.FromJson<Library>(text);
                if (data.version != 1 || data.rig != rig || data.motions?.Length != 4 || data.bones == null)
                    throw new InvalidOperationException("Invalid squad motion library: " + rig);
                foreach (var motion in data.motions)
                    if (motion.frames < 2 || motion.seconds <= 0 || motion.rotations?.Length != motion.frames * data.bones.Length * 4)
                        throw new InvalidOperationException("Incomplete squad motion: " + motion.name);
                cached = new Cached { Hash = hash, Data = data };
                Libraries[character] = cached;
            }
            m_Library = cached.Data;
            var hierarchy = new Dictionary<string, Transform>();
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true)) hierarchy[bone.name] = bone;
            m_Bones = new Transform[m_Library.bones.Length];
            m_Bind = new Quaternion[m_Bones.Length];
            m_From = new Quaternion[m_Bones.Length];
            m_Current = new Quaternion[m_Bones.Length];
            for (int i = 0; i < m_Bones.Length; i++)
            {
                if (!hierarchy.TryGetValue(m_Library.bones[i], out m_Bones[i]))
                    throw new InvalidOperationException("Missing squad bone: " + m_Library.bones[i]);
                m_Current[i] = m_Bind[i] = m_Bones[i].localRotation;
            }
            foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (!mesh) continue;
                foreach (var entry in new[] { ("まばたき", 0), ("口角上げ", 1), ("笑い", 2), ("困る", 3), ("真面目", 4), ("ウィンク", 5), ("ω", 6) })
                {
                    int index = mesh.GetBlendShapeIndex(entry.Item1);
                    if (index >= 0) m_Expressions.Add(new Expression { Renderer = renderer, Index = index, Kind = entry.Item2,
                        Baseline = renderer.GetBlendShapeWeight(index) });
                }
            }
        }

        public void RestoreBeforeAnimation()
        {
            for (int i = 0; i < m_Bones.Length; i++) m_Bones[i].localRotation = m_Bind[i];
        }

        public void RestoreExpressions()
        {
            foreach (var expression in m_Expressions)
                expression.Renderer.SetBlendShapeWeight(expression.Index, expression.Baseline);
        }

        public void Apply(int pose, double time, float dt, bool settle, float overlay = 1)
        {
            pose = Mathf.Clamp(pose, 0, 3);
            if (pose != m_Pose)
            {
                Array.Copy(m_Current, m_From, m_Bones.Length);
                m_Pose = pose; m_Blend = 0;
            }
            m_Blend = settle ? 1 : Mathf.Min(1, m_Blend + dt / .25f);
            float blend = Mathf.SmoothStep(0, 1, m_Blend);
            var motion = m_Library.motions[pose];
            float phase = (float)(time % motion.seconds / motion.seconds);
            float frame = phase * (motion.frames - 1);
            int a = (int)frame, b = Mathf.Min(a + 1, motion.frames - 1);
            int stride = m_Bones.Length * 4;
            for (int i = 0; i < m_Bones.Length; i++)
            {
                var q = Quaternion.Slerp(Read(motion.rotations, a * stride + i * 4), Read(motion.rotations, b * stride + i * 4), frame - a);
                m_Current[i] = Quaternion.Slerp(m_From[i], q, blend);
                m_Bones[i].localRotation = overlay >= 1
                    ? m_Current[i] : Quaternion.Slerp(m_Bones[i].localRotation, m_Current[i], overlay);
            }
            float blink = settle ? 0 : Mathf.Clamp01(1 - Mathf.Abs(phase - .82f) / .035f) * 90;
            float expressionBlend = settle ? 1 : 1 - Mathf.Exp(-dt * 14);
            for (int i = 0; i < m_Expressions.Count; i++)
            {
                var e = m_Expressions[i];
                float target = e.Kind switch
                {
                    0 => blink,
                    1 => pose == 2 ? 50 : pose == 3 ? 28 : 0,
                    2 => pose == 2 ? 22 : pose == 3 ? 12 : 0,
                    3 => pose == 3 ? 38 : 0,
                    4 => pose < 2 ? 18 : 0,
                    5 => pose == 2 && !settle ? Mathf.Clamp01(1 - Mathf.Abs(phase - .43f) / .08f) * 70 : 0,
                    6 => pose == 3 ? 28 : 0,
                    _ => 0
                };
                e.Weight = Mathf.Lerp(e.Weight, target, expressionBlend);
                e.Renderer.SetBlendShapeWeight(e.Index, Mathf.Lerp(e.Baseline, e.Weight, overlay));
                m_Expressions[i] = e;
            }
        }

        private static Quaternion Read(float[] values, int index) =>
            new Quaternion(values[index], values[index + 1], values[index + 2], values[index + 3]);
    }
}
