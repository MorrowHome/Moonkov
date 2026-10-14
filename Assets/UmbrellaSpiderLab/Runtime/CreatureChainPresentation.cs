using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Owns generated surfaces, bristles and collider-local proleg anchors.
    /// The centreline drives the mesh; feet solve after it and cannot steer the head.</summary>
    public sealed class CreatureChainPresentation
    {
        private const int Sides = 16;
        private readonly Transform m_Root, m_Head;
        private readonly Mesh m_Tube, m_Bristles;
        private readonly Material m_Body, m_Belly, m_Accent;
        private readonly CreatureHeadDetails m_Details;
        private readonly Vector3[] m_Vertices = new Vector3[CreatureChain.Rings * (Sides + 1)];
        private readonly Vector3[] m_Normals = new Vector3[CreatureChain.Rings * (Sides + 1)];
        private readonly LineRenderer m_Tongue, m_Fork;
        private readonly List<Foot> m_Feet = new List<Foot>();
        private readonly List<Renderer> m_Renderers = new List<Renderer>();
        private bool m_Visible = true;
        private int m_ActiveFeet;
        private Vector3[] m_HairVertices;
        private int m_HairCount = -1;
        private readonly int m_Mask;
        public int PlantedFeet { get; private set; }
        public float MaximumAnchorSlip { get; private set; }
        private sealed class Foot
        {
            public Transform Upper, Lower;
            public Vector3 Position, Start, End, LocalAnchor;
            public Collider Surface;
            public float Elapsed;
            public bool Initialized, Swing;
            public Vector3 Root, Knee;
            public Vector3 PreviousRoot;
        }

        public CreatureChainPresentation(Transform owner, int mask)
        {
            m_Mask = mask;
            m_Root = new GameObject("Generated chain creature").transform;
            m_Root.SetParent(owner, false); m_Root.gameObject.layer = 2;
            m_Body = Material("Creature skin", new Color(.04f, .05f, .04f));
            m_Belly = Material("Creature underside", new Color(.15f, .12f, .08f));
            m_Accent = Material("Mouth and tongue", new Color(.10f, .017f, .012f));
            m_Tube = new Mesh { name = "Continuous procedural creature" }; m_Tube.MarkDynamic();
            var tube = new GameObject("Body / continuous surface"); tube.layer = 2; tube.transform.SetParent(m_Root, false);
            tube.AddComponent<MeshFilter>().sharedMesh = m_Tube;
            var tubeRenderer = tube.AddComponent<MeshRenderer>(); tubeRenderer.sharedMaterials = new[] { m_Body, m_Belly };
            m_Renderers.Add(tubeRenderer);
            var dorsal = new List<int>(); var belly = new List<int>();
            var uv = new Vector2[m_Vertices.Length];
            for (int ring = 0; ring < CreatureChain.Rings; ring++) for (int side = 0; side <= Sides; side++)
            {
                int a = ring * (Sides + 1) + side;
                uv[a] = new Vector2(side / (float)Sides, ring / (CreatureChain.Rings - 1f));
                if (ring == CreatureChain.Rings - 1 || side == Sides) continue;
                var indices = Mathf.Sin((side + .5f) / Sides * Mathf.PI * 2) < -.35f ? belly : dorsal;
                int b = a + Sides + 1;
                indices.Add(a); indices.Add(b); indices.Add(a + 1);
                indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
            }
            m_Tube.vertices = m_Vertices; m_Tube.uv = uv; m_Tube.subMeshCount = 2;
            m_Tube.SetTriangles(dorsal, 0); m_Tube.SetTriangles(belly, 1);
            m_Head = Primitive("Head", PrimitiveType.Sphere, m_Body);
            m_Details = new CreatureHeadDetails(m_Root, m_Body);
            m_Tongue = Line("Tongue", .012f); m_Fork = Line("Forked tip", .006f);
            m_Bristles = new Mesh { name = "Procedural larva bristles" }; m_Bristles.MarkDynamic();
            var hairs = new GameObject("Larva bristles"); hairs.layer = 2; hairs.transform.SetParent(m_Root, false);
            hairs.AddComponent<MeshFilter>().sharedMesh = m_Bristles;
            var hairRenderer = hairs.AddComponent<MeshRenderer>(); hairRenderer.sharedMaterial = m_Body; m_Renderers.Add(hairRenderer);
        }

        private static Material Material(string name, Color colour)
        {
            var result = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            result.SetColor("_BaseColor", colour); return result;
        }

        private Transform Primitive(string name, PrimitiveType type, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.layer = 2;
            go.transform.SetParent(m_Root, false); var collider = go.GetComponent<Collider>(); collider.enabled = false; Dispose(collider);
            var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.enabled = m_Visible; m_Renderers.Add(renderer); return go.transform;
        }

        private LineRenderer Line(string name, float width)
        {
            var go = new GameObject(name); go.layer = 2; go.transform.SetParent(m_Root, false);
            var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = m_Accent;
            line.positionCount = 3; line.widthMultiplier = width; line.useWorldSpace = true;
            m_Renderers.Add(line);
            return line;
        }

        public void Draw(CreatureChain chain, CreatureChainSettings s, bool caterpillar, float clock, float dt, bool detached = false)
        {
            m_Body.SetColor("_BaseColor", s.bodyColour); m_Belly.SetColor("_BaseColor", s.bellyColour);
            m_Body.SetFloat("_Smoothness", s.smoothness); m_Belly.SetFloat("_Smoothness", s.smoothness * .65f);
            m_Body.SetFloat("_Metallic", s.metallic); m_Belly.SetFloat("_Metallic", s.metallic);
            for (int ring = 0; ring < CreatureChain.Rings; ring++)
            {
                Vector3 up = chain.Ups[ring], tangent = chain.Tangents[ring];
                Vector3 right = Vector3.Cross(up, tangent).normalized;
                if (right.sqrMagnitude < .1f) right = m_Root.right;
                up = Vector3.Cross(tangent, right).normalized;
                for (int side = 0; side <= Sides; side++)
                {
                    float angle = side / (float)Sides * Mathf.PI * 2;
                    Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle) * s.verticalScale;
                    int index = ring * (Sides + 1) + side;
                    m_Vertices[index] = m_Root.InverseTransformPoint(chain.Centres[ring] + radial * chain.Radii[ring]);
                    Vector3 normal = right * Mathf.Cos(angle) + up * Mathf.Sin(angle) / s.verticalScale;
                    m_Normals[index] = m_Root.InverseTransformDirection(normal.normalized);
                }
            }
            m_Tube.vertices = m_Vertices; m_Tube.normals = m_Normals; m_Tube.RecalculateBounds();
            Vector3 head = chain.Centres[0], forward = chain.Tangents[0], headUp = chain.Ups[0];
            m_Head.SetPositionAndRotation(head + forward * .06f, Quaternion.LookRotation(forward, headUp));
            m_Head.localScale = s.headScale;
            m_Details.Draw(s, head + forward * .06f, Quaternion.LookRotation(forward, headUp));
            bool flick = !caterpillar && Mathf.Repeat(clock, 2.3f) < .20f;
            m_Tongue.enabled = m_Fork.enabled = flick && m_Visible;
            if (flick)
            {
                Vector3 start = head + forward * s.headScale.z * .45f - headUp * s.headScale.y * .10f;
                Vector3 tip = start + forward * .18f, right = Vector3.Cross(headUp, forward);
                m_Tongue.SetPosition(0, start); m_Tongue.SetPosition(1, tip); m_Tongue.SetPosition(2, tip + forward * .05f + right * .025f);
                m_Fork.SetPosition(0, tip); m_Fork.SetPosition(1, tip + forward * .04f - right * .018f);
                m_Fork.SetPosition(2, tip + forward * .05f - right * .025f);
            }
            DrawBristles(chain, s, caterpillar);
            DrawFeet(chain, s, caterpillar, dt, detached);
        }

        private void DrawBristles(CreatureChain chain, CreatureChainSettings s, bool enabled)
        {
            int count = enabled ? s.segments * s.bristlesPerSegment : 0;
            if (count != m_HairCount)
            {
                m_HairCount = count; m_HairVertices = new Vector3[count * 4];
                m_Bristles.Clear(); m_Bristles.vertices = m_HairVertices;
                var triangles = new int[count * 6];
                for (int i = 0; i < count; i++)
                { int a = i * 4, b = i * 6; triangles[b] = a; triangles[b + 1] = a + 1; triangles[b + 2] = a + 2;
                    triangles[b + 3] = a + 2; triangles[b + 4] = a + 1; triangles[b + 5] = a + 3; }
                m_Bristles.triangles = triangles;
            }
            for (int i = 0; i < count; i++)
            {
                int segment = i / s.bristlesPerSegment, side = i % s.bristlesPerSegment;
                int ring = Mathf.RoundToInt((segment + .5f) / s.segments * (CreatureChain.Rings - 1));
                float angle = Mathf.Lerp(-.2f, Mathf.PI + .2f, (side + .5f) / s.bristlesPerSegment);
                Vector3 tangent = chain.Tangents[ring], up = chain.Ups[ring], right = Vector3.Cross(up, tangent).normalized;
                Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                Vector3 start = chain.Centres[ring] + radial * chain.Radii[ring];
                float variation = .6f + .4f * Mathf.Sin(i * 17.7f) * Mathf.Sin(i * 17.7f);
                Vector3 end = start + (radial - tangent * .4f).normalized * s.bristleLength * variation;
                Vector3 thickness = tangent * .005f;
                int v = i * 4;
                m_HairVertices[v] = m_Root.InverseTransformPoint(start - thickness);
                m_HairVertices[v + 1] = m_Root.InverseTransformPoint(start + thickness);
                m_HairVertices[v + 2] = m_Root.InverseTransformPoint(end - thickness * .12f);
                m_HairVertices[v + 3] = m_Root.InverseTransformPoint(end + thickness * .12f);
            }
            if (count > 0) { m_Bristles.vertices = m_HairVertices; m_Bristles.RecalculateNormals(); m_Bristles.RecalculateBounds(); }
        }

        private void DrawFeet(CreatureChain chain, CreatureChainSettings s, bool enabled, float dt, bool folded)
        {
            int count = enabled ? s.legPairs * 2 : 0;
            m_ActiveFeet = count;
            while (m_Feet.Count < count) m_Feet.Add(new Foot
            {
                Upper = Primitive("Proleg upper", PrimitiveType.Cylinder, m_Belly),
                Lower = Primitive("Proleg claw", PrimitiveType.Cylinder, m_Belly)
            });
            PlantedFeet = 0; MaximumAnchorSlip = 0;
            for (int i = 0; i < m_Feet.Count; i++)
            {
                var foot = m_Feet[i]; foot.Upper.gameObject.SetActive(i < count); foot.Lower.gameObject.SetActive(i < count);
                if (i >= count) { foot.Initialized = false; continue; }
                float t = Mathf.Lerp(.12f, .91f, (i / 2) / (s.legPairs - 1f));
                int ring = Mathf.RoundToInt(t * (CreatureChain.Rings - 1));
                Vector3 up = chain.Ups[ring], forward = chain.Tangents[ring], side = Vector3.Cross(up, forward).normalized * (i % 2 == 0 ? -1 : 1);
                Vector3 root = chain.Centres[ring] + side * chain.Radii[ring] * .65f;
                if (folded)
                {
                    foot.Initialized = foot.Swing = false; foot.Surface = null;
                    foot.Root = root; foot.Position = root + side * .08f - up * .07f;
                    foot.Knee = Vector3.Lerp(root, foot.Position, .5f) + up * .025f;
                    Segment(foot.Upper, root, foot.Knee, s.legRadius);
                    Segment(foot.Lower, foot.Knee, foot.Position, s.legRadius * .7f);
                    continue;
                }
                Vector3 velocity = foot.Initialized && dt > .001f ? (root - foot.PreviousRoot) / dt : Vector3.zero;
                foot.PreviousRoot = root;
                Vector3 lead = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(velocity, up) * s.footPrediction, .24f);
                Vector3 home = root + side * s.legReach + forward * .06f + lead;
                bool grounded = Physics.Raycast(home + up * .4f, -up, out var ground, 1.2f, m_Mask, QueryTriggerInteraction.Ignore);
                Vector3 end = grounded ? ground.point + ground.normal * .02f : home - up * .15f;
                if (!foot.Initialized) { foot.Position = end; foot.Initialized = true; foot.Surface = ground.collider; foot.LocalAnchor = foot.Surface ? foot.Surface.transform.InverseTransformPoint(end) : end; }
                if (!foot.Swing && foot.Surface)
                {
                    Vector3 anchor = foot.Surface.transform.TransformPoint(foot.LocalAnchor);
                    foot.Position = anchor;
                }
                float phase = Mathf.Repeat((chain.Phase - t * s.length / s.wavelength * Mathf.PI * 2) / (Mathf.PI * 2) + (i % 2) * .5f, 1);
                if (!foot.Swing && grounded && chain.Drive > .05f &&
                    ((phase < .24f && Vector3.Distance(foot.Position, end) > .09f) || Vector3.Distance(foot.Position, root) > s.legReach + .25f))
                { foot.Start = foot.Position; foot.End = end; foot.Elapsed = 0; foot.Swing = true; foot.Surface = ground.collider; }
                if (foot.Swing)
                {
                    float duration = .30f / s.waveFrequency;
                    foot.Elapsed += dt; float progress = Mathf.Clamp01(foot.Elapsed / duration);
                    foot.Position = Vector3.Lerp(foot.Start, foot.End, Mathf.SmoothStep(0, 1, progress)) + up * Mathf.Sin(progress * Mathf.PI) * s.footLift;
                    if (progress >= 1) { foot.Swing = false; foot.LocalAnchor = foot.Surface ? foot.Surface.transform.InverseTransformPoint(foot.End) : foot.End; }
                }
                else if (foot.Surface)
                {
                    PlantedFeet++;
                    MaximumAnchorSlip = Mathf.Max(MaximumAnchorSlip, Vector3.Distance(foot.Position, foot.Surface.transform.TransformPoint(foot.LocalAnchor)));
                }
                Vector3 knee = Vector3.Lerp(root, foot.Position, .45f) + up * .035f + side * .04f;
                foot.Root = root; foot.Knee = knee;
                Segment(foot.Upper, root, knee, s.legRadius); Segment(foot.Lower, knee, foot.Position, s.legRadius * .7f);
            }
        }

        private static void Segment(Transform segment, Vector3 a, Vector3 b, float radius)
        {
            Vector3 direction = b - a; segment.position = (a + b) * .5f;
            if (direction.sqrMagnitude > .000001f) segment.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            segment.localScale = new Vector3(radius * 2, direction.magnitude * .5f, radius * 2);
        }

        public void Release()
        {
            m_Root.gameObject.SetActive(false);
            m_Details.Release();
            Dispose(m_Root.gameObject); Dispose(m_Tube); Dispose(m_Bristles); Dispose(m_Body); Dispose(m_Belly); Dispose(m_Accent);
        }
        public void ResetContacts()
        {
            foreach (var foot in m_Feet) { foot.Initialized = foot.Swing = false; foot.Surface = null; }
        }
        public int CopyLegPoints(Vector3[] points)
        {
            for (int i = 0; i < m_ActiveFeet; i++)
            { points[i * 3] = m_Feet[i].Root; points[i * 3 + 1] = m_Feet[i].Knee; points[i * 3 + 2] = m_Feet[i].Position; }
            return m_ActiveFeet;
        }
        public void SetVisible(bool visible)
        {
            if (m_Visible == visible) return;
            m_Visible = visible;
            m_Details.SetVisible(visible);
            foreach (var renderer in m_Renderers) if (renderer) renderer.enabled = visible;
        }
        private static void Dispose(Object obj) { if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
    }
}
