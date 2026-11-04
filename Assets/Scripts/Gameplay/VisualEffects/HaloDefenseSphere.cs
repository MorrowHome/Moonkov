using System.Collections.Generic;
using Unity.MP_FPS.Client;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    // Twelve copies of the formal halo artwork, projected onto one sphere.
    // Four shared material batches; no lights, physics, particles or per-frame mesh rebuild.
    public sealed class HaloDefenseSphere : MonoBehaviour
    {
        private static Mesh s_Mesh;
        private static Material[] s_Materials;
        private static int s_Users;
        private MeshRenderer m_Renderer;
        private MaterialPropertyBlock m_Properties;
        private float m_Deployment;
        private bool m_Retained;
        public float Deployment => m_Deployment;
        public Renderer Surface => m_Renderer;

        public void Present(bool active, Vector3 head, Vector3 center, float radius, Color color,
            float energy, float hitAge, Vector3 hitPoint, bool firstPerson, float dt)
        {
            if (!active && m_Deployment <= 0) return;
            if (!m_Renderer && !Build()) return;
            m_Deployment = Mathf.MoveTowards(m_Deployment, active ? 1 : 0, Mathf.Max(0, dt) / (active ? .25f : .2f));
            float eased = m_Deployment * m_Deployment * (3 - 2 * m_Deployment);
            transform.position = Vector3.Lerp(head, center, eased);
            transform.rotation = Quaternion.identity;
            float size = Mathf.Lerp(.24f, radius, eased);
            var parentScale = transform.parent ? transform.parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(size / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
                size / Mathf.Max(.001f, Mathf.Abs(parentScale.y)), size / Mathf.Max(.001f, Mathf.Abs(parentScale.z)));
            m_Renderer.enabled = m_Deployment > 0;
            color.a = 1;
            m_Properties.SetColor("_HaloColor", color);
            m_Properties.SetVector("_HaloState", new Vector4(eased, Mathf.Clamp01(energy), Mathf.Max(0, hitAge), firstPerson ? 1 : 0));
            m_Properties.SetVector("_HaloHit", hitPoint);
            m_Renderer.SetPropertyBlock(m_Properties);
        }

        public void Clear()
        { m_Deployment = 0; if (m_Renderer) m_Renderer.enabled = false; }

        private bool Build()
        {
            if (!s_Mesh)
            {
                var art = Resources.Load<HaloProgressArtwork>("Moonkov/UI/HaloProgressArtwork");
                var shader = Resources.Load<Shader>("Moonkov/HaloDefense");
                if (!art || !shader || !shader.isSupported) return false;
                var sprites = new[] { art.Petal, art.OuterArc, art.InnerArc, art.Cell };
                foreach (var sprite in sprites) if (!sprite) return false;
                s_Mesh = CreateMesh(sprites);
                s_Materials = new Material[4];
                for (int i = 0; i < 4; i++)
                {
                    s_Materials[i] = new Material(shader) { name = "Halo defense artwork " + i, hideFlags = HideFlags.HideAndDontSave };
                    s_Materials[i].SetTexture("_MainTex", sprites[i].texture);
                }
            }
            s_Users++; m_Retained = true;
            gameObject.AddComponent<MeshFilter>().sharedMesh = s_Mesh;
            m_Renderer = gameObject.AddComponent<MeshRenderer>();
            m_Renderer.sharedMaterials = s_Materials;
            m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Renderer.lightProbeUsage = LightProbeUsage.Off;
            m_Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m_Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            m_Properties = new MaterialPropertyBlock();
            return true;
        }

        private static Mesh CreateMesh(Sprite[] sprites)
        {
            var vertices = new List<Vector3>(5000);
            var uv = new List<Vector2>(5000);
            var colors = new List<Color>(5000);
            var triangles = new List<int>[4];
            int[] counts = { 6, 6, 3, 30 };
            float[] radii = { .171f, .135f, .09f, .114f };
            float golden = (1 + Mathf.Sqrt(5)) / 2;
            var normals = new List<Vector3>(12);
            for (int a = -1; a <= 1; a += 2)
                for (int b = -1; b <= 1; b += 2)
                {
                    normals.Add(new Vector3(0, a, b * golden).normalized);
                    normals.Add(new Vector3(a, b * golden, 0).normalized);
                    normals.Add(new Vector3(b * golden, 0, a).normalized);
                }
            for (int layer = 0; layer < 4; layer++)
            {
                triangles[layer] = new List<int>();
                var sprite = sprites[layer];
                var rect = sprite.textureRect;
                Vector2 low = (sprite.textureRectOffset - sprite.pivot) / sprite.pixelsPerUnit;
                Vector2 span = rect.size / sprite.pixelsPerUnit;
                for (int face = 0; face < normals.Count; face++)
                {
                    Vector3 normal = normals[face];
                    Vector3 right = Vector3.Cross(Mathf.Abs(normal.y) > .9f ? Vector3.forward : Vector3.up, normal).normalized;
                    Vector3 up = Vector3.Cross(normal, right);
                    for (int piece = 0; piece < counts[layer]; piece++)
                    {
                        float angle = piece * Mathf.PI * 2 / counts[layer];
                        float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                        int first = vertices.Count;
                        // Subdivide the sprite twice so each motif follows the spherical surface.
                        for (int y = 0; y <= 2; y++)
                            for (int x = 0; x <= 2; x++)
                            {
                                Vector2 p = (low + Vector2.Scale(span, new Vector2(x * .5f, y * .5f))) * (layer == 3 ? .9f : 1);
                                p.x += radii[layer];
                                var rotated = new Vector2(c * p.x - s * p.y, s * p.x + c * p.y) * 2.7f;
                                vertices.Add((normal + right * rotated.x + up * rotated.y).normalized);
                                uv.Add(new Vector2((rect.xMin + rect.width * x * .5f) / sprite.texture.width,
                                    (rect.yMin + rect.height * y * .5f) / sprite.texture.height));
                                colors.Add(new Color(face / 12f, layer / 4f, piece / (float)counts[layer], 1));
                            }
                        for (int y = 0; y < 2; y++)
                            for (int x = 0; x < 2; x++)
                            {
                                int v = first + y * 3 + x;
                                triangles[layer].AddRange(new[] { v, v + 1, v + 4, v, v + 4, v + 3 });
                            }
                    }
                }
            }
            var mesh = new Mesh { name = "Twelve formal halos / spherical defense", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.subMeshCount = 4;
            for (int i = 0; i < 4; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateBounds(); mesh.UploadMeshData(true);
            return mesh;
        }

        private void OnDestroy()
        {
            if (!m_Retained) return;
            m_Retained = false;
            if (--s_Users > 0) return;
            DestroyOwned(s_Mesh); s_Mesh = null;
            if (s_Materials != null) foreach (var material in s_Materials) DestroyOwned(material);
            s_Materials = null;
        }
        private static void DestroyOwned(Object item)
        { if (!item) return; if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
    }
}
