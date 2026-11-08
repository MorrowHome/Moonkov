using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    [DisallowMultipleComponent]
    public sealed class MoonMeshDebris : MonoBehaviour
    {
        private const int GridSide = 2048;
        private const float CellSize = 2;

        [SerializeField] private Transform geometry;
        [SerializeField] private TextAsset groundExclusion;
        [SerializeField] private Material rock;
        [SerializeField] private Material regolith;

        private readonly HashSet<Collider> terrain = new();
        private byte[] excluded;
        private MoonRegolithDebris debris;
        private int groundLayers;
        private float rayTop, rayLength, nextTick;

        public void Configure(Transform geometryRoot, TextAsset exclusion, Material rockMaterial, Material groundMaterial)
        {
            geometry = geometryRoot;
            groundExclusion = exclusion;
            rock = rockMaterial;
            regolith = groundMaterial;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                !geometry || !groundExclusion || !rock || !regolith) return;

            excluded = groundExclusion.bytes;
            if (excluded.Length != GridSide * GridSide / 8)
            {
                Debug.LogError("Invalid lunar ground exclusion mask.", this);
                return;
            }
            float bottom = float.MaxValue, top = float.MinValue;
            foreach (var collider in geometry.GetComponentsInChildren<MeshCollider>())
            {
                if (!collider.name.StartsWith("DEM_", StringComparison.Ordinal) || !collider.sharedMesh) continue;
                terrain.Add(collider);
                bottom = Mathf.Min(bottom, collider.bounds.min.y);
                top = Mathf.Max(top, collider.bounds.max.y);
            }
            if (terrain.Count == 0) return;
            groundLayers = LayerMask.GetMask("Default", "Ground");
            rayTop = top + 30;
            rayLength = rayTop - bottom + 2;
            debris = new MoonRegolithDebris(transform, SampleOpenGround, rock, regolith);
        }

        private void LateUpdate()
        {
            if (debris == null || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + .10f;
            debris.Tick();
        }

        private bool SampleOpenGround(float x, float z, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            int cellX = Mathf.FloorToInt(x / CellSize), cellZ = Mathf.FloorToInt(z / CellSize);
            if ((uint)cellX >= GridSide || (uint)cellZ >= GridSide) return false;
            int index = cellX + cellZ * GridSide;
            if ((excluded[index >> 3] & (1 << (index & 7))) != 0) return false;
            if (!Physics.Raycast(new Vector3(x, rayTop, z), Vector3.down, out var hit,
                    rayLength, groundLayers, QueryTriggerInteraction.Ignore) || !terrain.Contains(hit.collider)) return false;
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        private void OnDisable()
        {
            debris?.Dispose();
            debris = null;
            excluded = null;
            terrain.Clear();
            nextTick = 0;
        }
    }
}
