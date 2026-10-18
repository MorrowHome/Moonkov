using System;
using System.Collections.Generic;
using Unity.MP_FPS.SpiderLab;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS
{
    /// <summary>Filled articulated volumes for the existing Umbra particle/volume/
    /// filament solver. Gameplay bones remain authoritative; GPU work is client-only.</summary>
    public sealed class ArticulatedAlienFluid
    {
        private readonly Transform m_Owner, m_Pivot;
        private readonly StiltWalkerRig m_Stilt;
        private readonly FaceHuggerRig m_Face;
        private readonly Vector3[] m_A, m_B;
        private readonly float[] m_Radii;
        private readonly Matrix4x4[] m_Inverse, m_Matrices;
        private readonly Vector4[] m_Centres;
        private readonly float m_Scale;
        private bool m_Attached;
        public SkeletonFluidPresentation Fluid { get; }
        private Vector3 Core => m_Stilt != null ? m_Stilt.Head : m_Face.Centre;
        private Quaternion CoreRotation => m_Stilt != null ? m_Stilt.HeadRotation : m_Face.CoreRotation;
        private Vector3 CoreRadii => m_Stilt != null ? m_Stilt.HeadRadii : m_Face.CoreRadii;

        public ArticulatedAlienFluid(Transform owner, int mask, float scale, StiltWalkerRig stilt = null, FaceHuggerRig face = null)
        {
            if ((stilt == null) == (face == null)) throw new ArgumentException("Exactly one articulated rig is required.");
            m_Owner = owner; m_Stilt = stilt; m_Face = face; m_Scale = Mathf.Clamp(scale, .1f, 4);
            if (stilt != null) { m_A = stilt.A; m_B = stilt.B; m_Radii = stilt.Radii; }
            else
            {
                int count = FaceHuggerRig.Fingers * 6 + FaceHuggerRig.TailPoints - 1;
                m_A = new Vector3[count]; m_B = new Vector3[count]; m_Radii = new float[count]; CaptureFace();
            }
            int bones = 1 + m_A.Length * 2;
            m_Inverse = new Matrix4x4[bones]; m_Matrices = new Matrix4x4[bones]; m_Centres = new Vector4[bones];
            m_Pivot = new GameObject("Articulated liquid guide").transform; m_Pivot.SetParent(owner, false); m_Pivot.gameObject.layer = 2;
            m_Pivot.localScale = Vector3.one * m_Scale; PlacePivot();
            var seeds = new List<SkeletonFluidPresentation.Seed>(8000);
            const float spacing = .03f;
            Vector3 radii = CoreRadii / m_Scale;
            SetRest(0, Matrix4x4.TRS(Core, CoreRotation, CoreRadii), Core);
            for (float z = -radii.z; z <= radii.z; z += spacing)
            for (float y = -radii.y; y <= radii.y; y += spacing)
            for (float x = -radii.x; x <= radii.x; x += spacing)
                if (x*x/(radii.x*radii.x)+y*y/(radii.y*radii.y)+z*z/(radii.z*radii.z) <= 1)
                    Add(seeds, Core + CoreRotation * (new Vector3(x,y,z) * m_Scale), 0, 0, 0, -1);
            for (int segment = 0; segment < m_A.Length; segment++)
            {
                Vector3 axis = m_B[segment] - m_A[segment];
                Quaternion rotation = SegmentRotation(axis);
                float radius = Mathf.Max(.004f, m_Radii[segment]);
                Vector3 cross = rotation * Vector3.right, up = rotation * Vector3.up;
                int a = 1 + segment * 2, b = a + 1;
                SetRest(a, Frame(m_A[segment], rotation, radius), m_A[segment]);
                SetRest(b, Frame(m_B[segment], rotation, radius), m_B[segment]);
                int slices = Mathf.Max(1, Mathf.CeilToInt(axis.magnitude / (spacing * m_Scale)));
                float normalizedRadius = radius / m_Scale;
                for (int slice = 0; slice <= slices; slice++)
                {
                    float t = slice / (float)slices;
                    Vector3 centre = Vector3.Lerp(m_A[segment], m_B[segment], t);
                    if (normalizedRadius < spacing * 1.4f)
                    {
                        Add(seeds, centre, a, b, t, segment);
                        for (int radial = 0; radial < 4; radial++)
                        {
                            float angle = radial * Mathf.PI * .5f;
                            Add(seeds, centre + (cross * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius * .55f, a, b, t, segment);
                        }
                    }
                    else for (float y = -normalizedRadius; y <= normalizedRadius; y += spacing)
                    for (float x = -normalizedRadius; x <= normalizedRadius; x += spacing)
                        if (x*x+y*y <= normalizedRadius*normalizedRadius)
                            Add(seeds, centre + (cross*x+up*y)*m_Scale, a, b, t, segment);
                }
            }
            // Same normalized volume resolution as the other filled creature rigs;
            // scaling a creature never deletes its particle/filament detail.
            Fluid = new SkeletonFluidPresentation(m_Pivot, mask, seeds.ToArray(), bones, 2.8f, 128, true, true);
        }
        private void SetRest(int index, Matrix4x4 world, Vector3 centre)
        {
            m_Inverse[index] = (m_Pivot.worldToLocalMatrix * world).inverse;
            Vector3 point = m_Pivot.InverseTransformPoint(centre); m_Centres[index] = new Vector4(point.x,point.y,point.z,1);
        }
        private void Add(List<SkeletonFluidPresentation.Seed> seeds, Vector3 point, int a, int b, float t, int region)
        {
            Vector3 rest = m_Pivot.InverseTransformPoint(point);
            seeds.Add(new SkeletonFluidPresentation.Seed {rest=new Vector4(rest.x,rest.y,rest.z,1),
                bones=new Vector4(a,b,t,region),weights=new Vector4(1-t,t,0,0)});
        }
        private static Quaternion SegmentRotation(Vector3 axis)
        {
            if (axis.sqrMagnitude < .000001f) return Quaternion.identity;
            Vector3 up = Mathf.Abs(Vector3.Dot(axis.normalized,Vector3.up)) > .95f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(axis,up);
        }
        private static Matrix4x4 Frame(Vector3 point, Quaternion rotation, float radius)
            => Matrix4x4.TRS(point,rotation,new Vector3(radius,radius,1));
        private void CaptureFace()
        {
            int count = 0;
            float size = m_Face.BodyRadius / .17f;
            for (int finger = 0; finger < FaceHuggerRig.Fingers; finger++) for (int part = 0; part < 6; part++)
            {
                m_A[count] = m_Face.FingerPoint(finger,part/6f); m_B[count] = m_Face.FingerPoint(finger,(part+1)/6f);
                m_Radii[count++] = Mathf.Lerp(.029f,.006f,(part+.5f)/6f)*size;
            }
            for (int point=0;point<FaceHuggerRig.TailPoints-1;point++)
            {
                m_A[count]=m_Face.Tail[point];m_B[count]=m_Face.Tail[point+1];
                m_Radii[count++]=Mathf.Lerp(.032f,.003f,(point+.5f)/(FaceHuggerRig.TailPoints-1))*size;
            }
        }
        private void PlacePivot()
        {
            var bounds = new Bounds(Core,CoreRadii*2);
            for(int i=0;i<m_A.Length;i++){bounds.Encapsulate(m_A[i]);bounds.Encapsulate(m_B[i]);}
            m_Pivot.SetPositionAndRotation(bounds.center,m_Owner.rotation);
        }
        public void Draw(SurfaceMotor motor, SpiderFluidSettings style, float dt, bool attached)
        {
            if(m_Face!=null)CaptureFace(); PlacePivot();
            var local=m_Pivot.worldToLocalMatrix;
            m_Matrices[0]=local*Matrix4x4.TRS(Core,CoreRotation,CoreRadii)*m_Inverse[0];
            for(int i=0;i<m_A.Length;i++)
            {
                var rotation=SegmentRotation(m_B[i]-m_A[i]);float radius=Mathf.Max(.004f,m_Radii[i]);
                m_Matrices[1+i*2]=local*Frame(m_A[i],rotation,radius)*m_Inverse[1+i*2];
                m_Matrices[2+i*2]=local*Frame(m_B[i],rotation,radius)*m_Inverse[2+i*2];
            }
            // Attachment changes the parasite topology around the head abruptly.
            // Re-initialize on the current guide rather than pulling stale liquid
            // through the player's face on the transition frame.
            if(attached!=m_Attached){Fluid.SetVisible(false);m_Attached=attached;}
            Fluid.Settings=style;Fluid.Predatory=style.heavy;Fluid.AttractionEnabled=style.attraction;
            Fluid.SetVisible(true);Fluid.Draw(m_Matrices,m_Centres,motor.Velocity,dt);
        }
        public void Release()
        {
            Fluid.Release(); if(Application.isPlaying)Object.Destroy(m_Pivot.gameObject);else Object.DestroyImmediate(m_Pivot.gameObject);
        }
    }
}
