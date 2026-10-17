using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Samples Blender's exact-rig local rotations, including MMD's weighted twist bones.</summary>
    internal sealed class DollSingerBakedGestures
    {
        [Serializable] private sealed class Motion
        {
            public int weapon, poseFrames, fireFrames, reloadFrames;
            public string name;
            public float fireSeconds;
            public float[] pose, fire, reload;
            public float pitch;
            public Motion[] aimPoses;
        }
        [Serializable] private sealed class Library
        {
            public int version, frameRate;
            public string[] bones;
            public Quaternion chestBindRotation;
            public Motion[] motions;
        }
        private static TextAsset cachedAsset;
        private static Library cachedLibrary;
        private static string cachedText;
        private readonly Library library;
        private readonly Transform[] bones;
        private readonly Quaternion[] previous, target, bindRotation;
        private readonly Vector3[] bindPosition;
        private readonly Transform leftShoulder, rightShoulder;
        private readonly Transform rightIndex, rightIndexTip;
        private readonly Transform body;
        private readonly DollSingerMovement movement;
        private int currentWeapon;
        private float transition;
        private bool initialized;

        public DollSingerBakedGestures(Animator animator, TextAsset asset)
        {
            body = animator.transform;
            movement = animator.GetComponent<DollSingerMovement>();
            string text = asset.text;
            if (cachedAsset != asset || cachedLibrary == null || cachedText != text)
            {
                cachedLibrary = JsonUtility.FromJson<Library>(text);
                cachedText = text;
                cachedAsset = asset;
            }
            library = cachedLibrary;
            if (library.version != 1 || library.bones == null || library.motions.Length != 4)
                throw new InvalidOperationException("Unsupported Blender gesture bake.");
            var hierarchy = new Dictionary<string, Transform>();
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true)) hierarchy[bone.name] = bone;
            bones = new Transform[library.bones.Length];
            previous = new Quaternion[bones.Length]; target = new Quaternion[bones.Length];
            bindRotation = new Quaternion[bones.Length]; bindPosition = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!hierarchy.TryGetValue(library.bones[i], out bones[i]))
                    throw new InvalidOperationException("Blender gesture bone missing: " + library.bones[i]);
                bindRotation[i] = bones[i].localRotation; bindPosition[i] = bones[i].localPosition;
            }
            leftShoulder = hierarchy["肩P.L"]; rightShoulder = hierarchy["肩P.R"];
            rightIndex = hierarchy["人指１.R"]; rightIndexTip = hierarchy["人指先.R"];
        }

        // Extra MMD bones are not necessarily written by the Humanoid Animator.
        // Restore before animation evaluation so aim-out, pitch and re-enable cannot accumulate a previous bake.
        public void RestoreBeforeAnimation()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localRotation = bindRotation[i];
                bones[i].localPosition = bindPosition[i];
            }
        }

        public void Apply(int weapon, float blend, float shotAge, bool reloading, float reloadProgress,
            float transitionSeconds, Transform chest, Vector3 aimDirection)
        {
            if (blend <= .0001f || weapon < 1 || weapon > 4) { initialized = false; return; }
            Motion motion = library.motions[weapon - 1];
            Motion low = motion, high = motion;
            float authoredPitch = 0, pitchMix = 0;
            if (motion.aimPoses != null && motion.aimPoses.Length > 0 && aimDirection.sqrMagnitude > .001f)
            {
                authoredPitch = Mathf.Clamp(Mathf.Asin(-aimDirection.normalized.y) * Mathf.Rad2Deg,
                    motion.aimPoses[0].pitch, motion.aimPoses[motion.aimPoses.Length - 1].pitch);
                foreach (var pose in motion.aimPoses)
                {
                    if (pose.pitch <= authoredPitch && (low.pitch > authoredPitch || pose.pitch > low.pitch)) low = pose;
                    if (pose.pitch >= authoredPitch && (high.pitch < authoredPitch || pose.pitch < high.pitch)) high = pose;
                }
                pitchMix = Mathf.InverseLerp(low.pitch, high.pitch, authoredPitch);
            }
            if (currentWeapon != weapon)
            {
                Array.Copy(target, previous, target.Length);
                currentWeapon = weapon; transition = initialized ? 0 : 1;
            }
            transition = Mathf.MoveTowards(transition, 1, Time.deltaTime / Mathf.Max(.01f, transitionSeconds));
            float mix = Mathf.SmoothStep(0, 1, transition);
            float[] samples = low.pose, highSamples = high.pose;
            float frame = 0; int count = 1;
            if (reloading) { samples = low.reload; highSamples = high.reload; count = motion.reloadFrames; frame = Mathf.Clamp01(reloadProgress) * (count - 1); }
            else if (shotAge >= 0 && shotAge < motion.fireSeconds)
            { samples = low.fire; highSamples = high.fire; count = motion.fireFrames; frame = shotAge / motion.fireSeconds * (count - 1); }
            int a = Mathf.FloorToInt(frame), b = Mathf.Min(a + 1, count - 1);
            for (int i = 0; i < bones.Length; i++)
            {
                Quaternion q = Quaternion.Slerp(Read(samples, a, i), Read(samples, b, i), frame - a);
                if (pitchMix > 0) q = Quaternion.Slerp(q,
                    Quaternion.Slerp(Read(highSamples, a, i), Read(highSamples, b, i), frame - a), pitchMix);
                target[i] = initialized && mix < 1 ? Quaternion.Slerp(previous[i], q, mix) : q;
                bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, target[i], blend);
                bones[i].localPosition = Vector3.Lerp(bones[i].localPosition, bindPosition[i], blend);
            }
            initialized = true;
            // Rotate the authored pair as one unit. Never solve or reorient wrists independently.
            Quaternion authoredBasis = chest.rotation * Quaternion.Inverse(library.chestBindRotation);
            // Animated chest-up can become parallel to the aim ray near vertical
            // pitch, flipping the entire authored pair. Use the player's stable up
            // axis; add only the explicit view lean rather than animation roll.
            Quaternion aimBasis = aimDirection.sqrMagnitude > .001f
                ? Quaternion.AngleAxis(movement ? movement.LeanRoll : 0, aimDirection) *
                  Quaternion.LookRotation(aimDirection, body.up) : authoredBasis;
            Quaternion correction = Quaternion.Slerp(Quaternion.identity,
                aimBasis * Quaternion.AngleAxis(-authoredPitch, Vector3.right) * Quaternion.Inverse(authoredBasis), blend);
            // Interpolating complete arm chains can slightly change their combined
            // orientation between authored pitch poses. Align the pointing pair as
            // one unit; never turn the wrist or fingers independently.
            if (weapon == 2 && aimDirection.sqrMagnitude > .001f)
            {
                Vector3 index = correction * (rightIndexTip.position - rightIndex.position);
                correction = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(index, aimDirection), blend) * correction;
            }
            Vector3 pivot = (leftShoulder.position + rightShoulder.position) * .5f;
            RotateShoulder(leftShoulder, pivot, correction);
            RotateShoulder(rightShoulder, pivot, correction);
        }

        private Quaternion Read(float[] samples, int frame, int bone)
        {
            int i = (frame * bones.Length + bone) * 4;
            return new Quaternion(samples[i], samples[i + 1], samples[i + 2], samples[i + 3]);
        }
        private static void RotateShoulder(Transform shoulder, Vector3 pivot, Quaternion rotation)
        {
            shoulder.SetPositionAndRotation(pivot + rotation * (shoulder.position - pivot), rotation * shoulder.rotation);
        }
    }
}
