using System;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Runtime component that actively tracks Nathan's position relative to upcoming
    /// decision intersections along the active escape route.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class AlesaJunctionDetector : MonoBehaviour
    {
        [Header("Target & References")]
        [Tooltip("Nathan's Transform. If unassigned, automatically finds Player in scene.")]
        public Transform playerTransform;

        [Tooltip("The route navigator managing active paths and replanning.")]
        public AlesaNavigator navigator;

        [Header("Junction Lookahead Settings")]
        [Tooltip("Distance in meters along the corridor at which Alesa detects and prepares junction guidance (default ~5m).")]
        [Range(2f, 12f)]
        public float lookaheadDistance = 5.0f;

        [Tooltip("Hysteresis distance (m) to prevent trigger flapping when hovering near threshold.")]
        [Range(0.2f, 2f)]
        public float triggerHysteresis = 0.8f;

        [Header("Debug Status (Read-Only)")]
        [SerializeField] private string currentJunctionId = string.Empty;
        [SerializeField] private int junctionRouteIndex = -1;
        [SerializeField] private float distanceToUpcomingJunction = float.PositiveInfinity;
        [SerializeField] private int junctionDegree = 0;
        [SerializeField] private bool isWithinGuidanceRange = false;

        private JunctionDetector _detector;
        private JunctionInfo _currentJunction;

        public JunctionDetector Detector => _detector ??= new JunctionDetector(lookaheadDistance, triggerHysteresis);
        public JunctionInfo CurrentJunction => _currentJunction;
        public bool IsWithinGuidanceRange => isWithinGuidanceRange;
        public string CurrentJunctionId => currentJunctionId;
        public float DistanceToJunction => distanceToUpcomingJunction;

        public event Action<JunctionInfo> OnJunctionApproached;
        public event Action<string> OnJunctionPassed;

        private void Awake()
        {
            InitializeReferences();
            _detector = new JunctionDetector(lookaheadDistance, triggerHysteresis);
        }

        private void OnEnable()
        {
            InitializeReferences();
        }

        private void Update()
        {
            if (playerTransform == null || navigator == null)
            {
                InitializeReferences();
                if (playerTransform == null || navigator == null) return;
            }

            UpdateJunctionTracking();
        }

        public void InitializeReferences()
        {
            if (navigator == null)
            {
                navigator = GetComponent<AlesaNavigator>() ?? FindFirstObjectByType<AlesaNavigator>();
            }

            if (playerTransform == null)
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj == null)
                {
                    var nathan = GameObject.Find("Player_Nathan");
                    if (nathan != null) playerObj = nathan;
                }

                if (playerObj != null)
                {
                    playerTransform = playerObj.transform;
                }
            }

            if (_detector == null)
            {
                _detector = new JunctionDetector(lookaheadDistance, triggerHysteresis);
            }
        }

        /// <summary>
        /// Scans the active route ahead of Nathan to identify the upcoming decision junction and measure corridor distance.
        /// </summary>
        public bool UpdateJunctionTracking()
        {
            if (playerTransform == null || navigator == null) return false;

            var route = navigator.ActiveRoute;
            var graph = navigator.Graph;

            if (route == null || !route.IsValid || graph == null)
            {
                ClearStatus();
                return false;
            }

            _detector.DefaultLookaheadDistance = lookaheadDistance;
            _detector.TriggerHysteresisBuffer = triggerHysteresis;

            NodePosition playerPos = playerTransform.position.ToNodePosition();
            bool approaching = _detector.IsApproachingJunction(graph, route, playerPos, lookaheadDistance, out JunctionInfo nextJunction);

            if (nextJunction.IsValid)
            {
                string prevId = currentJunctionId;
                currentJunctionId = nextJunction.NodeId;
                junctionRouteIndex = nextJunction.RouteIndex;
                distanceToUpcomingJunction = nextJunction.DistanceAlongRoute;
                junctionDegree = nextJunction.TraversableDegree;
                _currentJunction = nextJunction;

                bool wasInRange = isWithinGuidanceRange;
                isWithinGuidanceRange = approaching;

                if (!wasInRange && isWithinGuidanceRange)
                {
                    OnJunctionApproached?.Invoke(nextJunction);
                }

                if (!string.IsNullOrEmpty(prevId) && !string.Equals(prevId, currentJunctionId, StringComparison.OrdinalIgnoreCase))
                {
                    OnJunctionPassed?.Invoke(prevId);
                }

                return true;
            }

            ClearStatus();
            return false;
        }

        private void ClearStatus()
        {
            currentJunctionId = string.Empty;
            junctionRouteIndex = -1;
            distanceToUpcomingJunction = float.PositiveInfinity;
            junctionDegree = 0;
            isWithinGuidanceRange = false;
            _currentJunction = default;
        }

        private void OnDrawGizmos()
        {
            if (!isWithinGuidanceRange || string.IsNullOrEmpty(currentJunctionId) || navigator == null) return;

            var graph = navigator.Graph;
            if (graph == null || !graph.TryGetNode(currentJunctionId, out var node)) return;

            Vector3 junctionWorldPos = node.Position.ToVector3();

            // Draw golden alert beacon at junction
            Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(junctionWorldPos + Vector3.up * 1f, 0.75f);
            Gizmos.DrawLine(junctionWorldPos, junctionWorldPos + Vector3.up * 2f);

            // Connect player to upcoming junction
            if (playerTransform != null)
            {
                Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.45f);
                Gizmos.DrawLine(playerTransform.position + Vector3.up * 1f, junctionWorldPos + Vector3.up * 1f);
            }
        }
    }
}
