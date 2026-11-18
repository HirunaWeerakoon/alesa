using System.Collections.Generic;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Visualizes the waypoint graph, corridor connections, dynamic hazards,
    /// and Yen's K-Shortest ranked alternative escape routes directly in the Scene View.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(AlesaWaypointNetwork))]
    public class AlesaRouteVisualizer : MonoBehaviour
    {
        [Header("Display Toggles")]
        public bool showNodes = true;
        public bool showConnections = true;
        public bool showActiveRoute = true;
        public bool showAlternativeRoutes = true;
        public bool showBlockedEdges = true;

        [Header("Route Colors")]
        [Tooltip("Primary (1st shortest) recommended route.")]
        public Color primaryRouteColor = new Color(0.1f, 1f, 0.4f, 1f);       // Bright Green

        [Tooltip("Second-best alternative route.")]
        public Color secondRouteColor = new Color(0f, 0.85f, 1f, 0.85f);       // Electric Cyan

        [Tooltip("Third-best alternative route.")]
        public Color thirdRouteColor = new Color(1f, 0.85f, 0.15f, 0.85f);     // Amber Yellow

        [Tooltip("Warning color when exit is completely cut off.")]
        public Color noPathColor = new Color(1f, 0.15f, 0.15f, 1f);            // Hazard Red

        [Header("Graph Element Colors")]
        public Color normalNodeColor = new Color(0.2f, 0.85f, 1f, 0.85f);
        public Color junctionNodeColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        public Color startNodeColor = new Color(0.1f, 0.5f, 1f, 1f);
        public Color exitNodeColor = new Color(0.2f, 1f, 0.3f, 1f);
        public Color edgeColor = new Color(0.6f, 0.65f, 0.7f, 0.4f);
        public Color blockedColor = new Color(1f, 0.15f, 0.15f, 1f);

        [Header("Dimensions & Elevation")]
        [Range(0.1f, 1.0f)] public float nodeRadius = 0.35f;
        [Range(0.2f, 1.5f)] public float junctionRadius = 0.55f;
        public float primaryElevation = 0.40f;
        public float secondElevation = 0.25f;
        public float thirdElevation = 0.12f;

        private AlesaWaypointNetwork _network;
        private AlesaNavigator _navigator;
        private readonly HashSet<(int, int)> _drawnPairs = new HashSet<(int, int)>();

        private void OnEnable()
        {
            _network = GetComponent<AlesaWaypointNetwork>();
            _navigator = GetComponent<AlesaNavigator>();
        }

        private void OnDrawGizmos()
        {
            if (_network == null) _network = GetComponent<AlesaWaypointNetwork>();
            if (_navigator == null) _navigator = GetComponent<AlesaNavigator>();
            if (_network == null) return;

            var waypoints = _network.waypoints;
            if (waypoints == null || waypoints.Count == 0) return;

            // Auto-request initial route if not yet calculated
            if (_network.LastRoute == null && _network.startWaypoint != null && _network.exitWaypoint != null)
            {
                if (_navigator != null)
                    _navigator.ReplanRoute();
                else
                    _network.RequestRoute();
            }

            // 1. Draw Graph Connections
            if (showConnections)
            {
                DrawCorridorConnections(waypoints);
            }

            // 2. Draw Waypoint Nodes
            if (showNodes)
            {
                DrawWaypointNodes(waypoints);
            }

            // 3. Draw Ranked Routes or NoPath Warning
            DrawRoutes();
        }

        private void DrawCorridorConnections(List<AlesaWaypoint> waypoints)
        {
            _drawnPairs.Clear();

            for (int i = 0; i < waypoints.Count; i++)
            {
                var fromWp = waypoints[i];
                if (fromWp == null) continue;

                Vector3 fromPos = fromWp.transform.position;

                for (int c = 0; c < fromWp.connections.Count; c++)
                {
                    var conn = fromWp.connections[c];
                    if (conn == null || conn.targetWaypoint == null) continue;

                    var toWp = conn.targetWaypoint;
                    Vector3 toPos = toWp.transform.position;

                    int idA = fromWp.GetInstanceID();
                    int idB = toWp.GetInstanceID();
                    var pairKey = idA < idB ? (idA, idB) : (idB, idA);

                    if (_drawnPairs.Contains(pairKey)) continue;
                    _drawnPairs.Add(pairKey);

                    if (conn.isBlocked)
                    {
                        if (showBlockedEdges)
                        {
                            Gizmos.color = blockedColor;
                            Gizmos.DrawLine(fromPos, toPos);
                            Vector3 mid = (fromPos + toPos) * 0.5f + Vector3.up * 0.5f;
                            Gizmos.DrawWireCube(mid, new Vector3(0.9f, 0.9f, 0.9f));
                        }
                    }
                    else
                    {
                        Gizmos.color = edgeColor;
                        Gizmos.DrawLine(fromPos, toPos);
                    }
                }
            }
        }

        private void DrawWaypointNodes(List<AlesaWaypoint> waypoints)
        {
            for (int i = 0; i < waypoints.Count; i++)
            {
                var wp = waypoints[i];
                if (wp == null) continue;

                Vector3 pos = wp.transform.position;

                if (wp == _network.startWaypoint)
                {
                    Gizmos.color = startNodeColor;
                    Gizmos.DrawSphere(pos, junctionRadius * 1.2f);
                    Gizmos.DrawWireSphere(pos, junctionRadius * 1.5f);
                }
                else if (wp == _network.exitWaypoint)
                {
                    Gizmos.color = exitNodeColor;
                    Gizmos.DrawSphere(pos, junctionRadius * 1.2f);
                    Gizmos.DrawWireSphere(pos, junctionRadius * 1.5f);
                }
                else if (wp.isJunction)
                {
                    Gizmos.color = junctionNodeColor;
                    Gizmos.DrawSphere(pos, junctionRadius);
                }
                else
                {
                    Gizmos.color = normalNodeColor;
                    Gizmos.DrawSphere(pos, nodeRadius);
                }
            }
        }

        private void DrawRoutes()
        {
            var graph = _network.ActiveGraph;
            if (graph == null) return;

            // Check if active route exists
            RouteResult activeRoute = (_navigator != null && _navigator.ActiveRoute != null)
                ? _navigator.ActiveRoute
                : _network.LastRoute;

            // 1. If no path exists, draw warning markers
            if (activeRoute == null || !activeRoute.IsValid || activeRoute.Status == RouteStatus.NoPath)
            {
                DrawNoPathWarning();
                return;
            }

            // 2. Multi-route rendering from Yen's algorithm
            MultiRouteResult multi = _navigator != null ? _navigator.MultiRoute : null;

            if (showAlternativeRoutes && multi != null && multi.HasRoutes)
            {
                // Draw 3rd route (bottom layer, amber)
                if (multi.Count >= 3)
                {
                    DrawSinglePath(graph, multi.Routes[2], thirdRouteColor, thirdElevation, nodeRadius * 0.5f);
                }

                // Draw 2nd route (middle layer, cyan)
                if (multi.Count >= 2)
                {
                    DrawSinglePath(graph, multi.Routes[1], secondRouteColor, secondElevation, nodeRadius * 0.65f);
                }

                // Draw 1st route (top layer, bright green)
                if (showActiveRoute && multi.Count >= 1)
                {
                    DrawSinglePath(graph, multi.Routes[0], primaryRouteColor, primaryElevation, nodeRadius * 0.85f);
                }
            }
            else if (showActiveRoute && activeRoute.IsValid)
            {
                DrawSinglePath(graph, activeRoute, primaryRouteColor, primaryElevation, nodeRadius * 0.85f);
            }
        }

        private void DrawSinglePath(WaypointGraph graph, RouteResult route, Color color, float elevation, float sphereRadius)
        {
            if (route == null || !route.IsValid || route.NodeIds.Count < 2) return;

            Gizmos.color = color;

            for (int i = 0; i < route.NodeIds.Count - 1; i++)
            {
                string fromId = route.NodeIds[i];
                string toId = route.NodeIds[i + 1];

                if (!graph.TryGetNode(fromId, out var fromNode) ||
                    !graph.TryGetNode(toId, out var toNode))
                    continue;

                Vector3 p1 = fromNode.Position.ToVector3() + Vector3.up * elevation;
                Vector3 p2 = toNode.Position.ToVector3() + Vector3.up * elevation;

                Gizmos.DrawLine(p1, p2);
                Gizmos.DrawSphere(p1, sphereRadius);

                if (i == route.NodeIds.Count - 2)
                {
                    Gizmos.DrawSphere(p2, sphereRadius);
                }
            }
        }

        private void DrawNoPathWarning()
        {
            if (_network.exitWaypoint != null)
            {
                Vector3 exitPos = _network.exitWaypoint.transform.position + Vector3.up * 1f;
                Gizmos.color = noPathColor;
                Gizmos.DrawWireCube(exitPos, new Vector3(2.5f, 2.5f, 2.5f));
                Gizmos.DrawLine(exitPos + new Vector3(-1.2f, 0, -1.2f), exitPos + new Vector3(1.2f, 0, 1.2f));
                Gizmos.DrawLine(exitPos + new Vector3(-1.2f, 0, 1.2f), exitPos + new Vector3(1.2f, 0, -1.2f));
            }
        }
    }
}
