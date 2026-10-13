using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>The same local continuous mouth/feeler paths feed solid geometry and
    /// particle seeding. No separate pale line material is introduced for these details.</summary>
    public sealed class CreatureHeadDetails
    {
        private readonly Transform m_Root;
        private readonly Mesh m_Mesh;
        private readonly Renderer m_Renderer;
        private Vector3[] m_Vertices, m_Normals;
        private List<Vector3[]> m_Paths;
        private CreatureChainSettings.HeadFeature m_Feature;
        private float m_Length;
        private Vector3 m_Head;
        private const int Sides = 10;

        public CreatureHeadDetails(Transform owner, Material material)
        {
            var go = new GameObject("Mouth rims and sensory appendages"); go.layer = 2; go.transform.SetParent(owner, false);
            m_Root = go.transform; m_Mesh = new Mesh { name = "Continuous mouth details" }; m_Mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_Renderer = go.AddComponent<MeshRenderer>(); m_Renderer.sharedMaterial = material;
        }

        public static List<Vector3[]> Paths(CreatureChainSettings s)
        {
            var paths = new List<Vector3[]>();
            if (s.headFeature == CreatureChainSettings.HeadFeature.Sucker)
            {
                var rim = new Vector3[25];
                for (int j = 0; j < rim.Length; j++)
                {
                    float angle = j / 24f * Mathf.PI * 2;
                    rim[j] = new Vector3(Mathf.Cos(angle) * s.headScale.x * .48f,
                        Mathf.Sin(angle) * s.headScale.y * .48f, s.headScale.z * .62f);
                }
                paths.Add(rim);
            }
            else if (s.headFeature == CreatureChainSettings.HeadFeature.Mandibles)
                for (int side = -1; side <= 1; side += 2)
                {
                    var jaw = new Vector3[13]; var antenna = new Vector3[17];
                    for (int j = 0; j < jaw.Length; j++)
                    {
                        float t = j / 12f;
                        jaw[j] = new Vector3(side * s.headScale.x * (.35f - .28f * t * t), -.035f,
                            s.headScale.z * .32f + .25f * t);
                    }
                    for (int j = 0; j < antenna.Length; j++)
                    {
                        float t = j / 16f;
                        antenna[j] = new Vector3(side * (.08f + s.feelerLength * .45f * t),
                            .04f + .10f * Mathf.Sin(t * Mathf.PI), s.headScale.z * .3f + s.feelerLength * t);
                    }
                    paths.Add(jaw); paths.Add(antenna);
                }
            return paths;
        }
        public static float Radius(CreatureChainSettings s, int path, float t) =>
            s.headFeature == CreatureChainSettings.HeadFeature.Sucker ? .035f : path % 2 == 0 ? Mathf.Lerp(.04f, .012f, t) : Mathf.Lerp(.025f, .009f, t);

        public void Draw(CreatureChainSettings s, Vector3 position, Quaternion rotation)
        {
            if (m_Paths == null || m_Feature != s.headFeature || m_Length != s.feelerLength || m_Head != s.headScale)
            {
                m_Paths = Paths(s); m_Feature = s.headFeature; m_Length = s.feelerLength; m_Head = s.headScale;
                int count = 0, triangles = 0;
                foreach (var path in m_Paths) { count += path.Length * (Sides + 1); triangles += (path.Length - 1) * Sides * 6; }
                m_Vertices = new Vector3[count]; m_Normals = new Vector3[count]; var indices = new int[triangles]; int offset = 0, n = 0;
                foreach (var path in m_Paths)
                {
                    for (int j = 0; j < path.Length - 1; j++) for (int side = 0; side < Sides; side++)
                    {
                        int a = offset + j * (Sides + 1) + side, b = a + Sides + 1;
                        indices[n++] = a; indices[n++] = b; indices[n++] = a + 1;
                        indices[n++] = a + 1; indices[n++] = b; indices[n++] = b + 1;
                    }
                    offset += path.Length * (Sides + 1);
                }
                m_Mesh.Clear(); m_Mesh.vertices = m_Vertices; m_Mesh.triangles = indices;
            }
            m_Root.SetPositionAndRotation(position, rotation); int vertex = 0;
            for (int p = 0; p < m_Paths.Count; p++)
            {
                var path = m_Paths[p];
                for (int j = 0; j < path.Length; j++)
                {
                    Vector3 axis = (path[Mathf.Min(j + 1, path.Length - 1)] - path[Mathf.Max(j - 1, 0)]).normalized;
                    Vector3 cross = Vector3.Cross(axis, Vector3.up).normalized;
                    if (cross.sqrMagnitude < .1f) cross = Vector3.right;
                    Vector3 up = Vector3.Cross(cross, axis);
                    for (int side = 0; side <= Sides; side++)
                    {
                        float angle = side / (float)Sides * Mathf.PI * 2;
                        Vector3 normal = cross * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                        m_Vertices[vertex] = path[j] + normal * Radius(s, p, j / (path.Length - 1f));
                        m_Normals[vertex++] = normal;
                    }
                }
            }
            if (vertex > 0) { m_Mesh.vertices = m_Vertices; m_Mesh.normals = m_Normals; m_Mesh.RecalculateBounds(); }
        }
        public void SetVisible(bool visible) { m_Renderer.enabled = visible; }
        public void Release() { if (Application.isPlaying) Object.Destroy(m_Mesh); else Object.DestroyImmediate(m_Mesh); }
    }
}
