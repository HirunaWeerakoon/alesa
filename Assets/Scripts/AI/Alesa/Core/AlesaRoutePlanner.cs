using System;
using System.Collections.Generic;

namespace Alesa.Core
{
    public enum RouteChangeReason
    {
        InitialPlan,
        CorridorBlocked,
        CorridorReopened,
        PlayerDeviated,
        ManualRequest,
        DestinationChanged
    }

    /// <summary>
    /// Pure C# route coordinator for Agent 1. Manages active escape path, caches top K alternative routes,
    /// tracks graph version changes, detects route invalidation, and throttles replanning.
    /// </summary>
    public sealed class AlesaRoutePlanner
    {
        private readonly WaypointGraph _graph;
        private readonly YenKShortestPaths _yen;
        private readonly int _maxAlternatives;

        private string _startNodeId;
        private string _goalNodeId;
        private int _cachedGraphVersion;
        private MultiRouteResult _cachedMultiRoute;
        private RouteResult _activeRoute;

        public WaypointGraph Graph => _graph;
        public string StartNodeId => _startNodeId;
        public string GoalNodeId => _goalNodeId;
        public int CachedGraphVersion => _cachedGraphVersion;
        public MultiRouteResult CachedMultiRoute => _cachedMultiRoute;
        public RouteResult ActiveRoute => _activeRoute;
        public bool HasValidRoute => _activeRoute != null && _activeRoute.IsValid;

        public event Action<RouteResult, RouteChangeReason> OnRouteChanged;
        public event Action<string> OnActiveRouteBlocked;
        public event Action OnNoPathAvailable;

        public AlesaRoutePlanner(WaypointGraph graph, YenKShortestPaths yen = null, int maxAlternatives = 3)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _yen = yen ?? new YenKShortestPaths();
            _maxAlternatives = Math.Max(1, maxAlternatives);
            _cachedGraphVersion = -1;
        }

        public void SetEndpoints(string startNodeId, string goalNodeId, bool replanImmediately = true)
        {
            bool changed = !string.Equals(_startNodeId, startNodeId, StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(_goalNodeId, goalNodeId, StringComparison.OrdinalIgnoreCase);

            _startNodeId = startNodeId;
            _goalNodeId = goalNodeId;

            if (changed && replanImmediately)
            {
                Replan(RouteChangeReason.DestinationChanged, force: true);
            }
        }

        /// <summary>
        /// Recalculates up to K shortest paths using Yen's algorithm.
        /// If graph has not changed and force is false, returns cached result without recalculating.
        /// </summary>
        public MultiRouteResult Replan(RouteChangeReason reason = RouteChangeReason.ManualRequest, bool force = false)
        {
            if (string.IsNullOrWhiteSpace(_startNodeId) || string.IsNullOrWhiteSpace(_goalNodeId))
            {
                return MultiRouteResult.Failure(RouteStatus.StartNodeNotFound, "Endpoints not set.", _graph.Version);
            }

            // Optimization: if graph version is unchanged and not forced, return cached result
            if (!force && _cachedMultiRoute != null && _graph.Version == _cachedGraphVersion && IsActiveRouteTraversable())
            {
                return _cachedMultiRoute;
            }

            _cachedGraphVersion = _graph.Version;
            _cachedMultiRoute = _yen.FindKShortestPaths(_graph, _startNodeId, _goalNodeId, _maxAlternatives);

            var oldRoute = _activeRoute;
            if (_cachedMultiRoute.HasRoutes)
            {
                _activeRoute = _cachedMultiRoute.BestRoute;
                OnRouteChanged?.Invoke(_activeRoute, reason);
            }
            else
            {
                _activeRoute = RouteResult.Failure(RouteStatus.NoPath, "No escape path available.", _graph.Version);
                OnNoPathAvailable?.Invoke();
                OnRouteChanged?.Invoke(_activeRoute, reason);
            }

            return _cachedMultiRoute;
        }

        /// <summary>
        /// Checks whether all corridor edges on the currently active route remain unblocked and traversable.
        /// </summary>
        public bool IsActiveRouteTraversable()
        {
            if (_activeRoute == null || !_activeRoute.IsValid || _activeRoute.NodeIds.Count < 2)
                return false;

            var nodes = _activeRoute.NodeIds;
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                if (!_graph.IsEdgeTraversable(nodes[i], nodes[i + 1]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Call periodically or on graph events. If the graph version changed or the active route
        /// has become obstructed by a hazard, immediately triggers replanning.
        /// </summary>
        public bool CheckAndReplanIfInvalid(out RouteResult updatedRoute)
        {
            updatedRoute = _activeRoute;

            bool versionChanged = _graph.Version != _cachedGraphVersion;
            bool routeBlocked = !IsActiveRouteTraversable();

            if (versionChanged || routeBlocked)
            {
                if (routeBlocked)
                {
                    OnActiveRouteBlocked?.Invoke("Active route obstructed by hazard or closed passage.");
                }

                var reason = routeBlocked ? RouteChangeReason.CorridorBlocked : RouteChangeReason.CorridorReopened;
                Replan(reason, force: true);
                updatedRoute = _activeRoute;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Updates Nathan's current closest graph node.
        /// If Nathan deviates to a node not in the active route, triggers replanning from the new node.
        /// </summary>
        public bool UpdatePlayerNode(string playerNodeId)
        {
            if (string.IsNullOrWhiteSpace(playerNodeId) || !string.Equals(_startNodeId, playerNodeId, StringComparison.OrdinalIgnoreCase))
            {
                // Check if player is still along the remaining active path
                if (IsNodeOnActiveRoute(playerNodeId))
                {
                    // Player is progressing normally along recommended route; update start without full Yen recalculation
                    _startNodeId = playerNodeId;
                    return false;
                }

                // Player deviated from the recommended escape path! Replan from their new position
                _startNodeId = playerNodeId;
                Replan(RouteChangeReason.PlayerDeviated, force: true);
                return true;
            }

            return false;
        }

        public bool IsNodeOnActiveRoute(string nodeId)
        {
            if (_activeRoute == null || !_activeRoute.IsValid || string.IsNullOrWhiteSpace(nodeId))
                return false;

            for (int i = 0; i < _activeRoute.NodeIds.Count; i++)
            {
                if (string.Equals(_activeRoute.NodeIds[i], nodeId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
