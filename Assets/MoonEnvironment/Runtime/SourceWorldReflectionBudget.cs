using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Keeps the fixed world's local reflection set within the URP atlas budget.</summary>
    public sealed class SourceWorldReflectionBudget
    {
        public const int MaximumActiveProbes = 12;
        private const double RefreshInterval = .2;
        private readonly Entry[] entries;
        private readonly int[] selected;
        private readonly Plane[] planes = new Plane[6];
        private int frame = -1;
        private double nextRefresh;
        private Camera previousCamera;
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private bool restored;
        public int Limit { get; }
        public int ActiveCount { get; private set; }

        private struct Entry
        {
            public ReflectionProbe probe;
            public Bounds bounds;
            public Vector3 position;
            public bool originallyEnabled, selected, containsCamera;
            public float score;
        }

        public SourceWorldReflectionBudget(ReflectionProbe[] probes, int maximumTextureSize)
        {
            entries = new Entry[probes.Length];
            int largestFace = 1;
            for (int i = 0; i < probes.Length; i++)
            {
                var probe = probes[i];
                if (!probe || probe.mode != ReflectionProbeMode.Custom || !probe.customBakedTexture) continue;
                entries[i] = new Entry
                {
                    probe = probe, bounds = probe.bounds, position = probe.transform.position,
                    originallyEnabled = probe.enabled
                };
                if (probe.enabled)
                {
                    largestFace = Mathf.Max(largestFace, probe.customBakedTexture.width);
                    probe.enabled = false;
                }
            }
            // URP 17.5 allocates a 4*face base block and up to seven mip blocks.
            // Reserve another complete set for the previous-frame atlas cache.
            long side = Mathf.NextPowerOfTwo(largestFace * 4), pixels = 0;
            for (int mip = 0; mip < 7; mip++) { pixels += side * side; side = Math.Max(4, side / 2); }
            Limit = (int)Math.Min(MaximumActiveProbes, (long)maximumTextureSize * maximumTextureSize / (pixels * 2));
            selected = new int[Limit];
        }

        public void Prepare(Camera camera, int frameIndex, double time)
        {
            if (restored || !camera || frame == frameIndex) return;
            // One global set per frame. Camera stacks and secondary views cannot
            // each add another set to the same renderer's reflection atlas.
            frame = frameIndex;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            if (camera == previousCamera && time < nextRefresh &&
                (position - previousPosition).sqrMagnitude < 1 && Quaternion.Angle(rotation, previousRotation) < 10) return;
            previousCamera = camera; previousPosition = position; previousRotation = rotation;
            nextRefresh = time + RefreshInterval;
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            int count = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                ref var entry = ref entries[i];
                if (!entry.probe || !entry.originallyEnabled || !entry.probe.gameObject.activeInHierarchy) continue;
                float distance = entry.bounds.SqrDistance(position);
                entry.containsCamera = distance < .0001f;
                if (!entry.containsCamera && !GeometryUtility.TestPlanesAABB(planes, entry.bounds)) continue;
                // Prefer the containing room, then nearby receivers and captures.
                // A small retention bias prevents churn at equal-distance borders.
                entry.score = distance + (position - entry.position).sqrMagnitude * .01f;
                if (entry.selected) entry.score *= .8f;
                int rank = count;
                while (rank > 0 && IsBetter(entry, entries[selected[rank - 1]])) rank--;
                if (rank >= Limit) continue;
                for (int j = Math.Min(count, Limit - 1); j > rank; j--) selected[j] = selected[j - 1];
                selected[rank] = i;
                count = Math.Min(count + 1, Limit);
            }
            for (int i = 0; i < entries.Length; i++) entries[i].selected = false;
            for (int i = 0; i < count; i++) entries[selected[i]].selected = true;
            ActiveCount = count;
            for (int i = 0; i < entries.Length; i++)
            {
                ref var entry = ref entries[i];
                if (entry.probe && entry.originallyEnabled && entry.probe.enabled != entry.selected)
                    entry.probe.enabled = entry.selected;
            }
        }

        private static bool IsBetter(Entry candidate, Entry current)
        {
            if (candidate.containsCamera != current.containsCamera) return candidate.containsCamera;
            if (candidate.score != current.score) return candidate.score < current.score;
            return candidate.probe.importance > current.probe.importance;
        }

        public void Restore()
        {
            if (restored) return;
            restored = true;
            foreach (var entry in entries)
                if (entry.probe) entry.probe.enabled = entry.originallyEnabled;
            ActiveCount = 0;
        }
    }
}
