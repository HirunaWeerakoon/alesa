using System;
using System.Collections.Generic;
using System.Linq;

namespace Alesa.Core
{
    /// <summary>
    /// Pure C# implementation of Yen's K-Shortest Simple (Loopless) Paths algorithm.
    /// Computes up to K ranked alternative routes in non-decreasing order of total cost.
    /// Preserves original graph state without mutating edges or nodes during spur calculations.
    /// </summary>
    public sealed class YenKShortestPaths
    {
        private readonly AStarPathfinder _pathfinder;

        public YenKShortestPaths(AStarPathfinder pathfinder = null)
        {
            _pathfinder = pathfinder ?? new AStarPathfinder();
        }

        /// <summary>
        /// Finds up to K distinct, loopless paths from startNodeId to goalNodeId.
        /// </summary>
        /// <param name="graph">The waypoint graph to search.</param>
        /// <param name="startNodeId">Origin node ID.</param>
        /// <param name="goalNodeId">Destination node ID.</param>
        /// <param name="k">Maximum number of alternative paths to return (default 3).</param>
        public MultiRouteResult FindKShortestPaths(WaypointGraph graph, string startNodeId, string goalNodeId, int k = 3)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (k <= 0) throw new ArgumentOutOfRangeException(nameof(k), "K must be greater than zero.");

            if (graph.NodeCount == 0)
                return MultiRouteResult.Failure(RouteStatus.EmptyGraph, "Graph contains no nodes.", graph.Version);

            if (string.IsNullOrWhiteSpace(startNodeId))
                return MultiRouteResult.Failure(RouteStatus.StartNodeNotFound, "Start node ID is null or whitespace.", graph.Version);

            if (string.IsNullOrWhiteSpace(goalNodeId))
                return MultiRouteResult.Failure(RouteStatus.GoalNodeNotFound, "Goal node ID is null or whitespace.", graph.Version);

            startNodeId = startNodeId.Trim();
            goalNodeId = goalNodeId.Trim();

            if (!graph.ContainsNode(startNodeId))
                return MultiRouteResult.Failure(RouteStatus.StartNodeNotFound, $"Start node '{startNodeId}' does not exist.", graph.Version);

            if (!graph.ContainsNode(goalNodeId))
                return MultiRouteResult.Failure(RouteStatus.GoalNodeNotFound, $"Goal node '{goalNodeId}' does not exist.", graph.Version);

            // Trivial case: start equals goal
            if (string.Equals(startNodeId, goalNodeId, StringComparison.OrdinalIgnoreCase))
            {
                var trivialRoute = RouteResult.Success(new[] { startNodeId }, 0f, graph.Version);
                return MultiRouteResult.Success(new[] { trivialRoute }, graph.Version);
            }

            // Step 1: Find the 1st shortest path using A*
            var firstRoute = _pathfinder.FindPath(graph, startNodeId, goalNodeId);
            if (!firstRoute.IsValid)
            {
                return MultiRouteResult.Failure(firstRoute.Status, firstRoute.Message, graph.Version);
            }

            // A holds the determined K-shortest paths
            var A = new List<RouteResult> { firstRoute };

            if (k == 1)
            {
                return MultiRouteResult.Success(A, graph.Version);
            }

            // B holds potential candidate paths (ordered by total cost)
            var B = new List<RouteResult>();
            var seenPathSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                GetPathSignature(firstRoute.NodeIds)
            };

