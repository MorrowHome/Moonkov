using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    public enum HandAction { None, Search, Eat, Drink, Inject, Treat, RefillOxygen }

    /// <summary>Authored motions retargeted offline to each production skeleton.</summary>
    public sealed class DollSingerActionAnimation
    {
        // 道具、手臂和骨骼使用相同的帧边界与插值权重。
        private readonly struct FrameSample
        {
            public readonly int First, Next;
            public readonly float Blend;

            public FrameSample(float phase, int frames)
            {
                float time = Mathf.Clamp01(phase) * (frames - 1);
                First = Mathf.FloorToInt(time);
                Next = Mathf.Min(First + 1, frames - 1);
                Blend = time - First;
            }

            public Vector3 Position(float[] values) =>
                Vector3.Lerp(ReadPosition(values, First * 3), ReadPosition(values, Next * 3), Blend);

            public Quaternion Rotation(float[] values, int stride = 1, int bone = 0) =>
                Quaternion.Slerp(ReadRotation(values, (First * stride + bone) * 4),
                    ReadRotation(values, (Next * stride + bone) * 4), Blend);

            private static Vector3 ReadPosition(float[] values, int offset) =>
                new Vector3(values[offset], values[offset + 1], values[offset + 2]);

            private static Quaternion ReadRotation(float[] values, int offset) =>
                new Quaternion(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]);
        }

        [Serializable] public sealed class PropTrack
        {
            public string name, anchor;
            public float[] positions, rotations;
            public bool[] visible;
            public void Sample(float phase, int frames, out Vector3 position, out Quaternion rotation)
            {
                var sample = new FrameSample(phase, frames);
                position = sample.Position(positions);
                rotation = sample.Rotation(rotations);
            }
        }
        [Serializable] public sealed class Motion
        {
            public string action, source;
            public float seconds, contact;
            public int frames;
            public float[] rotations;
            public string[] items;
            public float[] propRotation;
            public PropTrack root;
            public PropTrack[] parts;
            public float contactIn, contactOut, openMarker;
            public float[] injectionSite;
            [NonSerialized] public HandAction kind;
        }
        [Serializable] public sealed class Library
        {
            public int version = 1, frameRate = 60, anatomicalContactVersion;
            public string rig;
            public string[] bones;
            public Motion[] motions;
        }
        // 同一份动作资源共用索引，每个角色只保存自己绑定的骨骼。
        private sealed class MotionCatalog
        {
            public readonly int ContentHash;
            public readonly string[] BoneNames;
            private readonly Dictionary<HandAction, Motion> defaults = new Dictionary<HandAction, Motion>();
            private readonly Dictionary<string, Motion> variants = new Dictionary<string, Motion>();

            public MotionCatalog(string text)
            {
                var data = JsonUtility.FromJson<Library>(text);
                if (data == null || data.version != 1 || data.anatomicalContactVersion < 1 ||
                    data.bones == null || data.motions == null)
                    throw new InvalidOperationException("Invalid action animation library.");

                ContentHash = text.GetHashCode();
                BoneNames = data.bones;
                foreach (var motion in data.motions)
                {
                    if (!Enum.TryParse(motion.action, out HandAction action) || motion.frames < 2 ||
                        motion.rotations?.Length != motion.frames * BoneNames.Length * 4)
                        throw new InvalidOperationException("Invalid action samples: " + motion.action);
                    motion.kind = action;
                    defaults.TryAdd(action, motion);
                    if (motion.items == null) continue;
                    foreach (var item in motion.items) variants.Add(item, motion);
                }
                if (defaults.TryGetValue(HandAction.Eat, out var consuming))
                    defaults.TryAdd(HandAction.Drink, consuming);
            }

            public Motion Find(HandAction action, string item)
            {
                if (item != null && variants.TryGetValue(item, out var variant) && variant.kind == action)
                    return variant;
                defaults.TryGetValue(action, out var motion);
                return motion;
            }
        }

        private static readonly Dictionary<TextAsset, MotionCatalog> Libraries = new Dictionary<TextAsset, MotionCatalog>();
        private readonly MotionCatalog catalog;
        private readonly Transform[] bones;
        private readonly Quaternion[] rest;
        private readonly Vector3[] positions;
        public Transform RightHand { get; }
        public Transform LeftHand { get; }
        private readonly Transform leftForearm;

        public DollSingerActionAnimation(Animator animator, TextAsset asset)
        {
            if (!animator || !asset) throw new ArgumentException("Action animation requires an exact-rig library.");
            catalog = LoadCatalog(asset);
            var hierarchy = new Dictionary<string, Transform>();
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true)) hierarchy[bone.name] = bone;
            bones = new Transform[catalog.BoneNames.Length]; rest = new Quaternion[bones.Length];
            positions = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!hierarchy.TryGetValue(catalog.BoneNames[i], out bones[i]))
                    throw new InvalidOperationException("Action rig bone missing: " + catalog.BoneNames[i]);
                rest[i] = bones[i].localRotation;
                positions[i] = bones[i].localPosition;
            }
            RightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            LeftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            leftForearm=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        }

        private static MotionCatalog LoadCatalog(TextAsset asset)
        {
            if (Libraries.TryGetValue(asset, out var cached))
            {
#if UNITY_EDITOR
                if (Application.isPlaying || cached.ContentHash == asset.text.GetHashCode()) return cached;
#else
                return cached;
#endif
            }
            var catalog = new MotionCatalog(asset.text);
            Libraries[asset] = catalog;
            return catalog;
        }

        public bool Has(HandAction action) => catalog.Find(action, null) != null;
        public Motion GetMotion(HandAction action, string item = null) => catalog.Find(action,item);
        public float Seconds(HandAction action, string item = null) => catalog.Find(action,item)?.seconds ?? 3;
        public float ContactWeight(HandAction action, string item, float phase)
        {
            var m=catalog.Find(action,item);if(m==null)return 0;
            float enter=m.contactIn>0?m.contactIn:.36f,exit=m.contactOut>0?m.contactOut:.68f;
            return Mathf.SmoothStep(0,1,Mathf.InverseLerp(enter-.08f,enter,phase))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(exit,exit+.08f,phase)));
        }
        public float Contact(HandAction action, string item = null) => catalog.Find(action, item)?.contact ?? .5f;
        public Vector3 InjectionSite(string item,float phase)
        {
            var motion = catalog.Find(HandAction.Inject, item);
            if (motion == null || motion.injectionSite?.Length != motion.frames * 3) return leftForearm.position;
            var sample = new FrameSample(phase, motion.frames);
            return leftForearm.position + leftForearm.rotation * sample.Position(motion.injectionSite);
        }
        public Quaternion PropRotation(HandAction action, string item)
        {
            var values = catalog.Find(action, item)?.propRotation;
            return values?.Length == 4 ? new Quaternion(values[0],values[1],values[2],values[3]) : Quaternion.identity;
        }
        public void RestoreBeforeAnimation()
        {
            for (int i = 0; i < bones.Length; i++)
            { bones[i].localRotation = rest[i]; bones[i].localPosition = positions[i]; }
        }
        public void Sample(HandAction action, float phase, float weight, string item = null)
        {
            var motion = catalog.Find(action, item);
            if (motion == null) return;
            var sample = new FrameSample(phase, motion.frames);
            for (int i = 0; i < bones.Length; i++)
            {
                var target = sample.Rotation(motion.rotations, bones.Length, i);
                bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, target, weight);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Libraries.Clear();
    }
}
