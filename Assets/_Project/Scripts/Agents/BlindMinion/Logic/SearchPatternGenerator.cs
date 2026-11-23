using System;
using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.Graph;

namespace Alesa.Agents.BlindMinion
{
    /// <summary>
    /// Generates structured spatial search waypoints around a cold acoustic trail (LKP).
    /// Prevents the minion from simply halting when arriving at an empty noise origin;
    /// instead, generates radial sweep points and graph-constrained corridor inspections.
    /// </summary>
    public static class SearchPatternGenerator
    {
        /// <summary>
        /// Selects candidate maze nodes around the center point within the current uncertainty radius,
        /// prioritized by proximity and outward dispersal.
        /// </summary>
        public static List<int> FindSearchNodes(MazeGraph graph, Vector3 center, float searchRadius, int maxNodes = 4)
        {
            var results = new List<(int node, float dist)>();
            if (graph == null) return new List<int>();

            int nearest = graph.NearestNode(center);
            if (nearest < 0) return new List<int>();

            // Traverse adjacent neighborhood up to search radius
            var visited = new HashSet<int> { nearest };
            var queue = new Queue<int>();
            queue.Enqueue(nearest);

            while (queue.Count > 0)
            {
                int curr = queue.Dequeue();
                float dist = FlatMath.Distance(graph[curr].Position, center);
                if (dist <= searchRadius)
                {
                    results.Add((curr, dist));
                }

                foreach (var edge in graph[curr].Neighbours)
                {
                    if (!edge.Traversable) continue;
                    int other = edge.Other(curr);
                    if (visited.Add(other))
                    {
                        queue.Enqueue(other);
                    }
                }
            }

            // Sort by distance and return top nodes
            results.Sort((a, b) => a.dist.CompareTo(b.dist));
            var output = new List<int>();
            for (int i = 0; i < Math.Min(maxNodes, results.Count); i++)
            {
                output.Add(results[i].node);
            }
            return output;
        }

        /// <summary>
        /// Computes 4-point cardinal search offsets around an LKP for local area sweeps.
        /// Useful when investigating an open room or clearing a junction.
        /// </summary>
        public static Vector3[] GenerateRadialSweepOffsets(Vector3 center, float radius)
        {
            float r = Math.Max(0.5f, radius);
            return new[]
            {
                center + new Vector3(r, 0f, 0f),
                center + new Vector3(0f, 0f, r),
                center + new Vector3(-r, 0f, 0f),
                center + new Vector3(0f, 0f, -r)
            };
        }
    }
}
