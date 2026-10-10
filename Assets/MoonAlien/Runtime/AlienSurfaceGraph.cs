using System;
using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    public enum AlienPathResult { Found, NoRoute, BudgetExhausted, InvalidInput, OutputTooSmall }

    /// <summary>
    /// A small authored, directed surface graph. Edges are author assertions, not proof of
    /// contact or body clearance; the motor must still validate every proposed pose.
    /// Not thread safe: each instance owns reusable search scratch buffers.
    /// </summary>
    public sealed class AlienSurfaceGraph
    {
        public const int MaxNodes = 128;
        public const int MaxEdges = 512;

        public readonly struct Node
        {
            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            internal Node(Vector3 position, Vector3 normal) { Position = position; Normal = normal; }
        }

        public readonly struct Edge
        {
            public readonly int From;
            public readonly int To;
            public readonly float TravelCost;
            public readonly float ExposureCost;
            public readonly bool Enabled;
            internal Edge(int from, int to, float travelCost, float exposureCost, bool enabled)
            { From = from; To = to; TravelCost = travelCost; ExposureCost = exposureCost; Enabled = enabled; }
        }

        private readonly Node[] nodes = new Node[MaxNodes];
        private readonly Edge[] edges = new Edge[MaxEdges];
        private readonly double[] distances = new double[MaxNodes];
        private readonly int[] previous = new int[MaxNodes];
        private readonly bool[] settled = new bool[MaxNodes];
        public int NodeCount { get; private set; }
        public int EdgeCount { get; private set; }

        // Indices never move. Invalid authoring or capacity exhaustion returns -1.
        public int AddNode(Vector3 position, Vector3 normal)
        {
            float normalLengthSquared = normal.sqrMagnitude;
            if (NodeCount == MaxNodes || !Finite(position) || !Finite(normal) ||
                !Finite(normalLengthSquared) || normalLengthSquared < .000001f) return -1;
            int index = NodeCount++;
            nodes[index] = new Node(position, normal.normalized);
            return index;
        }

        public int AddDirectedEdge(int from, int to, float travelCost, float exposureCost = 0f, bool enabled = true)
        {
            if (EdgeCount == MaxEdges || !ValidNode(from) || !ValidNode(to) ||
                !Finite(travelCost) || !Finite(exposureCost) || travelCost < 0f || exposureCost < 0f) return -1;
            int index = EdgeCount++;
            edges[index] = new Edge(from, to, travelCost, exposureCost, enabled);
            return index;
        }

        public bool SetEdgeEnabled(int index, bool enabled)
        {
            if (index < 0 || index >= EdgeCount) return false;
            Edge edge = edges[index];
            edges[index] = new Edge(edge.From, edge.To, edge.TravelCost, edge.ExposureCost, enabled);
            return true;
        }

        public bool SetEdgeExposure(int index, float exposureCost)
        {
            if (index < 0 || index >= EdgeCount || !Finite(exposureCost) || exposureCost < 0f) return false;
            Edge edge = edges[index];
            edges[index] = new Edge(edge.From, edge.To, edge.TravelCost, exposureCost, edge.Enabled);
            return true;
        }

        public Node GetNode(int index)
        {
            if (!ValidNode(index)) throw new ArgumentOutOfRangeException(nameof(index));
            return nodes[index];
        }

        public Edge GetEdge(int index)
        {
            if (index < 0 || index >= EdgeCount) throw new ArgumentOutOfRangeException(nameof(index));
            return edges[index];
        }

        public int FindNearestNode(Vector3 position)
        {
            if (!Finite(position)) return -1;
            int best = -1;
            double bestDistance = double.PositiveInfinity;
            for (int i = 0; i < NodeCount; i++)
            {
                double x = (double)position.x - nodes[i].Position.x;
                double y = (double)position.y - nodes[i].Position.y;
                double z = (double)position.z - nodes[i].Position.z;
                double distance = x * x + y * y + z * z;
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Deterministic Dijkstra with nonnegative travel + exposure costs. The lowest
        /// node index wins equal-distance frontier ties; edge insertion order cannot
        /// change that choice. Budget counts settled nodes, including the goal.
        /// Work is bounded by 128 node scans and 512 edge scans per settled node.
        /// An already-reached goal succeeds with zero expansions, even with zero budget.
        /// Successful output includes start and goal. Every failure returns count=0.
        /// No allocations after construction when the caller reuses its output buffer.
        /// </summary>
        public AlienPathResult FindPath(int start, int goal, int maxExpansions,
            int[] output, out int count, out int expansions)
        {
            count = 0;
            expansions = 0;
            if (!ValidNode(start) || !ValidNode(goal) || maxExpansions < 0 || output == null)
                return AlienPathResult.InvalidInput;
            if (start == goal)
            {
                if (output.Length == 0) return AlienPathResult.OutputTooSmall;
                output[0] = start;
                count = 1;
                return AlienPathResult.Found;
            }
            int budget = Math.Min(maxExpansions, MaxNodes);
            for (int i = 0; i < NodeCount; i++)
            { distances[i] = double.PositiveInfinity; previous[i] = -1; settled[i] = false; }
            distances[start] = 0d;

            while (true)
            {
                int current = -1;
                double best = double.PositiveInfinity;
                for (int i = 0; i < NodeCount; i++)
                {
                    if (!settled[i] && distances[i] < best) { best = distances[i]; current = i; }
                }
                if (current < 0) return AlienPathResult.NoRoute;
                if (expansions >= budget) return AlienPathResult.BudgetExhausted;
                settled[current] = true;
                expansions++;
                if (current == goal) return WritePath(start, goal, output, out count);

                for (int i = 0; i < EdgeCount; i++)
                {
                    Edge edge = edges[i];
                    if (!edge.Enabled || edge.From != current || settled[edge.To]) continue;
                    double candidate = best + edge.TravelCost + edge.ExposureCost;
                    if (candidate < distances[edge.To] ||
                        (candidate == distances[edge.To] && current < previous[edge.To]))
                    { distances[edge.To] = candidate; previous[edge.To] = current; }
                }
            }
        }

        private AlienPathResult WritePath(int start, int goal, int[] output, out int count)
        {
            count = 0;
            int length = 1;
            int node = goal;
            while (node != start && length <= NodeCount)
            {
                node = previous[node];
                if (node < 0) return AlienPathResult.NoRoute;
                length++;
            }
            if (length > NodeCount) return AlienPathResult.NoRoute;
            if (output.Length < length) return AlienPathResult.OutputTooSmall;
            node = goal;
            for (int i = length - 1; i >= 0; i--) { output[i] = node; node = previous[node]; }
            count = length;
            return AlienPathResult.Found;
        }

        private bool ValidNode(int index) => index >= 0 && index < NodeCount;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
