using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Builds a filled rest volume and weighted rig for the shared spider fluid solver.
    /// Body, head and prolegs follow actual solved poses; no CPU particle readback is needed.</summary>
    public sealed class ChainFluidPresentation
    {
        private readonly Transform m_Pivot;
        private readonly Matrix4x4[] m_RestInverse, m_Matrices;
        private readonly Vector4[] m_Centres;
        private readonly Vector3[] m_LegPoints = new Vector3[144];
        private readonly float[] m_RestRadii = new float[CreatureChain.Rings];
        private readonly bool m_Larva;
        private readonly float m_Length, m_Radius, m_Vertical, m_LegReach, m_LegRadius, m_HairLength;
        private readonly int m_Pairs, m_Segments, m_Hairs;
        private readonly Vector3 m_HeadScale;
        private readonly CreatureChainSettings.HeadFeature m_HeadFeature;
        private readonly float m_FeelerLength;
        private readonly List<SkeletonFluidPresentation.Seed> m_Seeds = new List<SkeletonFluidPresentation.Seed>();
        public SkeletonFluidPresentation Fluid { get; }

        public static SpiderFluidSettings Defaults(bool caterpillar) => new SpiderFluidSettings
        {
            bodyAttraction = 440, footAttraction = 850, bodyDamping = 42, footDamping = 65,
            viscosity = 30, pressure = 42, cohesion = 2.5f, freeFlowRadius = .012f,
            mantleThickness = 1, rootThickness = 1, kneeThickness = 1, toeThickness = .90f,
            localThicknessVariation = .05f, liquidMass = caterpillar ? .7f : .6f,
            surfaceThreshold = caterpillar ? .42f : .32f, motionTrail = .065f,
            filamentDensity = caterpillar ? .20f : .16f, minimumRadius = .004f, maximumRadius = .018f,
            smoothness = .87f, clearCoat = .6f, clearCoatSmoothness = .94f
        };

        public bool Matches(CreatureChainSettings s, bool larva) => m_Larva == larva &&
            m_Length == s.length && m_Radius == s.radius && m_Vertical == s.verticalScale && m_HeadScale == s.headScale &&
            m_HeadFeature == s.headFeature && m_FeelerLength == s.feelerLength &&
            (!larva || (m_Pairs == s.legPairs && m_LegReach == s.legReach && m_LegRadius == s.legRadius &&
                m_Segments == s.segments && m_Hairs == s.bristlesPerSegment && m_HairLength == s.bristleLength));

        public ChainFluidPresentation(Transform owner, int mask, CreatureChainSettings s, bool larva)
        {
            m_Larva = larva; m_Length = s.length; m_Radius = s.radius; m_Vertical = s.verticalScale;
            m_HeadScale = s.headScale; m_Pairs = larva ? s.legPairs : 0; m_LegReach = s.legReach;
            m_HeadFeature = s.headFeature; m_FeelerLength = s.feelerLength;
            m_LegRadius = s.legRadius; m_Segments = s.segments; m_Hairs = s.bristlesPerSegment; m_HairLength = s.bristleLength;
            int bones = CreatureChain.Rings + 1 + m_Pairs * 6;
            m_Centres = new Vector4[bones]; m_RestInverse = new Matrix4x4[bones]; m_Matrices = new Matrix4x4[bones];
            m_Pivot = new GameObject(larva ? "Liquid larva rig" : "Liquid serpent rig").transform;
            m_Pivot.SetParent(owner, false); m_Pivot.gameObject.layer = 2;
            for (int i = 0; i < CreatureChain.Rings; i++)
            {
                float t = i / (CreatureChain.Rings - 1f);
                m_RestRadii[i] = RestRadius(t, s, larva);
                SetRest(i, new Vector3(0, 0, m_Length * (.5f - t)), Quaternion.identity);
            }
            SetRest(CreatureChain.Rings, new Vector3(0, 0, m_Length * .5f + .06f), Quaternion.identity);
            float spacing = Mathf.Max(larva ? .046f : .035f,
                Mathf.Pow(m_Length * Mathf.PI * m_Radius * m_Radius / 7500, 1f / 3));
            int slices = Mathf.CeilToInt(m_Length / spacing);
            for (int i = 0; i <= slices; i++)
            {
                float t = i / (float)slices, f = t * (CreatureChain.Rings - 1);
                int a = Mathf.Min(CreatureChain.Rings - 2, Mathf.FloorToInt(f)); float blend = f - a;
                float radius = RestRadius(t, s, larva);
                Vector3 centre = new Vector3(0, 0, m_Length * (.5f - t));
                for (float y = -radius * m_Vertical; y <= radius * m_Vertical; y += spacing)
                for (float x = -radius; x <= radius; x += spacing)
                    if (x * x + y * y / (m_Vertical * m_Vertical) < radius * radius)
                        Add(centre + new Vector3(x, y, 0), a, a + 1, blend, false, 0);
                // A centre sample retains even the narrowest tail tip.
                Add(centre, a, a + 1, blend, false, 0);
            }
            Vector3 head = m_Centres[CreatureChain.Rings]; Vector3 half = m_HeadScale * .5f;
            for (float z = -half.z; z <= half.z; z += spacing)
            for (float y = -half.y; y <= half.y; y += spacing)
            for (float x = -half.x; x <= half.x; x += spacing)
                if (x * x / (half.x * half.x) + y * y / (half.y * half.y) + z * z / (half.z * half.z) < 1)
                    Add(head + new Vector3(x, y, z), CreatureChain.Rings, CreatureChain.Rings, 0, false, 1);
            BuildLegs(s, spacing);
            var details = CreatureHeadDetails.Paths(s);
            for (int p = 0; p < details.Count; p++) for (int j = 0; j < details[p].Length; j++)
            {
                var path = details[p]; float radius = Mathf.Max(.025f, CreatureHeadDetails.Radius(s, p, j / (path.Length - 1f)));
                Vector3 axis = (path[Mathf.Min(j + 1, path.Length - 1)] - path[Mathf.Max(j - 1, 0)]).normalized;
                Vector3 cross = Vector3.Cross(axis, Vector3.up).normalized;
                if (cross.sqrMagnitude < .1f) cross = Vector3.right;
                Vector3 up = Vector3.Cross(cross, axis);
                for (int k = 0; k < 7; k++)
                {
                    float angle = k * Mathf.PI / 3;
                    Add(head + path[j] + (k == 6 ? Vector3.zero : (cross * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius * .7f),
                        CreatureChain.Rings, CreatureChain.Rings, 0, false, 100 + p);
                    var seed = m_Seeds[m_Seeds.Count - 1]; seed.bones.y = j; m_Seeds[m_Seeds.Count - 1] = seed;
                }
            }
            if (larva) BuildTendrils(s);
            Fluid = new SkeletonFluidPresentation(m_Pivot, mask, m_Seeds.ToArray(), bones,
                Mathf.Max(2.8f, m_Length * .5f + 1.05f), 128, true);
        }

        private static float RestRadius(float t, CreatureChainSettings s, bool larva)
        {
            float radius = s.radius * (larva ? Mathf.Lerp(.56f, 1, Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.PI)), .3f)) :
                Mathf.Lerp(1, .065f, Mathf.Pow(t, 2.8f)));
            if (larva) radius *= 1 - s.segmentGrooveDepth * Mathf.Pow(.5f + .5f * Mathf.Cos(t * s.segments * Mathf.PI * 2), 4);
            return radius;
        }

        private void SetRest(int index, Vector3 centre, Quaternion rotation)
        {
            m_Centres[index] = new Vector4(centre.x, centre.y, centre.z, 1);
            m_RestInverse[index] = Matrix4x4.TRS(centre, rotation, Vector3.one).inverse;
        }

        private void Add(Vector3 rest, int a, int b, float blend, bool foot, int region)
        {
            m_Seeds.Add(new SkeletonFluidPresentation.Seed
            {
                rest = new Vector4(rest.x, rest.y, rest.z, 1), bones = new Vector4(a, b, foot ? 1 : 0, region),
                weights = new Vector4(1 - blend, blend, 0, 0)
            });
        }

        private void BuildLegs(CreatureChainSettings s, float spacing)
        {
            for (int leg = 0; leg < m_Pairs * 2; leg++)
            {
                float t = Mathf.Lerp(.12f, .91f, (leg / 2) / (m_Pairs - 1f));
                int ring = Mathf.RoundToInt(t * (CreatureChain.Rings - 1)); int bone = CreatureChain.Rings + 1 + leg * 3;
                Vector3 side = Vector3.right * (leg % 2 == 0 ? -1 : 1), centre = m_Centres[ring];
                Vector3 root = centre + side * m_RestRadii[ring] * .65f;
                Vector3 toe = root + side * s.legReach + Vector3.forward * .06f - Vector3.up * (m_RestRadii[ring] * m_Vertical - .02f);
                Vector3 knee = Vector3.Lerp(root, toe, .45f) + Vector3.up * .035f + side * .04f;
                var points = new[] { root, knee, toe };
                for (int j = 0; j < 3; j++) SetRest(bone + j, points[j], Quaternion.LookRotation(j == 0 ? knee - root : toe - knee, Vector3.up));
                float radius = s.headFeature == CreatureChainSettings.HeadFeature.Mandibles ?
                    Mathf.Max(.033f, s.legRadius * 1.15f) : Mathf.Max(.05f, s.legRadius * 1.4f);
                for (int j = 0; j < 2; j++)
                {
                    int slices = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(points[j], points[j + 1]) / spacing));
                    Vector3 axis = (points[j + 1] - points[j]).normalized;
                    Vector3 cross = Vector3.Cross(axis, Vector3.forward).normalized;
                    Vector3 other = Vector3.Cross(axis, cross);
                    for (int slice = 0; slice <= slices; slice++)
                    for (int radial = 0; radial < 7; radial++)
                    {
                        float angle = radial * Mathf.PI * 2 / 6;
                        Vector3 offset = radial == 6 ? Vector3.zero : (cross * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * radius * .75f;
                        Add(Vector3.Lerp(points[j], points[j + 1], slice / (float)slices) + offset,
                            bone + j, bone + j + 1, slice / (float)slices, true, 2 + leg);
                    }
                }
            }
        }

        private void BuildTendrils(CreatureChainSettings s)
        {
            for (int segment = 0; segment < s.segments; segment++)
            for (int hair = 0; hair < s.bristlesPerSegment; hair++)
            {
                int ring = Mathf.RoundToInt((segment + .5f) / s.segments * (CreatureChain.Rings - 1));
                float angle = Mathf.Lerp(-.2f, Mathf.PI + .2f, (hair + .5f) / s.bristlesPerSegment);
                Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                Vector3 start = (Vector3)m_Centres[ring] + radial * m_RestRadii[ring];
                for (int j = 0; j < 5; j++)
                {
                    Add(start + (radial - Vector3.forward * .4f).normalized * Mathf.Max(.14f, s.bristleLength) * j / 4,
                        ring, ring, 0, false, 2 + m_Pairs * 2 + segment * s.bristlesPerSegment + hair);
                    // Zero-weight second slot carries a tendril coordinate for bond selection.
                    var seed = m_Seeds[m_Seeds.Count - 1]; seed.bones.y = j; m_Seeds[m_Seeds.Count - 1] = seed;
                }
            }
        }

        public void Draw(CreatureChain chain, CreatureChainPresentation body, CreatureChainSettings s,
            SurfaceMotor motor, SpiderFluidSettings style, float dt)
        {
            m_Pivot.SetPositionAndRotation(chain.Centres[CreatureChain.Rings / 2], motor.Current.Rotation);
            var toLocal = m_Pivot.worldToLocalMatrix;
            for (int i = 0; i < CreatureChain.Rings; i++)
            {
                var rotation = Quaternion.LookRotation(chain.Tangents[i], chain.Ups[i]);
                float scale = chain.Radii[i] / Mathf.Max(.006f, m_RestRadii[i]);
                m_Matrices[i] = toLocal * Matrix4x4.TRS(chain.Centres[i], rotation, new Vector3(scale, scale, 1)) * m_RestInverse[i];
            }
            m_Matrices[CreatureChain.Rings] = toLocal * Matrix4x4.TRS(chain.Centres[0] + chain.Tangents[0] * .06f,
                Quaternion.LookRotation(chain.Tangents[0], chain.Ups[0]), Vector3.one) * m_RestInverse[CreatureChain.Rings];
            int legs = body.CopyLegPoints(m_LegPoints);
            for (int leg = 0; leg < legs; leg++)
            {
                int ring = Mathf.RoundToInt(Mathf.Lerp(.12f, .91f, (leg / 2) / (m_Pairs - 1f)) * (CreatureChain.Rings - 1));
                int bone = CreatureChain.Rings + 1 + leg * 3;
                for (int j = 0; j < 3; j++)
                {
                    Vector3 direction = j == 0 ? m_LegPoints[leg * 3 + 1] - m_LegPoints[leg * 3] : m_LegPoints[leg * 3 + 2] - m_LegPoints[leg * 3 + 1];
                    Quaternion rotation = direction.sqrMagnitude > .000001f ? Quaternion.LookRotation(direction, chain.Ups[ring]) : motor.Current.Rotation;
                    m_Matrices[bone + j] = toLocal * Matrix4x4.TRS(m_LegPoints[leg * 3 + j], rotation, Vector3.one) * m_RestInverse[bone + j];
                }
            }
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
