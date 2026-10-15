using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Shares two lower mesh tiers across static rocks without changing their collision.</summary>
    [DisallowMultipleComponent]
    public sealed class MoonRockLod : MonoBehaviour
    {
        [SerializeField] private Transform rocks;
        [SerializeField] private Mesh[] sourceMeshes = Array.Empty<Mesh>();
        [SerializeField] private Mesh[] middleMeshes = Array.Empty<Mesh>();
        [SerializeField] private Mesh[] distantMeshes = Array.Empty<Mesh>();
        [SerializeField, Min(1)] private float fullDetailDistance = 18;
        [SerializeField, Min(1)] private float middleDetailDistance = 80;
        [SerializeField, Min(1)] private float visibilityDistance = 600;

        private struct Rock
        {
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Vector3 Center;
            public float Radius;
            public int Variant, Tier;
            public bool InitiallyEnabled;
        }

        private Rock[] entries;
        private Camera viewer;
        private float nextRefresh;

        public void Configure(Transform root, Mesh[] sources, Mesh[] middle, Mesh[] distant)
        {
            Restore();
            rocks = root;
            sourceMeshes = sources;
            middleMeshes = middle;
            distantMeshes = distant;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            if (!viewer || !viewer.isActiveAndEnabled) viewer = Camera.main;
            if (!viewer) return;
            if (entries == null && !Cache()) return;
            Refresh(viewer.transform.position);
        }

        private bool Cache()
        {
            if (!rocks || sourceMeshes.Length == 0 || sourceMeshes.Length != middleMeshes.Length ||
                sourceMeshes.Length != distantMeshes.Length) return false;
            for (int i = 0; i < sourceMeshes.Length; i++)
                if (!sourceMeshes[i] || !middleMeshes[i] || !distantMeshes[i]) return false;
            var filters = rocks.GetComponentsInChildren<MeshFilter>(true);
            var collected = new System.Collections.Generic.List<Rock>(filters.Length);
            foreach (var filter in filters)
            {
                int variant = Array.IndexOf(sourceMeshes, filter.sharedMesh);
                var renderer = filter.GetComponent<MeshRenderer>();
                if (variant < 0 || !renderer) continue;
                var bounds = renderer.bounds;
                collected.Add(new Rock { Filter = filter, Renderer = renderer, Center = bounds.center,
                    Radius = bounds.extents.magnitude, Variant = variant, Tier = -1,
                    InitiallyEnabled = renderer.enabled });
            }
            entries = collected.ToArray();
            return true;
        }

        private void Refresh(Vector3 position)
        {
            if (entries == null) return;
            for (int i = 0; i < entries.Length; i++)
            {
                ref var rock = ref entries[i];
                if (!rock.Filter || !rock.Renderer) continue;
                float distanceSquared = (rock.Center - position).sqrMagnitude;
                float near = fullDetailDistance + rock.Radius;
                float middle = Mathf.Max(fullDetailDistance, middleDetailDistance) + rock.Radius;
                float far = Mathf.Max(middleDetailDistance, visibilityDistance) + rock.Radius;
                int tier = distanceSquared <= near * near ? 0 : distanceSquared <= middle * middle ? 1 :
                    distanceSquared <= far * far ? 2 : 3;
                if (tier == rock.Tier) continue;
                rock.Tier = tier;
                rock.Filter.sharedMesh = tier == 0 ? sourceMeshes[rock.Variant] :
                    tier == 1 ? middleMeshes[rock.Variant] : distantMeshes[rock.Variant];
                rock.Renderer.enabled = rock.InitiallyEnabled && tier < 3;
            }
        }

        private void Restore()
        {
            if (entries != null)
                foreach (var rock in entries)
                {
                    if (rock.Filter && rock.Variant < sourceMeshes.Length)
                        rock.Filter.sharedMesh = sourceMeshes[rock.Variant];
                    if (rock.Renderer) rock.Renderer.enabled = rock.InitiallyEnabled;
                }
            entries = null;
            viewer = null;
            nextRefresh = 0;
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
    }
}
