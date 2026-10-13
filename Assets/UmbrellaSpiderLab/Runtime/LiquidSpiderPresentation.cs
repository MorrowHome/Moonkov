using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>
    /// Authored Umbra skin driven by the existing IK endpoints. Only interior tissue lags:
    /// hips, knees and contacts share the motor/feet frame, so a cosmetic spring cannot
    /// strand a planted foot. Owns its generated mesh, renderer, bones and material.
    /// </summary>
    public sealed class LiquidSpiderPresentation
    {
        private const int Samples = 11;
        private readonly Transform m_Root;
        private readonly Transform[] m_Bones;
        private readonly Vector3[] m_Rest, m_Drag, m_DragVelocity;
        private readonly float[] m_Radii;
        private readonly Collider[] m_Nearby = new Collider[16];
        private readonly int m_Mask;
        private readonly Vector3[] m_Targets = new Vector3[8];
        private readonly SkinnedMeshRenderer m_Renderer;
        private readonly Mesh m_Mesh;
        private readonly Material m_Material;
        private readonly MaterialPropertyBlock m_Properties = new MaterialPropertyBlock();
        private readonly Vector3[] m_Tips = new Vector3[8];
        private Quaternion m_PreviousRotation;
        private Vector3 m_PreviousVelocity;
        private float m_Clock;
        private bool m_FirstFrame = true;
        private static readonly int FlowTime = Shader.PropertyToID("_FlowTime");
        public float MaxTissueLag { get; private set; }
        public float MaxEndpointError { get; private set; }
        public SkinnedMeshRenderer Renderer => m_Renderer;

        /// <summary>Guidance transforms for free fluid particles; their solver never writes back to this rig.</summary>
        public void CopySkinMatrices(Matrix4x4[] matrices, bool fullFluidLegRadius = false)
        {
            Matrix4x4 toPresentation = m_Root.parent.worldToLocalMatrix;
            for (int i = 0; i < m_Bones.Length; i++)
            {
                // Fluid has its own volume and GPU collision response. The source sculpt's
                // thin toe collars must not shrink a newly allocated fluid cross-section.
                Matrix4x4 pose = fullFluidLegRadius && i > 0 ?
                    Matrix4x4.TRS(m_Bones[i].position, m_Bones[i].rotation, Vector3.one) : m_Bones[i].localToWorldMatrix;
                matrices[i] = toPresentation * pose * Matrix4x4.Translate(-m_Rest[i]);
            }
        }

        public void CopyRestCentres(Vector4[] centres)
        {
            for (int i = 0; i < m_Rest.Length; i++) centres[i] = new Vector4(m_Rest[i].x, m_Rest[i].y, m_Rest[i].z, 1);
        }

        public LiquidSpiderPresentation(Transform parent, int mask)
        {
            m_Mask = mask;
            var payload = Resources.Load<TextAsset>("Umbra08Liquid");
            var shader = Resources.Load<Shader>("Umbra08Viscous");
            if (!payload || !shader) throw new InvalidOperationException("Umbra08 liquid payload/shader is missing.");
            m_Root = new GameObject("Umbra 08 / continuous viscous variant").transform;
            m_Root.SetParent(parent, false); m_Root.gameObject.layer = 2;
            using (var reader = new BinaryReader(new MemoryStream(payload.bytes, false)))
            {
                if (new string(reader.ReadChars(4)) != "UML2") throw new InvalidDataException("Unsupported Umbra skin payload.");
                int vertices = reader.ReadInt32(), indices = reader.ReadInt32(), bones = reader.ReadInt32();
                if (vertices <= 0 || vertices > 100000 || indices <= 0 || indices > 600000 || bones != 89)
                    throw new InvalidDataException("Invalid Umbra skin budget or bone layout.");
                m_Rest = ReadVectors(reader, bones);
                m_Radii = new float[bones];
                for (int i = 0; i < bones; i++) m_Radii[i] = reader.ReadSingle();
                var points = ReadVectors(reader, vertices);
                var normals = ReadVectors(reader, vertices);
                var colors = new Color[vertices];
                var uv = new Vector2[vertices]; var uv2 = new Vector2[vertices];
                for (int i = 0; i < vertices; i++)
                {
                    colors[i] = new Color(reader.ReadSingle(), 0, 0, 1);
                    uv[i] = new Vector2(points[i].x, points[i].z); uv2[i] = new Vector2(points[i].y, 0);
                }
                var triangles = new int[indices];
                for (int i = 0; i < indices; i++) triangles[i] = reader.ReadInt32();
                var weights = new BoneWeight[vertices];
                for (int i = 0; i < vertices; i++)
                {
                    weights[i].boneIndex0 = reader.ReadInt32(); weights[i].boneIndex1 = reader.ReadInt32();
                    weights[i].boneIndex2 = reader.ReadInt32(); weights[i].boneIndex3 = reader.ReadInt32();
                }
                for (int i = 0; i < vertices; i++)
                {
                    weights[i].weight0 = reader.ReadSingle(); weights[i].weight1 = reader.ReadSingle();
                    weights[i].weight2 = reader.ReadSingle(); weights[i].weight3 = reader.ReadSingle();
                }
                m_Bones = new Transform[bones]; m_Drag = new Vector3[bones]; m_DragVelocity = new Vector3[bones];
                var bindposes = new Matrix4x4[bones];
                for (int i = 0; i < bones; i++)
                {
                    m_Bones[i] = new GameObject(i == 0 ? "Mantle tissue" : "Tissue " + i).transform;
                    m_Bones[i].SetParent(m_Root, false); m_Bones[i].localPosition = m_Rest[i];
                    bindposes[i] = Matrix4x4.Translate(-m_Rest[i]);
                }
                m_Mesh = new Mesh { name = "Umbra08 / continuous runtime skin", indexFormat = IndexFormat.UInt32 };
                m_Mesh.vertices = points; m_Mesh.normals = normals; m_Mesh.colors = colors;
                m_Mesh.uv = uv; m_Mesh.uv2 = uv2; m_Mesh.triangles = triangles;
                m_Mesh.boneWeights = weights; m_Mesh.bindposes = bindposes; m_Mesh.RecalculateBounds();
                // CPU copies are only needed while constructing the mesh; BakeMesh remains available.
                m_Mesh.UploadMeshData(true);
            }
            m_Material = new Material(shader) { name = "Umbra08 / wet black flowing skin" };
            m_Renderer = m_Root.gameObject.AddComponent<SkinnedMeshRenderer>();
            m_Renderer.sharedMesh = m_Mesh; m_Renderer.sharedMaterial = m_Material;
            m_Renderer.bones = m_Bones; m_Renderer.rootBone = m_Root;
            m_Renderer.quality = SkinQuality.Bone4;
            // Legs may fold above the body or stretch toward a ceiling; authoring AABB is insufficient.
            m_Renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 5.4f);
            m_Renderer.updateWhenOffscreen = false;
        }

        private static Vector3[] ReadVectors(BinaryReader reader, int count)
        {
            var result = new Vector3[count];
            for (int i = 0; i < count; i++) result[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            return result;
        }

        public void SetVisible(bool visible)
        {
            if (m_Root.gameObject.activeSelf == visible) return;
            m_Root.gameObject.SetActive(visible);
            if (visible) m_FirstFrame = true;
        }

        public void BeginFrame(SurfaceMotor motor, PredictiveFeet feet, float dt)
        {
            var rotation = feet.BodyPose.Rotation;
            m_Clock += dt;
            Quaternion transport = Quaternion.Inverse(rotation) * m_PreviousRotation;
            Vector3 acceleration = m_FirstFrame || dt < .0001f ? Vector3.zero :
                (motor.Velocity - m_PreviousVelocity) / dt;
            Vector3 drag = Quaternion.Inverse(rotation) * Vector3.ClampMagnitude(-acceleration * .0028f - motor.Velocity * .003f, .085f);
            m_PreviousVelocity = motor.Velocity; m_PreviousRotation = rotation;
            MaxTissueLag = MaxEndpointError = 0;
            // Cosmetic integration is bounded even after a pause or a manual reset.
            float integrationTime = Mathf.Clamp(dt, 0, .1f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(integrationTime / .008f));
            float h = integrationTime / steps;
            for (int i = 0; i < m_Bones.Length; i++)
            {
                if (m_FirstFrame) { m_Drag[i] = m_DragVelocity[i] = Vector3.zero; }
                else { m_Drag[i] = transport * m_Drag[i]; m_DragVelocity[i] = transport * m_DragVelocity[i]; }
                int sample = (i - 1) % Samples;
                float envelope = i == 0 ? .55f : sample == 0 || sample == 5 || sample == 10 ? 0 :
                    Mathf.Sin(Mathf.PI * (sample % 5) / 5f);
                Vector3 flow = i == 0 ? Vector3.up * Mathf.Sin(m_Clock * 2.1f) * .018f :
                    Vector3.up * Mathf.Sin(m_Clock * 3.2f - sample * .7f - (i / Samples) * .8f) * .024f;
                Vector3 target = (drag + flow) * envelope;
                for (int step = 0; step < steps; step++)
                {
                    m_DragVelocity[i] += ((target - m_Drag[i]) * 145 - m_DragVelocity[i] * 23) * h;
                    m_Drag[i] = Vector3.ClampMagnitude(m_Drag[i] + m_DragVelocity[i] * h, .09f);
                }
                MaxTissueLag = Mathf.Max(MaxTissueLag, m_Drag[i].magnitude);
            }
            m_FirstFrame = false;
            m_Bones[0].localPosition = m_Drag[0];
            m_Bones[0].localRotation = Quaternion.Euler(-m_Drag[0].z * 36, 0, m_Drag[0].x * 36);
            CompressTissue(0);
            m_Properties.SetFloat(FlowTime, m_Clock); m_Renderer.SetPropertyBlock(m_Properties);
        }

        public void SetLeg(int leg, Vector3 hipWorld, Vector3 kneeWorld, Vector3 tipWorld)
        {
            Vector3 hip = m_Root.InverseTransformPoint(hipWorld), knee = m_Root.InverseTransformPoint(kneeWorld),
                tip = m_Root.InverseTransformPoint(tipWorld);
            int start = 1 + leg * Samples;
            Vector3 restHip = m_Rest[start], restKnee = m_Rest[start + 5], restTip = m_Rest[start + 10];
            Quaternion upper = Quaternion.FromToRotation(restKnee - restHip, knee - hip);
            Quaternion lower = Quaternion.FromToRotation(restTip - restKnee, tip - knee);
            for (int sample = 0; sample < Samples; sample++)
            {
                int index = start + sample;
                bool distal = sample > 5;
                Vector3 restA = distal ? restKnee : restHip, restB = distal ? restTip : restKnee;
                Vector3 delta = restB - restA;
                float t = Vector3.Dot(m_Rest[index] - restA, delta) / delta.sqrMagnitude;
                Quaternion bend = sample == 5 ? Quaternion.Slerp(upper, lower, .5f) : distal ? lower : upper;
                Vector3 offset = m_Rest[index] - Vector3.LerpUnclamped(restA, restB, t);
                Vector3 point = Vector3.LerpUnclamped(distal ? knee : hip, distal ? tip : knee, t) + bend * offset;
                m_Bones[index].SetLocalPositionAndRotation(point + m_Drag[index], bend);
                CompressTissue(index);
            }
            m_Tips[leg] = tipWorld; m_Targets[leg] = m_Bones[start + 10].position;
            MaxEndpointError = Mathf.Max(MaxEndpointError, Vector3.Distance(m_Targets[leg], tipWorld));
        }

        private void CompressTissue(int index)
        {
            // This is cosmetic proximity response, not another body motor. In crowded MC00
            // corners the soft skin narrows instead of pushing the body or re-anchoring a foot.
            Vector3 point = m_Bones[index].position;
            float radius = m_Radii[index];
            float gap = radius;
            int count = Physics.OverlapSphereNonAlloc(point, radius + .012f, m_Nearby, m_Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) gap = Mathf.Min(gap, Vector3.Distance(point, m_Nearby[i].ClosestPoint(point)));
            float scale = count == 0 ? 1 : Mathf.Clamp((gap - .012f) / radius, .16f, 1);
            m_Bones[index].localScale = Vector3.one * scale;
        }

        /// <summary>Validation only: sample the actual deformed surface, excluding planted toe collars.</summary>
        public bool SkinObstructed(Mesh scratch, int mask, out string part)
        {
            part = null; m_Renderer.BakeMesh(scratch);
            var points = scratch.vertices;
            for (int i = 0; i < points.Length; i += 23)
            {
                Vector3 point = m_Root.TransformPoint(points[i]);
                bool collar = false;
                for (int leg = 0; leg < 8; leg++) collar |= Vector3.SqrMagnitude(point - m_Tips[leg]) < .0121f;
                if (!collar && Physics.CheckSphere(point, .006f, mask, QueryTriggerInteraction.Ignore))
                { part = "Umbra skin sample at " + point + " / local " + points[i]; return true; }
            }
            return false;
        }

        public void Release()
        {
            Dispose(m_Root.gameObject); Dispose(m_Mesh); Dispose(m_Material);
        }

        private static void Dispose(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
