using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    public enum HandAction { None, Search, Eat, Drink, Inject, Treat }

    /// <summary>Authored motions retargeted offline to each production skeleton.</summary>
    public sealed class DollSingerActionAnimation
    {
        [Serializable] public sealed class PropTrack
        {
            public string name, anchor;
            public float[] positions, rotations;
            public bool[] visible;
            public void Sample(float phase, int frames, out Vector3 position, out Quaternion rotation)
            {
                float sample = Mathf.Clamp01(phase) * (frames - 1);
                int a = Mathf.FloorToInt(sample), b = Mathf.Min(a + 1, frames - 1);
                Vector3 P(int i) => new Vector3(positions[i*3],positions[i*3+1],positions[i*3+2]);
                Quaternion Q(int i) => new Quaternion(rotations[i*4],rotations[i*4+1],rotations[i*4+2],rotations[i*4+3]);
                position=Vector3.Lerp(P(a),P(b),sample-a); rotation=Quaternion.Slerp(Q(a),Q(b),sample-a);
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
        private sealed class Cached { public int Hash; public Library Data; }
        private static readonly Dictionary<TextAsset, Cached> Libraries = new Dictionary<TextAsset, Cached>();
        private readonly Library library;
        private readonly Transform[] bones;
        private readonly Quaternion[] rest;
        private readonly Vector3[] positions;
        private readonly Dictionary<HandAction, Motion> motions = new Dictionary<HandAction, Motion>();
        private readonly Dictionary<string, Motion> itemMotions = new Dictionary<string, Motion>();
        public Transform RightHand { get; }
        public Transform LeftHand { get; }
        private readonly Transform leftForearm;

        public DollSingerActionAnimation(Animator animator, TextAsset asset)
        {
            if (!animator || !asset) throw new ArgumentException("Action animation requires an exact-rig library.");
            bool reload = !Libraries.TryGetValue(asset, out var cached);
#if UNITY_EDITOR
            // Edit-mode previews can reimport data without a domain reload.
            // A running build shares one parsed library between every actor.
            if (!reload && !Application.isPlaying) reload = cached.Hash != asset.text.GetHashCode();
#endif
            if (reload)
            {
                string text = asset.text;
                cached = new Cached { Hash = text.GetHashCode(), Data = JsonUtility.FromJson<Library>(text) };
                Libraries[asset] = cached;
            }
            library = cached.Data;
            if (library == null || library.version != 1 || library.anatomicalContactVersion < 1 || library.bones == null || library.motions == null)
                throw new InvalidOperationException("Invalid action animation library.");
            var hierarchy = new Dictionary<string, Transform>();
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true)) hierarchy[bone.name] = bone;
            bones = new Transform[library.bones.Length]; rest = new Quaternion[bones.Length];
            positions = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!hierarchy.TryGetValue(library.bones[i], out bones[i]))
                    throw new InvalidOperationException("Action rig bone missing: " + library.bones[i]);
                rest[i] = bones[i].localRotation;
                positions[i] = bones[i].localPosition;
            }
            foreach (var motion in library.motions)
            {
                if (!Enum.TryParse(motion.action, out HandAction action) || motion.frames < 2 ||
                    motion.rotations == null || motion.rotations.Length != motion.frames * bones.Length * 4)
                    throw new InvalidOperationException("Invalid action samples: " + motion.action);
                motion.kind = action;
                if (!motions.ContainsKey(action)) motions.Add(action, motion);
                if (motion.items != null) foreach (string item in motion.items) itemMotions.Add(item, motion);
            }
            if (!motions.ContainsKey(HandAction.Drink) && motions.TryGetValue(HandAction.Eat, out var consuming))
                motions.Add(HandAction.Drink, consuming);
            RightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            LeftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            leftForearm=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        }

        public bool Has(HandAction action) => motions.ContainsKey(action);
        public Motion GetMotion(HandAction action, string item = null) => Find(action,item);
        public float Seconds(HandAction action, string item = null) => Find(action,item)?.seconds ?? 3;
        public float ContactWeight(HandAction action, string item, float phase)
        {
            var m=Find(action,item);if(m==null)return 0;
            float enter=m.contactIn>0?m.contactIn:.36f,exit=m.contactOut>0?m.contactOut:.68f;
            return Mathf.SmoothStep(0,1,Mathf.InverseLerp(enter-.08f,enter,phase))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(exit,exit+.08f,phase)));
        }
        private Motion Find(HandAction action, string item) => item != null && itemMotions.TryGetValue(item, out var specific) && specific.kind == action
            ? specific : motions.TryGetValue(action, out var fallback) ? fallback : null;
        public float Contact(HandAction action, string item = null) => Find(action, item)?.contact ?? .5f;
        public Vector3 InjectionSite(string item,float phase)
        {
            var m=Find(HandAction.Inject,item);if(m==null||m.injectionSite==null||m.injectionSite.Length!=m.frames*3)return leftForearm.position;
            float sample=Mathf.Clamp01(phase)*(m.frames-1);int a=Mathf.FloorToInt(sample),b=Mathf.Min(a+1,m.frames-1);
            Vector3 P(int i)=>new Vector3(m.injectionSite[i*3],m.injectionSite[i*3+1],m.injectionSite[i*3+2]);
            return leftForearm.position+leftForearm.rotation*Vector3.Lerp(P(a),P(b),sample-a);
        }
        public Quaternion PropRotation(HandAction action, string item)
        {
            var values = Find(action, item)?.propRotation;
            return values?.Length == 4 ? new Quaternion(values[0],values[1],values[2],values[3]) : Quaternion.identity;
        }
        public void RestoreBeforeAnimation()
        {
            for (int i = 0; i < bones.Length; i++)
            { bones[i].localRotation = rest[i]; bones[i].localPosition = positions[i]; }
        }
        public void Sample(HandAction action, float phase, float weight, string item = null)
        {
            var motion = Find(action, item); if (motion == null) return;
            float frame = Mathf.Clamp01(phase) * (motion.frames - 1);
            int a = Mathf.FloorToInt(frame), b = Mathf.Min(a + 1, motion.frames - 1);
            for (int i = 0; i < bones.Length; i++)
            {
                Quaternion Read(int f)
                {
                    int p = (f * bones.Length + i) * 4;
                    return new Quaternion(motion.rotations[p], motion.rotations[p + 1], motion.rotations[p + 2], motion.rotations[p + 3]);
                }
                var q = Quaternion.Slerp(Read(a), Read(b), frame - a);
                bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, q, weight);
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Libraries.Clear();
    }
}
