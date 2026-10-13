using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    // Client-only shell. One shared mesh/finish set serves every authored cache;
    // no colliders, loot rules, transforms or network state belong to this view.
    internal sealed class RaidSupplyCacheVisuals : IDisposable
    {
        private const int Paint = 0, Polymer = 1, Metal = 2, Hazard = 3;
        private readonly Mesh m_Body, m_Indicator;
        private readonly Material[] m_Finishes;
        private readonly Material m_Ready, m_Empty;
        private readonly Texture2D m_PaintGrain;
        private readonly MeshRenderer[] m_Indicators;
        private readonly bool[] m_EmptyState;

        public RaidSupplyCacheVisuals(Shader shader, int count)
        {
            m_Indicators = new MeshRenderer[count]; m_EmptyState = new bool[count];
            m_PaintGrain = PaintTexture();
            m_Finishes = new[]
            {
                Finish(shader, "Cache / ceramic field paint", new Color(.68f, .66f, .57f), .12f, .27f),
                Finish(shader, "Cache / graphite seals", new Color(.045f, .057f, .059f), .1f, .24f),
                Finish(shader, "Cache / exposed alloy", new Color(.34f, .38f, .39f), .78f, .48f),
                Finish(shader, "Cache / safety ochre", new Color(.73f, .38f, .055f), .15f, .3f)
            };
            m_Finishes[Paint].SetTexture("_BaseMap", m_PaintGrain);
            m_Ready = Finish(shader, "Cache / stocked indicator", new Color(.36f, .67f, .55f), .15f, .4f);
            m_Ready.SetColor("_EmissionColor", new Color(.24f, .65f, .42f) * 1.8f);
            m_Ready.EnableKeyword("_EMISSION");
            m_Empty = Finish(shader, "Cache / depleted indicator", new Color(.2f, .12f, .045f), .1f, .25f);

            var mesh = new ShellBuilder(4);
            // Preserve the original x/z envelope and root position. Authoring puts roots
            // .35 m above the floor, so only the skid feet extend down to that contact plane.
            mesh.Box(new Vector3(0, -.035f, 0), new Vector3(1.08f, .49f, .76f), .055f, Paint);
            mesh.Box(new Vector3(0, .229f, 0), new Vector3(1.08f, .024f, .77f), .024f, Polymer);
            mesh.Box(new Vector3(0, .293f, 0), new Vector3(1.12f, .102f, .80f), .035f, Paint);
            mesh.Box(new Vector3(0, .347f, 0), new Vector3(.79f, .027f, .50f), .012f, Metal);
            mesh.Box(new Vector3(0, .362f, 0), new Vector3(.65f, .012f, .39f), .005f, Polymer);

            // Protective skid rails and hard corner shoes make the silhouette readable at a distance.
            for (int x = -1; x <= 1; x += 2)
            {
                mesh.Box(new Vector3(x * .435f, -.29f, 0), new Vector3(.16f, .12f, .80f), .018f, Polymer);
                mesh.Box(new Vector3(x * .435f, .327f, 0), new Vector3(.09f, .086f, .82f), .017f, Metal);
                foreach (int z in new[] { -1, 1 })
                {
                    mesh.Box(new Vector3(x * .50f, -.065f, z * .342f), new Vector3(.14f, .40f, .15f), .032f, Polymer);
                    mesh.Box(new Vector3(x * .513f, .143f, z * .352f), new Vector3(.10f, .105f, .13f), .017f, Metal);
                    mesh.Box(new Vector3(x * .36f, .313f, z * .404f), new Vector3(.095f, .06f, .014f), .005f, Hazard);
                }
                // End handles sit over a dark recess, with a visible gap beneath the metal grip.
                mesh.Box(new Vector3(x * .545f, .025f, 0), new Vector3(.012f, .175f, .35f), .005f, Polymer);
                mesh.Box(new Vector3(x * .562f, .08f, 0), new Vector3(.014f, .039f, .29f), .006f, Metal);
                for (int z = -1; z <= 1; z += 2)
                    mesh.Box(new Vector3(x * .559f, .027f, z * .139f), new Vector3(.018f, .135f, .039f), .007f, Metal);
            }
            for (int z = -1; z <= 1; z += 2)
            {
                mesh.Box(new Vector3(0, -.055f, z * .383f), new Vector3(.70f, .245f, .018f), .009f, Polymer);
                mesh.Box(new Vector3(0, -.063f, z * .395f), new Vector3(.62f, .178f, .010f), .004f, Paint);
                for (int x = -1; x <= 1; x += 2)
                {
                    mesh.Box(new Vector3(x * .272f, .19f, z * .398f), new Vector3(.09f, .19f, .033f), .01f, Polymer);
                    mesh.Box(new Vector3(x * .272f, .193f, z * .419f), new Vector3(.064f, .125f, .002f), .0008f, Metal);
                    mesh.Box(new Vector3(x * .272f, .158f, z * .416f), new Vector3(.088f, .025f, .006f), .002f, Hazard);
                }
                // Small recessed equipment plate, with raised bars in place of tiny unreadable text.
                mesh.Box(new Vector3(0, .128f, z * .389f), new Vector3(.18f, .11f, .02f), .008f, Metal);
                mesh.Box(new Vector3(0, .147f, z * .403f), new Vector3(.12f, .021f, .006f), .002f, Polymer);
                for (int bar = 0; bar < 5; bar++)
                    mesh.Box(new Vector3(-.043f + bar * .021f, .10f, z * .403f), new Vector3(.006f, .025f, .006f), .002f, Polymer);
            }
            m_Body = mesh.Build("Raid supply cache / shared chamfered shell");
            var light = new ShellBuilder(1);
            light.Box(new Vector3(0, .147f, -.408f), new Vector3(.105f, .012f, .005f), .002f, 0);
            light.Box(new Vector3(0, .147f, .408f), new Vector3(.105f, .012f, .005f), .002f, 0);
            m_Indicator = light.Build("Raid supply cache / shared status inset");
        }

        public GameObject Create(Transform parent, Vector3 position, int index)
        {
            var root = new GameObject("Supply cache " + (index + 1).ToString("00"));
            root.transform.SetParent(parent, false); root.transform.position = position;
            root.AddComponent<MeshFilter>().sharedMesh = m_Body;
            var body = root.AddComponent<MeshRenderer>();
            body.sharedMaterials = m_Finishes;
            body.shadowCastingMode = ShadowCastingMode.On;
            var inset = new GameObject("Cache status inset"); inset.transform.SetParent(root.transform, false);
            inset.AddComponent<MeshFilter>().sharedMesh = m_Indicator;
            var indicator = inset.AddComponent<MeshRenderer>();
            indicator.sharedMaterial = m_Ready;
            indicator.shadowCastingMode = ShadowCastingMode.Off;
            m_Indicators[index] = indicator;
            return root;
        }

        public void SetEmpty(int index, bool empty)
        {
            if (m_EmptyState[index] == empty) return;
            m_EmptyState[index] = empty;
            m_Indicators[index].sharedMaterial = empty ? m_Empty : m_Ready;
        }

        private static Material Finish(Shader shader, string name, Color color, float metallic, float smoothness)
        {
            var material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private static Texture2D PaintTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            { name = "Cache / shared paint grain", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    uint hash = unchecked((uint)(x * 73856093 ^ y * 19349663));
                    hash = (hash ^ (hash >> 13)) * 1274126177u;
                    float noise = (hash & 255) / 255f;
                    int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    float grain = .92f + noise * .08f;
                    if (edge < 3 && noise > .63f) grain = .40f + noise * .16f;
                    byte shade = (byte)(grain * 255);
                    pixels[y * size + x] = new Color32(shade, shade, shade, 255);
                }
            texture.SetPixels32(pixels); texture.Apply(true, true);
            return texture;
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(m_Body); UnityEngine.Object.Destroy(m_Indicator);
            foreach (var material in m_Finishes) UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(m_Ready); UnityEngine.Object.Destroy(m_Empty);
            UnityEngine.Object.Destroy(m_PaintGrain);
        }

        private sealed class ShellBuilder
        {
            private readonly List<Vector3> m_Vertices = new List<Vector3>();
            private readonly List<Vector3> m_Normals = new List<Vector3>();
            private readonly List<Vector2> m_Uvs = new List<Vector2>();
            private readonly List<int>[] m_Triangles;

            public ShellBuilder(int materials)
            {
                m_Triangles = new List<int>[materials];
                for (int i = 0; i < materials; i++) m_Triangles[i] = new List<int>();
            }

            public void Box(Vector3 center, Vector3 size, float bevel, int material)
            {
                Vector3 half = size * .5f;
                float b = Mathf.Min(bevel, Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * .8f);
                Vector3 inner = half - Vector3.one * b;
                for (int axis = 0; axis < 3; axis++)
                {
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 normal = Vector3.zero; normal[axis] = sign;
                        Vector3 a = normal * half[axis], du = Vector3.zero, dv = Vector3.zero;
                        du[u] = inner[u]; dv[v] = inner[v];
                        Face(center, normal, material, a - du - dv, a + du - dv, a + du + dv, a - du + dv);
                    }
                    for (int su = -1; su <= 1; su += 2)
                        for (int sv = -1; sv <= 1; sv += 2)
                        {
                            Vector3 a = Vector3.zero, bPoint = Vector3.zero, along = Vector3.zero, normal = Vector3.zero;
                            a[u] = su * half[u]; a[v] = sv * inner[v];
                            bPoint[u] = su * inner[u]; bPoint[v] = sv * half[v];
                            along[axis] = inner[axis]; normal[u] = su; normal[v] = sv;
                            Face(center, normal.normalized, material, a - along, bPoint - along, bPoint + along, a + along);
                        }
                }
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            Face(center, new Vector3(x, y, z).normalized, material,
                                new Vector3(x * half.x, y * inner.y, z * inner.z),
                                new Vector3(x * inner.x, y * half.y, z * inner.z),
                                new Vector3(x * inner.x, y * inner.y, z * half.z));
            }

            private void Face(Vector3 center, Vector3 normal, int material, params Vector3[] points)
            {
                int start = m_Vertices.Count;
                bool reverse = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) < 0;
                for (int i = 0; i < points.Length; i++)
                {
                    m_Vertices.Add(center + points[i]); m_Normals.Add(normal);
                    m_Uvs.Add(new Vector2(i == 1 || i == 2 ? 1 : 0, i >= 2 ? 1 : 0));
                }
                for (int i = 1; i < points.Length - 1; i++)
                {
                    m_Triangles[material].Add(start);
                    m_Triangles[material].Add(start + (reverse ? i + 1 : i));
                    m_Triangles[material].Add(start + (reverse ? i : i + 1));
                }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = m_Triangles.Length };
                mesh.SetVertices(m_Vertices); mesh.SetNormals(m_Normals); mesh.SetUVs(0, m_Uvs);
                for (int i = 0; i < m_Triangles.Length; i++) mesh.SetTriangles(m_Triangles[i], i);
                mesh.RecalculateBounds(); mesh.UploadMeshData(true);
                return mesh;
            }
        }
    }
}
