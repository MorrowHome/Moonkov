using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>
    /// Disposable presentation meshes, authored in metres for the body and in the existing
    /// centred, two-unit +Y segment convention for limbs. No colliders, anchors or motor state.
    /// Submeshes: 0 hard shell, 1 recessed dark surface, 2 exposed bone/edge.
    /// </summary>
    public static class SpiderSkinGeometry
    {
        private sealed class Builder
        {
            private readonly List<Vector3> m_Vertices = new List<Vector3>();
            private readonly List<int>[] m_Indices = { new List<int>(), new List<int>(), new List<int>() };

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, int slot)
            {
                int start = m_Vertices.Count;
                m_Vertices.Add(a); m_Vertices.Add(b); m_Vertices.Add(c);
                m_Indices[slot].Add(start); m_Indices[slot].Add(start + 1); m_Indices[slot].Add(start + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int slot)
            {
                Triangle(a, b, c, slot); Triangle(a, c, d, slot);
            }

            public Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = 3 };
                mesh.SetVertices(m_Vertices);
                for (int i = 0; i < 3; i++) mesh.SetTriangles(m_Indices[i], i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static Vector3 Radial(float angle, float radius, float y)
        {
            return new Vector3(Mathf.Sin(angle) * radius, y, Mathf.Cos(angle) * radius);
        }

        public static Mesh Shell()
        {
            var b = new Builder();
            // Fixed fractures and unequal plate lengths give a folded, incomplete silhouette.
            // Membrane stays inside the protected 0.22..0.49 m radius, never between moving legs.
            float[] tips = { .63f, .59f, .52f, .62f, .35f, .54f, .64f, .58f };
            for (int plate = 0; plate < 8; plate++)
            {
                float center = (plate * 45 + 22.5f) * Mathf.Deg2Rad;
                float width = (plate == 2 || plate == 5 ? 15 : 20) * Mathf.Deg2Rad;
                // The rear-left damaged lobe retreats at BOTH the middle fold and lip. Trimming
                // only the tip leaves the wider middle row outside the intended local envelope.
                float[] radii = plate == 4 ? new[] { .15f, .24f, .30f, tips[plate] } :
                    new[] { .15f, .35f, .48f, tips[plate] };
                float[] heights = { .21f, .31f + (plate % 3) * .018f, .24f, .15f };
                for (int row = 0; row < 3; row++)
                for (int side = 0; side < 2; side++)
                {
                    float a0 = center - width + side * width;
                    float a1 = a0 + width;
                    float ridge0 = side == 0 ? .035f : 0;
                    float ridge1 = side == 1 ? .035f : 0;
                    Vector3 a = Radial(a0, radii[row], heights[row] + ridge0);
                    Vector3 d = Radial(a1, radii[row], heights[row] + ridge1);
                    Vector3 p = Radial(a0, radii[row + 1], heights[row + 1] + ridge0 * .7f);
                    Vector3 c = Radial(a1, radii[row + 1], heights[row + 1] + ridge1 * .7f);
                    if (plate == 4 || plate == 5)
                    {
                        // A cut-back side margin, not a scaled whole shell. Keep the damaged seam
                        // inside its lateral envelope, including the inner fold during body lean.
                        a.x = Mathf.Max(a.x, -.085f); d.x = Mathf.Max(d.x, -.085f);
                        p.x = Mathf.Max(p.x, -.085f); c.x = Mathf.Max(c.x, -.085f);
                    }
                    // One small chip opens the rim rather than making an entire soft umbrella panel.
                    if (plate == 2 && row == 2 && side == 1) c = Vector3.Lerp(c, d, .38f);
                    Vector3 thickness = Vector3.up * Mathf.Lerp(.028f, .012f, row / 2f);
                    b.Quad(a, p, c, d, 0);
                    b.Quad(d - thickness, c - thickness, p - thickness, a - thickness, 1);
                    if (side == 0) b.Quad(a, a - thickness, p - thickness, p, 1);
                    if (side == 1) b.Quad(c, c - thickness, d - thickness, d, 1);
                    if (row == 2) b.Quad(p, p - thickness, c - thickness, c, 2);
                    if (row == 0) b.Quad(d, d - thickness, a - thickness, a, 2);
                }
            }
            return b.Finish("Eight fractured folded carapace plates");
        }

        public static Mesh Understructure()
        {
            var b = new Builder();
            for (int i = 0; i < 8; i++)
            {
                float angle = (i * 45 + 22.5f) * Mathf.Deg2Rad;
                if (i == 4)
                {
                    // The damaged lobe has an exposed shoulder. Its remaining thin guard sits
                    // on the inward side; a broken rib/ring leaves the same clearance as the lip.
                    // These are cover vertices only: the actual hip remains at the original root.
                    Tube(b, Radial(angle, .34f, .04f) + Vector3.right * .045f,
                        Radial(angle, .45f, .14f) + Vector3.right * .07f, .028f, .014f, 6, 0);
                    continue;
                }
                // Narrow internal ribs and a broad shoulder wrap connect the shell to each hip.
                Tube(b, Radial(angle, .12f, .15f), Radial(angle, .42f, .17f), .020f, .015f, 6, 2);
                Tube(b, Radial(angle, .34f, .04f), Radial(angle, .45f, .14f), .072f, .060f, 6, 0);
                float next = angle + Mathf.PI / 4;
                Tube(b, Radial(angle, .26f, .12f), Radial(next, .26f, .12f), .020f, .020f, 5, 1);
            }
            return b.Finish("Protected ribs and eight coxal shoulder wraps");
        }

        public static Mesh Membrane()
        {
            var b = new Builder();
            foreach (int panel in new[] { 1, 3, 6 })
            {
                float center = (panel * 45 + 45) * Mathf.Deg2Rad;
                float half = 11 * Mathf.Deg2Rad;
                b.Quad(Radial(center - half, .22f, .17f), Radial(center - half, .49f, .20f),
                    Radial(center + half, .49f, .20f), Radial(center + half, .22f, .17f), 0);
            }
            return b.Finish("Three recessed protected membrane patches");
        }

        public static Mesh ArmoredSegment(bool upper)
        {
            var b = new Builder();
            float[] y = upper ? new[] { -1f, -.80f, -.38f, -.28f, -.16f, .26f, .36f, .78f, 1f } :
                new[] { -1f, -.72f, -.22f, -.12f, .16f, .55f, .84f, 1f };
            float[] radius = upper ? new[] { 1.04f, .99f, .84f, .79f, .74f, .58f, .52f, .39f, .30f } :
                new[] { .78f, .70f, .54f, .50f, .40f, .26f, .14f, .04f };
            const int facets = 6;
            for (int row = 0; row < y.Length - 1; row++)
            for (int face = 0; face < facets; face++)
            {
                float a0 = (face + .5f) * Mathf.PI * 2 / facets;
                float a1 = a0 + Mathf.PI * 2 / facets;
                Vector3 a = SegmentPoint(y[row], radius[row], a0);
                Vector3 d = SegmentPoint(y[row], radius[row], a1);
                Vector3 p = SegmentPoint(y[row + 1], radius[row + 1], a0);
                Vector3 c = SegmentPoint(y[row + 1], radius[row + 1], a1);
                int slot = radius[row + 1] < radius[row] * .7f ? 1 : face == 0 || face == 3 ? 2 : 0;
                b.Quad(a, d, c, p, slot);
            }
            for (int face = 0; face < facets; face++)
            {
                float a0 = (face + .5f) * Mathf.PI * 2 / facets, a1 = a0 + Mathf.PI * 2 / facets;
                b.Triangle(new Vector3(0, -1, 0), SegmentPoint(-1, radius[0], a1), SegmentPoint(-1, radius[0], a0), 1);
                b.Triangle(new Vector3(0, 1, 0), SegmentPoint(1, radius[radius.Length - 1], a0),
                    SegmentPoint(1, radius[radius.Length - 1], a1), 2);
            }
            return b.Finish(upper ? "Segmented broad root armor" : "Segmented taper to contact needle");
        }

        private static Vector3 SegmentPoint(float y, float radius, float a)
        {
            // Unequal cross-section gives a blade/bone profile instead of another circular rod.
            return new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius * .62f);
        }

        public static Mesh JointCuff()
        {
            var b = new Builder();
            const int slices = 9;
            for (int i = 0; i < slices; i++)
            {
                float a0 = (58 + i * 244f / slices) * Mathf.Deg2Rad;
                float a1 = (58 + (i + 1) * 244f / slices) * Mathf.Deg2Rad;
                Vector3 a = Radial(a0, .40f, -.25f), d = Radial(a1, .40f, -.25f);
                Vector3 p = Radial(a0, .33f, .21f), c = Radial(a1, .33f, .21f);
                Vector3 ai = Radial(a0, .30f, -.25f), di = Radial(a1, .30f, -.25f);
                Vector3 pi = Radial(a0, .26f, .21f), ci = Radial(a1, .26f, .21f);
                b.Quad(a, d, c, p, 0); b.Quad(di, ai, pi, ci, 1);
                b.Quad(p, c, ci, pi, 2); b.Quad(d, a, ai, di, 1);
                if (i == 0) b.Quad(ai, a, p, pi, 2);
                if (i == slices - 1) b.Quad(d, di, ci, c, 2);
            }
            return b.Finish("Notched articulated joint guard");
        }

        private static void Tube(Builder b, Vector3 from, Vector3 to, float r0, float r1, int sides, int slot)
        {
            var q = Quaternion.FromToRotation(Vector3.up, to - from);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                Vector3 p0 = q * new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0));
                Vector3 p1 = q * new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1));
                b.Quad(from + p0 * r0, from + p1 * r0, to + p1 * r1, to + p0 * r1, slot);
                b.Triangle(from, from + p1 * r0, from + p0 * r0, slot);
                b.Triangle(to, to + p0 * r1, to + p1 * r1, slot);
            }
        }
    }
}
