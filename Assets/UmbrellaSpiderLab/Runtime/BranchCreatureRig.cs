using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Continuous appendage surfaces and their actual guide bones. Contacts live
    /// in collider-local space; both the solid mesh and the fluid consume these same poses.
    /// This rig owns presentation/contact state, never the surface motor's body position.</summary>
    public sealed class BranchCreatureRig
    {
        public const int Samples = 17;
        private const int Sides = 12;
        private static readonly Vector3[] Circle = CreateCircle();

        private static Vector3[] CreateCircle()
        {
            var circle = new Vector3[Sides + 1];
            for (int side = 0; side <= Sides; side++)
            {
                float angle = side / (float)Sides * Mathf.PI * 2;
                circle[side] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
            }
            return circle;
        }
        private readonly Transform m_Root, m_Body;
        private readonly Mesh m_Mesh;
        private readonly Material m_Material;
        private readonly Renderer m_Renderer, m_BodyRenderer;
        private readonly Vector3[] m_Vertices, m_Normals;
        private readonly Contact[] m_Contacts;
        private readonly int m_Mask;
        private readonly BranchCreatureSettings m_Shape;
        private readonly ProceduralMenagerieExperiment.Species m_Kind;
        private readonly float[] m_Growth;
        private bool m_Initialized;
        private readonly bool m_Render;
        public readonly Vector3[] Rest, Points;
        public readonly float[] RestRadii, Radii;
        public readonly Quaternion[] Rotations;
        public readonly int[] Parents;
        public readonly Matrix4x4[] RestInverse;
        public int Arms { get; }
        public int Branches { get; }
        public int BoneCount => Points.Length;
        public Vector3 BodyScale { get; private set; } = Vector3.one;
        public Vector3 RestBodyRadii { get; }
        public int Grips { get; private set; }
        public int ContactRecoveries { get; private set; }
        public float MaximumAnchorSlip { get; private set; }
        public float MaximumExtension { get; private set; }
        public int ObstructedTips { get; private set; }

        private sealed class Contact
        {
            public Collider Surface;
            public Vector3 Local, Position, Start;
            public bool Initialized, Stance;
        }

        public bool Matches(BranchCreatureSettings s, ProceduralMenagerieExperiment.Species kind) =>
            kind == m_Kind && s.arms == Arms && s.reach == m_Shape.reach &&
            s.rootRadius == m_Shape.rootRadius && s.tipRadius == m_Shape.tipRadius && s.bodyRadii == RestBodyRadii;

        public BranchCreatureRig(Transform owner, int mask, BranchCreatureSettings s, ProceduralMenagerieExperiment.Species kind, bool render = true)
        {
            m_Render = render;
            m_Kind = kind; m_Mask = mask; Arms = Mathf.Clamp(s.arms, 3, 8);
            bool roots = kind == ProceduralMenagerieExperiment.Species.Roots;
            Branches = roots ? Arms + Mathf.Min(Arms, 6) : Arms;
            m_Shape = new BranchCreatureSettings { reach = s.reach, rootRadius = s.rootRadius, tipRadius = s.tipRadius };
            RestBodyRadii = new Vector3(Mathf.Max(.08f, s.bodyRadii.x), Mathf.Max(.08f, s.bodyRadii.y), Mathf.Max(.08f, s.bodyRadii.z));
            int bones = 1 + Branches * Samples;
            Rest = new Vector3[bones]; Points = new Vector3[bones]; Rotations = new Quaternion[bones];
            RestRadii = new float[bones]; Radii = new float[bones]; Parents = new int[Branches];
            RestInverse = new Matrix4x4[bones]; m_Contacts = new Contact[Arms]; m_Growth = new float[Branches];
            RestInverse[0] = Matrix4x4.identity; RestRadii[0] = RestBodyRadii.x;
            for (int a = 0; a < Arms; a++)
            {
                m_Contacts[a] = new Contact(); Parents[a] = -1;
                Vector3 side = Direction(a);
                for (int j = 0; j < Samples; j++)
                {
                    float t = j / (Samples - 1f);
                    Rest[Index(a, j)] = Vector3.Scale(side, RestBodyRadii) * .6f + side * s.reach * t -
                        Vector3.up * RestBodyRadii.y * .65f + Vector3.up * Mathf.Sin(t * Mathf.PI) * .12f;
                    RestRadii[Index(a, j)] = Mathf.Lerp(s.rootRadius, s.tipRadius, Mathf.Pow(t, .7f));
                }
                m_Growth[a] = roots ? .42f + .035f * a : 1;
            }
            if (roots)
                for (int a = Arms; a < Branches; a++)
                {
                    int parent = a - Arms; Parents[a] = parent;
                    Vector3 root = Rest[Index(parent, 9)], side = Direction(parent);
                    Vector3 cross = Vector3.Cross(Vector3.up, side) * (parent % 2 == 0 ? 1 : -1);
                    for (int j = 0; j < Samples; j++)
                    {
                        float t = j / (Samples - 1f);
                        Rest[Index(a, j)] = root + (cross * .7f + side * .3f) * s.reach * .48f * t + Vector3.up * .10f * Mathf.Sin(t * Mathf.PI);
                        RestRadii[Index(a, j)] = Mathf.Lerp(s.rootRadius * .55f, s.tipRadius * .75f, t);
                    }
                    m_Growth[a] = .22f;
                }
            for (int a = 0; a < Branches; a++) for (int j = 0; j < Samples; j++)
            {
                int index = Index(a, j);
                Vector3 axis = Rest[Index(a, Mathf.Min(j + 1, Samples - 1))] - Rest[Index(a, Mathf.Max(j - 1, 0))];
                RestInverse[index] = Matrix4x4.TRS(Rest[index], Quaternion.LookRotation(axis.normalized, Vector3.up), Vector3.one).inverse;
            }
            m_Root = new GameObject("Continuous branch rig / " + kind).transform;
            m_Root.SetParent(owner, false); m_Root.gameObject.layer = 2;
            if (!m_Render) return;
            m_Material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Menagerie solid rollback skin" };
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere); body.name = "Central mantle"; body.layer = 2;
            body.transform.SetParent(m_Root, false); body.GetComponent<Collider>().enabled = false; Dispose(body.GetComponent<Collider>());
            m_Body = body.transform; m_BodyRenderer = body.GetComponent<Renderer>(); m_BodyRenderer.sharedMaterial = m_Material;
            var surface = new GameObject("Continuous curved appendages"); surface.layer = 2; surface.transform.SetParent(m_Root, false);
            m_Mesh = new Mesh { name = "Branched creature surface", indexFormat = IndexFormat.UInt32 }; m_Mesh.MarkDynamic();
            m_Vertices = new Vector3[Branches * Samples * (Sides + 1)]; m_Normals = new Vector3[m_Vertices.Length];
            var indices = new int[Branches * (Samples - 1) * Sides * 6]; int n = 0;
            for (int a = 0; a < Branches; a++) for (int j = 0; j < Samples - 1; j++) for (int side = 0; side < Sides; side++)
            {
                int i = (a * Samples + j) * (Sides + 1) + side, k = i + Sides + 1;
                indices[n++] = i; indices[n++] = k; indices[n++] = i + 1;
                indices[n++] = i + 1; indices[n++] = k; indices[n++] = k + 1;
            }
            m_Mesh.vertices = m_Vertices; m_Mesh.triangles = indices;
            surface.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_Renderer = surface.AddComponent<MeshRenderer>(); m_Renderer.sharedMaterial = m_Material;
        }

        public static int Index(int arm, int point) => 1 + arm * Samples + point;
        private Vector3 Direction(int arm)
        {
            float angle = (arm + .5f) / Arms * Mathf.PI * 2;
            return new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
        }

        public void Tick(SurfaceMotor motor, BranchCreatureSettings s, float clock, float drive, float action, float dt)
        {
            var pose = motor.Current; Vector3 up = pose.Up;
            bool slime = m_Kind == ProceduralMenagerieExperiment.Species.Slime;
            bool roots = m_Kind == ProceduralMenagerieExperiment.Species.Roots;
            Vector3 body = pose.Position - up * (motor.BodyHeight - RestBodyRadii.y * (slime ? .88f : 1.05f));
            float stretch = 1 + drive * s.bodyStretch * (slime ? 1 : .35f);
            float flatten = slime ? 1 - action * .5f : 1;
            BodyScale = new Vector3(1 / Mathf.Sqrt(stretch * flatten), flatten / Mathf.Sqrt(stretch), stretch);
            Points[0] = body; Rotations[0] = pose.Rotation;
            Grips = ObstructedTips = 0; MaximumAnchorSlip = MaximumExtension = 0;
            for (int a = 0; a < Arms; a++)
            {
                Vector3 radial = pose.Rotation * Direction(a), sideways = Vector3.Cross(up, radial);
                Vector3 root = body + pose.Rotation * Vector3.Scale(Direction(a), Vector3.Scale(RestBodyRadii, BodyScale)) * .6f - up * RestBodyRadii.y * .5f;
                float phase = Mathf.Repeat(clock * s.frequency * Mathf.Lerp(.2f, 1, drive) + a * .618034f, 1);
                bool stance = !motor.Detached && (drive < .02f || phase < s.stanceFraction);
                var contact = m_Contacts[a];
                Vector3 predicted = pose.Position + radial * s.reach * (slime ? .9f : 1) + Vector3.ClampMagnitude(motor.Velocity * s.prediction, .75f);
                Vector3 goal = predicted - up * (motor.BodyHeight - .05f * motor.Size);
                if (motor.Detached) goal = root + radial * s.reach * .28f + up * .12f;
                bool hasSurface = false;
                if (!motor.Detached && Physics.Raycast(predicted + up * (.6f * motor.Size), -up, out var ground, 2 * motor.Size, m_Mask, QueryTriggerInteraction.Ignore))
                {
                    hasSurface = true;
                    goal = ground.point + ground.normal * .05f;
                    if (!contact.Initialized || (stance && !contact.Stance))
                    { contact.Surface = ground.collider; contact.Local = ground.collider.transform.InverseTransformPoint(goal); }
                }
                if (contact.Initialized && stance && contact.Surface)
                {
                    goal = contact.Surface.transform.TransformPoint(contact.Local);
                    if (Vector3.Distance(root, goal) > s.reach * 1.6f)
                    { stance = false; contact.Surface = null; ContactRecoveries++; }
                }
                if (!contact.Initialized) contact.Position = contact.Start = goal;
                if (!stance)
                {
                    if (contact.Stance) contact.Start = contact.Position;
                    float t = Mathf.InverseLerp(s.stanceFraction, 1, phase);
                    goal = Vector3.Lerp(contact.Start, goal, Mathf.SmoothStep(0, 1, t)) + up * Mathf.Sin(t * Mathf.PI) * s.lift;
                    contact.Position = Vector3.Lerp(contact.Position, goal, 1 - Mathf.Exp(-s.follow * dt));
                }
                else contact.Position = goal;
                if (stance && hasSurface && contact.Surface)
                {
                    Grips++; MaximumAnchorSlip = Mathf.Max(MaximumAnchorSlip,
                        Vector3.Distance(contact.Position, contact.Surface.transform.TransformPoint(contact.Local)));
                }
                contact.Initialized = true; contact.Stance = stance;
                Vector3 toe = contact.Position;
                // A grip cannot be behind a cabinet: sweep the tip above its support plane.
                Vector3 delta = toe - root;
                if (delta.sqrMagnitude > .01f && Physics.SphereCast(root + up * .10f, .035f, delta.normalized,
                    out var obstruction, delta.magnitude, m_Mask, QueryTriggerInteraction.Ignore) && obstruction.collider != contact.Surface)
                { toe = root + delta.normalized * Mathf.Max(.05f, obstruction.distance - .05f); ObstructedTips++; }
                MaximumExtension = Mathf.Max(MaximumExtension, Vector3.Distance(root, toe) / s.reach);
                m_Growth[a] = Mathf.MoveTowards(m_Growth[a], 1, dt * s.growthRate);
                for (int j = 0; j < Samples; j++)
                {
                    float t = j / (Samples - 1f), arch = Mathf.Sin(t * Mathf.PI);
                    Vector3 point = Vector3.Lerp(root, toe, t * (roots ? m_Growth[a] : 1));
                    float curl = s.curl * Mathf.Pow(t, 3) * (stance ? .25f : 1);
                    point += up * arch * (slime ? .04f : .12f + s.lift * .3f) + sideways * Mathf.Sin(t * Mathf.PI * 2 + a) * arch * curl;
                    if (action > 0 && Vector3.Dot(radial, pose.Forward) > 0)
                    {
                        if (roots) point += sideways * Mathf.Sin(t * Mathf.PI * 3) * action * .3f + up * Mathf.Sin(t * Mathf.PI) * action * .35f;
                        else point += pose.Forward * action * s.actionExtension * t + up * action * s.lift * arch;
                    }
                    int index = Index(a, j); Points[index] = point;
                    float taper = Mathf.Lerp(s.rootRadius, s.tipRadius, Mathf.Pow(t, .7f));
                    float pooling = slime ? 1 + .32f * Mathf.Sin(clock * s.frequency * 6.28f - t * 5 + a) * drive : 1;
                    Radii[index] = taper * pooling * (roots ? Mathf.Lerp(.60f, 1, m_Growth[a]) : 1);
                }
            }
            for (int a = Arms; a < Branches; a++)
            {
                int parent = Parents[a]; Vector3 root = Points[Index(parent, 9)], radial = pose.Rotation * Direction(parent);
                Vector3 side = Vector3.Cross(up, radial) * (parent % 2 == 0 ? 1 : -1);
                m_Growth[a] = Mathf.MoveTowards(m_Growth[a], 1, dt * s.growthRate);
                for (int j = 0; j < Samples; j++)
                {
                    float t = j / (Samples - 1f), length = s.reach * .48f * m_Growth[a];
                    Vector3 point = root + (side * .7f + radial * .3f) * length * t;
                    point += up * Mathf.Sin(t * Mathf.PI) * (.10f + action * .35f) + radial * Mathf.Sin(t * Mathf.PI * (2 + action * 2)) * t * s.curl * (.3f + action);
                    int index = Index(a, j); Points[index] = point;
                    Radii[index] = Mathf.Lerp(s.rootRadius * .55f, s.tipRadius * .75f, t) * Mathf.Lerp(.60f, 1, m_Growth[a]);
                }
            }
            if (m_Render)
            {
                m_Material.SetColor("_BaseColor", s.colour); m_Material.SetFloat("_Smoothness", s.smoothness);
                m_Body.SetPositionAndRotation(body, pose.Rotation); m_Body.localScale = Vector3.Scale(RestBodyRadii * 2, BodyScale);
            }
            Matrix4x4 worldToLocal = m_Render ? m_Root.worldToLocalMatrix : Matrix4x4.identity;
            Quaternion inverseRotation = m_Render ? Quaternion.Inverse(m_Root.rotation) : Quaternion.identity;
            for (int a = 0; a < Branches; a++) for (int j = 0; j < Samples; j++)
            {
                int bone = Index(a, j); Vector3 axis = Points[Index(a, Mathf.Min(j + 1, Samples - 1))] - Points[Index(a, Mathf.Max(j - 1, 0))];
                if (axis.sqrMagnitude < .000001f) axis = pose.Forward;
                Vector3 normalUp = Vector3.ProjectOnPlane(up, axis.normalized).normalized;
                if (normalUp.sqrMagnitude < .1f) normalUp = pose.Right;
                Rotations[bone] = Quaternion.LookRotation(axis, normalUp);
                if (!m_Render) continue;
                float ripple = 1 + s.ripple * Mathf.Sin(j * .9f + clock * 3 + a * 1.3f);
                for (int side = 0; side <= Sides; side++)
                {
                    Vector3 radial = Rotations[bone] * Circle[side];
                    if (slime) radial = Vector3.ProjectOnPlane(radial, up) + up * Vector3.Dot(radial, up) * .48f;
                    int vertex = (a * Samples + j) * (Sides + 1) + side;
                    m_Vertices[vertex] = worldToLocal.MultiplyPoint3x4(Points[bone] + radial * Radii[bone] * ripple);
                    m_Normals[vertex] = inverseRotation * radial.normalized;
                }
            }
            if (m_Render) { m_Mesh.vertices = m_Vertices; m_Mesh.normals = m_Normals; m_Mesh.RecalculateBounds(); }
            m_Initialized = true;
        }

        public void ResetContacts()
        {
            foreach (var contact in m_Contacts) { contact.Initialized = contact.Stance = false; contact.Surface = null; }
        }
        public void SetVisible(bool visible) { if (m_Render) m_Renderer.enabled = m_BodyRenderer.enabled = visible && m_Initialized; }
        public void Release() { m_Root.gameObject.SetActive(false); Dispose(m_Root.gameObject); Dispose(m_Mesh); Dispose(m_Material); }
        private static void Dispose(Object obj) { if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
    }
}
