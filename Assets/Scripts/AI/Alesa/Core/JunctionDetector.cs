using System;
using System.Collections.Generic;

namespace Alesa.Core
{
    /// <summary>
    /// Pure C# information container representing an upcoming decision junction on the active route.
    /// </summary>
    public readonly struct JunctionInfo : IEquatable<JunctionInfo>
    {
        public string NodeId { get; }
        public int RouteIndex { get; }
        public NodePosition Position { get; }
        public float DistanceAlongRoute { get; }
        public int TraversableDegree { get; }
        public bool IsValid => !string.IsNullOrEmpty(NodeId);

        public JunctionInfo(string nodeId, int routeIndex, NodePosition position, float distanceAlongRoute, int traversableDegree)
        {
            NodeId = nodeId;
            RouteIndex = routeIndex;
            Position = position;
            DistanceAlongRoute = distanceAlongRoute;
            TraversableDegree = traversableDegree;
        }

        public bool Equals(JunctionInfo other)
        {
            return string.Equals(NodeId, other.NodeId, StringComparison.OrdinalIgnoreCase) &&
                   RouteIndex == other.RouteIndex;
        }

        public override bool Equals(object obj) => obj is JunctionInfo other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(NodeId ?? string.Empty), RouteIndex);
        public override string ToString() => IsValid ? $"Junction[{NodeId}] @ Index {RouteIndex}, Dist={DistanceAlongRoute:F1}m, Deg={TraversableDegree}" : "Junction[None]";
    }

    /// <summary>
    /// Pure C# proactive junction detection and route-distance calculator.
    /// Detects upcoming decision intersections along Nathan's active escape route
    /// by evaluating graph node degree, traversable connections, and corridor distance.
    /// </summary>
    public sealed class JunctionDetector
    {
        public float DefaultLookaheadDistance { get; set; } = 5.0f;
        public float TriggerHysteresisBuffer { get; set; } = 0.8f;

        public JunctionDetector(float lookaheadDistance = 5.0f, float triggerHysteresisBuffer = 0.8f)
        {
            DefaultLookaheadDistance = Math.Max(1.0f, lookaheadDistance);
            TriggerHysteresisBuffer = Math.Max(0.1f, triggerHysteresisBuffer);
        }

        /// <summary>
        /// Determines whether a graph node constitutes a navigational decision junction.
        /// A node is classified as a junction if:
        /// 1. It is explicitly authored as a junction (node.IsJunction == true), OR
        /// 2. It has at least 3 currently traversable (unblocked) connections.
        /// </summary>
        public bool IsJunctionNode(WaypointGraph graph, string nodeId, out int traversableDegree)
        {
            traversableDegree = 0;
            if (graph == null || string.IsNullOrWhiteSpace(nodeId))
                return false;

            if (!graph.TryGetNode(nodeId, out var node))
                return false;

            var edges = graph.GetOutgoingEdges(nodeId);
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].IsTraversable)
                {
                    traversableDegree++;
                }
            }

            // A decision intersection requires either explicit authoring OR at least 3 traversable choices.
            // If a 3-way intersection has 1 edge blocked, it degrades to a 2-degree bend (no branching choice).
            if (node.IsJunction && traversableDegree >= 2)
            {
                return true;
            }

            return traversableDegree >= 3;
        }

        /// <summary>
        /// Finds the next upcoming junction on the active route starting from a specified route index.
        /// Ignores the start node if startIndex is 0, since guidance is given before entering a junction.
        /// </summary>
        public bool TryFindNextJunctionOnRoute(
            WaypointGraph graph,
            RouteResult route,
            int currentSegmentStartIndex,
            out int junctionRouteIndex,
            out string junctionNodeId,
            out int traversableDegree)
        {
            junctionRouteIndex = -1;
            junctionNodeId = null;
            traversableDegree = 0;

            if (graph == null || route == null || !route.IsValid || route.NodeCount < 2)
                return false;

            int searchStart = Math.Max(1, currentSegmentStartIndex);

            // Search forward along the route for the first decision point
            for (int i = searchStart; i < route.NodeCount; i++)
            {
                string candidateId = route.NodeIds[i];
                if (IsJunctionNode(graph, candidateId, out int degree))
                {
                    junctionRouteIndex = i;
                    junctionNodeId = candidateId;
                    traversableDegree = degree;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Calculates the exact distance along the route polyline from Nathan's position to the target junction node.
        /// Uses corridor geometry rather than straight-line Euclidean distance to prevent false triggers through walls.
        /// </summary>
        public float CalculateDistanceAlongRoute(
            WaypointGraph graph,
            RouteResult route,
            NodePosition playerPosition,
            int targetJunctionIndex)
        {
            if (graph == null || route == null || !route.IsValid)
                return float.PositiveInfinity;

            if (targetJunctionIndex <= 0 || targetJunctionIndex >= route.NodeCount)
                return float.PositiveInfinity;

            // Find the active route segment (i -> i+1) that Nathan is closest to
            int bestSegment = 0;
            float minPerpDistSq = float.PositiveInfinity;
            float bestRemainingOnSegment = 0f;

            for (int i = 0; i < targetJunctionIndex; i++)
            {
                if (!graph.TryGetNode(route.NodeIds[i], out var nodeA) ||
                    !graph.TryGetNode(route.NodeIds[i + 1], out var nodeB))
                {
                    continue;
                }

                float segDx = nodeB.Position.X - nodeA.Position.X;
                float segDz = nodeB.Position.Z - nodeA.Position.Z;
                float segLenSq = segDx * segDx + segDz * segDz;

                float t = 0f;
                if (segLenSq > 1e-4f)
                {
                    float pDx = playerPosition.X - nodeA.Position.X;
                    float pDz = playerPosition.Z - nodeA.Position.Z;
                    t = Math.Clamp((pDx * segDx + pDz * segDz) / segLenSq, 0f, 1f);
                }

                float projX = nodeA.Position.X + t * segDx;
                float projZ = nodeA.Position.Z + t * segDz;

                float diffX = playerPosition.X - projX;
                float diffZ = playerPosition.Z - projZ;
                float distSq = diffX * diffX + diffZ * diffZ;

                if (distSq < minPerpDistSq)
                {
                    minPerpDistSq = distSq;
                    bestSegment = i;

                    float remDx = nodeB.Position.X - projX;
                    float remDz = nodeB.Position.Z - projZ;
                    bestRemainingOnSegment = (float)Math.Sqrt(remDx * remDx + remDz * remDz);
                }
            }

            // Distance is: perpendicular offset from player to corridor centerline + distance along corridor to junction
            float perpDist = (float)Math.Sqrt(minPerpDistSq);
            float accumulatedDistance = perpDist + bestRemainingOnSegment;

            // Accumulate subsequent edge lengths up to target junction
            for (int k = bestSegment + 1; k < targetJunctionIndex; k++)
            {
                string fromId = route.NodeIds[k];
                string toId = route.NodeIds[k + 1];

                if (graph.TryGetEdge(fromId, toId, out var edge))
                {
                    accumulatedDistance += edge.TraversalCost;
                }
                else if (graph.TryGetNode(fromId, out var fNode) && graph.TryGetNode(toId, out var tNode))
                {
                    accumulatedDistance += fNode.Position.DistanceTo(tNode.Position);
                }
            }

            return accumulatedDistance;
        }

        /// <summary>
        /// Finds the closest upcoming waypoint index ahead of the player on the route.
        /// </summary>
        public int FindClosestRouteSegment(
            WaypointGraph graph,
            RouteResult route,
            NodePosition playerPosition,
            int maxSearchIndex)
        {
            if (route == null || route.NodeCount == 0) return -1;

            int bestIndex = 0;
            float minDistance = float.PositiveInfinity;
            int limit = Math.Min(maxSearchIndex + 1, route.NodeCount);

            for (int i = 0; i < limit; i++)
            {
                if (graph.TryGetNode(route.NodeIds[i], out var node))
                {
                    float d = playerPosition.DistanceTo(node.Position);
                    if (d < minDistance)
                    {
                        minDistance = d;
                        bestIndex = i;
                    }
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// Evaluates whether Nathan is within the proactive lookahead range of an upcoming junction.
        /// </summary>
        public bool IsApproachingJunction(
            WaypointGraph graph,
            RouteResult route,
            NodePosition playerPosition,
            float lookaheadDistance,
            out JunctionInfo upcomingJunction)
        {
            upcomingJunction = default;

            if (graph == null || route == null || !route.IsValid || route.NodeCount < 2)
                return false;

            // 1. Identify which segment Nathan is near
            int currentSegment = FindClosestRouteSegment(graph, route, playerPosition, route.NodeCount - 1);

            // 2. Find the next junction ahead of Nathan on the active route
            if (!TryFindNextJunctionOnRoute(graph, route, currentSegment, out int jIndex, out string jNodeId, out int degree))
            {
                return false;
            }

            // 3. Calculate distance along the corridor path to that junction
            float corridorDistance = CalculateDistanceAlongRoute(graph, route, playerPosition, jIndex);

            if (graph.TryGetNode(jNodeId, out var jNode))
            {
                upcomingJunction = new JunctionInfo(jNodeId, jIndex, jNode.Position, corridorDistance, degree);

                // Proactive trigger: within lookahead range (~5m) and not past the junction
                if (corridorDistance <= lookaheadDistance && corridorDistance >= 0.5f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
