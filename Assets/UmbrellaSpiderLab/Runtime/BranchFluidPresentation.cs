using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>A filled mantle plus weighted continuous appendages, using the existing
    /// GPU particle solver. One instance owns all GPU resources, including child branches.</summary>
    public sealed class BranchFluidPresentation
    {
        private readonly Transform m_Pivot;
        private readonly Matrix4x4[] m_Matrices;
        private readonly Vector4[] m_Centres;
        private readonly BranchCreatureRig m_Rig;
        private readonly float m_PhysicalScale;
        public SkeletonFluidPresentation Fluid { get; }

        public static SpiderFluidSettings Defaults(ProceduralMenagerieExperiment.Species species)
        {
            var s = ChainFluidPresentation.Defaults(false);
            s.mantleThickness = s.rootThickness = s.kneeThickness = s.toeThickness = 1;
            s.legAsymmetry = 0; s.localThicknessVariation = .07f;
            s.liquidMass = .75f; s.surfaceThreshold = .34f; s.filamentDensity = .17f;
            s.bodyAttraction = 380; s.footAttraction = 800;
            if (species == ProceduralMenagerieExperiment.Species.Slime)
            {
                s.bodyAttraction = 190; s.bodyDamping = 32; s.viscosity = 38;
                s.liquidMass = .9f; s.surfaceThreshold = .38f; s.motionTrail = .09f; s.filamentDensity = .12f;
            }
            if (species == ProceduralMenagerieExperiment.Species.Roots)
            { s.surfaceThreshold = .24f; s.filamentDensity = .24f; s.minimumRadius = .003f; s.maximumRadius = .015f; }
            return s;
        }

        public BranchFluidPresentation(Transform owner, int mask, BranchCreatureRig rig, BranchCreatureSettings s,
            ProceduralMenagerieExperiment.Species species, float physicalScale = 1, BranchCreatureSettings restShape = null, float particleSpacing = .045f)
        {
            m_PhysicalScale = Mathf.Clamp(physicalScale, .1f, 4);
            m_Rig = rig; m_Pivot = new GameObject("Weighted branched liquid guide").transform;
            m_Pivot.SetParent(owner, false); m_Pivot.gameObject.layer = 2;
            m_Pivot.localScale = Vector3.one * m_PhysicalScale;
            m_Matrices = new Matrix4x4[rig.BoneCount]; m_Centres = new Vector4[rig.BoneCount];
            for (int i = 0; i < rig.BoneCount; i++) m_Centres[i] = new Vector4(rig.Rest[i].x / m_PhysicalScale, rig.Rest[i].y / m_PhysicalScale, rig.Rest[i].z / m_PhysicalScale, 1);
            var seeds = new List<SkeletonFluidPresentation.Seed>();
            float spacing = Mathf.Clamp(particleSpacing, .045f, .12f);
            Vector3 r = rig.RestBodyRadii / m_PhysicalScale;
            for (float z = -r.z; z <= r.z; z += spacing)
            for (float y = -r.y; y <= r.y; y += spacing)
            for (float x = -r.x; x <= r.x; x += spacing)
                if (x * x / (r.x * r.x) + y * y / (r.y * r.y) + z * z / (r.z * r.z) < 1)
                    Add(seeds, new Vector3(x, y, z), 0, 0, 0, 0, -1);
            bool slime = species == ProceduralMenagerieExperiment.Species.Slime;
            for (int arm = 0; arm < rig.Branches; arm++)
            for (int j = 0; j < BranchCreatureRig.Samples - 1; j++)
            {
                int a = BranchCreatureRig.Index(arm, j), b = a + 1;
                Vector3 from = rig.Rest[a] / m_PhysicalScale, to = rig.Rest[b] / m_PhysicalScale, axis = (to - from).normalized;
                Vector3 cross = Vector3.Cross(axis, Vector3.up).normalized;
                if (cross.sqrMagnitude < .1f) cross = Vector3.right;
                Vector3 up = Vector3.Cross(cross, axis).normalized;
                int slices = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / spacing));
                for (int slice = 0; slice < slices; slice++)
                {
                    float blend = slice / (float)slices, coordinate = (j + blend) / (BranchCreatureRig.Samples - 1f);
                    float radius = Mathf.Lerp(rig.RestRadii[a], rig.RestRadii[b], blend) / m_PhysicalScale;
                    Vector3 centre = Vector3.Lerp(from, to, blend);
                    float vertical = slime ? .48f : 1;
                    for (float y = -radius; y <= radius; y += spacing)
                    for (float x = -radius; x <= radius; x += spacing)
                        if (x * x + y * y <= radius * radius)
                            Add(seeds, centre + cross * x + up * y * vertical, a, b, blend, coordinate, arm);
                    // Narrow tips retain a core and four off-centre samples, rather than
                    // disappearing when their diameter falls below a grid sampling interval.
                    if (radius < spacing * 1.4f)
                    {
                        Add(seeds, centre, a, b, blend, coordinate, arm);
                        for (int radial = 0; radial < 4; radial++)
                        {
                            float angle = radial * Mathf.PI * .5f;
                            Add(seeds, centre + (cross * Mathf.Cos(angle) + up * Mathf.Sin(angle) * vertical) * radius * .55f,
                                a, b, blend, coordinate, arm);
                        }
                    }
                }
            }
            float half = Mathf.Max(2.8f, (restShape ?? s).reach + Mathf.Max(r.x, r.z) + .45f);
            Fluid = new SkeletonFluidPresentation(m_Pivot, mask, seeds.ToArray(), rig.BoneCount, half, 128, true, true);
        }

        private static void Add(List<SkeletonFluidPresentation.Seed> seeds, Vector3 position, int a, int b,
            float blend, float coordinate, int region)
        {
            seeds.Add(new SkeletonFluidPresentation.Seed
            {
                rest = new Vector4(position.x, position.y, position.z, 1),
                bones = new Vector4(a, b, coordinate, region), weights = new Vector4(1 - blend, blend, 0, 0)
            });
        }

        public void Draw(SurfaceMotor motor, SpiderFluidSettings style, float dt, LiquidReaction reaction = default)
        {
            m_Pivot.SetPositionAndRotation(m_Rig.Points[0], motor.Current.Rotation);
            var local = m_Pivot.worldToLocalMatrix;
            m_Matrices[0] = local * Matrix4x4.TRS(m_Rig.Points[0], m_Rig.Rotations[0], m_Rig.BodyScale * m_PhysicalScale);
            for (int i = 1; i < m_Matrices.Length; i++)
            {
                float scale = m_Rig.Radii[i] / Mathf.Max(.01f, m_Rig.RestRadii[i]);
                // The rig rest points are world-sized, while fluid rest points stay
                // normalized so shrinking an enemy retains voxel/particle detail.
                m_Matrices[i] = local * Matrix4x4.TRS(m_Rig.Points[i], m_Rig.Rotations[i], new Vector3(scale, scale, 1)) *
                    m_Rig.RestInverse[i] * Matrix4x4.Scale(Vector3.one * m_PhysicalScale);
            }
            Fluid.Reaction = reaction;
            Fluid.Settings = style; Fluid.Predatory = style.heavy; Fluid.AttractionEnabled = style.attraction;
            Fluid.SetVisible(true); Fluid.Draw(m_Matrices, m_Centres, motor.Velocity, dt);
        }
        public void Release()
        {
            Fluid.Release();
            if (Application.isPlaying) Object.Destroy(m_Pivot.gameObject); else Object.DestroyImmediate(m_Pivot.gameObject);
        }
    }
}
