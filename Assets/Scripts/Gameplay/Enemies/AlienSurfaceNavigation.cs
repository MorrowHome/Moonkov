using System;
using System.Collections.Generic;
using Unity.MP_FPS.SpiderLab;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>A small, baked contact graph for one encounter. SurfaceMotor remains the only
    /// movement owner; this component chooses which connected surface to approach next.</summary>
    [DisallowMultipleComponent]
    public sealed class AlienSurfaceNavigation : MonoBehaviour
    {
        [Serializable]
        public struct Node
        {
            public Vector3 Point;
            public Vector3 Normal;
            public Vector3 BodyPosition(float scale) => Point + Normal * (SurfaceMotor.Height * scale);
        }

        [Serializable]
        private struct Link
        {
            public int A, B;
            public float Cost;
        }

        /// <summary>Owned by one server enemy. No references or route indices are replicated.</summary>
        public sealed class Route
        {
            internal AlienSurfaceNavigation Owner;
            internal readonly List<int> Path = new List<int>();
            internal readonly HashSet<int> Blocked = new HashSet<int>();
            internal int Cursor, Revision;
            internal float Scale, ReplanAt, ValidateAt, ForgetBlockedAt;
            internal Vector3 Goal;
            internal float[] Costs = Array.Empty<float>();
            internal int[] Parents = Array.Empty<int>();
            internal bool[] Closed = Array.Empty<bool>();
            internal readonly List<int> Heap = new List<int>();
            internal readonly List<float> Priorities = new List<float>();
            public string LastFailure { get; internal set; }
            public bool HasPath => Cursor < Path.Count;
            public void Clear()
            {
                Path.Clear(); Blocked.Clear(); Cursor = 0; ReplanAt = 0; Owner = null;
                LastFailure = null;
            }
        }

        private const int MaximumNodes = 4096;
        private const int MaximumCells = 12000;
        private const int MaximumBakeQueries = 350000;
        private static readonly Vector3[] s_Directions =
            { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        private static readonly List<AlienSurfaceNavigation> s_Active = new List<AlienSurfaceNavigation>();
        private static bool s_Discovered;
        [SerializeField] private Bounds worldBounds;
        [SerializeField, Min(.35f)] private float sampleSpacing = .75f;
        [SerializeField, Range(.1f, 4)] private float bakedScale = 1;
        [SerializeField] private Node[] nodes = Array.Empty<Node>();
        [SerializeField] private Link[] links = Array.Empty<Link>();
        [SerializeField, TextArea] private string lastBakeReport;
        private List<int>[] m_Adjacency;
        private int m_Revision, m_QueryBudget = -1, m_Mask;
        private bool m_BudgetExceeded;
        private int Mask => m_Mask != 0 ? m_Mask : (m_Mask = LayerMask.GetMask("Default", "Ground"));
        public IReadOnlyList<Node> Nodes => nodes;
        public string LastBakeReport => lastBakeReport;
        public int LinkCount => links.Length;
        public Bounds WorldBounds => worldBounds;
        public float SampleSpacing => sampleSpacing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { s_Active.Clear(); s_Discovered = false; }
        private void OnEnable() { if (!s_Active.Contains(this)) s_Active.Add(this); }
        private void OnDisable() => s_Active.Remove(this);
        private void OnValidate() { m_Adjacency = null; m_Revision++; }
        public bool Contains(Vector3 position) => worldBounds.Contains(position);
        public static AlienSurfaceNavigation Find(Vector3 position)
        {
            // Covers Edit Mode fixtures and Enter Play with domain reload disabled.
            // Discovery runs once, never once per enemy tick; later loads register normally.
            if (!s_Discovered)
            {
                s_Discovered = true;
                foreach (var graph in UnityEngine.Object.FindObjectsByType<AlienSurfaceNavigation>())
                    if (graph.isActiveAndEnabled && !s_Active.Contains(graph)) s_Active.Add(graph);
            }
            foreach (var graph in s_Active)
                if (graph && graph.isActiveAndEnabled && graph.Contains(position) && graph.nodes.Length > 0) return graph;
            return null;
        }

        /// <summary>Authoring only. Samples real colliders within a bounded region; failure leaves
        /// the previous graph intact. Caller owns dirtying/saving its scene after success.</summary>
        public bool Bake(Bounds bounds, float spacing = .75f, float clearanceScale = 1, Transform samplingSpace = null)
        {
            if (!Finite(bounds.center) || !Finite(bounds.size) || bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
            { lastBakeReport = "Invalid sampling bounds; previous graph preserved."; return false; }
            spacing = Mathf.Max(.35f, spacing);
            clearanceScale = Mathf.Clamp(clearanceScale, .1f, 4);
            while (CellCount(bounds.size, spacing) > MaximumCells) spacing *= 1.15f;
            UnityEngine.Physics.SyncTransforms();
            var found = new List<Node>();
            var duplicate = new Dictionary<Vector3Int, List<int>>();
            var builtLinks = new List<Link>();
            m_QueryBudget = MaximumBakeQueries; m_BudgetExceeded = false;
            Vector3 min = bounds.min;
            int nx = Mathf.CeilToInt(bounds.size.x / spacing);
            int ny = Mathf.CeilToInt(bounds.size.y / spacing);
            int nz = Mathf.CeilToInt(bounds.size.z / spacing);
            float merge = spacing * .3f;
            for (int x = 0; x <= nx && !m_BudgetExceeded; x++)
            for (int y = 0; y <= ny && !m_BudgetExceeded; y++)
            for (int z = 0; z <= nz && !m_BudgetExceeded; z++)
            {
                var origin = min + new Vector3(Mathf.Min(x * spacing, bounds.size.x),
                    Mathf.Min(y * spacing, bounds.size.y), Mathf.Min(z * spacing, bounds.size.z));
                if (samplingSpace) origin = samplingSpace.TransformPoint(origin);
                foreach (var direction in s_Directions)
                {
                    Vector3 ray = samplingSpace ? samplingSpace.TransformDirection(direction) : direction;
                    if (!Ray(origin, ray, spacing * 1.05f, out var hit) ||
                        !bounds.Contains(samplingSpace ? samplingSpace.InverseTransformPoint(hit.point) : hit.point)) continue;
                    var node = new Node { Point = hit.point, Normal = hit.normal.normalized };
                    var key = Cell(node.Point, merge);
                    bool exists = false;
                    for (int dx = -1; dx <= 1 && !exists; dx++) for (int dy = -1; dy <= 1 && !exists; dy++) for (int dz = -1; dz <= 1 && !exists; dz++)
                        if (duplicate.TryGetValue(key + new Vector3Int(dx, dy, dz), out var nearby))
                            foreach (int index in nearby)
                                if (Vector3.Dot(found[index].Normal, node.Normal) > .96f &&
                                    (found[index].Point - node.Point).sqrMagnitude < merge * merge) { exists = true; break; }
                    if (exists || !Support(node, clearanceScale)) continue;
                    if (!duplicate.TryGetValue(key, out var bucket)) duplicate[key] = bucket = new List<int>();
                    bucket.Add(found.Count); found.Add(node);
                    if (found.Count > MaximumNodes)
                    { m_QueryBudget = -1; lastBakeReport = "Node limit exceeded; use smaller bounds or larger spacing. Previous graph preserved."; return false; }
                }
            }
            // Spatial buckets bound candidate links to nearby contacts, including both sides of a corner.
            var cells = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < found.Count; i++)
            {
                var key = Cell(found[i].Point, spacing);
                if (!cells.TryGetValue(key, out var bucket)) cells[key] = bucket = new List<int>();
                bucket.Add(i);
            }
            float cornerReach = spacing * 2.9f + SurfaceMotor.Height * clearanceScale;
            int range = Mathf.CeilToInt(cornerReach / spacing);
            var considered = new HashSet<long>();
            var connected = new HashSet<long>();
            var candidates = new List<int>[14];
            for (int i = 0; i < candidates.Length; i++) candidates[i] = new List<int>();
            for (int i = 0; i < found.Count && !m_BudgetExceeded; i++)
            {
                var key = Cell(found[i].Point, spacing);
                foreach (var list in candidates) list.Clear();
                Vector3 tangent = Vector3.Cross(found[i].Normal, Vector3.up);
                if (tangent.sqrMagnitude < .01f) tangent = Vector3.right;
                tangent.Normalize(); Vector3 bitangent = Vector3.Cross(found[i].Normal, tangent);
                for (int x = -range; x <= range && !m_BudgetExceeded; x++)
                for (int y = -range; y <= range && !m_BudgetExceeded; y++)
                for (int z = -range; z <= range && !m_BudgetExceeded; z++)
                {
                    if (!cells.TryGetValue(key + new Vector3Int(x, y, z), out var bucket)) continue;
                    foreach (int j in bucket)
                    {
                        if (j == i) continue;
                        float dot = Vector3.Dot(found[i].Normal, found[j].Normal);
                        // This indoor graph needs coplanar travel and right-angle
                        // wall transitions, not every decorative bevel on furniture.
                        if (dot <= .94f && Mathf.Abs(dot) > .12f) continue;
                        float distance = Vector3.Distance(found[i].Point, found[j].Point);
                        if (dot < -.7f || distance > (dot > .94f ? spacing * 1.6f : cornerReach)) continue;
                        Vector3 delta = found[j].Point - found[i].Point;
                        int group;
                        if (dot > .94f)
                        {
                            if (Mathf.Abs(Vector3.Dot(delta, found[i].Normal)) > .16f * clearanceScale + .035f) continue;
                            float angle = Mathf.Atan2(Vector3.Dot(delta, bitangent), Vector3.Dot(delta, tangent));
                            group = Mathf.FloorToInt((angle + Mathf.PI) * 8 / (Mathf.PI * 2)) % 8;
                        }
                        else
                        {
                            Vector3 n = found[j].Normal;
                            group = 8 + (Mathf.Abs(n.y) > .7f ? (n.y > 0 ? 0 : 1) : Mathf.Abs(n.x) > Mathf.Abs(n.z) ? (n.x > 0 ? 2 : 3) : (n.z > 0 ? 4 : 5));
                        }
                        var list = candidates[group];
                        int at = 0;
                        while (at < list.Count && (found[list[at]].Point - found[i].Point).sqrMagnitude < delta.sqrMagnitude) at++;
                        int limit = 2;
                        if (at < limit) { list.Insert(at, j); if (list.Count > limit) list.RemoveAt(limit); }
                    }
                }
                foreach (var list in candidates)
                    foreach (int j in list)
                    {
                        int a = Mathf.Min(i, j), b = Mathf.Max(i, j);
                        long pair = ((long)a << 32) | (uint)b;
                        if (connected.Contains(pair)) break;
                        if (!considered.Add(pair)) continue;
                        bool flat = Vector3.Dot(found[i].Normal, found[j].Normal) > .94f;
                        if (Traverse(found[i], found[j], clearanceScale, spacing, out float cost) &&
                            (flat || Traverse(found[j], found[i], clearanceScale, spacing, out _)))
                        { builtLinks.Add(new Link { A = i, B = j, Cost = cost }); connected.Add(pair); break; }
                        if (m_BudgetExceeded) break;
                    }
            }
            int usedQueries = MaximumBakeQueries - Mathf.Max(0, m_QueryBudget);
            m_QueryBudget = -1;
            if (m_BudgetExceeded || found.Count == 0 || builtLinks.Count == 0)
            {
                lastBakeReport = m_BudgetExceeded ? $"Graph budget exceeded: {found.Count} nodes, {builtLinks.Count} links, {usedQueries} queries. Previous graph preserved." :
                    "No connected supported surfaces found; previous graph preserved.";
                return false;
            }
            worldBounds = samplingSpace ? new Bounds(samplingSpace.TransformPoint(bounds.center), Vector3.zero) : bounds;
            if (samplingSpace)
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    worldBounds.Encapsulate(samplingSpace.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))));
            sampleSpacing = spacing; bakedScale = clearanceScale;
            nodes = found.ToArray(); links = builtLinks.ToArray(); m_Adjacency = null; m_Revision++;
            lastBakeReport = $"{nodes.Length} supported nodes, {links.Length} links, {usedQueries} queries; spacing {spacing:F2} m, body scale {clearanceScale:F2}.";
            return true;
        }

        /// <summary>Returns false for missing/unreachable/invalid paths. The caller must not replace
        /// failure with a direct vector through walls. 'now' is the enemy's server simulation clock.</summary>
        public bool TryGetSteering(Route route, SurfaceMotor motor, Vector3 goal, float now, out Vector3 bodyGoal)
        {
            bodyGoal = motor != null ? motor.Current.Position : goal;
            if (route == null || motor == null || !isActiveAndEnabled || nodes.Length == 0 || !Finite(goal)) return false;
            if (motor.Detached) { route.LastFailure = "Detached"; return false; }
            if (route.Owner != this || route.Revision != m_Revision || Mathf.Abs(route.Scale - motor.Size) > .001f)
            { route.Clear(); route.Owner = this; route.Revision = m_Revision; route.Scale = motor.Size; }
            bool moved = (goal - route.Goal).sqrMagnitude > sampleSpacing * sampleSpacing;
            if (now >= route.ForgetBlockedAt || moved)
            { route.Blocked.Clear(); route.ForgetBlockedAt = now + 4; }
            // The intermediate arc normal may match neither endpoint. Finish an already chosen
            // corner before selecting a new start node, otherwise replanning can stop mid-arc.
            if (!motor.Transitioning && (now >= route.ReplanAt || moved))
            {
                route.Goal = goal; route.ReplanAt = now + (route.HasPath ? 1.5f : .65f);
                if (!Plan(route, motor.Current, goal, motor.Size)) return false;
            }
            if (!route.HasPath) return false;
            float arrival = Mathf.Clamp(sampleSpacing * .32f, .12f, .32f);
            while (route.Cursor < route.Path.Count - 1 &&
                Vector3.Distance(motor.Current.Position, nodes[route.Path[route.Cursor]].BodyPosition(motor.Size)) <= arrival &&
                Vector3.Dot(motor.Current.Up, nodes[route.Path[route.Cursor]].Normal) > .8f) route.Cursor++;
            var next = nodes[route.Path[route.Cursor]];
            bodyGoal = next.BodyPosition(motor.Size);
            if (!motor.Transitioning && now >= route.ValidateAt)
            {
                route.ValidateAt = now + .35f;
                var from = route.Cursor > 0 ? nodes[route.Path[route.Cursor - 1]] :
                    new Node { Point = motor.Current.Position - motor.Current.Up * motor.BodyHeight, Normal = motor.Current.Up };
                if (!Traverse(from, next, motor.Size, sampleSpacing, out _))
                {
                    if (route.Cursor > 0) BlockLink(route, route.Path[route.Cursor - 1], route.Path[route.Cursor]);
                    route.Path.Clear(); route.ReplanAt = now + .15f; route.LastFailure = "Surface or clearance changed";
                    return false;
                }
            }
            route.LastFailure = null;
            return true;
        }

        public bool TryFindPerch(Vector3 origin, Vector3 target, float radius, out Vector3 bodyPosition, float scale = 1)
        {
            bodyPosition = origin;
            EnsureAdjacency();
            int start = Nearest(origin, Vector3.zero, scale, false);
            if (start < 0) return false;
            // Topological reachability first; candidate contacts still need current support/visibility.
            var reachable = new bool[nodes.Length];
            var pending = new Queue<int>(); pending.Enqueue(start); reachable[start] = true;
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                foreach (int edge in m_Adjacency[current])
                {
                    int next = Other(links[edge], current);
                    if (!reachable[next]) { reachable[next] = true; pending.Enqueue(next); }
                }
            }
            float best = float.PositiveInfinity;
            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i]; var body = node.BodyPosition(scale);
                if (!reachable[i] || node.Normal.y > -.65f || Vector3.Distance(origin, body) > radius || body.y < target.y + .8f) continue;
                float score = Vector3.Distance(body, target) + Vector3.Distance(body, origin) * .15f;
                if (score >= best || !Support(node, scale) || !Visible(body, target)) continue;
                best = score; bodyPosition = body;
            }
            return best < float.PositiveInfinity;
        }

        private bool Plan(Route route, SurfaceMotor.Pose pose, Vector3 goal, float scale)
        {
            route.Path.Clear(); route.Cursor = 0;
            EnsureAdjacency();
            int start = Nearest(pose.Position, pose.Up, scale, true);
            int end = Nearest(goal, Vector3.zero, scale, false);
            if (start < 0 || end < 0)
            { route.LastFailure = "No visible supported route endpoint"; return false; }
            int count = nodes.Length;
            if (route.Costs.Length != count)
            { route.Costs = new float[count]; route.Parents = new int[count]; route.Closed = new bool[count]; }
            for (int i = 0; i < count; i++) { route.Costs[i] = float.PositiveInfinity; route.Parents[i] = -1; route.Closed[i] = false; }
            route.Heap.Clear(); route.Priorities.Clear(); route.Costs[start] = 0;
            Push(route, start, 0);
            int expanded = 0;
            while (route.Heap.Count > 0 && expanded++ < MaximumNodes * 8)
            {
                int current = Pop(route);
                if (route.Closed[current]) continue;
                if (current == end)
                {
                    for (int at = end; at >= 0; at = route.Parents[at]) route.Path.Add(at);
                    route.Path.Reverse(); route.ValidateAt = 0; route.LastFailure = null; return true;
                }
                route.Closed[current] = true;
                foreach (int edge in m_Adjacency[current])
                {
                    if (route.Blocked.Contains(edge)) continue;
                    int next = Other(links[edge], current);
                    if (route.Closed[next]) continue;
                    float cost = route.Costs[current] + links[edge].Cost;
                    if (cost >= route.Costs[next]) continue;
                    route.Costs[next] = cost; route.Parents[next] = current;
                    Push(route, next, cost + Vector3.Distance(nodes[next].Point, nodes[end].Point));
                }
            }
            route.LastFailure = "Unreachable surface component"; return false;
        }

        private int Nearest(Vector3 body, Vector3 normal, float scale, bool requireTraverse)
        {
            int nearest = -1;
            float best = Mathf.Pow(sampleSpacing * 2.5f + SurfaceMotor.Height * scale, 2);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (m_Adjacency != null && m_Adjacency[i].Count == 0) continue;
                if (normal.sqrMagnitude > .1f && Vector3.Dot(normal, nodes[i].Normal) < .8f) continue;
                float distance = (nodes[i].BodyPosition(scale) - body).sqrMagnitude;
                if (distance >= best || !Visible(body, nodes[i].BodyPosition(scale))) continue;
                if (requireTraverse && !Traverse(new Node { Point = body - normal * (SurfaceMotor.Height * scale), Normal = normal },
                    nodes[i], scale, sampleSpacing, out _)) continue;
                if (!requireTraverse && !Support(nodes[i], scale)) continue;
                best = distance; nearest = i;
            }
            return nearest;
        }

        private bool Traverse(Node a, Node b, float scale, float spacing, out float cost)
        {
            cost = Vector3.Distance(a.Point, b.Point);
            float dot = Vector3.Dot(a.Normal, b.Normal);
            if (dot > .94f)
                return Mathf.Abs(Vector3.Dot(b.Point - a.Point, a.Normal)) < .16f * scale + .035f &&
                    Flat(a.BodyPosition(scale), b.BodyPosition(scale), a.Normal, scale, spacing);
            if (dot < -.7f || dot > .966f || !Support(a, scale) || !Support(b, scale)) return false;
            float height = SurfaceMotor.Height * scale, turnRadius = .42f * scale;
            bool convex = Vector3.Dot(b.Point - a.Point, a.Normal) < 0;
            Vector3 p = a.BodyPosition(scale), target = b.BodyPosition(scale);
            Vector3 direction = Vector3.ProjectOnPlane(target - p, a.Normal).normalized;
            Vector3 axis = Vector3.Cross(a.Normal, b.Normal).normalized;
            Vector3 incoming = Vector3.Cross(axis, a.Normal).normalized;
            if (Vector3.Dot(incoming, direction) < 0) { incoming = -incoming; axis = -axis; }
            if (Vector3.Dot(incoming, direction) < .35f) return false;
            float planeA = convex ? -height : 0;
            float planeB = Vector3.Dot(b.Point - p, b.Normal) + (convex ? 0 : height);
            float denominator = 1 - dot * dot;
            Vector3 intersection = p + a.Normal * ((planeA - dot * planeB) / denominator) +
                b.Normal * ((planeB - dot * planeA) / denominator);
            float angle = Vector3.SignedAngle(a.Normal, b.Normal, axis);
            Vector3 entry, center, radius;
            if (convex) { center = intersection; radius = a.Normal * height; entry = center + radius; }
            else
            {
                entry = intersection - incoming * (turnRadius * Mathf.Tan(Mathf.Abs(angle) * .5f * Mathf.Deg2Rad));
                center = entry + Quaternion.AngleAxis(angle > 0 ? 90 : -90, axis) * incoming * turnRadius;
                radius = entry - center;
            }
            if (Vector3.Dot(entry - p, incoming) < -.08f || !Flat(p, entry, a.Normal, scale, spacing)) return false;
            Vector3 previous = entry;
            for (int i = 1; i <= 8; i++)
            {
                Vector3 next = center + Quaternion.AngleAxis(angle * i / 8f, axis) * radius;
                if (!Clear(previous, next, scale)) return false;
                previous = next;
            }
            if (!Flat(previous, target, b.Normal, scale, spacing)) return false;
            cost = Vector3.Distance(p, entry) + radius.magnitude * Mathf.Abs(angle) * Mathf.Deg2Rad + Vector3.Distance(previous, target);
            return true;
        }

        private bool Flat(Vector3 from, Vector3 to, Vector3 normal, float scale, float spacing)
        {
            if (!Clear(from, to, scale)) return false;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / Mathf.Min(.3f, spacing * .4f)));
            if (steps > 32) return false;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 body = Vector3.Lerp(from, to, i / (float)steps);
                if (!Ray(body + normal * .04f, -normal, SurfaceMotor.Height * scale + .12f, out var hit) ||
                    Vector3.Dot(hit.normal, normal) < .9f || Mathf.Abs(hit.distance - SurfaceMotor.Height * scale - .04f) > .07f) return false;
            }
            return true;
        }

        private bool Support(Node node, float scale)
        {
            if (!Ray(node.Point + node.Normal * .08f, -node.Normal, .16f, out var hit) ||
                (hit.point - node.Point).sqrMagnitude > .0016f || Vector3.Dot(hit.normal, node.Normal) < .94f) return false;
            return SpendQuery() && !UnityEngine.Physics.CheckSphere(node.BodyPosition(scale), SurfaceMotor.BodyRadius * scale,
                Mask, QueryTriggerInteraction.Ignore);
        }
        private bool Clear(Vector3 from, Vector3 to, float scale)
        {
            if (!SpendQuery() || UnityEngine.Physics.CheckSphere(from, SurfaceMotor.BodyRadius * scale, Mask, QueryTriggerInteraction.Ignore)) return false;
            Vector3 delta = to - from;
            return delta.sqrMagnitude < .000001f || SpendQuery() && !UnityEngine.Physics.SphereCast(from, SurfaceMotor.BodyRadius * scale,
                delta.normalized, out _, delta.magnitude, Mask, QueryTriggerInteraction.Ignore);
        }
        private bool Visible(Vector3 from, Vector3 to) => SpendQuery() &&
            !UnityEngine.Physics.Linecast(from, to, Mask, QueryTriggerInteraction.Ignore);
        private bool Ray(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = default;
            return SpendQuery() && UnityEngine.Physics.Raycast(origin, direction, out hit, distance, Mask, QueryTriggerInteraction.Ignore);
        }
        private bool SpendQuery()
        {
            if (m_QueryBudget < 0) return true;
            if (m_QueryBudget > 0) { m_QueryBudget--; return true; }
            m_BudgetExceeded = true; return false;
        }
        private void EnsureAdjacency()
        {
            if (m_Adjacency != null && m_Adjacency.Length == nodes.Length) return;
            m_Adjacency = new List<int>[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) m_Adjacency[i] = new List<int>();
            for (int i = 0; i < links.Length; i++)
            {
                var edge = links[i];
                if (edge.A < 0 || edge.B < 0 || edge.A >= nodes.Length || edge.B >= nodes.Length) continue;
                m_Adjacency[edge.A].Add(i); m_Adjacency[edge.B].Add(i);
            }
        }
        private void BlockLink(Route route, int a, int b)
        {
            foreach (int edge in m_Adjacency[a]) if (Other(links[edge], a) == b) route.Blocked.Add(edge);
        }
        private static int Other(Link edge, int node) => edge.A == node ? edge.B : edge.A;
        private static Vector3Int Cell(Vector3 position, float size) => new Vector3Int(
            Mathf.FloorToInt(position.x / size), Mathf.FloorToInt(position.y / size), Mathf.FloorToInt(position.z / size));
        private static double CellCount(Vector3 size, float spacing) =>
            (Math.Ceiling(size.x / spacing) + 1) * (Math.Ceiling(size.y / spacing) + 1) * (Math.Ceiling(size.z / spacing) + 1);
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        private static void Push(Route route, int node, float priority)
        {
            int child = route.Heap.Count; route.Heap.Add(node); route.Priorities.Add(priority);
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (route.Priorities[parent] <= priority) break;
                route.Heap[child] = route.Heap[parent]; route.Priorities[child] = route.Priorities[parent]; child = parent;
            }
            route.Heap[child] = node; route.Priorities[child] = priority;
        }
        private static int Pop(Route route)
        {
            int result = route.Heap[0], last = route.Heap.Count - 1;
            int node = route.Heap[last]; float priority = route.Priorities[last];
            route.Heap.RemoveAt(last); route.Priorities.RemoveAt(last);
            if (last == 0) return result;
            int parent = 0;
            while (parent * 2 + 1 < last)
            {
                int child = parent * 2 + 1;
                if (child + 1 < last && route.Priorities[child + 1] < route.Priorities[child]) child++;
                if (route.Priorities[child] >= priority) break;
                route.Heap[parent] = route.Heap[child]; route.Priorities[parent] = route.Priorities[child]; parent = child;
            }
            route.Heap[parent] = node; route.Priorities[parent] = priority;
            return result;
        }
    }
}
