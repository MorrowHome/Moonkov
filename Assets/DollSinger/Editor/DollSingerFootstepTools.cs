using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.MP_FPS.DollSinger.Editor
{
    public static class DollSingerFootstepTools
    {
        [MenuItem("Tools/Doll Singer/Set Up Footstep Events")]
        public static void SetUp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            foreach (string folder in new[] { DollSingerLocomotionTools.ClipRoot, DollSingerStanceTools.Root })
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (!clip || clip.name.EndsWith("_Idle", StringComparison.Ordinal)) continue;
                SetEvents(clip);
                AssetDatabase.SaveAssetIfDirty(clip);
            }
        }

        // Bake contacts once in the asset. Runtime sound timing comes only from
        // AnimationEvents, never a distance accumulator or a per-frame bone probe.
        public static void SetEvents(AnimationClip clip)
        {
            if (clip.name.EndsWith("_Idle", StringComparison.Ordinal)) return;
            var events = AnimationUtility.GetAnimationEvents(clip).Where(e => e.functionName != "OnFootstep").ToList();
            var root = Curve(clip, "RootT.y");
            bool crawl = clip.name == "Prone_Crawl";
            for (int foot = 0; foot < 2; foot++)
            {
                // Crawling foley follows hand plants; feet may drag without lifting.
                var height = Curve(clip, (foot == 0 ? "Left" : "Right") + (crawl ? "HandT.y" : "FootT.y"));
                if (height == null) throw new InvalidOperationException("Missing contact curve: " + clip.name);
                foreach (float time in ContactTimes(height, root, clip.length))
                    events.Add(new AnimationEvent { time = time, functionName = "OnFootstep", intParameter = foot, stringParameter = foot == 0 ? "Left" : "Right",
                        messageOptions = SendMessageOptions.DontRequireReceiver });
            }
            if (!events.Any(e => e.functionName == "OnFootstep"))
                throw new InvalidOperationException("No foot/hand plants detected: " + clip.name);
            AnimationUtility.SetAnimationEvents(clip, events.OrderBy(e => e.time).ToArray());
            EditorUtility.SetDirty(clip);
        }

        private static AnimationCurve Curve(AnimationClip clip, string property) =>
            AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property));

        private static IEnumerable<float> ContactTimes(AnimationCurve height, AnimationCurve root, float length)
        {
            int samples = Mathf.Max(60, Mathf.CeilToInt(length * 120));
            var y = new float[samples];
            float low = float.MaxValue, high = float.MinValue;
            for (int i = 0; i < samples; i++)
            {
                float t = i * length / samples;
                y[i] = height.Evaluate(t) + (root?.Evaluate(t) ?? 0);
                low = Mathf.Min(low, y[i]); high = Mathf.Max(high, y[i]);
            }
            if (high - low < .003f) yield break;
            float lift = Mathf.Lerp(low, high, .55f), contact = Mathf.Lerp(low, high, .18f);
            bool airborne = false;
            // Warm one cycle to detect a swing that crosses the clip's loop seam.
            for (int i = 0; i < samples * 2; i++)
            {
                float value = y[i % samples];
                if (value >= lift) airborne = true;
                if (!airborne || value > contact) continue;
                airborne = false;
                if (i >= samples) yield return (i - samples) * length / samples;
            }
        }
    }
}
