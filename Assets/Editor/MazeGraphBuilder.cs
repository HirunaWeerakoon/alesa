using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Alesa.Core.Graph;

/// <summary>
/// Derives a <see cref="MazeGraphAsset"/> from the scene's baked NavMesh.
///
/// Hand-authoring a corridor graph for a whole hospital is hours of clicking and goes stale the
/// moment the level changes. The NavMesh already encodes exactly what we need — where the player
/// and the AI can physically walk — so this samples it instead:
///
///   1. Grid-sample the NavMesh bounds and snap each sample onto the walkable surface.
///   2. Thin the samples so no two nodes sit closer than minSpacing.
///   3. Connect pairs that are within maxEdgeLength AND have a clear NavMesh line between them,
///      which is what stops edges being drawn through walls.
///   4. Keep only each node's nearest few neighbours, so open rooms do not turn into a mesh
///      where sound travels in impossible straight lines.
///   5. Tag junctions and dead ends, and mark edges that a HospitalDoor sits on.
///
/// Run from Alesa/AI after baking the NavMesh.
/// </summary>
public static class MazeGraphBuilder
{
    private const string AssetPath = "Assets/Scripts/hive_mind/Data/HospitalGraph.asset";

    // Nodes are thinned to MinSpacing apart. Smaller gives finer ambush placement but more edges.
    private const float MinSpacing = 2.5f;
    private const float MaxEdgeLength = 9f;
    private const int MaxNeighbours = 6;
    private const float DoorMatchRadius = 2.5f;

    /// <summary>
    /// How much longer than the straight line a walked path may be and still count as an edge.
    /// A straight NavMesh raycast clips the frame of almost every doorway, which would leave each
    /// room an island; walking round the frame is only fractionally longer, so this reconnects
    /// genuine doorways without ever connecting two points through a solid wall.
    /// </summary>
    private const float PathDetourFactor = 1.6f;

    [MenuItem("Alesa/AI/Build Maze Graph from NavMesh")]
    public static void Build()
    {
        string report = BuildGraph(out MazeGraphAsset asset);
        EditorUtility.DisplayDialog(asset != null ? "Maze graph built" : "Could not build", report, "OK");
        if (asset != null) Selection.activeObject = asset;
    }

    /// <summary>
    /// Does the work and returns a human-readable report. Separate from the menu item so it can be
    /// driven by automation: a modal dialog would block any non-interactive caller.
    /// </summary>
    public static string BuildGraph(out MazeGraphAsset asset)
    {
        asset = null;
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (tri.vertices == null || tri.vertices.Length == 0)
            return "No baked NavMesh found in the open scene. Bake it first (Window > AI > Navigation).";

        Bounds bounds = new Bounds(tri.vertices[0], Vector3.zero);
        foreach (var v in tri.vertices) bounds.Encapsulate(v);

        List<Vector3> positions = SampleNodes(tri);
        if (positions.Count == 0) return "Sampling found no walkable positions.";

        List<(int a, int b)> edges = ConnectNodes(positions);
        asset = WriteAsset(positions, edges, bounds);

        int islands = CountIslands(positions.Count, edges);
        int doorEdges = 0;
        foreach (var e in asset.edges) if (e.hasDoor) doorEdges++;

        string report =
            $"Nodes: {positions.Count}\nEdges: {edges.Count}\nEdges with a door: {doorEdges}\n" +
            $"Disconnected regions: {islands}\n\nSaved to {AssetPath}\n\n" +
            (islands > 1
                ? "More than one region means parts of the level are unreachable from each other. "
                + "That is often correct (a locked room), but check it is intentional."
                : "The whole level is one connected region.");

        Debug.Log("[MazeGraphBuilder] " + report.Replace("\n", " "), asset);
        return report;
    }

