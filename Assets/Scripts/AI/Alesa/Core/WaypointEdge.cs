using System;

namespace Alesa.Core
{
    /// <summary>
    /// Represents a walkable corridor edge between two waypoint nodes in the hospital graph.
    /// Supports dynamic blockage (boss, door, hazards) and non-negative risk weighting.
    /// </summary>
    public sealed class WaypointEdge
    {
        public string FromNodeId { get; }
        public string ToNodeId { get; }
        public float BaseLength { get; }
        public float RiskPenalty { get; private set; }
        public bool IsBlocked { get; private set; }
        public string BlockReason { get; private set; }

        public WaypointEdge(string fromNodeId, string toNodeId, float baseLength, float riskPenalty = 0f)
        {
            if (string.IsNullOrWhiteSpace(fromNodeId))
                throw new ArgumentException("FromNodeId cannot be null or whitespace.", nameof(fromNodeId));
            if (string.IsNullOrWhiteSpace(toNodeId))
                throw new ArgumentException("ToNodeId cannot be null or whitespace.", nameof(toNodeId));
            if (baseLength < 0f)
                throw new ArgumentOutOfRangeException(nameof(baseLength), "Edge length must be non-negative.");
            if (riskPenalty < 0f)
                throw new ArgumentOutOfRangeException(nameof(riskPenalty), "Risk penalty must be non-negative.");

            FromNodeId = fromNodeId.Trim();
            ToNodeId = toNodeId.Trim();
            BaseLength = baseLength;
            RiskPenalty = riskPenalty;
            IsBlocked = false;
            BlockReason = string.Empty;
        }

        /// <summary>
        /// Total cost for pathfinding. Returns PositiveInfinity when blocked.
        /// </summary>
        public float TraversalCost => IsBlocked ? float.PositiveInfinity : (BaseLength + RiskPenalty);

        /// <summary>
        /// True if the edge is open and traversable by agents.
        /// </summary>
        public bool IsTraversable => !IsBlocked && !float.IsPositiveInfinity(TraversalCost);

        public void SetBlocked(bool blocked, string reason = null)
        {
            IsBlocked = blocked;
            BlockReason = blocked ? (reason ?? "Obstacle detected") : string.Empty;
        }

        public void SetRiskPenalty(float penalty)
        {
            if (penalty < 0f)
                throw new ArgumentOutOfRangeException(nameof(penalty), "Risk penalty must be non-negative.");
            RiskPenalty = penalty;
        }

        public override string ToString() =>
            $"Edge[{FromNodeId} -> {ToNodeId}] Cost={TraversalCost:F1}" +
            (IsBlocked ? $" [BLOCKED: {BlockReason}]" : "");
    }
}

