using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>
    /// Sub-voxel liquid bonds between actual simulated particles. The coarse density
    /// volume cannot resolve millimetre threads; this mesh reads the same GPU positions,
    /// narrows under extension and disappears when a bond breaks. No skeleton animation
    /// or CPU particle readback is used to fabricate the endpoints.
    /// </summary>
    public sealed class FluidFilaments
    {
        private readonly GameObject m_Object;
        private readonly Mesh m_Mesh;
        private readonly Material m_Material;
        public int Count { get; }

        public FluidFilaments(Transform parent, Vector3[] rest, int[] regions, float[] segments, out Vector4[] indices, out Vector4[] lengths, float boundsSize = 5.6f)
        {
            var bonds = new List<Vector2Int>(); var unique = new HashSet<long>();
            indices = new Vector4[rest.Length]; lengths = new Vector4[rest.Length];
            var degrees = new int[rest.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = Vector4.one * -1;
            // A sparse polymer network: local lengthwise threads plus bridges between
            // neighbouring legs/root folds. Limit degree and count to bound GPU work.
            for (int cross = 0; cross < 2; cross++)
            for (int a = cross; a < rest.Length && bonds.Count < (cross == 0 ? 760 : 900); a += cross == 0 ? 7 : 13)
            {
                if (cross == 1 && (segments[a] > 3 || regions[a] < 0)) continue;
                float choice = Mathf.Repeat(Mathf.Sin(a * 17.13f + cross * 9) * 237.17f, 1);
                float desired = cross == 0 ? Mathf.Lerp(.14f, .45f, choice * choice) : Mathf.Lerp(.22f, .82f, choice);
                int nearest = -1; float best = float.MaxValue, chosenDistance = 0;
                for (int b = 0; b < rest.Length; b++)
                {
                    if (degrees[a] >= 4 || degrees[b] >= 4 || a == b) continue;
                    if ((regions[a] == regions[b]) != (cross == 0)) continue;
                    if (cross == 1 && (segments[b] > 3 || regions[b] < 0)) continue;
                    if (cross == 0 && regions[a] >= 0 && Mathf.Abs(segments[a] - segments[b]) < 1.3f) continue;
                    float distance = (rest[a] - rest[b]).sqrMagnitude;
                    if (distance < .10f * .10f || distance > (cross == 0 ? .5f * .5f : .9f * .9f)) continue;
                    float score = Mathf.Abs(distance - desired * desired);
                    if (score >= best) continue;
                    long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                    if (unique.Contains(key)) continue;
                    best = score; chosenDistance = distance; nearest = b;
                }
                if (nearest < 0) continue;
                long pair = ((long)Math.Min(a, nearest) << 32) | (uint)Math.Max(a, nearest);
                unique.Add(pair); bonds.Add(new Vector2Int(a, nearest)); float length = Mathf.Sqrt(chosenDistance);
                indices[a][degrees[a]] = nearest; lengths[a][degrees[a]++] = length;
                indices[nearest][degrees[nearest]] = a; lengths[nearest][degrees[nearest]++] = length;
            }
            Count = bonds.Count;
            const int rings = 17, sides = 12;
            var vertices = new Vector3[Count * rings * sides];
            var uv = new Vector2[vertices.Length]; var uv2 = new Vector2[vertices.Length];
            var triangles = new int[Count * (rings - 1) * sides * 6]; int triangle = 0;
            for (int bond = 0; bond < Count; bond++)
            {
                var pair = bonds[bond]; float length = Vector3.Distance(rest[pair.x], rest[pair.y]);
                for (int ring = 0; ring < rings; ring++) for (int side = 0; side < sides; side++)
                {
                    int index = (bond * rings + ring) * sides + side;
                    float angle = side * Mathf.PI * 2 / sides;
                    vertices[index] = new Vector3(ring / (rings - 1f), Mathf.Cos(angle), Mathf.Sin(angle));
                    uv[index] = new Vector2(pair.x, pair.y); uv2[index] = new Vector2(length, bond * .618034f);
                    if (ring == rings - 1) continue;
                    int nextSide = (bond * rings + ring) * sides + (side + 1) % sides;
                    triangles[triangle++] = index; triangles[triangle++] = nextSide; triangles[triangle++] = index + sides;
                    triangles[triangle++] = nextSide; triangles[triangle++] = nextSide + sides; triangles[triangle++] = index + sides;
                }
            }
            m_Mesh = new Mesh { name = "Liquid polymer threads", indexFormat = IndexFormat.UInt32 };
            m_Mesh.vertices = vertices; m_Mesh.uv = uv; m_Mesh.uv2 = uv2; m_Mesh.triangles = triangles;
            m_Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * boundsSize); m_Mesh.UploadMeshData(true);
            var shader = Resources.Load<Shader>("Umbra08Filaments");
            if (!shader) throw new InvalidOperationException("Fluid filament shader is missing.");
            m_Material = new Material(shader) { name = "Wet tensile liquid threads" };
            m_Object = new GameObject("Sub-voxel liquid filaments"); m_Object.layer = 2; m_Object.transform.SetParent(parent, false);
            m_Object.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_Object.AddComponent<MeshRenderer>().sharedMaterial = m_Material;
        }

        public void Draw(ComputeBuffer particles, Vector3 gravity, Vector3 carrier, bool visible, SpiderFluidSettings settings,
            RenderTexture density, float threshold, float halfVolume, int volumeResolution)
        {
            visible &= settings.filamentDensity > 0;
            m_Object.SetActive(visible);
            if (!visible) return;
            settings.ApplyMaterial(m_Material);
            m_Material.SetTexture("_DensityTex", density);
            m_Material.SetFloat("_Threshold", threshold);
            m_Material.SetFloat("_HalfVolume", halfVolume);
            m_Material.SetFloat("_VolumeResolution", volumeResolution);
            m_Material.SetFloat("_ThreadDensity", settings.filamentDensity);
            m_Material.SetFloat("_RadiusMin", Mathf.Min(settings.minimumRadius, settings.maximumRadius));
            m_Material.SetFloat("_RadiusMax", Mathf.Max(settings.minimumRadius, settings.maximumRadius));
            m_Material.SetFloat("_RadiusVariation", settings.radiusVariation);
            m_Material.SetFloat("_Necking", settings.necking);
            m_Material.SetFloat("_RootPooling", settings.rootPooling);
            m_Material.SetFloat("_Sag", settings.sag);
            m_Material.SetFloat("_VelocityLag", settings.velocityLag);
            m_Material.SetFloat("_BreakLength", settings.minimumBreakLength);
            m_Material.SetFloat("_BreakStretch", settings.breakStretch);
            m_Material.SetBuffer("_Particles", particles); m_Material.SetVector("_Gravity", gravity.normalized);
            m_Material.SetVector("_CarrierVelocity", carrier);
        }

        public void Release() { Dispose(m_Object); Dispose(m_Mesh); Dispose(m_Material); }
        private static void Dispose(UnityEngine.Object obj)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj); }
    }
}