    /// <summary>
    /// Takes candidate positions from the NavMesh triangulation itself, then thins them so no two
    /// are closer than MinSpacing.
    ///
    /// Sampling the triangulation rather than a grid over the bounds is the difference between a
    /// connected graph and a shattered one. A grid coarse enough to be cheap steps straight over
    /// corridors only a couple of metres wide, leaving gaps no edge can bridge; the triangulation
    /// is the walkable surface, so coverage automatically follows wherever the level actually goes.
    ///
    /// Triangle centroids come first and vertices second, so nodes sit in the middle of open
    /// floor where possible rather than clinging to walls.
    /// </summary>
    private static List<Vector3> SampleNodes(NavMeshTriangulation tri)
    {
        var candidates = new List<Vector3>(tri.indices.Length / 3 + tri.vertices.Length);
        for (int i = 0; i + 2 < tri.indices.Length; i += 3)
        {
            Vector3 centroid = (tri.vertices[tri.indices[i]]
                              + tri.vertices[tri.indices[i + 1]]
                              + tri.vertices[tri.indices[i + 2]]) / 3f;
            candidates.Add(centroid);
        }
        candidates.AddRange(tri.vertices);

        // Greedy thinning. A spatial hash keyed on MinSpacing-sized cells keeps this near-linear;
        // comparing every candidate against every accepted node would be O(n^2) on 3000 candidates.
        var grid = new Dictionary<(int, int), List<Vector3>>();
        var accepted = new List<Vector3>();
        float minSq = MinSpacing * MinSpacing;

        foreach (var c in candidates)
        {
            int cx = Mathf.FloorToInt(c.x / MinSpacing);
            int cz = Mathf.FloorToInt(c.z / MinSpacing);

            bool tooClose = false;
            // Only the 3x3 neighbourhood can hold anything within MinSpacing.
            for (int dx = -1; dx <= 1 && !tooClose; dx++)
            for (int dz = -1; dz <= 1 && !tooClose; dz++)
            {
                if (!grid.TryGetValue((cx + dx, cz + dz), out var bucket)) continue;
                foreach (var p in bucket)
                {
                    if ((p - c).sqrMagnitude >= minSq) continue;
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            accepted.Add(c);
            if (!grid.TryGetValue((cx, cz), out var own)) grid[(cx, cz)] = own = new List<Vector3>();
            own.Add(c);
        }
        return accepted;
    }

    /// <summary>
    /// Connects each node to its nearest neighbours that are actually walkable in a straight line.
    /// NavMesh.Raycast returns true when it crosses a NavMesh boundary, i.e. a wall, so an
    /// unobstructed pair is one where it returns false.
    /// </summary>
    private static List<(int a, int b)> ConnectNodes(List<Vector3> positions)
    {
        var edges = new List<(int a, int b)>();
        var seen = new HashSet<long>();
        float maxSq = MaxEdgeLength * MaxEdgeLength;
        var candidates = new List<(int index, float distSq)>();

        for (int i = 0; i < positions.Count; i++)
        {
            candidates.Clear();
            for (int j = 0; j < positions.Count; j++)
            {
                if (i == j) continue;
                float d = (positions[i] - positions[j]).sqrMagnitude;
                if (d <= maxSq) candidates.Add((j, d));
            }
            candidates.Sort((x, y) => x.distSq.CompareTo(y.distSq));

            int added = 0;
            foreach (var c in candidates)
            {
                if (added >= MaxNeighbours) break;
                if (!AreAdjacent(positions[i], positions[c.index])) continue;

                // Undirected: key on the ordered pair so each edge is recorded once.
                int lo = Mathf.Min(i, c.index), hi = Mathf.Max(i, c.index);
                long key = ((long)lo << 32) | (uint)hi;
                if (seen.Add(key)) edges.Add((lo, hi));
                added++;
            }
        }
        return edges;
    }

    /// <summary>
    /// True when two positions are close enough to be one corridor step apart.
    ///
    /// The cheap test is a straight NavMesh raycast. When that is blocked the pair may still be a
    /// doorway, where the line clips the frame but you can walk through perfectly well, so we fall
    /// back to asking for an actual path and accepting it only if it is barely longer than the
    /// straight line. A pair separated by a wall needs a long way round and is rejected, which is
    /// exactly the distinction we want.
    /// </summary>
    private static bool AreAdjacent(Vector3 a, Vector3 b)
    {
        if (!NavMesh.Raycast(a, b, out _, NavMesh.AllAreas)) return true;

        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path)) return false;
        if (path.status != NavMeshPathStatus.PathComplete) return false;

        float walked = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            walked += Vector3.Distance(path.corners[i - 1], path.corners[i]);

        return walked <= Vector3.Distance(a, b) * PathDetourFactor;
    }

