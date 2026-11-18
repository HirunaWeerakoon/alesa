using System;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    public struct RouteUpdateInfo
    {
        public string StartNodeId;
        public string ExitNodeId;
        public RouteResult ActiveRoute;
        public int TotalAlternatives;
        public int GraphRevision;
        public RouteChangeReason ChangeReason;
    }

    /// <summary>
    /// Runtime route manager and coordinator for Agent 1 (Alesa).
    /// Manages dynamic replanning, handles Boss/hazard block events, and exposes
    /// route-change notifications for voice guidance and director coordination.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AlesaWaypointNetwork))]
    public class AlesaNavigator : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The waypoint network managing scene nodes and the core graph.")]
        [SerializeField] private AlesaWaypointNetwork network;

        [Header("Replanning Throttle")]
        [Tooltip("Interval in seconds between graph change and route validation checks.")]
        [Range(0.05f, 1f)]
        public float checkInterval = 0.2f;

        [Header("State")]
        [SerializeField] private bool isActive = true;

        private AlesaRoutePlanner _planner;
        private float _lastCheckTime;

        public AlesaRoutePlanner Planner => _planner;
        public WaypointGraph Graph => network != null ? network.ActiveGraph : null;
        public RouteResult ActiveRoute => _planner?.ActiveRoute;
        public MultiRouteResult MultiRoute => _planner?.CachedMultiRoute;
        public bool IsActive => isActive;

        public event Action<RouteUpdateInfo> OnRecommendedRouteChanged;
        public event Action<string> OnHazardEncountered;
        public event Action OnPathLost;

        private void Awake()
        {
            if (network == null) network = GetComponent<AlesaWaypointNetwork>();
            InitializePlanner();
        }

        private void Start()
        {
            InitializePlanner();
            if (isActive && _planner != null && !_planner.HasValidRoute)
            {
                ReplanRoute(RouteChangeReason.InitialPlan);
            }
        }

        private void OnEnable()
        {
            InitializePlanner();
        }

        private void Update()
        {
            if (!isActive || _planner == null || network == null) return;

            // Throttled graph version and route validity check
            if (Time.time - _lastCheckTime >= checkInterval)
            {
                _lastCheckTime = Time.time;
                CheckAndHandleReplanning();
            }
        }

        public void InitializePlanner()
        {
            if (network == null) network = GetComponent<AlesaWaypointNetwork>();
            if (network == null) return;

            var graph = network.ActiveGraph ?? network.BuildGraph();
            if (graph == null) return;

            if (_planner == null || _planner.Graph != graph)
            {
                _planner = new AlesaRoutePlanner(graph);
                _planner.OnRouteChanged += HandlePlannerRouteChanged;
                _planner.OnActiveRouteBlocked += HandlePlannerRouteBlocked;
                _planner.OnNoPathAvailable += HandlePlannerNoPath;
            }

            if (network.startWaypoint != null && network.exitWaypoint != null)
            {
                _planner.SetEndpoints(network.startWaypoint.Id, network.exitWaypoint.Id, replanImmediately: false);
            }
        }

        public MultiRouteResult ReplanRoute(RouteChangeReason reason = RouteChangeReason.ManualRequest)
        {
            InitializePlanner();
            if (_planner == null) return null;

            return _planner.Replan(reason, force: true);
        }

        public void CheckAndHandleReplanning()
        {
            if (_planner == null) return;

            if (_planner.CheckAndReplanIfInvalid(out _))
            {
                Debug.Log($"[AlesaNavigator] Dynamic route replanned (v{_planner.CachedGraphVersion}). Active route cost: {_planner.ActiveRoute?.TotalCost:F1}m", this);
            }
        }

        /// <summary>
        /// External contract: Blocks or unblocks a corridor edge (invoked by Boss, door, or hazard triggers).
        /// </summary>
        public bool SetCorridorBlocked(string fromId, string toId, bool isBlocked, string reason = null, bool bidirectional = true)
        {
            if (network == null || network.ActiveGraph == null) return false;

            bool changed = network.ActiveGraph.SetEdgeBlocked(fromId, toId, isBlocked, reason, bidirectional);
            if (changed)
            {
                CheckAndHandleReplanning();
            }
            return changed;
        }

        /// <summary>
        /// External contract: Updates supernatural hazard risk penalty on a corridor.
        /// </summary>
        public bool SetCorridorRisk(string fromId, string toId, float riskPenalty, bool bidirectional = true)
        {
            if (network == null || network.ActiveGraph == null) return false;

            bool changed = network.ActiveGraph.SetEdgeRisk(fromId, toId, riskPenalty, bidirectional);
            if (changed)
            {
                CheckAndHandleReplanning();
            }
            return changed;
        }

        /// <summary>
        /// External contract: Activates companion navigation after Nathan rescues Alesa at the keypad.
        /// </summary>
        public void ActivateAfterRescue()
        {
            isActive = true;
            ReplanRoute(RouteChangeReason.InitialPlan);
            Debug.Log("[AlesaNavigator] Alesa companion navigation activated post-rescue.", this);
        }

        public void UpdatePlayerPosition(string closestNodeId)
        {
            if (!isActive || _planner == null) return;

            if (_planner.UpdatePlayerNode(closestNodeId))
            {
                Debug.Log($"[AlesaNavigator] Player deviated to '{closestNodeId}'. Rerouted successfully.", this);
            }
        }

        private void HandlePlannerRouteChanged(RouteResult newRoute, RouteChangeReason reason)
        {
            var info = new RouteUpdateInfo
            {
                StartNodeId = _planner.StartNodeId,
                ExitNodeId = _planner.GoalNodeId,
                ActiveRoute = newRoute,
                TotalAlternatives = _planner.CachedMultiRoute?.Count ?? 0,
                GraphRevision = _planner.CachedGraphVersion,
                ChangeReason = reason
            };

            OnRecommendedRouteChanged?.Invoke(info);
        }

        private void HandlePlannerRouteBlocked(string reason)
        {
            OnHazardEncountered?.Invoke(reason);
        }

        private void HandlePlannerNoPath()
        {
            OnPathLost?.Invoke();
        }
    }
}
