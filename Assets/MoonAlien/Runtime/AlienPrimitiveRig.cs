using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    // Runtime-only own geometry: no imported assets, generated prefabs or editor import hooks.
    [RequireComponent(typeof(AlienGroundProbe), typeof(ProceduralAlienLegs))]
    public sealed class AlienPrimitiveRig : MonoBehaviour
    {
        private Material m_Material;
        private Mesh m_UpperTaper, m_LowerTaper;
        private GameObject m_VisualRoot;

        private void Start()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) { Debug.LogError("Alien sandbox requires URP/Lit.", this); enabled = false; return; }
            m_Material = new Material(shader) { name = "Alien sandbox black chitin (runtime)" };
            m_Material.SetColor("_BaseColor", new Color(.018f, .021f, .026f));
            m_Material.SetFloat("_Metallic", .2f);
            m_Material.SetFloat("_Smoothness", .42f);
            m_UpperTaper = CreateTaper(.4f);
            m_LowerTaper = CreateTaper(.015f);
            m_VisualRoot = new GameObject("Presentation only - no hitboxes");
            m_VisualRoot.transform.SetParent(transform, false);
            var body = new GameObject("Small central junction").transform;
            body.SetParent(m_VisualRoot.transform, false);
            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "Low black carapace";
            shell.transform.SetParent(body, false);
            shell.transform.localScale = new Vector3(.55f, .22f, .85f);
            var collider = shell.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            shell.GetComponent<Renderer>().sharedMaterial = m_Material;
            var legs = new ProceduralAlienLegs.Leg[4];
            for (int i = 0; i < legs.Length; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float front = i < 2 ? 1f : -1f;
                legs[i] = new ProceduralAlienLegs.Leg
                {
                    Hip = new Vector3(side * .22f, 0f, front * .3f),
                    RestFoot = new Vector3(side * 1.05f, 0f, front * .85f),
                    Upper = Segment("Leg " + i + " thick upper", m_UpperTaper),
                    Lower = Segment("Leg " + i + " pointed lower", m_LowerTaper),
                    Toe = new GameObject("Foot contact " + i).transform
                };
                legs[i].Toe.SetParent(m_VisualRoot.transform, false);
            }
            GetComponent<ProceduralAlienLegs>().Configure(body, GetComponent<AlienGroundProbe>(), legs);
        }

        private Transform Segment(string label, Mesh mesh)
        {
            var piece = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            piece.transform.SetParent(m_VisualRoot.transform, false);
            piece.GetComponent<MeshFilter>().sharedMesh = mesh;
            piece.GetComponent<MeshRenderer>().sharedMaterial = m_Material;
            return piece.transform;
        }

        private static Mesh CreateTaper(float tipRadius)
        {
            const int sides = 8;
            var vertices = new Vector3[sides * 2 + 2];
            var triangles = new int[sides * 12];
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                // Segment local +Y points toward the knee/foot, narrowing to a sharp tip.
                vertices[i] = new Vector3(Mathf.Cos(angle), -1f, Mathf.Sin(angle));
                vertices[i + sides] = new Vector3(Mathf.Cos(angle) * tipRadius, 1f, Mathf.Sin(angle) * tipRadius);
            }
            vertices[sides * 2] = Vector3.down;
            vertices[sides * 2 + 1] = Vector3.up;
            int cursor = 0;
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                triangles[cursor++] = i; triangles[cursor++] = i + sides; triangles[cursor++] = next;
                triangles[cursor++] = next; triangles[cursor++] = i + sides; triangles[cursor++] = next + sides;
                triangles[cursor++] = sides * 2; triangles[cursor++] = i; triangles[cursor++] = next;
                triangles[cursor++] = sides * 2 + 1; triangles[cursor++] = next + sides; triangles[cursor++] = i + sides;
            }
            var mesh = new Mesh { name = "Alien original tapered segment" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (m_VisualRoot) Destroy(m_VisualRoot);
            if (m_Material) Destroy(m_Material);
            if (m_UpperTaper) Destroy(m_UpperTaper);
            if (m_LowerTaper) Destroy(m_LowerTaper);
        }
    }
}
