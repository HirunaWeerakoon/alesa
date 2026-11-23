using System;
using System.Collections.Generic;

namespace Alesa.Core
{
    /// <summary>
    /// Pure C# implementation of the A* (A-Star) shortest path algorithm.
    /// Uses Euclidean straight-line distance as an admissible heuristic: f(n) = g(n) + h(n).
    /// Guarantees optimal shortest paths on non-negative weighted graphs with loop prevention.
    /// </summary>
    public sealed class AStarPathfinder
    {
        private readonly Func<WaypointNode, WaypointNode, float> _customHeuristic;

        public AStarPathfinder(Func<WaypointNode, WaypointNode, float> customHeuristic = null)
        {
            _customHeuristic = customHeuristic;
        }

        /// <summary>
        /// Finds the minimum-cost route between startNodeId and goalNodeId.
        /// </summary>
        /// <param name="graph">The waypoint graph to search.</param>
        /// <param name="startNodeId">Origin node ID.</param>
        /// <param name="goalNodeId">Destination node ID.</param>
        /// <param name="excludedEdges">Optional set of (from, to) edges to ignore (e.g. for Yen's spur paths).</param>
        /// <param name="excludedNodes">Optional set of node IDs to ignore.</param>
        public RouteResult FindPath(
            WaypointGraph graph,
            string startNodeId,
            string goalNodeId,
            ISet<(string from, string to)> excludedEdges = null,
            ISet<string> excludedNodes = null)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (graph.NodeCount == 0)
                return RouteResult.Failure(RouteStatus.EmptyGraph, "Graph contains no nodes.", graph.Version);

            if (string.IsNullOrWhiteSpace(startNodeId))
                return RouteResult.Failure(RouteStatus.StartNodeNotFound, "Start node ID is null or empty.", graph.Version);

            if (string.IsNullOrWhiteSpace(goalNodeId))
                return RouteResult.Failure(RouteStatus.GoalNodeNotFound, "Goal node ID is null or empty.", graph.Version);

            startNodeId = startNodeId.Trim();
            goalNodeId = goalNodeId.Trim();

            if (!graph.TryGetNode(startNodeId, out var startNode))
                return RouteResult.Failure(RouteStatus.StartNodeNotFound, $"Start node '{startNodeId}' does not exist.", graph.Version);

            if (!graph.TryGetNode(goalNodeId, out var goalNode))
                return RouteResult.Failure(RouteStatus.GoalNodeNotFound, $"Goal node '{goalNodeId}' does not exist.", graph.Version);

            if (excludedNodes != null)
            {
                if (excludedNodes.Contains(startNodeId))
                    return RouteResult.Failure(RouteStatus.NoPath, $"Start node '{startNodeId}' is excluded.", graph.Version);
                if (excludedNodes.Contains(goalNodeId))
                    return RouteResult.Failure(RouteStatus.NoPath, $"Goal node '{goalNodeId}' is excluded.", graph.Version);
            }

            // Trivial case: already at destination
            if (string.Equals(startNodeId, goalNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return RouteResult.Success(new[] { startNodeId }, 0f, graph.Version);
            }

            // gScore[n]: Cost of cheapest path from start to n currently known
            var gScore = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            // cameFrom[n]: Immediately preceding node on cheapest known path to n
            var cameFrom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // closedSet: Nodes whose shortest path has already been confirmed
            var closedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // openSet: Priority queue ordered by fScore = gScore + hScore
            var openSet = new MinHeap<string, float>(initialCapacity: 32);

            gScore[startNodeId] = 0f;
            float initialH = CalculateHeuristic(startNode, goalNode);
            openSet.Enqueue(startNodeId, initialH);

            while (!openSet.IsEmpty)
            {
                string currentId = openSet.Dequeue();

                // If already processed with an optimal path, skip stale duplicate heap entries
                if (closedSet.Contains(currentId))
                    continue;

                // Reached destination!
                if (string.Equals(currentId, goalNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return ReconstructPath(cameFrom, currentId, gScore[currentId], graph.Version);
                }

                closedSet.Add(currentId);

                float currentG = gScore[currentId];
                var outgoingEdges = graph.GetOutgoingEdges(currentId);

                for (int i = 0; i < outgoingEdges.Count; i++)
                {
                    var edge = outgoingEdges[i];

                    // Check if edge is traversable
                    if (!edge.IsTraversable)
                        continue;

                    string neighborId = edge.ToNodeId;

                    if (closedSet.Contains(neighborId))
                        continue;

                    if (excludedNodes != null && excludedNodes.Contains(neighborId))
                        continue;

                    if (excludedEdges != null && (
                        excludedEdges.Contains((edge.FromNodeId, edge.ToNodeId)) ||
                        excludedEdges.Contains((edge.ToNodeId, edge.FromNodeId))))
                    {
                        continue;
                    }

                    float tentativeG = currentG + edge.TraversalCost;

                    if (!gScore.TryGetValue(neighborId, out float existingG) || tentativeG < existingG)
                    {
                        cameFrom[neighborId] = currentId;
                        gScore[neighborId] = tentativeG;

                        if (!graph.TryGetNode(neighborId, out var neighborNode))
                            continue;

                        float h = CalculateHeuristic(neighborNode, goalNode);
                        float f = tentativeG + h;

                        openSet.Enqueue(neighborId, f);
                    }
                }
            }

            // Open set exhausted without reaching goal -> Graph is disconnected or blocked
            return RouteResult.Failure(RouteStatus.NoPath, $"No valid path exists from '{startNodeId}' to '{goalNodeId}'.", graph.Version);
        }

        private float CalculateHeuristic(WaypointNode from, WaypointNode to)
        {
            if (_customHeuristic != null)
                return _customHeuristic(from, to);

            // Euclidean distance in physical space is strictly admissible
            // because a straight line is the shortest possible path between two points.
            return from.Position.DistanceTo(to.Position);
        }

        private static RouteResult ReconstructPath(
            Dictionary<string, string> cameFrom,
            string currentId,
            float totalCost,
            int graphVersion)
        {
            var path = new List<string> { currentId };
            while (cameFrom.TryGetValue(currentId, out string previousId))
            {
                path.Add(previousId);
                currentId = previousId;
            }

            path.Reverse();
            return RouteResult.Success(path, totalCost, graphVersion);
        }
    }
}

