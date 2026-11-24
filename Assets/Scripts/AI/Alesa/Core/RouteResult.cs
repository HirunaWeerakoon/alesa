using System;
using System.Collections.Generic;

namespace Alesa.Core
{
    public enum RouteStatus
    {
        Valid,
        NoPath,
        StartNodeNotFound,
        GoalNodeNotFound,
        EmptyGraph,
        InvalidGraphState
    }

    /// <summary>
    /// Immutable result produced by pathfinding algorithms (A*, Yen's K-Shortest Paths).
    /// </summary>
    public sealed class RouteResult
    {
        public RouteStatus Status { get; }
        public IReadOnlyList<string> NodeIds { get; }
        public float TotalCost { get; }
        public int GraphVersion { get; }
        public string Message { get; }

        public bool IsValid => Status == RouteStatus.Valid && NodeIds != null && NodeIds.Count > 0;
        public int NodeCount => NodeIds?.Count ?? 0;

        private RouteResult(RouteStatus status, IReadOnlyList<string> nodeIds, float totalCost, int graphVersion, string message)
        {
            Status = status;
            NodeIds = nodeIds ?? Array.Empty<string>();
            TotalCost = totalCost;
            GraphVersion = graphVersion;
            Message = message ?? string.Empty;
        }

        public static RouteResult Success(IReadOnlyList<string> nodeIds, float totalCost, int graphVersion)
        {
            if (nodeIds == null || nodeIds.Count == 0)
                throw new ArgumentException("Valid route must contain at least one node.", nameof(nodeIds));

            return new RouteResult(RouteStatus.Valid, nodeIds, totalCost, graphVersion, "Route resolved successfully.");
        }

        public static RouteResult Failure(RouteStatus status, string message, int graphVersion)
        {
            if (status == RouteStatus.Valid)
                throw new ArgumentException("Cannot create a failure result with RouteStatus.Valid.", nameof(status));

            return new RouteResult(status, Array.Empty<string>(), float.PositiveInfinity, graphVersion, message);
        }

        public override string ToString()
        {
            if (IsValid)
                return $"RouteResult: Valid | Cost={TotalCost:F2} | Nodes=[{string.Join(" -> ", NodeIds)}] (v{GraphVersion})";

            return $"RouteResult: {Status} | Message='{Message}' (v{GraphVersion})";
        }
    }
}