            // Step 2: Iterate to find remaining K - 1 paths
            for (int kIndex = 1; kIndex < k; kIndex++)
            {
                var previousPath = A[kIndex - 1].NodeIds;

                // Iterate over every node in the previous path as a spur node (except the destination)
                for (int i = 0; i < previousPath.Count - 1; i++)
                {
                    string spurNode = previousPath[i];

                    // Root path is the sub-path from start to spur node
                    var rootPath = new List<string>();
                    for (int r = 0; r <= i; r++)
                    {
                        rootPath.Add(previousPath[r]);
                    }

                    float rootCost = CalculatePathCost(graph, rootPath);
                    if (float.IsPositiveInfinity(rootCost))
                        continue;

                    // Temporary exclusions for this spur calculation
                    var excludedEdges = new HashSet<(string from, string to)>();
                    var excludedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // 1. Exclude edges used by previously found paths in A that share the identical root path
                    for (int a = 0; a < A.Count; a++)
                    {
                        var candidate = A[a].NodeIds;
                        if (candidate.Count > i && HasMatchingPrefix(candidate, rootPath))
                        {
                            excludedEdges.Add((candidate[i], candidate[i + 1]));
                        }
                    }

                    // 2. Exclude all nodes in root path except spurNode to prevent cycles (loopless path)
                    for (int n = 0; n < rootPath.Count - 1; n++)
                    {
                        excludedNodes.Add(rootPath[n]);
                    }

                    // Calculate spur path from spurNode to goalNodeId
                    var spurResult = _pathfinder.FindPath(graph, spurNode, goalNodeId, excludedEdges, excludedNodes);

                    if (spurResult.IsValid)
                    {
                        // Assemble total candidate path: rootPath + spurPath (omitting spurNode duplicate)
                        var totalPathNodes = new List<string>(rootPath);
                        for (int s = 1; s < spurResult.NodeIds.Count; s++)
                        {
                            totalPathNodes.Add(spurResult.NodeIds[s]);
                        }

                        // Ensure path is strictly simple / loopless
                        if (IsLoopless(totalPathNodes))
                        {
                            string signature = GetPathSignature(totalPathNodes);
                            if (!seenPathSignatures.Contains(signature))
                            {
                                seenPathSignatures.Add(signature);
                                float totalCost = rootCost + spurResult.TotalCost;
                                var candidateRoute = RouteResult.Success(totalPathNodes, totalCost, graph.Version);
                                B.Add(candidateRoute);
                            }
                        }
                    }
                }

                if (B.Count == 0)
                {
                    // No more alternative paths exist in the graph
                    break;
                }

                // Sort candidate paths in B by total cost ascending (with deterministic tie-breaking)
                B.Sort(CompareRoutes);

                // Add lowest-cost candidate from B to A
                var bestCandidate = B[0];
                B.RemoveAt(0);
                A.Add(bestCandidate);
            }

            return MultiRouteResult.Success(A, graph.Version);
        }

        private static bool HasMatchingPrefix(IReadOnlyList<string> fullPath, IReadOnlyList<string> prefix)
        {
            if (fullPath.Count < prefix.Count) return false;
            for (int i = 0; i < prefix.Count; i++)
            {
                if (!string.Equals(fullPath[i], prefix[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static float CalculatePathCost(WaypointGraph graph, IReadOnlyList<string> path)
        {
            float total = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                if (!graph.TryGetEdge(path[i], path[i + 1], out var edge) || !edge.IsTraversable)
                    return float.PositiveInfinity;

                total += edge.TraversalCost;
            }
            return total;
        }

        private static bool IsLoopless(IReadOnlyList<string> nodes)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!set.Add(nodes[i]))
                    return false;
            }
            return true;
        }

        private static string GetPathSignature(IReadOnlyList<string> nodes)
        {
            return string.Join(">", nodes);
        }

        private static int CompareRoutes(RouteResult a, RouteResult b)
        {
            int cmp = a.TotalCost.CompareTo(b.TotalCost);
            if (cmp != 0) return cmp;

            // Deterministic tie-breaking: prefer fewer hops, then lexicographical order
            int hopCmp = a.NodeCount.CompareTo(b.NodeCount);
            if (hopCmp != 0) return hopCmp;

            return string.Compare(GetPathSignature(a.NodeIds), GetPathSignature(b.NodeIds), StringComparison.Ordinal);
        }
    }
}
