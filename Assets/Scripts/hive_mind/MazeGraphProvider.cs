using System.Collections.Generic;
using UnityEngine;
using Alesa.Core.Graph;
using Alesa.Runtime.World;

/// <summary>
/// Hands the authored corridor graph to <see cref="AIBootstrap"/> before any agent wakes up,
/// then keeps the graph's doors in step with the real doors in the scene.
///
/// Execution order matters and is the whole reason this component exists. AIBootstrap is
/// [DefaultExecutionOrder(-100)] and initialises its context in Awake; anything that wants to
/// supply a graph must therefore run earlier, hence -200. Miss that window and AIBootstrap has
/// already built an empty graph, and every agent silently does nothing.
/// </summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public class MazeGraphProvider : MonoBehaviour
{
    [Tooltip("The corridor graph to use. Without one the AI has no topology and cannot act.")]
    public MazeGraphAsset graph;

    [Tooltip("Keep MazeEdge.HasClosedDoor in step with HospitalDoor state, so shut doors muffle sound.")]
    public bool trackDoors = true;

    [Header("Debug")]
    public bool drawGizmos;
    [SerializeField] private int nodeCount;
    [SerializeField] private int edgeCount;
    [SerializeField] private int trackedDoors;

    /// <summary>The runtime graph handed to the bootstrap; null until Awake has run.</summary>
    public MazeGraph Runtime { get; private set; }

    /// <summary>Doors found in the scene, paired with the graph edge they sit on.</summary>
    private readonly List<KeyValuePair<HospitalDoor, MazeEdge>> _doors =
        new List<KeyValuePair<HospitalDoor, MazeEdge>>();

    private void Awake()
    {
        if (graph == null)
        {
            Debug.LogError("[MazeGraphProvider] No graph assigned. Every AI agent will be inert: "
                + "minions hear nothing and the hive mind never starts.", this);
            return;
        }

        var doorEdges = new List<KeyValuePair<string, MazeEdge>>();
        Runtime = graph.Build(doorEdges);
        nodeCount = Runtime.Count;
        edgeCount = Runtime.Edges.Count;

        var bootstrap = AIBootstrap.Instance != null ? AIBootstrap.Instance : FindFirstObjectByType<AIBootstrap>();
        if (bootstrap == null)
        {
            Debug.LogError("[MazeGraphProvider] No AIBootstrap in the scene to hand the graph to.", this);
            return;
        }

        // Explicit Initialize overrides the empty/fallback graph AIBootstrap would otherwise build.
        bootstrap.Initialize(Runtime);

        if (trackDoors) BindDoors(doorEdges);
    }

    /// <summary>
    /// Matches each door edge recorded at build time to the actual HospitalDoor in the scene and
    /// subscribes to it, so the graph's idea of "this way is shut" follows the real door.
    /// </summary>
    private void BindDoors(List<KeyValuePair<string, MazeEdge>> doorEdges)
    {
        foreach (var pair in doorEdges)
        {
            if (string.IsNullOrEmpty(pair.Key)) continue;
            var go = GameObject.Find(pair.Key);
            var door = go != null ? go.GetComponent<HospitalDoor>() : null;
            if (door == null) continue;

            _doors.Add(new KeyValuePair<HospitalDoor, MazeEdge>(door, pair.Value));
            // Seed the starting state, then follow every swing from here on.
            pair.Value.HasClosedDoor = !door.IsOpen;
            door.Toggled += OnDoorToggled;
        }
        trackedDoors = _doors.Count;
    }

    private void OnDoorToggled(HospitalDoor door, bool opened)
    {
        foreach (var pair in _doors)
        {
            if (pair.Key != door) continue;
            // A door counts as shut for sound the instant it starts closing, which is near enough
            // and avoids the AI's acoustics flickering mid-swing.
            pair.Value.HasClosedDoor = !opened;
        }
    }

    private void OnDestroy()
    {
        foreach (var pair in _doors)
            if (pair.Key != null) pair.Key.Toggled -= OnDoorToggled;
        _doors.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || graph == null) return;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
        foreach (var n in graph.nodes) Gizmos.DrawWireSphere(n.position, 0.35f);

        foreach (var e in graph.edges)
        {
            if (e.a < 0 || e.b < 0 || e.a >= graph.nodes.Count || e.b >= graph.nodes.Count) continue;
            Gizmos.color = e.hasDoor ? Color.red : new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawLine(graph.nodes[e.a].position, graph.nodes[e.b].position);
        }
    }
}
