using System;
using System.Collections.Generic;
using System.Linq;

namespace Alesa.Core
{
    /// <summary>
    /// Pure C# weighted graph representing the hospital maze waypoint network.
    /// Tracks graph revision version to support dynamic replanning and cache invalidation.
    /// </summary>
    public sealed class WaypointGraph
    {
        private readonly Dictionary<string, WaypointNode> _nodes = new Dictionary<string, WaypointNode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<WaypointEdge>> _adjacency = new Dictionary<string, List<WaypointEdge>>(StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlyList<WaypointEdge> EmptyEdges = Array.Empty<WaypointEdge>();

        /// <summary>
        /// Monotonically increasing revision counter. Increments whenever nodes, edges,
        /// or corridor blockage/risk states change.
        /// </summary>
        public int Version { get; private set; }

        public int NodeCount => _nodes.Count;
        public int EdgeCount => _adjacency.Values.Sum(list => list.Count);

        public WaypointGraph()
        {
            Version = 1;
        }

        public void AddNode(WaypointNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (_nodes.ContainsKey(node.Id))
                throw new ArgumentException($"Node with ID '{node.Id}' already exists in graph.", nameof(node));

            _nodes[node.Id] = node;
            _adjacency[node.Id] = new List<WaypointEdge>();
            Version++;
        }

        public WaypointNode AddNode(string id, NodePosition position, bool isJunction = false, string label = null)
        {
            var node = new WaypointNode(id, position, isJunction, label);
            AddNode(node);
            return node;
        }

        public void AddEdge(string fromId, string toId, float? baseLength = null, bool bidirectional = true, float riskPenalty = 0f)
        {
            if (string.IsNullOrWhiteSpace(fromId)) throw new ArgumentException("fromId is required", nameof(fromId));
            if (string.IsNullOrWhiteSpace(toId)) throw new ArgumentException("toId is required", nameof(toId));

            if (!_nodes.TryGetValue(fromId, out var fromNode))
                throw new KeyNotFoundException($"Origin node '{fromId}' does not exist in graph.");
            if (!_nodes.TryGetValue(toId, out var toNode))
                throw new KeyNotFoundException($"Destination node '{toId}' does not exist in graph.");

            float length = baseLength ?? fromNode.Position.DistanceTo(toNode.Position);
            if (length < 0f)
                throw new ArgumentOutOfRangeException(nameof(baseLength), "Edge length must be non-negative.");

            // Avoid duplicate edges in same direction
            var forwardEdges = _adjacency[fromId];
            var existingForward = forwardEdges.FirstOrDefault(e => string.Equals(e.ToNodeId, toId, StringComparison.OrdinalIgnoreCase));
            if (existingForward != null)
                forwardEdges.Remove(existingForward);

            forwardEdges.Add(new WaypointEdge(fromId, toId, length, riskPenalty));

            if (bidirectional)
            {
                var backwardEdges = _adjacency[toId];
                var existingBackward = backwardEdges.FirstOrDefault(e => string.Equals(e.ToNodeId, fromId, StringComparison.OrdinalIgnoreCase));
                if (existingBackward != null)
                    backwardEdges.Remove(existingBackward);

                backwardEdges.Add(new WaypointEdge(toId, fromId, length, riskPenalty));
            }

            Version++;
        }

        public bool SetEdgeBlocked(string fromId, string toId, bool isBlocked, string reason = null, bool bidirectional = true)
        {
            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                return false;

            bool modified = false;

            if (_adjacency.TryGetValue(fromId, out var forwardList))
            {
                var edge = forwardList.FirstOrDefault(e => string.Equals(e.ToNodeId, toId, StringComparison.OrdinalIgnoreCase));
                if (edge != null && edge.IsBlocked != isBlocked)
                {
                    edge.SetBlocked(isBlocked, reason);
                    modified = true;
                }
            }

            if (bidirectional && _adjacency.TryGetValue(toId, out var backwardList))
            {
                var edge = backwardList.FirstOrDefault(e => string.Equals(e.ToNodeId, fromId, StringComparison.OrdinalIgnoreCase));
                if (edge != null && edge.IsBlocked != isBlocked)
                {
                    edge.SetBlocked(isBlocked, reason);
                    modified = true;
                }
            }

            if (modified)
            {
                Version++;
            }

            return modified;
        }

        public bool SetEdgeRisk(string fromId, string toId, float riskPenalty, bool bidirectional = true)
        {
            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                return false;

            bool modified = false;

            if (_adjacency.TryGetValue(fromId, out var forwardList))
            {
                var edge = forwardList.FirstOrDefault(e => string.Equals(e.ToNodeId, toId, StringComparison.OrdinalIgnoreCase));
                if (edge != null && Math.Abs(edge.RiskPenalty - riskPenalty) > 1e-4f)
                {
                    edge.SetRiskPenalty(riskPenalty);
                    modified = true;
                }
            }

            if (bidirectional && _adjacency.TryGetValue(toId, out var backwardList))
            {
                var edge = backwardList.FirstOrDefault(e => string.Equals(e.ToNodeId, fromId, StringComparison.OrdinalIgnoreCase));
                if (edge != null && Math.Abs(edge.RiskPenalty - riskPenalty) > 1e-4f)
                {
                    edge.SetRiskPenalty(riskPenalty);
                    modified = true;
                }
            }

            if (modified)
            {
                Version++;
            }

            return modified;
        }

        /// <summary>
        /// Safely retrieves an edge between two nodes. Returns false if either node or edge does not exist.
        /// </summary>
        public bool TryGetEdge(string fromId, string toId, out WaypointEdge edge)
        {
            edge = null;
            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                return false;

            if (_adjacency.TryGetValue(fromId, out var edgeList))
            {
                for (int i = 0; i < edgeList.Count; i++)
                {
                    if (string.Equals(edgeList[i].ToNodeId, toId, StringComparison.OrdinalIgnoreCase))
                    {
                        edge = edgeList[i];
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks whether a connection exists and is currently traversable.
        /// </summary>
        public bool IsEdgeTraversable(string fromId, string toId)
        {
            return TryGetEdge(fromId, toId, out var edge) && edge.IsTraversable;
        }

        /// <summary>
        /// Checks whether a connection exists and is currently blocked.
        /// </summary>
        public bool IsEdgeBlocked(string fromId, string toId)
        {
            return TryGetEdge(fromId, toId, out var edge) && edge.IsBlocked;
        }

        /// <summary>
        /// Returns all currently blocked edges across the entire graph.
        /// </summary>
        public IReadOnlyList<WaypointEdge> GetBlockedEdges()
        {
            var list = new List<WaypointEdge>();
            foreach (var edgeList in _adjacency.Values)
            {
                for (int i = 0; i < edgeList.Count; i++)
                {
                    if (edgeList[i].IsBlocked)
                        list.Add(edgeList[i]);
                }
            }
            return list;
        }

        /// <summary>
        /// Reopens all blocked edges across the entire graph in one atomic operation.
        /// Increments Version if any edges were reopened.
        /// </summary>
        public int ResetAllBlockedEdges()
        {
            int resetCount = 0;
            foreach (var edgeList in _adjacency.Values)
            {
                for (int i = 0; i < edgeList.Count; i++)
                {
                    if (edgeList[i].IsBlocked)
                    {
                        edgeList[i].SetBlocked(false);
                        resetCount++;
                    }
                }
            }

            if (resetCount > 0)
            {
                Version++;
            }

            return resetCount;
        }

        /// <summary>
        /// Removes an edge connection between two nodes.
        /// </summary>
        public bool RemoveEdge(string fromId, string toId, bool bidirectional = true)
        {
            if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
                return false;

            bool removed = false;
            if (_adjacency.TryGetValue(fromId, out var forwardList))
            {
                var existing = forwardList.FirstOrDefault(e => string.Equals(e.ToNodeId, toId, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    forwardList.Remove(existing);
                    removed = true;
                }
            }

            if (bidirectional && _adjacency.TryGetValue(toId, out var backwardList))
            {
                var existing = backwardList.FirstOrDefault(e => string.Equals(e.ToNodeId, fromId, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    backwardList.Remove(existing);
                    removed = true;
                }
            }

            if (removed)
            {
                Version++;
            }

            return removed;
        }

        public bool ContainsNode(string id) => !string.IsNullOrEmpty(id) && _nodes.ContainsKey(id);

        public bool TryGetNode(string id, out WaypointNode node)
        {
            if (string.IsNullOrEmpty(id))
            {
                node = null;
                return false;
            }
            return _nodes.TryGetValue(id, out node);
        }

        public WaypointNode GetNode(string id)
        {
            if (TryGetNode(id, out var node)) return node;
            throw new KeyNotFoundException($"Node '{id}' was not found in graph.");
        }

        public IReadOnlyList<WaypointEdge> GetOutgoingEdges(string nodeId)
        {
            if (!string.IsNullOrEmpty(nodeId) && _adjacency.TryGetValue(nodeId, out var edges))
                return edges;

            return EmptyEdges;
        }

        public IReadOnlyCollection<WaypointNode> GetAllNodes() => _nodes.Values;

        public IEnumerable<WaypointEdge> GetAllEdges()
        {
            foreach (var list in _adjacency.Values)
            {
                foreach (var edge in list)
                {
                    yield return edge;
                }
            }
        }

        public void Clear()
        {
            _nodes.Clear();
            _adjacency.Clear();
            Version++;
        }
    }
}

