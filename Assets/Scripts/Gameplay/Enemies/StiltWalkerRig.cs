using System;
using System.Collections.Generic;
using Unity.MP_FPS.SpiderLab;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS
{
    /// <summary>Upright, planted biped shared by render and authoritative hit volumes.
    /// Long limbs and a narrow torso are separate proportions, not a stretched crawler.</summary>
    public sealed class StiltWalkerRig : IDisposable
    {
        public const int Segments = 24;
        public readonly Vector3[] A = new Vector3[Segments], B = new Vector3[Segments];
        public readonly float[] Radii = new float[Segments];
        public Vector3 Chest { get; private set; }
        public Vector3 Head { get; private set; }
        public Quaternion HeadRotation { get; private set; }
        public Vector3 HeadRadii => new Vector3(.105f * m_Scale, .06f * Height, .105f * m_Scale);
        public readonly float Height, Thickness, CollisionRadius;
        private readonly AlienCreatureProfile m_Profile;
        private readonly float m_Scale;
        private readonly int m_Mask;
        private readonly Vector3[] m_Feet = new Vector3[2];
        private Vector3 m_Previous, m_StepStart, m_StepEnd;
        private int m_Swing = -1, m_NextFoot;
        private float m_StepAge;
        private bool m_Ready;
        private readonly GameObject m_Root;
        private readonly Transform m_Head, m_MaskFace;
        private readonly Mesh m_Mesh, m_HeadMesh;
        private readonly Material m_Skin, m_Bone;
        private readonly List<Vector3> m_Vertices = new List<Vector3>(700);
        private readonly List<int> m_Triangles = new List<int>(3000);

        public StiltWalkerRig(Transform owner, AlienCreatureProfile profile, float scale, bool render, int mask)
        {
            m_Profile = profile; m_Scale = scale; m_Mask = mask;
            Height = profile.UprightHeight(scale); Thickness = profile.StiltThickness * scale; CollisionRadius = profile.UprightRadius(scale);
            if (!render) return;
            m_Root = new GameObject("Stilt walker / narrow torso and planted legs"); m_Root.transform.SetParent(owner, false);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            m_Skin = new Material(shader) { name = "Stilt walker sinew", hideFlags = HideFlags.DontSave };
            m_Skin.SetColor("_BaseColor", new Color(.105f, .085f, .075f)); m_Skin.SetFloat("_Smoothness", .32f);
            m_Bone = new Material(shader) { name = "Stilt walker facial shield", hideFlags = HideFlags.DontSave };
            m_Bone.SetColor("_BaseColor", new Color(.39f, .34f, .27f)); m_Bone.SetFloat("_Smoothness", .2f);
            m_Mesh = new Mesh { name = "Stilt walker articulated body", hideFlags = HideFlags.DontSave }; m_Mesh.MarkDynamic();
            MeshObject("Exposed spine ribs and limbs", m_Mesh, m_Skin);
            m_HeadMesh = BuildHead();
            m_Head = MeshObject("Long faceless skull", m_HeadMesh, m_Skin).transform;
            m_MaskFace = MeshObject("Narrow bone face", m_HeadMesh, m_Bone).transform;
        }

        public void Tick(SurfaceMotor motor, AlienAction action, float age, float windup, float strike, float clock, float dt)
        {
            var pose = motor.Current; Vector3 up = Vector3.up, forward = pose.Forward, right = pose.Right;
            Vector3 ground = pose.Position - up * motor.BodyHeight;
            if (!m_Ready || Vector3.Distance(m_Previous, ground) > Height)
            {
                for (int i = 0; i < 2; i++) m_Feet[i] = Foot(ground + right * ((i == 0 ? -1 : 1) * .23f * m_Scale));
                m_Ready = true; m_Swing = -1;
            }
            m_Previous = ground;
            bool walking = motor.Velocity.sqrMagnitude > .01f && action != AlienAction.Dead;
            float stepSeconds = Mathf.Max(.25f, m_Profile.StiltStepSeconds);
            if (m_Swing < 0 && walking)
            {
                int foot = m_NextFoot;
                Vector3 desired = ground + right * ((foot == 0 ? -1 : 1) * .23f * m_Scale);
                if (Vector3.Distance(desired, m_Feet[foot]) > m_Profile.StiltStride * m_Scale * .32f)
                {
                    m_Swing = foot; m_StepAge = 0; m_StepStart = m_Feet[foot];
                    m_StepEnd = Foot(desired + forward * m_Profile.StiltStride * m_Scale * .55f);
                }
            }
            if (m_Swing >= 0)
            {
                m_StepAge += dt;
                float t = Mathf.Clamp01(m_StepAge / stepSeconds), ease = t * t * (3 - 2 * t);
                m_Feet[m_Swing] = Vector3.Lerp(m_StepStart, m_StepEnd, ease) + up * (Mathf.Sin(t * Mathf.PI) * .14f * m_Scale);
                if (t >= 1) { m_NextFoot = 1 - m_Swing; m_Swing = -1; }
            }
            float load = action == AlienAction.Windup ? Mathf.Clamp01(age / Mathf.Max(.01f, windup)) : 0;
            float rake = action == AlienAction.Strike ? Mathf.Sin(Mathf.Clamp01(age / Mathf.Max(.01f, strike)) * Mathf.PI) : 0;
            float death = action == AlienAction.Dead ? Mathf.Clamp01(age / .8f) : 0;
            float sway = Mathf.Sin(clock * 1.3f) * .018f * Height;
            Vector3 hip = ground + up * (Height * Mathf.Lerp(.53f, .12f, death)) + right * sway;
            Chest = hip + up * (Height * .20f * (1 - death)) + forward * ((load * .12f + rake * .13f + death * .25f) * Height);
            Head = Chest + up * (Height * .21f * (1 - death)) + forward * (.055f * Height) + right * (.035f * Height);
            Vector3 neck = Vector3.Lerp(Chest, Head, .65f);
            int segment = 0;
            Set(ref segment, hip, Chest, Thickness * 1.05f);
            Set(ref segment, Chest, neck, Thickness * .62f);
            Set(ref segment, neck, Head, Thickness * .5f);
            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? -1 : 1;
                Vector3 leg = hip + right * side * .14f * m_Scale;
                Vector3 knee = Vector3.Lerp(leg, m_Feet[sideIndex], .50f) - forward * (.16f * Height) + right * side * .045f * Height;
                Set(ref segment, leg, knee, Thickness * .8f);
                Set(ref segment, knee, m_Feet[sideIndex] + up * .05f, Thickness * .53f);
                Set(ref segment, m_Feet[sideIndex], m_Feet[sideIndex] + forward * .24f * m_Scale, Thickness * .6f);
                Vector3 shoulder = Chest + right * side * .22f * m_Scale;
                Vector3 elbow = shoulder - up * Height * (.19f - load * .2f) - forward * load * .3f * Height + right * side * .11f * Height;
                Vector3 hand = shoulder - up * Height * (.38f - load * .29f) + forward * (.04f + rake * .62f) * Height;
                hand += up * (-rake * .18f * Height) + right * side * (.04f + .08f * rake) * Height;
                Set(ref segment, Chest, shoulder, Thickness);
                Set(ref segment, shoulder, elbow, Thickness * .68f);
                Set(ref segment, elbow, hand, Thickness * .48f);
                for (int digit = 0; digit < 3; digit++)
                {
                    Vector3 tip = hand + forward * (.15f + digit * .035f) * m_Scale - up * .22f * m_Scale + right * (digit - 1) * .065f * m_Scale;
                    Set(ref segment, hand, tip, Thickness * .22f);
                }
            }
            // Three pairs of exposed ribs give a recognisable chest above eye level.
            for (int rib = 0; rib < 3; rib++)
            {
                Vector3 spine = Vector3.Lerp(hip, Chest, .5f + rib * .18f);
                Set(ref segment, spine - right * (.12f + rib * .025f) * m_Scale, spine + right * (.12f + rib * .025f) * m_Scale + forward * .045f, Thickness * .35f);
            }
            if (death > 0) for (int i = 0; i < Segments; i++)
            {
                float minimum = ground.y + Radii[i] + .01f;
                A[i].y = Mathf.Max(minimum, A[i].y); B[i].y = Mathf.Max(minimum, B[i].y);
            }
            HeadRotation = Quaternion.LookRotation(forward, up) * Quaternion.Euler(8 + rake * 12, -10, -12);
            if (!m_Root || !m_Root.activeSelf) return;
            Draw();
            var rotation = HeadRotation;
            m_Head.SetPositionAndRotation(Head, rotation); m_Head.localScale = HeadRadii;
            m_MaskFace.SetPositionAndRotation(Head + forward * .075f * m_Scale, rotation);
            m_MaskFace.localScale = new Vector3(.075f * m_Scale, .052f * Height, .055f * m_Scale);
        }
        public void SetVisible(bool visible) { if (m_Root) m_Root.SetActive(visible); }
        private Vector3 Foot(Vector3 target)
        {
            return UnityEngine.Physics.Raycast(target + Vector3.up * .5f, Vector3.down, out var hit, 1.2f, m_Mask,
                QueryTriggerInteraction.Ignore) ? hit.point + Vector3.up * .015f : target;
        }
        private void Set(ref int index, Vector3 a, Vector3 b, float radius)
        { A[index] = a; B[index] = b; Radii[index++] = radius; }
        private GameObject MeshObject(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name); go.transform.SetParent(m_Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material; return go;
        }
        private void Draw()
        {
            m_Vertices.Clear(); m_Triangles.Clear(); Matrix4x4 inverse = m_Root.transform.worldToLocalMatrix;
            for (int i = 0; i < Segments; i++)
            {
                Vector3 direction = (B[i] - A[i]).normalized;
                Vector3 right = Vector3.Cross(direction, Vector3.up);
                if (right.sqrMagnitude < .01f) right = Vector3.Cross(direction, Vector3.forward);
                right.Normalize(); Vector3 up = Vector3.Cross(right, direction).normalized;
                int first = m_Vertices.Count;
                for (int end = 0; end < 2; end++) for (int side = 0; side < 8; side++)
                {
                    float a = side * Mathf.PI / 4;
                    m_Vertices.Add(inverse.MultiplyPoint3x4((end == 0 ? A[i] : B[i]) + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * Radii[i] * (end == 0 ? 1 : .72f)));
                    if (end == 0) continue;
                    int p = first + side, q = first + (side + 1) % 8;
                    m_Triangles.Add(p); m_Triangles.Add(p + 8); m_Triangles.Add(q);
                    m_Triangles.Add(q); m_Triangles.Add(p + 8); m_Triangles.Add(q + 8);
                }
            }
            m_Mesh.Clear(); m_Mesh.SetVertices(m_Vertices); m_Mesh.SetTriangles(m_Triangles, 0); m_Mesh.RecalculateNormals(); m_Mesh.RecalculateBounds();
        }
        private static Mesh BuildHead()
        {
            const int rings = 8, sides = 12;
            var v = new Vector3[(rings + 1) * sides]; var t = new List<int>(rings * sides * 6);
            for (int r = 0; r <= rings; r++) for (int s = 0; s < sides; s++)
            {
                float latitude = r * Mathf.PI / rings, angle = s * Mathf.PI * 2 / sides;
                v[r * sides + s] = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(angle), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(angle));
                if (r == 0) continue;
                int a = (r - 1) * sides + s, b = (r - 1) * sides + (s + 1) % sides, c = r * sides + s, d = r * sides + (s + 1) % sides;
                t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);
            }
            var mesh = new Mesh { name = "Narrow alien skull", hideFlags = HideFlags.DontSave };
            mesh.vertices = v; mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public void Dispose() { Destroy(m_Root); Destroy(m_Mesh); Destroy(m_HeadMesh); Destroy(m_Skin); Destroy(m_Bone); }
        private static void Destroy(Object value) { if (!value) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
