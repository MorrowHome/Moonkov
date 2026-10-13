using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Disposable primitive rig. Replace this layer with authored art after motion acceptance.</summary>
    public sealed class SpiderPresentation
    {
        public enum Stage { Body, Feet, Skeleton, Umbrella }
        public enum Look { Legacy, Carapace, Liquid, Fluid }
        private readonly Transform m_Root, m_Canopy, m_Body;
        private readonly Transform[] m_Upper = new Transform[8], m_Lower = new Transform[8],
            m_Knees = new Transform[8], m_Toes = new Transform[8];
        private readonly Renderer[] m_ToeRenderers = new Renderer[8];
        private readonly List<Material> m_Materials = new List<Material>();
        private readonly Material m_Stance, m_Swing, m_Recovery;
        private readonly Mesh m_Mesh;
        private readonly Mesh m_LegacySegment, m_LegacyJoint, m_ArmoredUpper, m_ArmoredLower, m_JointGuard;
        private readonly Transform m_SkinShell;
        private readonly Material[] m_LegacyMaterials, m_SkinMaterials;
        private readonly List<Mesh> m_SkinMeshes = new List<Mesh>();
        private readonly List<SkinSample> m_SkinSamples = new List<SkinSample>();
        private Stage m_Stage = Stage.Umbrella;
        public Look Appearance { get; private set; } = Look.Legacy;
        public bool DebugContacts { get; private set; }
        private Vector3 m_Tilt, m_TiltVelocity, m_PreviousVelocity;
        private readonly int m_Mask;
        private LiquidSpiderPresentation m_Liquid;
        private SkeletonFluidPresentation m_Fluid;
        private Mesh m_LiquidProbe;
        public LiquidSpiderPresentation Liquid => m_Liquid;
        public SkeletonFluidPresentation Fluid => m_Fluid;
        public float MaxContactError { get; private set; }
        public int LegObstructions { get; private set; }

        public SpiderPresentation(Transform owner, int mask)
        {
            m_Mask = mask;
            m_Root = new GameObject("Generated spider presentation").transform;
            m_Root.SetParent(owner, false);
            var metal = Material("Oxidized ribs", new Color(.21f, .25f, .26f), .45f);
            var cloth = Material("Canopy / plum", new Color(.29f, .035f, .095f), .07f);
            var lining = Material("Canopy / charcoal", new Color(.09f, .032f, .055f), .05f);
            m_Stance = Material("Contact / planted", new Color(.17f, .9f, .68f), .15f);
            m_Swing = Material("Contact / swinging", new Color(1, .62f, .16f), .15f);
            m_Recovery = Material("Contact / recovery", new Color(1, .12f, .2f), .15f);
            m_Body = Primitive("Umbrella heart", PrimitiveType.Sphere, m_Root, metal);
            m_Body.localScale = new Vector3(.45f, .29f, .45f);
            for (int i = 0; i < 8; i++)
            {
                m_Upper[i] = Primitive("Rib " + i + " upper", PrimitiveType.Cylinder, m_Root, metal);
                m_Lower[i] = Primitive("Rib " + i + " lower", PrimitiveType.Cylinder, m_Root, metal);
                m_Knees[i] = Primitive("Rib " + i + " joint", PrimitiveType.Sphere, m_Root, metal);
                m_Knees[i].localScale = Vector3.one * .115f;
                m_Toes[i] = Primitive("Contact " + i, PrimitiveType.Sphere, m_Root, m_Stance);
                m_Toes[i].localScale = Vector3.one * .085f;
                m_ToeRenderers[i] = m_Toes[i].GetComponent<Renderer>();
            }
            m_LegacySegment = m_Upper[0].GetComponent<MeshFilter>().sharedMesh;
            m_LegacyJoint = m_Knees[0].GetComponent<MeshFilter>().sharedMesh;
            m_LegacyMaterials = new[] { metal };
            m_Canopy = new GameObject("Canopy inertia (no leg children)").transform;
            m_Canopy.SetParent(m_Root, false);
            m_Canopy.gameObject.layer = 2;
            m_Mesh = CanopyMesh();
            m_Canopy.gameObject.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_Canopy.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { cloth, lining };
            var finial = Primitive("Finial", PrimitiveType.Sphere, m_Canopy, metal);
            finial.localPosition = Vector3.up * .58f;
            finial.localScale = new Vector3(.10f, .18f, .10f);
            for (int i = 0; i < 8; i++)
            {
                var rib = Primitive("Canopy seam " + i, PrimitiveType.Cylinder, m_Canopy, metal);
                Segment(rib, m_Canopy.TransformPoint(Vector3.up * .5f),
                    m_Canopy.TransformPoint(PredictiveFeet.Radial(i) * .63f + Vector3.up * .16f), .015f);
            }
            var armor = Material("Carapace / matte charcoal", new Color(.082f, .089f, .094f), .23f);
            armor.SetFloat("_Smoothness", .18f);
            var recess = Material("Carapace / deep recess", new Color(.025f, .032f, .035f), .08f);
            recess.SetFloat("_Smoothness", .11f);
            var edge = Material("Carapace / exposed folded edge", new Color(.13f, .14f, .145f), .34f);
            edge.SetFloat("_Smoothness", .30f);
            var membrane = Material("Carapace / protected membrane", new Color(.04f, .065f, .06f, .43f), .03f);
            membrane.SetFloat("_Surface", 1); membrane.SetFloat("_Blend", 0); membrane.SetFloat("_ZWrite", 0);
            membrane.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            membrane.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            membrane.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            membrane.SetOverrideTag("RenderType", "Transparent"); membrane.renderQueue = 3000;
            m_SkinMaterials = new[] { armor, recess, edge };
            m_SkinShell = new GameObject("Folded carapace inertia (no leg children)").transform;
            m_SkinShell.SetParent(m_Root, false); m_SkinShell.gameObject.layer = 2;
            m_SkinShell.localPosition = Vector3.up * .075f;
            SkinPart("Eight incomplete shell plates", m_SkinShell, Own(SpiderSkinGeometry.Shell()), m_SkinMaterials);
            SkinPart("Coxal wraps and internal ribs", m_SkinShell, Own(SpiderSkinGeometry.Understructure()), m_SkinMaterials);
            var membranePart = SkinPart("Protected internal membrane", m_SkinShell,
                Own(SpiderSkinGeometry.Membrane()), new[] { membrane, membrane, membrane });
            membranePart.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            m_ArmoredUpper = Own(SpiderSkinGeometry.ArmoredSegment(true));
            m_ArmoredLower = Own(SpiderSkinGeometry.ArmoredSegment(false));
            m_JointGuard = Own(SpiderSkinGeometry.JointCuff());
            for (int i = 0; i < 8; i++)
            {
                m_SkinSamples.Add(new SkinSample("upper " + i, m_Upper[i], m_ArmoredUpper, false));
                m_SkinSamples.Add(new SkinSample("lower " + i, m_Lower[i], m_ArmoredLower, true));
                m_SkinSamples.Add(new SkinSample("joint " + i, m_Knees[i], m_JointGuard, false));
            }
            ApplyVisibility();
        }

        private Mesh Own(Mesh mesh) { m_SkinMeshes.Add(mesh); return mesh; }

        private Transform SkinPart(string name, Transform parent, Mesh mesh, Material[] materials)
        {
            var go = new GameObject(name); go.layer = 2; go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            m_SkinSamples.Add(new SkinSample(name, go.transform, mesh, false));
            return go.transform;
        }

        /// <summary>Changes meshes/materials only; the solved rig and collider-local anchors are untouched.</summary>
        public void SetAppearance(Look look, bool debugContacts)
        {
            if (Appearance == look && DebugContacts == debugContacts) return;
            if (Appearance != look)
            {
                Appearance = look;
                if ((look == Look.Liquid || look == Look.Fluid) && m_Liquid == null) m_Liquid = new LiquidSpiderPresentation(m_Root, m_Mask);
                if (look == Look.Fluid && m_Fluid == null) m_Fluid = new SkeletonFluidPresentation(m_Root, m_Mask);
                bool skin = look == Look.Carapace;
                for (int i = 0; i < 8; i++)
                {
                    m_Upper[i].GetComponent<MeshFilter>().sharedMesh = skin ? m_ArmoredUpper : m_LegacySegment;
                    m_Lower[i].GetComponent<MeshFilter>().sharedMesh = skin ? m_ArmoredLower : m_LegacySegment;
                    m_Knees[i].GetComponent<MeshFilter>().sharedMesh = skin ? m_JointGuard : m_LegacyJoint;
                    m_Upper[i].GetComponent<Renderer>().sharedMaterials = skin ? m_SkinMaterials : m_LegacyMaterials;
                    m_Lower[i].GetComponent<Renderer>().sharedMaterials = skin ? m_SkinMaterials : m_LegacyMaterials;
                    m_Knees[i].GetComponent<Renderer>().sharedMaterials = skin ? m_SkinMaterials : m_LegacyMaterials;
                }
            }
            DebugContacts = debugContacts;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            bool skin = Appearance == Look.Carapace;
            bool liquid = Appearance == Look.Liquid || Appearance == Look.Fluid;
            m_Body.gameObject.SetActive((!skin && !liquid) || m_Stage == Stage.Body);
            m_Canopy.gameObject.SetActive(!skin && !liquid && m_Stage == Stage.Umbrella);
            m_Liquid?.SetVisible(Appearance == Look.Liquid && m_Stage == Stage.Umbrella);
            m_Fluid?.SetVisible(Appearance == Look.Fluid && m_Stage == Stage.Umbrella);
            m_SkinShell.gameObject.SetActive(skin && m_Stage != Stage.Body);
            for (int i = 0; i < 8; i++)
            {
                m_Toes[i].gameObject.SetActive(DebugContacts && m_Stage >= Stage.Feet);
                bool bones = m_Stage >= Stage.Skeleton && (!liquid || m_Stage == Stage.Skeleton);
                m_Upper[i].gameObject.SetActive(bones); m_Lower[i].gameObject.SetActive(bones);
                m_Knees[i].gameObject.SetActive(bones);
            }
        }

        private Material Material(string name, Color color, float metallic)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            var material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", .32f);
            material.SetFloat("_Cull", 0);
            if (name.StartsWith("Contact"))
            {
                material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * .45f);
            }
            m_Materials.Add(material);
            return material;
        }

        private static Transform Primitive(string name, PrimitiveType type, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.layer = 2;
            go.transform.SetParent(parent, false);
            var collider = go.GetComponent<Collider>(); collider.enabled = false; Dispose(collider);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        public void Draw(SurfaceMotor motor, PredictiveFeet feet, Stage stage, float dt)
        {
            var pose = feet.BodyPose;
            m_Root.SetPositionAndRotation(pose.Position, pose.Rotation);
            if (m_Stage != stage) { m_Stage = stage; ApplyVisibility(); }
            Vector3 acceleration = (motor.Velocity - m_PreviousVelocity) / Mathf.Max(.001f, dt);
            m_PreviousVelocity = motor.Velocity;
            Vector3 localAcceleration = Quaternion.Inverse(pose.Rotation) * Vector3.ClampMagnitude(acceleration, 35);
            Vector3 targetTilt = new Vector3(-localAcceleration.z, 0, localAcceleration.x) * .18f;
            // Only cloth gets delayed motion; body, hips and foot anchors retain the same render time.
            int substeps = Mathf.Max(1, Mathf.CeilToInt(dt / .01f));
            float h = dt / substeps;
            for (int j = 0; j < substeps; j++)
            {
                m_TiltVelocity += ((targetTilt - m_Tilt) * 180 - m_TiltVelocity * 23) * h;
                m_Tilt += m_TiltVelocity * h;
            }
            m_Canopy.localRotation = Quaternion.Euler(m_Tilt);
            m_Canopy.localScale = Vector3.Lerp(new Vector3(1.35f, 1, 1.35f),
                new Vector3(1.18f, 1.20f, 1.15f), Mathf.Clamp01(feet.RunIntensity));
            m_SkinShell.localRotation = Quaternion.Euler(m_Tilt * .65f);
            m_SkinShell.localScale = Vector3.Lerp(Vector3.one, new Vector3(.98f, .94f, .96f), Mathf.Clamp01(feet.RunIntensity));
            LegObstructions = 0;
            MaxContactError = 0;
            if (Appearance == Look.Liquid || Appearance == Look.Fluid) m_Liquid.BeginFrame(motor, feet, dt);
            for (int i = 0; i < 8; i++)
            {
                var foot = feet[i];
                m_Toes[i].position = foot.Position;
                m_ToeRenderers[i].sharedMaterial = foot.State == PredictiveFeet.ContactState.Swing ? m_Swing :
                    foot.State == PredictiveFeet.ContactState.Stance ? m_Stance : m_Recovery;
                Vector3 hip = PredictiveFeet.Hip(pose, i);
                bool blocked = !SpiderLegKinematics.Solve(pose, i, foot.Position, foot.Normal, m_Mask,
                    out var knee, out var tip);
                Segment(m_Upper[i], hip, knee, .035f); Segment(m_Lower[i], knee, tip, .024f);
                m_Knees[i].position = knee;
                Vector3 upperAxis = (knee - hip).normalized;
                Vector3 opening = Vector3.ProjectOnPlane(tip - knee, upperAxis).normalized;
                if (opening.sqrMagnitude > .001f) m_Knees[i].rotation = Quaternion.LookRotation(opening, upperAxis);
                if (foot.State == PredictiveFeet.ContactState.Stance)
                    MaxContactError = Mathf.Max(MaxContactError, Vector3.Distance(tip, foot.Position));
                // These are diagnostics, not claims of full joint collision solving.
                if (blocked) LegObstructions++;
                if (Appearance == Look.Liquid || Appearance == Look.Fluid) m_Liquid.SetLeg(i, hip, knee, tip);
            }
            if (Appearance == Look.Fluid) m_Fluid.Draw(m_Liquid, motor, dt);
        }

        private static void Segment(Transform t, Vector3 from, Vector3 to, float thickness)
        {
            t.position = (from + to) * .5f;
            t.rotation = Quaternion.FromToRotation(Vector3.up, to - from);
            t.localScale = new Vector3(thickness * 2, Vector3.Distance(from, to) * .5f, thickness * 2);
        }

        private static Mesh CanopyMesh()
        {
            var vertices = new List<Vector3>(); var plum = new List<int>(); var dark = new List<int>();
            for (int wedge = 0; wedge < 8; wedge++)
            for (int ring = 0; ring < 5; ring++)
            for (int slice = 0; slice < 5; slice++)
            {
                float r0 = ring / 5f, r1 = (ring + 1) / 5f;
                float a0 = (wedge + slice / 5f) * Mathf.PI / 4 + 22.5f * Mathf.Deg2Rad;
                float a1 = (wedge + (slice + 1) / 5f) * Mathf.PI / 4 + 22.5f * Mathf.Deg2Rad;
                int start = vertices.Count;
                vertices.Add(CanopyPoint(r0, a0)); vertices.Add(CanopyPoint(r1, a0));
                vertices.Add(CanopyPoint(r1, a1)); vertices.Add(CanopyPoint(r0, a1));
                var tris = wedge % 2 == 0 ? plum : dark;
                tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
                tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
            }
            var mesh = new Mesh { name = "Eight-panel experimental canopy", subMeshCount = 2 };
            mesh.SetVertices(vertices); mesh.SetTriangles(plum, 0); mesh.SetTriangles(dark, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 CanopyPoint(float r, float angle)
        {
            float scallop = 1 - .045f * Mathf.Pow(Mathf.Sin((angle - 22.5f * Mathf.Deg2Rad) * 4), 2) * r;
            return new Vector3(Mathf.Sin(angle) * r * .68f * scallop,
                .52f - .34f * r * r, Mathf.Cos(angle) * r * .68f * scallop);
        }

        public void Release()
        {
            m_Liquid?.Release(); Dispose(m_LiquidProbe);
            m_Fluid?.Release();
            Dispose(m_Root.gameObject); Dispose(m_Mesh);
            foreach (var material in m_Materials) Dispose(material);
            foreach (var mesh in m_SkinMeshes) Dispose(mesh);
        }

        private sealed class SkinSample
        {
            public readonly string Name;
            public readonly Transform Transform;
            public readonly Vector3[] Points;
            public readonly Vector2Int[] Edges;

            public SkinSample(string name, Transform transform, Mesh mesh, bool contactTip)
            {
                Name = name; Transform = transform;
                var source = mesh.vertices; var triangles = mesh.triangles;
                var indices = new Dictionary<Vector3, int>(); var points = new List<Vector3>();
                var edges = new HashSet<Vector2Int>();
                for (int i = 0; i < triangles.Length; i += 3)
                for (int j = 0; j < 3; j++)
                {
                    var a = source[triangles[i + j]];
                    var b = source[triangles[i + (j + 1) % 3]];
                    // The last 9 cm of a lower leg is the intended contact collar, not a wall fault.
                    if (contactTip)
                    {
                        if (a.y > .85f && b.y > .85f) continue;
                        if (a.y > .85f) a = Vector3.Lerp(a, b, (a.y - .85f) / (a.y - b.y));
                        if (b.y > .85f) b = Vector3.Lerp(b, a, (b.y - .85f) / (b.y - a.y));
                    }
                    if (!indices.TryGetValue(a, out int ai)) { ai = points.Count; indices.Add(a, ai); points.Add(a); }
                    if (!indices.TryGetValue(b, out int bi)) { bi = points.Count; indices.Add(b, bi); points.Add(b); }
                    if (ai != bi) edges.Add(new Vector2Int(Mathf.Min(ai, bi), Mathf.Max(ai, bi)));
                }
                Points = points.ToArray(); Edges = new Vector2Int[edges.Count]; edges.CopyTo(Edges);
            }
        }

        /// <summary>
        /// Validation-only queries of the ACTUAL skin vertices and edges against environment colliders.
        /// Checks both directions so an edge entering a closed proxy is seen even if its start is inside.
        /// This is sampled mesh clearance, not a replacement for the motor or a physical skin collider.
        /// </summary>
        public bool SkinObstructed(out string part)
        {
            part = null;
            if (Appearance == Look.Liquid)
            {
                if (!m_LiquidProbe) m_LiquidProbe = new Mesh { name = "Liquid clearance probe" };
                return m_Liquid.SkinObstructed(m_LiquidProbe, m_Mask, out part);
            }
            if (Appearance != Look.Carapace) return false;
            foreach (var sample in m_SkinSamples)
            {
                if (!sample.Transform.gameObject.activeInHierarchy) continue;
                var matrix = sample.Transform.localToWorldMatrix;
                foreach (var local in sample.Points)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(local);
                    var overlap = UnityEngine.Physics.OverlapSphere(point, .002f, m_Mask, QueryTriggerInteraction.Ignore);
                    if (overlap.Length > 0)
                    { part = sample.Name + " vertex at " + point + " / " + overlap[0].name; return true; }
                }
                foreach (var edge in sample.Edges)
                {
                    Vector3 a = matrix.MultiplyPoint3x4(sample.Points[edge.x]);
                    Vector3 b = matrix.MultiplyPoint3x4(sample.Points[edge.y]);
                    Vector3 delta = b - a; float distance = delta.magnitude;
                    if (distance < .008f) continue;
                    Vector3 direction = delta / distance;
                    if (UnityEngine.Physics.Raycast(a + direction * .003f, direction, out var hit, distance - .006f,
                        m_Mask, QueryTriggerInteraction.Ignore) || UnityEngine.Physics.Raycast(b - direction * .003f,
                        -direction, out hit, distance - .006f, m_Mask, QueryTriggerInteraction.Ignore))
                    { part = sample.Name + " edge at " + hit.point + " / " + hit.collider.name +
                        " / local " + matrix.inverse.MultiplyPoint3x4(hit.point) + " / body " + m_Root.position; return true; }
                }
            }
            return false;
        }

        private static void Dispose(Object obj)
        {
            if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj);
        }
    }
}
