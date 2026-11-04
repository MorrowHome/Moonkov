using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    // Twelve copies of the formal halo artwork, projected onto one sphere.
    // One shared mesh, atlas and material; changing weapons only updates a property block.
    public sealed class HaloDefenseSphere : MonoBehaviour
    {
        private static Mesh s_Mesh;
        private static Material s_Material;
        private static int s_Users;
        private MeshRenderer m_Renderer;
        private MaterialPropertyBlock m_Properties;
        private float m_Deployment;
        private bool m_Retained;
        private HaloAppearanceCatalog m_Appearance;
        public float Deployment => m_Deployment;
        public Renderer Surface => m_Renderer;

        public void Present(bool active, Vector3 head, Vector3 center, float radius, Color color,
            float energy, float hitAge, Vector3 hitPoint, bool firstPerson, float dt, uint weapon = DollSingerWeapons.Halo)
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
            m_Properties.SetVector("_HaloArtworkRect", m_Appearance.ForWeapon(weapon).ArtworkRect);
            m_Properties.SetVector("_HaloState", new Vector4(eased, Mathf.Clamp01(energy), Mathf.Max(0, hitAge), firstPerson ? 1 : 0));
            m_Properties.SetVector("_HaloHit", hitPoint);
            m_Renderer.SetPropertyBlock(m_Properties);
        }

        public void Clear()
        { m_Deployment = 0; if (m_Renderer) m_Renderer.enabled = false; }

        private bool Build()
        {
            m_Appearance = HaloAppearanceCatalog.Current;
            if (!m_Appearance || !m_Appearance.Artwork) return false;
            if (!s_Mesh)
            {
                var shader = Resources.Load<Shader>("Moonkov/HaloDefense");
                if (!shader || !shader.isSupported) return false;
                s_Mesh = CreateMesh();
                s_Material = new Material(shader) { name = "Equipped halo defense artwork", hideFlags = HideFlags.HideAndDontSave };
                s_Material.SetTexture("_MainTex", m_Appearance.Artwork);
            }
            s_Users++; m_Retained = true;
            gameObject.AddComponent<MeshFilter>().sharedMesh = s_Mesh;
            m_Renderer = gameObject.AddComponent<MeshRenderer>();
            m_Renderer.sharedMaterial = s_Material;
            m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            m_Renderer.lightProbeUsage = LightProbeUsage.Off;
            m_Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            m_Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            m_Properties = new MaterialPropertyBlock();
            return true;
        }

        private static Mesh CreateMesh()
        {
            const int divisions = 8;
            var vertices = new List<Vector3>(972);
            var uv = new List<Vector2>(972);
            var colors = new List<Color>(972);
            var triangles = new List<int>(4608);
            float golden = (1 + Mathf.Sqrt(5)) / 2;
            var normals = new List<Vector3>(12);
            for (int a = -1; a <= 1; a += 2)
                for (int b = -1; b <= 1; b += 2)
                {
                    normals.Add(new Vector3(0, a, b * golden).normalized);
                    normals.Add(new Vector3(a, b * golden, 0).normalized);
                    normals.Add(new Vector3(b * golden, 0, a).normalized);
                }
            for (int face = 0; face < normals.Count; face++)
            {
                Vector3 normal = normals[face];
                Vector3 right = Vector3.Cross(Mathf.Abs(normal.y) > .9f ? Vector3.forward : Vector3.up, normal).normalized;
                Vector3 up = Vector3.Cross(normal, right);
                int first = vertices.Count;
                // Curve the entire formal assembly around the sphere, preserving its silhouette.
                for (int y = 0; y <= divisions; y++)
                    for (int x = 0; x <= divisions; x++)
                    {
                        var point = new Vector2(x / (float)divisions, y / (float)divisions);
                        var offset = (point - Vector2.one * .5f) * 1.5f;
                        vertices.Add((normal + right * offset.x + up * offset.y).normalized);
                        uv.Add(point); colors.Add(new Color(face / 12f, 0, 0, 1));
                    }
                for (int y = 0; y < divisions; y++)
                    for (int x = 0; x < divisions; x++)
                {
                    int v = first + y * (divisions + 1) + x;
                    triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + divisions + 2);
                    triangles.Add(v); triangles.Add(v + divisions + 2); triangles.Add(v + divisions + 1);
                }
            }
            var mesh = new Mesh { name = "Twelve formal halos / spherical defense", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); mesh.UploadMeshData(true);
            return mesh;
        }

        private void OnDestroy()
        {
            if (!m_Retained) return;
            m_Retained = false;
            if (--s_Users > 0) return;
            DestroyOwned(s_Mesh); s_Mesh = null;
            DestroyOwned(s_Material); s_Material = null;
        }
        private static void DestroyOwned(Object item)
        { if (!item) return; if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
    }
}
