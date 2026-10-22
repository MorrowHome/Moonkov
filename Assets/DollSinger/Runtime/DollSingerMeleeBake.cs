using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Independent exact-rig melee library. No Humanoid retarget or runtime wrist IK.</summary>
    public sealed class DollSingerMeleeBake
    {
        [Serializable] public sealed class Samples { public float pitch; public int frames; public float[] values; }
        [Serializable] public sealed class Clip
        {
            public string weapon, action;
            public float seconds, windup, active, recovery;
            public Samples[] poses;
        }
        [Serializable] private sealed class Library
        {
            public int version, frameRate;
            public string revision;
            public string[] bones;
            public Quaternion chestBindRotation, gripRotation;
            public Vector3 gripPosition;
            public Clip[] clips;
        }
        private static TextAsset cachedAsset;
        private static string cachedText;
        private static Library cachedLibrary;
        private readonly Library library;
        private readonly Dictionary<int, Clip> clips = new Dictionary<int, Clip>();
        private readonly Transform[] bones;
        private readonly Quaternion[] bindRotations, transition, lastTarget;
        private readonly Vector3[] bindPositions;
        private readonly Transform leftShoulder, rightShoulder, body, chest;
        private float transitionStarted, transitionDuration;
        public Transform RightHand { get; }
        public Vector3 GripPosition => library.gripPosition;
        public Quaternion GripRotation => library.gripRotation;
        public string Revision => library.revision;

        public DollSingerMeleeBake(Animator animator, TextAsset asset)
        {
            if (!animator || !asset) throw new ArgumentException("Melee requires an Animator and independent baked library.");
            string text = asset.text;
            if (cachedAsset != asset || cachedText != text || cachedLibrary == null)
            {
                cachedLibrary = JsonUtility.FromJson<Library>(text);
                cachedAsset = asset; cachedText = text;
            }
            library = cachedLibrary;
            if (library == null || library.version != 1 || library.bones == null || library.clips == null)
                throw new InvalidOperationException("Unsupported melee bake.");
            var hierarchy = new Dictionary<string, Transform>();
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true)) hierarchy[bone.name] = bone;
            bones = new Transform[library.bones.Length]; bindRotations = new Quaternion[bones.Length];
            bindPositions = new Vector3[bones.Length]; transition = new Quaternion[bones.Length];
            lastTarget = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!hierarchy.TryGetValue(library.bones[i], out bones[i]))
                    throw new InvalidOperationException("Melee rig bone missing: " + library.bones[i]);
                bindRotations[i] = bones[i].localRotation; bindPositions[i] = bones[i].localPosition;
                lastTarget[i] = bindRotations[i];
            }
            body = animator.transform; chest = hierarchy["上半身2"];
            leftShoulder = hierarchy["肩P.L"]; rightShoulder = hierarchy["肩P.R"]; RightHand = hierarchy["手首.R"];
            foreach (var clip in library.clips)
            {
                foreach (var pose in clip.poses)
                    if (pose.frames < 1 || pose.values.Length != pose.frames * bones.Length * 4)
                        throw new InvalidOperationException("Melee sample length mismatch: " + clip.weapon + "/" + clip.action);
                if (!Enum.TryParse<MeleeWeaponKind>(clip.weapon, out var weapon))
                    throw new InvalidOperationException("Unknown melee weapon: " + clip.weapon);
                clips.Add((int)weapon * 8 + ActionIndex(clip.action), clip);
            }
        }
        public void RestoreBeforeAnimation()
        {
            for (int i = 0; i < bones.Length; i++)
            { bones[i].localRotation = bindRotations[i]; bones[i].localPosition = bindPositions[i]; }
        }
        public void BeginTransition(float seconds)
        {
            // Targets are stored before the shared world correction, so transitions cannot apply it twice.
            Array.Copy(lastTarget, transition, lastTarget.Length);
            transitionStarted = Time.time; transitionDuration = Mathf.Max(0, seconds);
        }
        public void Apply(MeleeWeaponKind weapon, string action, float elapsed, float blend,
            in MeleeTiming timing, Vector3 aim, float lean)
        {
            if (blend <= 0 || !clips.TryGetValue((int)weapon * 8 + ActionIndex(action), out var clip)) return;
            float sourceTime = elapsed;
            if (clip.active > 0)
            {
                sourceTime = elapsed < timing.Windup ? clip.windup * Mathf.Clamp01(elapsed / Mathf.Max(.0001f, timing.Windup)) :
                    elapsed < timing.Windup + timing.Active ? clip.windup + clip.active * Mathf.Clamp01((elapsed - timing.Windup) / Mathf.Max(.0001f, timing.Active)) :
                    clip.windup + clip.active + clip.recovery * Mathf.Clamp01((elapsed - timing.Windup - timing.Active) / Mathf.Max(.0001f, timing.Recovery));
            }
            float pitch = Mathf.Clamp(Mathf.Asin(-aim.normalized.y) * Mathf.Rad2Deg, -70, 70);
            Samples low = clip.poses[0], high = clip.poses[clip.poses.Length - 1];
            foreach (var samples in clip.poses)
            {
                if (samples.pitch <= pitch) low = samples;
                if (samples.pitch >= pitch) { high = samples; break; }
            }
            float pitchMix = Mathf.InverseLerp(low.pitch, high.pitch, pitch);
            float progress = clip.seconds > 0 ? Mathf.Clamp01(sourceTime / clip.seconds) : 0;
            float transitionMix = transitionDuration <= 0 ? 1 : Mathf.SmoothStep(0, 1, (Time.time - transitionStarted) / transitionDuration);
            for (int i = 0; i < bones.Length; i++)
            {
                Quaternion q = Quaternion.Slerp(Sample(low, progress, i), Sample(high, progress, i), pitchMix);
                if (transitionMix < 1) q = Quaternion.Slerp(transition[i], q, transitionMix);
                lastTarget[i] = q;
                bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, q, blend);
                bones[i].localPosition = Vector3.Lerp(bones[i].localPosition, bindPositions[i], blend);
            }
            // Rotate both complete authored chains together, preserving twist and finger axes.
            // The action now owns chest/shoulder wind-up. Using animated chest rotation
            // here would subtract that motion from the arms after sampling it.
            Quaternion basis = body.rotation;
            Quaternion desired = aim.sqrMagnitude > .001f
                ? Quaternion.AngleAxis(lean, aim) * Quaternion.LookRotation(aim, body.up) : basis;
            Quaternion correction = Quaternion.Slerp(Quaternion.identity,
                desired * Quaternion.AngleAxis(-pitch, Vector3.right) * Quaternion.Inverse(basis), blend);
            Vector3 pivot = (leftShoulder.position + rightShoulder.position) * .5f;
            Rotate(leftShoulder, pivot, correction); Rotate(rightShoulder, pivot, correction);
        }
        private Quaternion Sample(Samples samples, float progress, int bone)
        {
            float frame = progress * (samples.frames - 1); int a = Mathf.FloorToInt(frame), b = Mathf.Min(a + 1, samples.frames - 1);
            return Quaternion.Slerp(Read(samples.values, a, bone), Read(samples.values, b, bone), frame - a);
        }
        private Quaternion Read(float[] values, int frame, int bone)
        { int i = (frame * bones.Length + bone) * 4; return new Quaternion(values[i], values[i + 1], values[i + 2], values[i + 3]); }
        private static void Rotate(Transform shoulder, Vector3 pivot, Quaternion rotation) =>
            shoulder.SetPositionAndRotation(pivot + rotation * (shoulder.position - pivot), rotation * shoulder.rotation);
        private static int ActionIndex(string action) => action == "idle" ? 0 : action == "equip" ? 1 :
            action == "light0" ? 2 : action == "light1" ? 3 : action == "light2" ? 4 : action == "heavy" ? 5 : action == "recover" ? 6 : 7;
    }
}
