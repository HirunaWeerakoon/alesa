using System;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Runtime threat integration adapter for Agent 1 (Alesa).
    /// Implements IAlesaRouteBlocker to receive external corridor obstruction and risk events
    /// from Agent 3 (PossessedBoss) or Agent 4 (Encounter Director) without script modifications.
    /// Manages active threats, forwards graph mutations to AlesaNavigator, and provides editor mock triggers.
    /// </summary>
    [DisallowMultipleComponent]
    public class AlesaThreatAdapter : MonoBehaviour, IAlesaRouteBlocker
    {
        [Header("Target & References")]
        [Tooltip("The route navigator managing active escape routes.")]
        public AlesaNavigator navigator;

        [Tooltip("Optional reference to the PossessedBoss. Automatically clears boss blocks if silver bullet despawns her.")]
        public PossessedBoss monitoredBoss;

        [Header("Mock Test Corridor")]
        [Tooltip("Source waypoint node ID for inspector simulation.")]
        public string mockFromNodeId = "WP_West_Entry";

        [Tooltip("Target waypoint node ID for inspector simulation.")]
        public string mockToNodeId = "WP_West_Exit";

        [Header("Debug Status (Read-Only)")]
        [SerializeField] private int activeThreatCount = 0;
        [SerializeField] private string lastThreatReason = string.Empty;

        private ThreatEventCoordinator _coordinator;

        public ThreatEventCoordinator Coordinator => _coordinator ??= new ThreatEventCoordinator();
        public int ActiveThreatCount => _coordinator != null ? _coordinator.ActiveThreatCount : activeThreatCount;

        public event Action<ThreatEvent> OnThreatReported;
        public event Action<ThreatEvent> OnThreatResolved;

        private void Awake()
        {
            _coordinator = new ThreatEventCoordinator();
            _coordinator.OnThreatActivated += HandleThreatActivated;
            _coordinator.OnThreatResolved += HandleThreatResolved;
            FindMissingReferences();
        }

        private void Start()
        {
            FindMissingReferences();
        }

        private void Update()
        {
            // Auto-clear Boss threat when the Boss is hit by a silver bullet and despawns
            if (monitoredBoss != null && monitoredBoss.currentState == PossessedBoss.BossState.Despawned)
            {
                if (_coordinator != null && _coordinator.IsCorridorBlocked(mockFromNodeId, mockToNodeId))
                {
                    ClearThreat("BossPresence");
                }
            }
        }

        /// <summary>
        /// External contract: Reports a threat on a corridor edge.
        /// </summary>
        public bool ReportThreat(
            string threatId,
            string fromId,
            string toId,
            ThreatType type,
            string reason,
            bool isBlocking = true,
            float riskPenalty = 0f,
            bool bidirectional = true)
        {
            if (navigator == null) FindMissingReferences();
            var graph = navigator != null ? navigator.Graph : null;
            if (graph == null) return false;

            if (_coordinator == null) _coordinator = new ThreatEventCoordinator();

            var threat = new ThreatEvent(
                threatId,
                type,
                fromId,
                toId,
                isBlocking,
                riskPenalty,
                reason,
                bidirectional,
                Time.time);

            bool registered = _coordinator.ReportThreat(graph, threat);
            if (registered && navigator != null)
            {
                navigator.CheckAndHandleReplanning();
            }

            activeThreatCount = _coordinator.ActiveThreatCount;
            lastThreatReason = reason;
            return registered;
        }

        /// <summary>
        /// External contract: Clears a previously reported threat by ID.
        /// </summary>
        public bool ClearThreat(string threatId)
        {
            if (navigator == null) FindMissingReferences();
            var graph = navigator != null ? navigator.Graph : null;
            if (graph == null || _coordinator == null) return false;

            bool cleared = _coordinator.ClearThreat(graph, threatId);
            if (cleared && navigator != null)
            {
                navigator.CheckAndHandleReplanning();
            }

            activeThreatCount = _coordinator.ActiveThreatCount;
            return cleared;
        }

        /// <summary>
        /// Clears all currently registered threats, restoring baseline graph edge costs.
        /// </summary>
        public void ClearAllThreats()
        {
            if (navigator == null) FindMissingReferences();
            var graph = navigator != null ? navigator.Graph : null;

            _coordinator?.ClearAllThreats(graph);
            if (navigator != null)
            {
                navigator.CheckAndHandleReplanning();
            }

            activeThreatCount = 0;
            lastThreatReason = "All threats cleared";
        }

        /// <summary>
        /// Implementation of IAlesaRouteBlocker for direct teammate blocking calls.
        /// </summary>
        public void SetCorridorBlocked(string fromNodeId, string toNodeId, bool isBlocked, string reason, bool bidirectional = true)
        {
            string threatId = $"Block_{fromNodeId}_{toNodeId}";
            if (isBlocked)
            {
                ReportThreat(threatId, fromNodeId, toNodeId, ThreatType.StructuralBlockage, reason, isBlocking: true, riskPenalty: 0f, bidirectional: bidirectional);
            }
            else
            {
                ClearThreat(threatId);
            }
        }

        /// <summary>
        /// Implementation of IAlesaRouteBlocker for direct teammate risk calls.
        /// </summary>
        public void SetCorridorRisk(string fromNodeId, string toNodeId, float riskPenalty, bool bidirectional = true)
        {
            string threatId = $"Risk_{fromNodeId}_{toNodeId}";
            if (riskPenalty > 0f)
            {
                ReportThreat(threatId, fromNodeId, toNodeId, ThreatType.ParanormalRisk, $"Risk +{riskPenalty:F1}m", isBlocking: false, riskPenalty: riskPenalty, bidirectional: bidirectional);
            }
            else
            {
                ClearThreat(threatId);
            }
        }

        [ContextMenu("Simulate Boss Blocks Corridor")]
        public void SimulateBossBlock()
        {
            ReportThreat("BossPresence", mockFromNodeId, mockToNodeId, ThreatType.BossPresence, "Possessed Boss Stalking Hallway", isBlocking: true);
            Debug.Log($"<color=red>[THREAT ADAPTER]</color> Simulated Boss presence blocking corridor '{mockFromNodeId}' <-> '{mockToNodeId}'.", this);
        }

        [ContextMenu("Simulate Boss Cleared / Silver Bullet")]
        public void SimulateBossCleared()
        {
            ClearThreat("BossPresence");
            Debug.Log($"<color=green>[THREAT ADAPTER]</color> Simulated Boss threat cleared on corridor '{mockFromNodeId}' <-> '{mockToNodeId}'.", this);
        }

        [ContextMenu("Simulate Supernatural Risk Penalty (+20m)")]
        public void SimulateRiskPenalty()
        {
            SetCorridorRisk(mockFromNodeId, mockToNodeId, 20f);
            Debug.Log($"<color=yellow>[THREAT ADAPTER]</color> Simulated supernatural risk (+20m penalty) on corridor '{mockFromNodeId}' <-> '{mockToNodeId}'.", this);
        }

        [ContextMenu("Clear All Active Threats")]
        public void SimulateClearAll()
        {
            ClearAllThreats();
            Debug.Log("<color=green>[THREAT ADAPTER]</color> Cleared all active threats.", this);
        }

        private void HandleThreatActivated(ThreatEvent threat)
        {
            OnThreatReported?.Invoke(threat);
        }

        private void HandleThreatResolved(ThreatEvent threat)
        {
            OnThreatResolved?.Invoke(threat);
        }

        public void FindMissingReferences()
        {
            if (navigator == null) navigator = GetComponent<AlesaNavigator>() ?? FindFirstObjectByType<AlesaNavigator>();
            if (monitoredBoss == null) monitoredBoss = FindFirstObjectByType<PossessedBoss>();
        }
    }
}
