using System;
using System.Collections.Generic;
using UnityEngine;
using Alesa.Core.Graph;
using Alesa.Runtime; // ToCore()

/// <summary>
/// A hospital corridor graph, saved as an asset so it is authored once and version-controlled
/// rather than rebuilt at startup.
///
/// The AI reasons about the level as a graph, not as geometry: minions propagate sound along
/// edges, Alesa plans routes through nodes, and the hive mind director scores nodes as ambush
/// points. Without this the AI has no topology at all and does nothing.
///
/// Positions are stored in Unity world space and converted to the core AI's System.Numerics
/// coordinates when the runtime graph is built.
/// </summary>
[CreateAssetMenu(fileName = "HospitalGraph", menuName = "Alesa/Maze Graph", order = 100)]
public class MazeGraphAsset : ScriptableObject
{
    [Serializable]
    public struct NodeRecord
    {
        public Vector3 position;
        public string label;
        public NodeTag tags;
    }

    [Serializable]
    public struct EdgeRecord
    {
        public int a;
        public int b;

        /// <summary>
        /// True when a door sits on this edge. The provider keeps
        /// <see cref="MazeEdge.HasClosedDoor"/> in step with the real door, so shutting a door
        /// genuinely muffles the player for every listening minion.
        /// </summary>
        public bool hasDoor;

        /// <summary>Scene path of the HospitalDoor on this edge, so the provider can find it again.</summary>
        public string doorPath;
    }

    public List<NodeRecord> nodes = new List<NodeRecord>();
    public List<EdgeRecord> edges = new List<EdgeRecord>();

    [Tooltip("How the graph was produced, for the record.")]
    public string provenance = "";

    public int NodeCount => nodes != null ? nodes.Count : 0;
    public int EdgeCount => edges != null ? edges.Count : 0;

    /// <summary>
    /// Builds the runtime graph. <paramref name="doorEdges"/> comes back filled with the edges
    /// that carry a door, paired with that door's scene path, so the provider can wire them up.
    /// </summary>
    public MazeGraph Build(List<KeyValuePair<string, MazeEdge>> doorEdges = null)
    {
        var graph = new MazeGraph();
        if (nodes == null) return graph;

        foreach (var n in nodes)
            graph.AddNode(n.position.ToCore(), n.tags, string.IsNullOrEmpty(n.label) ? null : n.label);

        if (edges == null) return graph;
        foreach (var e in edges)
        {
            // Skip malformed records rather than throwing: a half-edited asset should still
            // produce a usable graph instead of breaking every agent in the scene.
            if (e.a < 0 || e.b < 0 || e.a >= graph.Count || e.b >= graph.Count || e.a == e.b) continue;

            MazeEdge edge = graph.Connect(e.a, e.b);
            if (e.hasDoor && doorEdges != null)
                doorEdges.Add(new KeyValuePair<string, MazeEdge>(e.doorPath, edge));
        }

        return graph;
    }

    /// <summary>World position of a node, for gizmos and spawning.</summary>
    public Vector3 PositionOf(int node) =>
        nodes != null && (uint)node < (uint)nodes.Count ? nodes[node].position : Vector3.zero;
}
