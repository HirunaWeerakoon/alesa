using System;
using System.Collections.Generic;

namespace Alesa.Core
{
    public enum ThreatType
    {
        BossPresence,
        AmbushHazard,
        StructuralBlockage,
        ParanormalRisk
    }

    /// <summary>
    /// Immutable record representing a gameplay hazard or enemy encounter affecting a navigation corridor.
    /// Strictly decoupled from UnityEngine.
    /// </summary>
    public readonly struct ThreatEvent
    {
        public string ThreatId { get; }
        public ThreatType Type { get; }
        public string SourceNodeId { get; }
        public string TargetNodeId { get; }
        public bool IsBlocking { get; }
        public float RiskPenalty { get; }
        public string Reason { get; }
        public bool Bidirectional { get; }
        public float Timestamp { get; }

        public ThreatEvent(
            string threatId,
            ThreatType type,
            string sourceNodeId,
            string targetNodeId,
            bool isBlocking = true,
            float riskPenalty = 0f,
            string reason = null,
            bool bidirectional = true,
            float timestamp = 0f)
        {
            ThreatId = threatId ?? Guid.NewGuid().ToString("N");
            Type = type;
            SourceNodeId = sourceNodeId ?? string.Empty;
            TargetNodeId = targetNodeId ?? string.Empty;
            IsBlocking = isBlocking;
            RiskPenalty = Math.Max(0f, riskPenalty);
            Reason = reason ?? type.ToString();
            Bidirectional = bidirectional;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Pure C# threat event coordinator for Agent 1.
    /// Manages active dynamic hazards (Boss presence, ambushes, collapsed doors)
    /// and maps them directly to WaypointGraph edge blocking and risk penalties.
    /// Ensures idempotent tracking, clean hazard clearing, and event dispatch.
    /// </summary>
    public sealed class ThreatEventCoordinator
    {
        private readonly Dictionary<string, ThreatEvent> _activeThreats = new Dictionary<string, ThreatEvent>(StringComparer.Ordinal);

        public IReadOnlyCollection<ThreatEvent> ActiveThreats => _activeThreats.Values;
        public int ActiveThreatCount => _activeThreats.Count;

        public event Action<ThreatEvent> OnThreatActivated;
        public event Action<ThreatEvent> OnThreatResolved;

        /// <summary>
        /// Registers an active threat and modifies the corresponding graph edges.
        /// </summary>
        public bool ReportThreat(WaypointGraph graph, ThreatEvent threat)
        {
            if (graph == null) return false;
            if (string.IsNullOrEmpty(threat.SourceNodeId) || string.IsNullOrEmpty(threat.TargetNodeId)) return false;

            // Idempotency: check if identical threat is already registered
            if (_activeThreats.TryGetValue(threat.ThreatId, out var existing))
            {
                if (existing.SourceNodeId == threat.SourceNodeId &&
                    existing.TargetNodeId == threat.TargetNodeId &&
                    existing.IsBlocking == threat.IsBlocking &&
                    Math.Abs(existing.RiskPenalty - threat.RiskPenalty) < 0.001f)
                {
                    return false;
                }
            }

            _activeThreats[threat.ThreatId] = threat;

            if (threat.IsBlocking)
            {
                graph.SetEdgeBlocked(threat.SourceNodeId, threat.TargetNodeId, true, threat.Reason, threat.Bidirectional);
            }
            else if (threat.RiskPenalty > 0f)
            {
                graph.SetEdgeRisk(threat.SourceNodeId, threat.TargetNodeId, threat.RiskPenalty, threat.Bidirectional);
            }

            OnThreatActivated?.Invoke(threat);
            return true;
        }

        /// <summary>
        /// Clears an active threat and restores the corresponding graph edge.
        /// </summary>
        public bool ClearThreat(WaypointGraph graph, string threatId)
        {
            if (string.IsNullOrEmpty(threatId) || !_activeThreats.TryGetValue(threatId, out var threat))
            {
                return false;
            }

            _activeThreats.Remove(threatId);

            if (graph != null)
            {
                if (threat.IsBlocking)
                {
                    // Unblock edge
                    graph.SetEdgeBlocked(threat.SourceNodeId, threat.TargetNodeId, false, null, threat.Bidirectional);
                }
                else if (threat.RiskPenalty > 0f)
                {
                    // Reset risk penalty to 0
                    graph.SetEdgeRisk(threat.SourceNodeId, threat.TargetNodeId, 0f, threat.Bidirectional);
                }
            }

            OnThreatResolved?.Invoke(threat);
            return true;
        }

        /// <summary>
        /// Restores all threatened edges back to default state and clears the registry.
        /// </summary>
        public void ClearAllThreats(WaypointGraph graph)
        {
            if (graph != null)
            {
                foreach (var threat in _activeThreats.Values)
                {
                    if (threat.IsBlocking)
                    {
                        graph.SetEdgeBlocked(threat.SourceNodeId, threat.TargetNodeId, false, null, threat.Bidirectional);
                    }
                    else if (threat.RiskPenalty > 0f)
                    {
                        graph.SetEdgeRisk(threat.SourceNodeId, threat.TargetNodeId, 0f, threat.Bidirectional);
                    }
                }
            }

            _activeThreats.Clear();
        }

        /// <summary>
        /// Checks whether a specific corridor currently has an active blocking threat.
        /// </summary>
        public bool IsCorridorBlocked(string nodeA, string nodeB)
        {
            foreach (var threat in _activeThreats.Values)
            {
                if (!threat.IsBlocking) continue;

                if ((threat.SourceNodeId == nodeA && threat.TargetNodeId == nodeB) ||
                    (threat.Bidirectional && threat.SourceNodeId == nodeB && threat.TargetNodeId == nodeA))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
