using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Snapshot-driven visual flinch. Never moves the motor or authoritative hitboxes.</summary>
    public sealed class AlienHitReaction
    {
        private struct RootPose
        {
            public Transform Root;
            public Vector3 Position;
            public Quaternion Rotation;
        }
        private readonly Transform owner;
        private readonly List<RootPose> roots = new();
        private readonly List<Renderer> renderers = new();
        private float health, strength, age;
        private bool initialized, applied;
        private int side = 1;
        public float Pulse => strength * (1f - Mathf.Exp(-age * 90f)) * Mathf.Exp(-age * 17f);

        public AlienHitReaction(Transform owner) { this.owner = owner; Refresh(); }

        public void Observe(float currentHealth, float maxHealth, bool suppressed)
        {
            if (!float.IsFinite(currentHealth)) return;
            if (initialized && currentHealth > 0 && currentHealth < health - .01f && !suppressed)
            {
                strength = Mathf.Min(1.5f, Pulse + Mathf.Lerp(.55f, 1.2f,
                    Mathf.Clamp01((health - currentHealth) / Mathf.Max(1f, maxHealth) * 4f)));
                age = 0; side = -side;
            }
            initialized = true; health = currentHealth;
            if (currentHealth <= 0 || suppressed) strength = 0;
        }

        // Called after a form change, never per shot. Only generated visual roots belong here.
        public void Refresh()
        {
            Restore(); roots.Clear(); renderers.Clear();
            owner.GetComponentsInChildren(true, renderers);
            foreach (var renderer in renderers)
            {
                var root = renderer.transform;
                if (root == owner) continue;
                while (root.parent && root.parent != owner) root = root.parent;
                bool known = false;
                foreach (var pose in roots) if (pose.Root == root) { known = true; break; }
                if (!known) roots.Add(new RootPose { Root = root });
            }
        }

        public void Restore()
        {
            if (!applied) return;
            foreach (var pose in roots)
                if (pose.Root) pose.Root.SetPositionAndRotation(pose.Position, pose.Rotation);
            applied = false;
        }

        public void Present(float deltaTime, Vector3 pivot, Vector3 up, Vector3 forward, float scale, bool boss)
        {
            age += Mathf.Max(0, deltaTime);
            float pulse = Pulse * (boss ? .35f : 1f);
            if (pulse < .0001f) return;
            var rotation = Quaternion.AngleAxis(6f * pulse, Vector3.Cross(up, forward)) *
                Quaternion.AngleAxis(side * 3f * pulse, forward);
            var offset = -up * (.035f * scale * pulse);
            for (int i = 0; i < roots.Count; i++)
            {
                var pose = roots[i];
                if (!pose.Root) continue;
                pose.Position = pose.Root.position; pose.Rotation = pose.Root.rotation;
                roots[i] = pose;
                pose.Root.SetPositionAndRotation(pivot + rotation * (pose.Position - pivot) + offset,
                    rotation * pose.Rotation);
            }
            applied = true;
        }
    }
}
