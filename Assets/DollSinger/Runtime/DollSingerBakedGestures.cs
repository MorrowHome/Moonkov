using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Samples Blender's exact-rig local rotations, including MMD's weighted twist bones.</summary>
    public sealed class DollSingerBakedGestures
    {
        [Serializable] private class Pose
        {
            public float[] pose, fire, reload;
            public float pitch;
            public FireVariant[] burstFires;
        }
        [Serializable] private sealed class Motion : Pose
        {
            public int weapon, poseFrames, fireFrames, reloadFrames;
            public string name;
            public float fireSeconds;
            public Pose[] aimPoses;
        }
        [Serializable] private sealed class FireVariant { public float[] values; }
        [Serializable] private sealed class Library
        {
            public int version, frameRate;
            public string[] bones;
            public Quaternion chestBindRotation;
            public Motion[] motions;
        }
        private sealed class Cached { public int Hash; public Library Data; }
        private static readonly Dictionary<TextAsset, Cached> Libraries = new Dictionary<TextAsset, Cached>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Libraries.Clear();
        private readonly Library library;
        private readonly Transform[] bones;
        private readonly Quaternion[] previous, target, bindRotation;
        private readonly Vector3[] bindPosition;
        private readonly Transform leftShoulder, rightShoulder;
        private readonly Transform rightIndex, rightIndexTip;
        private readonly Transform leftIndexTip, leftThumbTip, rightThumbTip;
        private readonly Transform leftUpperArm, leftForearm, leftHand, rightUpperArm, rightForearm, rightHand;
        private readonly Vector3 neutralEyeFromChest;
        private readonly Vector3[] presentationOffsets;
        private Vector3 previousPresentationOffset;
        private float sniperReloadOffset;
        private const float SupportElbowAngle = 60f;
        private readonly Transform body;
        private readonly DollSingerMovement movement;
        private int currentWeapon;
        private float transition;
        private bool initialized;

        public DollSingerBakedGestures(Animator animator, TextAsset asset)
        {
            body = animator.transform;
            movement = animator.GetComponent<DollSingerMovement>();
            bool reload = !Libraries.TryGetValue(asset, out var cached);
#if UNITY_EDITOR
            // Previews may reimport a bake without reloading the domain. In a raid,
            // each immutable exact-rig library is parsed once, even as rigs alternate.
            if (!reload && !Application.isPlaying) reload = cached.Hash != asset.text.GetHashCode();
#endif
            if (reload)
            {
                string text = asset.text;
                cached = new Cached { Hash = text.GetHashCode(), Data = JsonUtility.FromJson<Library>(text) };
                Libraries[asset] = cached;
            }
            library = cached.Data;
            if (library.version != 1 || library.bones == null || library.motions.Length < 4)
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
            leftIndexTip = hierarchy["人指先.L"]; leftThumbTip = hierarchy["親指先.L"]; rightThumbTip = hierarchy["親指先.R"];
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftForearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightForearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var chest = hierarchy["上半身2"];
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            foreach (var pair in new[] { new[] { "目.L", "目.R" }, new[] { "eye_L", "eye_R" }, new[] { "Eye_L", "Eye_R" } })
            {
                if (leftEye && rightEye) break;
                hierarchy.TryGetValue(pair[0], out leftEye); hierarchy.TryGetValue(pair[1], out rightEye);
            }
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            Vector3 eye = leftEye && rightEye ? (leftEye.position + rightEye.position) * .5f :
                head.position + body.rotation * new Vector3(0, .08f, .05f);
            neutralEyeFromChest = Quaternion.Inverse(chest.rotation * Quaternion.Inverse(library.chestBindRotation)) * (eye - chest.position);
            presentationOffsets = CalibratePresentation(chest, eye);
        }

        private Vector3[] CalibratePresentation(Transform chest, Vector3 eye)
        {
            var offsets = new Vector3[library.motions.Length];
            Quaternion basis = chest.rotation * Quaternion.Inverse(library.chestBindRotation);
            Quaternion inverse = Quaternion.Inverse(basis);
            Vector3 pivot = (leftShoulder.position + rightShoulder.position) * .5f;
            // Measure the neutral gesture once. A fixed offset leaves the whole
            // fire/reload animation intact; it never chases animated screen bounds.
            for (int weapon = 1; weapon <= offsets.Length; weapon++)
            {
                if (weapon == 4) continue;
                for (int i = 0; i < bones.Length; i++) bones[i].localRotation = Read(library.motions[weapon - 1].pose, 0, i);
                Quaternion correction = PointsWithIndex(weapon)
                    ? Quaternion.FromToRotation(rightIndexTip.position - rightIndex.position, basis * Vector3.forward)
                    : Quaternion.identity;
                Vector3 min = Vector3.one * float.PositiveInfinity, max = Vector3.one * float.NegativeInfinity;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (!library.bones[i].Contains("指") && !library.bones[i].StartsWith("手首")) continue;
                    Vector3 point = inverse * (pivot + correction * (bones[i].position - pivot) - eye);
                    min = Vector3.Min(min, point); max = Vector3.Max(max, point);
                }
                // The pair stays horizontally centred. Leave only a small gap
                // above the fingers, never move the whole gesture to one side.
                float height = -(max.y - min.y) * .5f - .015f;
                if (weapon == 1) height -= .013f;
                if (weapon == 7 || weapon == 13) height -= .005f;
                if (weapon == 2) height -= .02f;
                if (weapon == 3) height -= .05f;
                // These tall, open signs flank the sight point. Keep their lower
                // fingers visible instead of forcing their entire silhouette below it.
                if (weapon == 6 || weapon == 9 || weapon == 11) height += .03f;
                Vector3 center = new Vector3(0, height, .26f);
                offsets[weapon - 1] = center - (min + max) * .5f;
            }
            RestoreBeforeAnimation();
            return offsets;
        }

        private static bool PointsWithIndex(int weapon) => weapon == 2 || weapon == 5 || weapon == 8 || weapon == 11;

        public bool IsFiring(int weapon, float shotAge, float releaseSeconds = 0) => weapon >= 1 && weapon <= library.motions.Length &&
            shotAge >= 0 && shotAge < (releaseSeconds > 0 ? releaseSeconds : library.motions[weapon - 1].fireSeconds);

        // Keep the first 80 ms exactly as authored. Only the recovery changes length.
        public static float ShotSampleTime(float age, float authoredSeconds, float releaseSeconds)
        {
            if (releaseSeconds <= 0 || age <= .08f || authoredSeconds <= .08f) return age;
            return .08f + (age - .08f) * (authoredSeconds - .08f) / Mathf.Max(.01f, releaseSeconds - .08f);
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
            float transitionSeconds, Transform chest, Vector3 aimDirection, float deltaTime = -1f, int burstIndex = 0, float releaseSeconds = 0, Camera eye = null)
        {
            if (blend <= .0001f || weapon < 1 || weapon > library.motions.Length) { initialized = false; return; }
            Motion motion = library.motions[weapon - 1];
            float reloadLowering = reloading ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(reloadProgress)) : 0;
            sniperReloadOffset = weapon == 4 && reloading
                ? .04f * Mathf.SmoothStep(0, 1, Mathf.Min(reloadProgress, 1 - reloadProgress) / .12f) : 0;
            shotAge = ShotSampleTime(shotAge, motion.fireSeconds, releaseSeconds);
            Pose low = motion, high = motion;
            float authoredPitch = 0, pitchMix = 0;
            bool eyeRelative = eye && weapon != 4;
            if (!eyeRelative && motion.aimPoses != null && motion.aimPoses.Length > 0 && aimDirection.sqrMagnitude > .001f)
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
                previousPresentationOffset = currentWeapon > 0 ? presentationOffsets[currentWeapon - 1] : presentationOffsets[weapon - 1];
                currentWeapon = weapon; transition = initialized ? 0 : 1;
            }
            transition = Mathf.MoveTowards(transition, 1, (deltaTime < 0 ? Time.deltaTime : deltaTime) / Mathf.Max(.01f, transitionSeconds));
            float mix = Mathf.SmoothStep(0, 1, transition);
            float[] samples = low.pose, highSamples = high.pose;
            float frame = 0; int count = 1;
            if (reloading) { samples = low.reload; highSamples = high.reload; count = motion.reloadFrames; frame = Mathf.Clamp01(reloadProgress) * (count - 1); }
            else if (shotAge >= 0 && shotAge < motion.fireSeconds)
            { samples = low.burstFires != null ? low.burstFires[Mathf.Clamp(burstIndex,0,low.burstFires.Length-1)].values : low.fire;
              highSamples = high.burstFires != null ? high.burstFires[Mathf.Clamp(burstIndex,0,high.burstFires.Length-1)].values : high.fire;
              count = motion.fireFrames; frame = shotAge / motion.fireSeconds * (count - 1); }
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
            // Nocturne now raises its middle fingers; its folded index is not an aiming ray.
            if (PointsWithIndex(weapon) && aimDirection.sqrMagnitude > .001f)
            {
                Vector3 index = correction * (rightIndexTip.position - rightIndex.position);
                correction = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(index, aimDirection), blend) * correction;
            }
            Vector3 pivot = (leftShoulder.position + rightShoulder.position) * .5f;
            RotateShoulder(leftShoulder, pivot, correction);
            RotateShoulder(rightShoulder, pivot, correction);
            if (eyeRelative)
            {
                // Place the complete gesture around the tiny reticle window. Both hands get
                // one fixed offset; finger detail, contact and recoil stay authored.
                Vector3 neutralEye = chest.position + authoredBasis * neutralEyeFromChest;
                Vector3 framing = Vector3.Lerp(previousPresentationOffset, presentationOffsets[weapon - 1], mix);
                if (weapon == 1 && reloading)
                    framing.y -= .052f * Mathf.SmoothStep(0, 1, Mathf.Min(reloadProgress, 1 - reloadProgress) / .2f);
                if (weapon == 2 || weapon == 5 || weapon == 8)
                    framing.y -= (weapon == 2 ? .015f : .025f) * reloadLowering;
                if (weapon == 3) framing.y += .05f * reloadLowering;
                MoveHands((eye.transform.position + aimBasis * framing - (pivot + correction * (neutralEye - pivot))) * blend,
                    weapon == 2 ? -eye.transform.up : Vector3.zero, eye.transform.right, blend);
            }
        }

        private void MoveHands(Vector3 offset, Vector3 bendDirection, Vector3 right, float bendBlend)
        {
            if (offset.sqrMagnitude < .00000001f) return;
            // Project the common target into both arms' reach. Sliding along the
            // reach boundary lets a short arm raise its hand even when it cannot
            // move further forward. Never stretch bones or separate the hands.
            for (int i = 0; i < 8; i++)
            {
                Constrain(leftUpperArm, leftForearm, leftHand);
                Constrain(rightUpperArm, rightForearm, rightHand);
            }
            float fraction = Mathf.Min(ReachFraction(leftUpperArm, leftForearm, leftHand, offset),
                ReachFraction(rightUpperArm, rightForearm, rightHand, offset));
            offset *= fraction;
            // Tuck the supporting elbow beneath the crossed wrists. A long cuff
            // must not cut across the reticle when the player looks steeply up.
            bool guideElbows = bendDirection.sqrMagnitude > .001f;
            Vector3 leftPole = guideElbows ? Quaternion.AngleAxis(SupportElbowAngle, Vector3.Cross(bendDirection, right)) * bendDirection : Vector3.zero;
            Vector3 rightPole = guideElbows ? bendDirection + right * .75f : Vector3.zero;
            Solve(leftUpperArm, leftForearm, leftHand, leftHand.position + offset, leftPole, bendBlend);
            Solve(rightUpperArm, rightForearm, rightHand, rightHand.position + offset, rightPole, bendBlend);

            void Constrain(Transform upper, Transform lower, Transform hand)
            {
                float reach = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position) - .001f;
                Vector3 origin = upper.position - hand.position;
                offset = origin + Vector3.ClampMagnitude(offset - origin, reach);
            }
        }

        private static float ReachFraction(Transform upper, Transform lower, Transform hand, Vector3 offset)
        {
            float length = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
            Vector3 from = hand.position - upper.position;
            float a = offset.sqrMagnitude, b = Vector3.Dot(from, offset);
            if (a < .0000000001f) return 1;
            float c = from.sqrMagnitude - length * length;
            return Mathf.Clamp01((-b + Mathf.Sqrt(Mathf.Max(0, b * b - a * c))) / a);
        }

        /// <summary>Raise the real sniper frame to the eye ray. Never change camera optics or bone lengths.</summary>
        public void AlignViewfinder(Camera eye, float blend)
        {
            if (!eye || blend <= .0001f || !leftUpperArm || !leftForearm || !leftHand ||
                !rightUpperArm || !rightForearm || !rightHand) return;
            Vector3 center = (leftIndexTip.position + rightThumbTip.position + rightIndexTip.position + leftThumbTip.position) * .25f;
            Vector3 origin = eye.transform.position - eye.transform.up * sniperReloadOffset, forward = eye.transform.forward;
            // Both hands move by one common offset, preserving the authored finger
            // contacts and frame orientation. Keep the target inside BOTH arms' reach.
            float minimum = eye.nearClipPlane + .06f, maximum = .32f;
            if (!Reach(leftUpperArm, leftForearm, leftHand) || !Reach(rightUpperArm, rightForearm, rightHand) || minimum > maximum) return;
            // Hold the frame at the eye instead of pushing it towards full arm
            // extension. The common reach interval still protects both arms.
            float depth = Mathf.Clamp(.20f, minimum, maximum);
            Vector3 offset = (origin + forward * depth - center) * Mathf.SmoothStep(0, 1, blend);
            Solve(leftUpperArm, leftForearm, leftHand, leftHand.position + offset);
            Solve(rightUpperArm, rightForearm, rightHand, rightHand.position + offset);

            bool Reach(Transform upper, Transform lower, Transform hand)
            {
                float length = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position) - .005f;
                Vector3 relative = origin + hand.position - center - upper.position;
                float along = Vector3.Dot(relative, forward);
                float square = length * length - (relative.sqrMagnitude - along * along);
                if (square <= 0) return false;
                float extent = Mathf.Sqrt(square);
                minimum = Mathf.Max(minimum, -along - extent);
                maximum = Mathf.Min(maximum, -along + extent);
                return true;
            }
        }

        private static void Solve(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 bendDirection = default, float bendBlend = 0)
        {
            Vector3 shoulder = upper.position, elbow = lower.position, wrist = hand.position;
            Quaternion handRotation = hand.rotation;
            float upperLength = Vector3.Distance(shoulder, elbow), lowerLength = Vector3.Distance(elbow, wrist);
            Vector3 direction = target - shoulder;
            float distance = direction.magnitude;
            if (distance < .0001f || upperLength < .0001f || lowerLength < .0001f) return;
            direction /= distance;
            float reach = Mathf.Clamp(distance, Mathf.Abs(upperLength - lowerLength) + .0001f, upperLength + lowerLength - .0001f);
            Vector3 bend = Vector3.ProjectOnPlane(elbow - shoulder, direction);
            if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(upper.up, direction);
            if (bend.sqrMagnitude < .000001f) bend = Vector3.Cross(direction, upper.right);
            Vector3 pole = Vector3.ProjectOnPlane(bendDirection, direction);
            if (pole.sqrMagnitude > .000001f) bend = Vector3.Slerp(bend.normalized, pole.normalized, bendBlend);
            float along = (upperLength * upperLength + reach * reach - lowerLength * lowerLength) / (2 * reach);
            Vector3 solvedElbow = shoulder + direction * along + bend.normalized * Mathf.Sqrt(Mathf.Max(0, upperLength * upperLength - along * along));
            upper.rotation = Quaternion.FromToRotation(elbow - shoulder, solvedElbow - shoulder) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
            // Fingers keep their authored shape and both palms retain a common
            // orientation. Only the upper-arm/elbow swing changes to reach the eye.
            hand.rotation = handRotation;
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
