using System;
using System.Collections.Generic;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Bridges Unity scene waypoints to the pure C# WaypointGraph and A* Pathfinder.
    /// Provides graph building, network validation, and route query APIs.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class AlesaWaypointNetwork : MonoBehaviour
    {
        [Header("Waypoints")]
        [Tooltip("List of all waypoints in the network. Auto-collected if empty.")]
        public List<AlesaWaypoint> waypoints = new List<AlesaWaypoint>();

        [Header("Default Route Endpoints")]
        [Tooltip("Starting waypoint for Nathan and Alesa.")]
        public AlesaWaypoint startWaypoint;

        [Tooltip("Target escape exit waypoint.")]
        public AlesaWaypoint exitWaypoint;

        [Header("Runtime State")]
        [SerializeField] private bool rebuildOnStart = true;

        private WaypointGraph _graph;
        private AStarPathfinder _pathfinder;
        private RouteResult _lastRoute;

        public WaypointGraph ActiveGraph => _graph;
        public AStarPathfinder Pathfinder => _pathfinder ??= new AStarPathfinder();
        public RouteResult LastRoute => _lastRoute;

        private void Awake()
        {
            if (waypoints == null || waypoints.Count == 0)
            {
                CollectWaypoints();
            }

            BuildGraph();
        }

        private void Start()
        {
            if (rebuildOnStart || _graph == null)
            {
                BuildGraph();
            }
        }

        [ContextMenu("Collect Waypoints from Children")]
        public void CollectWaypoints()
        {
            waypoints.Clear();
            var found = GetComponentsInChildren<AlesaWaypoint>(true);
            waypoints.AddRange(found);
            Debug.Log($"[AlesaWaypointNetwork] Auto-collected {waypoints.Count} waypoints from hierarchy.", this);
        }

        [ContextMenu("Build and Validate Graph")]
        public WaypointGraph BuildGraph()
        {
            _graph = new WaypointGraph();
            _pathfinder = new AStarPathfinder();

            if (waypoints == null || waypoints.Count == 0)
            {
                CollectWaypoints();
            }

            var registeredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Register all nodes
            for (int i = 0; i < waypoints.Count; i++)
            {
                var wp = waypoints[i];
                if (wp == null) continue;

                string id = wp.Id;
                if (registeredIds.Contains(id))
                {
                    Debug.LogWarning($"[AlesaWaypointNetwork] Duplicate waypoint ID '{id}' on GameObject '{wp.gameObject.name}'. Skipping duplicate.", wp);
                    continue;
                }

                _graph.AddNode(id, wp.Position, wp.isJunction, wp.gameObject.name);
                registeredIds.Add(id);
            }

            // 2. Register all edges
            for (int i = 0; i < waypoints.Count; i++)
            {
                var fromWp = waypoints[i];
                if (fromWp == null) continue;

                string fromId = fromWp.Id;
                if (!_graph.ContainsNode(fromId)) continue;

                for (int c = 0; c < fromWp.connections.Count; c++)
                {
                    var conn = fromWp.connections[c];
                    if (conn == null || conn.targetWaypoint == null)
                    {
                        Debug.LogWarning($"[AlesaWaypointNetwork] Waypoint '{fromId}' has a null connection at index {c}.", fromWp);
                        continue;
                    }

                    string toId = conn.targetWaypoint.Id;
                    if (!_graph.ContainsNode(toId))
                    {
                        Debug.LogWarning($"[AlesaWaypointNetwork] Waypoint '{fromId}' connects to unregistered target '{toId}'.", fromWp);
                        continue;
                    }

                    if (string.Equals(fromId, toId, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogWarning($"[AlesaWaypointNetwork] Waypoint '{fromId}' connects to itself. Ignoring self-loop.", fromWp);
                        continue;
                    }

                    float length = conn.costOverride >= 0f
                        ? conn.costOverride
                        : fromWp.Position.DistanceTo(conn.targetWaypoint.Position);

                    _graph.AddEdge(fromId, toId, length, conn.bidirectional, conn.riskPenalty);

                    if (conn.isBlocked)
                    {
                        _graph.SetEdgeBlocked(fromId, toId, true, conn.blockReason, conn.bidirectional);
                    }
                }
            }

            Debug.Log($"[AlesaWaypointNetwork] Graph built successfully: {_graph.NodeCount} nodes, {_graph.EdgeCount} edges (v{_graph.Version}).", this);
            return _graph;
        }

        [ContextMenu("Test Find Route (Start to Exit)")]
        public RouteResult RequestRoute()
        {
            if (startWaypoint == null || exitWaypoint == null)
            {
                Debug.LogError("[AlesaWaypointNetwork] Cannot request route: Start or Exit waypoint is not assigned.", this);
                return null;
            }

            return RequestRoute(startWaypoint.Id, exitWaypoint.Id);
        }

        public RouteResult RequestRoute(AlesaWaypoint from, AlesaWaypoint to)
        {
            if (from == null || to == null)
            {
                Debug.LogError("[AlesaWaypointNetwork] Cannot request route: 'from' or 'to' waypoint is null.");
                return null;
            }

            return RequestRoute(from.Id, to.Id);
        }

        public RouteResult RequestRoute(string fromId, string toId)
        {
            if (_graph == null || _graph.NodeCount == 0)
            {
                BuildGraph();
            }

            _lastRoute = Pathfinder.FindPath(_graph, fromId, toId);

            if (_lastRoute.IsValid)
            {
                Debug.Log($"<color=cyan>[AlesaWaypointNetwork]</color> Route found: {string.Join(" -> ", _lastRoute.NodeIds)} | Cost={_lastRoute.TotalCost:F2} (v{_lastRoute.GraphVersion})", this);
            }
            else
            {
                Debug.LogWarning($"[AlesaWaypointNetwork] Route search failed: {_lastRoute.Status} - {_lastRoute.Message}", this);
            }

            return _lastRoute;
        }
    }
}

