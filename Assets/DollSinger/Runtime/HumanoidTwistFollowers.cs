using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Drives MMD sibling twist bones from the evaluated Humanoid pose.</summary>
    // Humanoid writes the main joints, but does not evaluate the PMX grant graph.
    // Authored weapon/melee bakes run at 100/110 and may replace these rotations.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]
    public sealed class HumanoidTwistFollowers : MonoBehaviour
    {
        private sealed class Segment
        {
            public Transform parent, distal;
            public Quaternion distalBindRelative;
            public readonly Transform[] followers = new Transform[3];
            public readonly Quaternion[] followerBindRelative = new Quaternion[3];
            public readonly Quaternion[] followerLocalBind = new Quaternion[3];
        }

        private Segment[] segments;
        private bool presentationDriven;
        public int FollowerCount => segments == null ? 0 : segments.Length * 3;

        private void Awake() => Initialize();
        private void OnEnable() => Initialize();

        private void Initialize()
        {
            if (segments != null) return;
            var hierarchy = new Dictionary<string, Transform>();
            foreach (var bone in GetComponentsInChildren<Transform>(true)) hierarchy[bone.name] = bone;
            Transform Bone(string name) => hierarchy.TryGetValue(name, out var bone) ? bone :
                throw new InvalidOperationException("Humanoid twist follower bone missing: " + name);
            var result = new Segment[4];
            int index = 0;
            foreach (var side in new[] { ".L", ".R" })
            {
                result[index++] = Bind("腕", "ひじ", "腕捩", side);
                result[index++] = Bind("ひじ", "手首", "手捩", side);
            }
            segments = result;

            Segment Bind(string parentName, string distalName, string prefix, string side)
            {
                var segment = new Segment { parent = Bone(parentName + side), distal = Bone(distalName + side) };
                var inverseParent = Quaternion.Inverse(segment.parent.rotation);
                segment.distalBindRelative = inverseParent * segment.distal.rotation;
                for (int i = 0; i < segment.followers.Length; i++)
                {
                    var follower = Bone(prefix + (i + 1) + side);
                    if (follower.parent != segment.parent)
                        throw new InvalidOperationException("Humanoid twist follower must be a sibling: " + follower.name);
                    segment.followers[i] = follower;
                    segment.followerBindRelative[i] = inverseParent * follower.rotation;
                    segment.followerLocalBind[i] = follower.localRotation;
                }
                return segment;
            }
        }

        /// <summary>Use when an owner evaluates the Animator graph manually.</summary>
        public void InitializePresentation()
        {
            Initialize();
            presentationDriven = true;
        }

        /// <summary>Remove last frame's follower/baked pose before animation evaluation.</summary>
        public void RestoreBeforeAnimation()
        {
            Initialize();
            foreach (var segment in segments)
                for (int i = 0; i < segment.followers.Length; i++)
                    segment.followers[i].localRotation = segment.followerLocalBind[i];
        }

        /// <summary>Read the final main joints and update only their sibling helpers.</summary>
        public void ApplyPresentation()
        {
            Initialize();
            foreach (var segment in segments)
            {
                Vector3 axis = segment.distal.position - segment.parent.position;
                if (axis.sqrMagnitude < 1e-10f) continue;
                axis.Normalize();
                Quaternion expected = segment.parent.rotation * segment.distalBindRelative;
                Quaternion delta = segment.distal.rotation * Quaternion.Inverse(expected);
                // Swing/twist decomposition in world space. Pure bending must not
                // roll the helper; world/root rotation cancels in the relative delta.
                float projection = delta.x * axis.x + delta.y * axis.y + delta.z * axis.z;
                float norm = Mathf.Sqrt(projection * projection + delta.w * delta.w);
                Quaternion twist = norm < 1e-6f ? Quaternion.identity : new Quaternion(
                    axis.x * projection / norm, axis.y * projection / norm,
                    axis.z * projection / norm, delta.w / norm);
                if (twist.w < 0) twist = new Quaternion(-twist.x, -twist.y, -twist.z, -twist.w);
                for (int i = 0; i < segment.followers.Length; i++)
                {
                    Quaternion transported = segment.parent.rotation * segment.followerBindRelative[i];
                    segment.followers[i].rotation = Quaternion.Slerp(Quaternion.identity, twist, (i + 1) * .25f) * transported;
                }
            }
        }

        private void Update()
        {
            if (!presentationDriven) RestoreBeforeAnimation();
        }

        private void LateUpdate()
        {
            if (!presentationDriven) ApplyPresentation();
        }

        private void OnDisable()
        {
            if (segments != null) RestoreBeforeAnimation();
        }
    }
}
