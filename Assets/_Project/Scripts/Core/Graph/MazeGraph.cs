using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alesa.Core.Graph
{
    /// <summary>What a node means to the AI. A node may carry several tags.</summary>
    [Flags]
    public enum NodeTag
    {
        None = 0,
        Junction = 1 << 0,
        Exit = 1 << 1,
        BossTrigger = 1 << 2,
        Door = 1 << 3,
        DeadEnd = 1 << 4,
        Room = 1 << 5,
    }

    public sealed class MazeNode
    {
        public int Id { get; }
        public Vector3 Position { get; }
        public NodeTag Tags { get; }
        public string Label { get; }

        internal readonly List<MazeEdge> Edges = new List<MazeEdge>();
        public IReadOnlyList<MazeEdge> Neighbours => Edges;

        public MazeNode(int id, Vector3 position, NodeTag tags, string label)
        {
            Id = id;
            Position = position;
            Tags = tags;
            Label = label;
        }

        public bool Has(NodeTag tag) => (Tags & tag) == tag;
        public override string ToString() => $"#{Id} {Label}";
    }

    /// <summary>
    /// Undirected edge. <see cref="Length"/> is the geometric distance; agents apply their
    /// own cost model on top through <see cref="EdgeCost"/>.
    /// </summary>
    public sealed class MazeEdge
    {
        public int A { get; }
        public int B { get; }
        public float Length { get; }

        /// <summary>False when a locked door / barricade blocks walking. Sound may still pass.</summary>
        public bool Traversable { get; set; } = true;

        /// <summary>True when a closed door sits on this edge (used for sound attenuation).</summary>
        public bool HasClosedDoor { get; set; }

        public MazeEdge(int a, int b, float length)
        {
            A = a;
            B = b;
            Length = length;
        }

        public int Other(int nodeId) => nodeId == A ? B : A;
    }

    /// <summary>
    /// Per-query edge cost. Return <see cref="float.PositiveInfinity"/> to forbid an edge.
    /// <paramref name="from"/> is the node the search is leaving.
    /// </summary>
    public delegate float EdgeCost(MazeEdge edge, int from);

    /// <summary>
    /// High-level decision graph of the hospital/maze: junctions, rooms, doors, exits and boss
    /// trigger points. Low-level locomotion between nodes is left to the Unity NavMesh.
    /// Pure C#, no UnityEngine dependency, so it can be unit tested and reasoned about in isolation.
    /// </summary>
    public sealed class MazeGraph
    {
        private readonly List<MazeNode> _nodes = new List<MazeNode>();
        private readonly List<MazeEdge> _edges = new List<MazeEdge>();

        public IReadOnlyList<MazeNode> Nodes => _nodes;
        public IReadOnlyList<MazeEdge> Edges => _edges;
        public int Count => _nodes.Count;

        public MazeNode this[int id] => _nodes[id];

        /// <summary>Default cost: geometric length, forbidden if not traversable.</summary>
        public static readonly EdgeCost WalkingCost =
            (e, _) => e.Traversable ? e.Length : float.PositiveInfinity;

        public MazeNode AddNode(Vector3 position, NodeTag tags = NodeTag.None, string label = null)
        {
            var node = new MazeNode(_nodes.Count, position, tags, label ?? $"N{_nodes.Count}");
            _nodes.Add(node);
            return node;
        }

        public MazeEdge Connect(int a, int b)
        {
            if (a == b) throw new ArgumentException("Self loops are not allowed.");
            var existing = FindEdge(a, b);
            if (existing != null) return existing;

            var edge = new MazeEdge(a, b, Vector3.Distance(_nodes[a].Position, _nodes[b].Position));
            _edges.Add(edge);
            _nodes[a].Edges.Add(edge);
            _nodes[b].Edges.Add(edge);
            return edge;
        }

        public MazeEdge FindEdge(int a, int b)
        {
            foreach (var e in _nodes[a].Edges)
                if (e.Other(a) == b) return e;
            return null;
        }

        /// <summary>Linear scan; fine for the few hundred nodes a level needs.</summary>
        public int NearestNode(Vector3 position, NodeTag requiredTag = NodeTag.None)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            foreach (var n in _nodes)
            {
                if (requiredTag != NodeTag.None && !n.Has(requiredTag)) continue;
                float d = Vector3.DistanceSquared(n.Position, position);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = n.Id;
                }
            }
            return best;
        }

        public IEnumerable<MazeNode> NodesWith(NodeTag tag)
        {
            foreach (var n in _nodes)
                if (n.Has(tag)) yield return n;
        }
    }
}