    private static MazeGraphAsset WriteAsset(List<Vector3> positions, List<(int a, int b)> edges, Bounds bounds)
    {
        var asset = AssetDatabase.LoadAssetAtPath<MazeGraphAsset>(AssetPath);
        bool isNew = asset == null;
        if (isNew) asset = ScriptableObject.CreateInstance<MazeGraphAsset>();

        // Degree drives the tags: a node with one way out is a dead end, three or more is a junction.
        var degree = new int[positions.Count];
        foreach (var e in edges) { degree[e.a]++; degree[e.b]++; }

        asset.nodes = new List<MazeGraphAsset.NodeRecord>(positions.Count);
        for (int i = 0; i < positions.Count; i++)
        {
            NodeTag tags = NodeTag.None;
            if (degree[i] >= 3) tags |= NodeTag.Junction;
            else if (degree[i] <= 1) tags |= NodeTag.DeadEnd;

            asset.nodes.Add(new MazeGraphAsset.NodeRecord
            {
                position = positions[i],
                label = "N" + i,
                tags = tags
            });
        }

        var doors = Object.FindObjectsByType<HospitalDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        asset.edges = new List<MazeGraphAsset.EdgeRecord>(edges.Count);
        foreach (var e in edges)
        {
            var record = new MazeGraphAsset.EdgeRecord { a = e.a, b = e.b };
            HospitalDoor door = DoorOnSegment(doors, positions[e.a], positions[e.b]);
            if (door != null)
            {
                record.hasDoor = true;
                record.doorPath = PathOf(door.transform);
            }
            asset.edges.Add(record);
        }

        asset.provenance = $"Built from NavMesh {System.DateTime.Now:yyyy-MM-dd HH:mm}, "
            + $"bounds {bounds.size.x:0}x{bounds.size.z:0} m, node spacing {MinSpacing} m";

        if (isNew)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AssetPath));
            AssetDatabase.CreateAsset(asset, AssetPath);
        }
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return asset;
    }

    /// <summary>The door whose pivot lies closest to this edge's midpoint, within DoorMatchRadius.</summary>
    private static HospitalDoor DoorOnSegment(HospitalDoor[] doors, Vector3 a, Vector3 b)
    {
        Vector3 mid = (a + b) * 0.5f;
        HospitalDoor best = null;
        float bestSq = DoorMatchRadius * DoorMatchRadius;
        foreach (var d in doors)
        {
            Vector3 p = d.hinge != null ? d.hinge.position : d.transform.position;
            // Compare horizontally: the hinge pivot and the walkable surface sit at different heights.
            float dx = p.x - mid.x, dz = p.z - mid.z;
            float sq = dx * dx + dz * dz;
            if (sq >= bestSq) continue;
            bestSq = sq;
            best = d;
        }
        return best;
    }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    /// <summary>Flood-fills to count how many separate regions the graph has.</summary>
    private static int CountIslands(int nodeCount, List<(int a, int b)> edges)
    {
        var adjacency = new List<int>[nodeCount];
        for (int i = 0; i < nodeCount; i++) adjacency[i] = new List<int>();
        foreach (var e in edges) { adjacency[e.a].Add(e.b); adjacency[e.b].Add(e.a); }

        var visited = new bool[nodeCount];
        int islands = 0;
        var stack = new Stack<int>();
        for (int i = 0; i < nodeCount; i++)
        {
            if (visited[i]) continue;
            islands++;
            stack.Push(i);
            visited[i] = true;
            while (stack.Count > 0)
            {
                int n = stack.Pop();
                foreach (int m in adjacency[n])
                {
                    if (visited[m]) continue;
                    visited[m] = true;
                    stack.Push(m);
                }
            }
        }
        return islands;
    }
}
