using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alesa.Core.Graph
{
    public sealed class PathResult
    {
        public static readonly PathResult None = new PathResult(Array.Empty<int>(), float.PositiveInfinity);

        public IReadOnlyList<int> Nodes { get; }
        public float Cost { get; }
        public bool Found => Nodes.Count > 0;

        public PathResult(IReadOnlyList<int> nodes, float cost)
        {
            Nodes = nodes;
            Cost = cost;
        }
    }

    /// <summary>Result of a single-source (or multi-source) Dijkstra flood.</summary>
    public sealed class CostField
    {
        public float[] Cost { get; }
        public int[] Previous { get; }

        public CostField(float[] cost, int[] previous)
        {
            Cost = cost;
            Previous = previous;
        }

        public bool Reached(int node) => !float.IsPositiveInfinity(Cost[node]);

        /// <summary>Walks back from <paramref name="target"/> to the nearest source.</summary>
        public PathResult PathTo(int target)
        {
            if (!Reached(target)) return PathResult.None;
            var nodes = new List<int>();
            for (int n = target; n != -1; n = Previous[n]) nodes.Add(n);
            nodes.Reverse();
            return new PathResult(nodes, Cost[target]);
        }
    }

    /// <summary>
    /// Shared informed searches over <see cref="MazeGraph"/>. Agent-specific algorithms
    /// (K-shortest paths, interception, sound propagation, encounter scoring) live in each
    /// agent's own folder and build on these.
    /// </summary>
    public static class GraphSearch
    {
        /// <summary>
        /// A* with a straight-line distance heuristic. The heuristic is admissible only while
        /// every edge cost is at least its geometric length, so cost models should add
        /// penalties, never discount below <see cref="MazeEdge.Length"/>.
        /// </summary>
        public static PathResult AStar(MazeGraph graph, int start, int goal, EdgeCost cost = null)
        {
            cost = cost ?? MazeGraph.WalkingCost;
            int n = graph.Count;
            var g = Filled(n, float.PositiveInfinity);
            var prev = Filled(n, -1);
            var closed = new bool[n];
            var open = new MinHeap<int>();
            var goalPos = graph[goal].Position;

            g[start] = 0f;
            open.Push(start, Vector3.Distance(graph[start].Position, goalPos));

            while (open.Count > 0)
            {
                int current = open.Pop(out _);
                if (closed[current]) continue;
                if (current == goal) return new CostField(g, prev).PathTo(goal);
                closed[current] = true;

                foreach (var edge in graph[current].Neighbours)
                {
                    int next = edge.Other(current);
                    if (closed[next]) continue;
                    float step = cost(edge, current);
                    if (float.IsPositiveInfinity(step)) continue;

                    float tentative = g[current] + step;
                    if (tentative >= g[next]) continue;
                    g[next] = tentative;
                    prev[next] = current;
                    open.Push(next, tentative + Vector3.Distance(graph[next].Position, goalPos));
                }
            }
            return PathResult.None;
        }

        /// <summary>
        /// Dijkstra flood from one or more sources, stopping at <paramref name="maxCost"/>.
        /// Useful for "how long until X can reach every node" (interception) and for
        /// attenuated propagation (sound).
        /// </summary>
        public static CostField Dijkstra(MazeGraph graph, IEnumerable<int> sources, EdgeCost cost = null,
            float maxCost = float.PositiveInfinity)
        {
            var seeds = new List<(int node, float cost)>();
            foreach (int s in sources) seeds.Add((s, 0f));
            return DijkstraFromSeeds(graph, seeds, cost, maxCost);
        }

        /// <summary>
        /// Dijkstra where each seed starts with its own initial cost, e.g. the distance from a point
        /// that lies between nodes (a gunshot mid-corridor) to the nodes around it.
        /// </summary>
        public static CostField DijkstraFromSeeds(MazeGraph graph, IEnumerable<(int node, float cost)> seeds,
            EdgeCost cost = null, float maxCost = float.PositiveInfinity)
        {
            cost = cost ?? MazeGraph.WalkingCost;
            int n = graph.Count;
            var dist = Filled(n, float.PositiveInfinity);
            var prev = Filled(n, -1);
            var heap = new MinHeap<int>();

            foreach (var (node, initial) in seeds)
            {
                if (initial >= dist[node]) continue;
                dist[node] = initial;
                heap.Push(node, initial);
            }

            while (heap.Count > 0)
            {
                int current = heap.Pop(out float d);
                if (d > dist[current]) continue; // stale entry

                foreach (var edge in graph[current].Neighbours)
                {
                    float step = cost(edge, current);
                    if (float.IsPositiveInfinity(step)) continue;
                    int next = edge.Other(current);
                    float nd = d + step;
                    if (nd > maxCost || nd >= dist[next]) continue;
                    dist[next] = nd;
                    prev[next] = current;
                    heap.Push(next, nd);
                }
            }
            return new CostField(dist, prev);
        }

        public static CostField Dijkstra(MazeGraph graph, int source, EdgeCost cost = null,
            float maxCost = float.PositiveInfinity)
            => Dijkstra(graph, new[] { source }, cost, maxCost);

        private static T[] Filled<T>(int n, T value)
        {
            var a = new T[n];
            for (int i = 0; i < n; i++) a[i] = value;
            return a;
        }
    }
}
