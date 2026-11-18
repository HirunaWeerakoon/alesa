using System.Collections.Generic;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Visualizes the waypoint graph, connections, blocked hazards, and A* shortest path
    /// directly in the Unity Scene View using Gizmos.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(AlesaWaypointNetwork))]
    public class AlesaRouteVisualizer : MonoBehaviour
    {
        [Header("Display Toggles")]
        public bool showNodes = true;
        public bool showConnections = true;
        public bool showOptimalPath = true;
        public bool showBlockedEdges = true;

        [Header("Gizmo Colors")]
        public Color normalNodeColor = new Color(0.2f, 0.85f, 1f, 0.85f);    // Cyan
        public Color junctionNodeColor = new Color(1f, 0.85f, 0.2f, 0.95f);  // Yellow
        public Color startNodeColor = new Color(0.1f, 0.5f, 1f, 1f);         // Blue
        public Color exitNodeColor = new Color(0.2f, 1f, 0.3f, 1f);          // Green
        public Color edgeColor = new Color(0.6f, 0.65f, 0.7f, 0.5f);         // Soft Grey
        public Color pathColor = new Color(0.1f, 1f, 0.4f, 1f);              // Bright Path Green
        public Color blockedColor = new Color(1f, 0.15f, 0.15f, 1f);         // Bright Hazard Red

        [Header("Sizes")]
        [Range(0.1f, 1.0f)] public float nodeRadius = 0.35f;
        [Range(0.2f, 1.5f)] public float junctionRadius = 0.55f;
        public float pathElevation = 0.35f;

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

            // 3. Draw Computed A* Shortest Path
            if (showOptimalPath)
            {
                DrawActiveRoute();
            }
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
                            // Draw an X hazard marker at midpoint
                            Vector3 mid = (fromPos + toPos) * 0.5f + Vector3.up * 0.5f;
                            Gizmos.DrawWireCube(mid, new Vector3(0.8f, 0.8f, 0.8f));
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

        private void DrawActiveRoute()
        {
            var route = (_navigator != null && _navigator.ActiveRoute != null) ? _navigator.ActiveRoute : _network.LastRoute;
            if (route == null || !route.IsValid || route.NodeIds.Count < 2) return;

            var graph = _network.ActiveGraph;
            if (graph == null) return;

            Gizmos.color = pathColor;

            for (int i = 0; i < route.NodeIds.Count - 1; i++)
            {
                string fromId = route.NodeIds[i];
                string toId = route.NodeIds[i + 1];

                if (!graph.TryGetNode(fromId, out var fromNode) ||
                    !graph.TryGetNode(toId, out var toNode))
                    continue;

                Vector3 p1 = fromNode.Position.ToVector3() + Vector3.up * pathElevation;
                Vector3 p2 = toNode.Position.ToVector3() + Vector3.up * pathElevation;

                // Draw elevated bright green path line
                Gizmos.DrawLine(p1, p2);

                // Draw path step spheres along route
                Gizmos.DrawSphere(p1, nodeRadius * 0.8f);
                if (i == route.NodeIds.Count - 2)
                {
                    Gizmos.DrawSphere(p2, nodeRadius * 0.8f);
                }
            }
        }
    }
}

