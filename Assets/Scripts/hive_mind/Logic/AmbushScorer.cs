using System;
using System.Collections.Generic;
using Alesa.Core.Graph;

namespace Alesa.HiveMind
{
    /// <summary>One place the hive could stage an encounter, and why it scored as it did.</summary>
    public readonly struct AmbushCandidate
    {
        public readonly int Node;
        public readonly float Score;

        /// <summary>Walking distance from the player, in metres.</summary>
        public readonly float Distance;

        /// <summary>True when the node lies ahead of the player on Alesa's recommended route.</summary>
        public readonly bool OnRoute;

        public bool IsValid => Node >= 0;
        public static AmbushCandidate None => new AmbushCandidate(-1, float.NegativeInfinity, 0f, false);

        public AmbushCandidate(int node, float score, float distance, bool onRoute)
        {
            Node = node;
            Score = score;
            Distance = distance;
            OnRoute = onRoute;
        }
    }

    /// <summary>
    /// Chooses where the hive should be waiting.
    ///
    /// The difference between a horror game and a shooting gallery is that the horror game is
    /// already there when you arrive. So rather than spawning minions near the player, this scores
    /// every node the player can still reach and picks the one that will produce the best moment:
    ///
    ///     score = w1*onRoute + w2*heat - w3*|distance - ideal| - w4*recentlyUsed
    ///
    ///   • onRoute      - the node is ahead of the player on the route Alesa is recommending.
    ///                    This is what makes the hive feel like it is thinking: it gets there first.
    ///   • heat         - the hive has reason to believe the player is heading this way.
    ///   • distance     - punished for deviating from an ideal range. Too near and the player sees
    ///                    the staging; too far and they may never arrive before the mood passes.
    ///   • recentlyUsed - the same corner twice running stops being frightening and starts being
    ///                    a pattern the player can read.
    ///
    /// Pure C#; no engine types, fully unit-testable.
    /// </summary>
    public sealed class AmbushScorer
    {
        /// <summary>Bonus for lying ahead of the player on the recommended route.</summary>
        public float RouteWeight { get; set; } = 1.0f;

        /// <summary>Bonus for the hive already believing the player is near this node.</summary>
        public float HeatWeight { get; set; } = 0.6f;

        /// <summary>Penalty per metre of deviation from <see cref="IdealDistance"/>.</summary>
        public float DistanceWeight { get; set; } = 0.04f;

        /// <summary>Penalty for a node used within <see cref="RecentUseMemorySeconds"/>.</summary>
        public float RecentUseWeight { get; set; } = 0.8f;

        /// <summary>The sweet spot: far enough to stage unseen, near enough to still matter.</summary>
        public float IdealDistance { get; set; } = 20f;

        /// <summary>Closer than this and the player would watch the ambush assemble.</summary>
        public float MinDistance { get; set; } = 8f;

        /// <summary>Beyond this the player will probably never walk into it.</summary>
        public float MaxDistance { get; set; } = 60f;

        /// <summary>How long a used node stays penalised.</summary>
        public float RecentUseMemorySeconds { get; set; } = 60f;

        /// <summary>
        /// Optional veto from the Unity side: a node currently in the player's line of sight, or
        /// with no floor to stand on, is not a staging point however well it scores.
        /// </summary>
        public Func<int, bool> IsUnusable { get; set; }

        /// <summary>When each node was last used to stage something.</summary>
        private readonly Dictionary<int, float> _lastUsed = new Dictionary<int, float>();

        /// <summary>Records that an encounter was staged at a node, so it will not be reused at once.</summary>
        public void NoteUsed(int node, float now) => _lastUsed[node] = now;

        /// <summary>0 when the node is fresh, 1 when it was used this instant.</summary>
        public float RecentUse(int node, float now)
        {
            if (RecentUseMemorySeconds <= 0f) return 0f;
            if (!_lastUsed.TryGetValue(node, out float used)) return 0f;

            float age = now - used;
            if (age >= RecentUseMemorySeconds || age < 0f) return 0f;
            // Linear fade so a spot becomes gradually acceptable again rather than snapping back.
            return 1f - age / RecentUseMemorySeconds;
        }

        /// <summary>
        /// Scores every reachable node and returns the best place to stage an encounter.
        /// </summary>
        /// <param name="graph">The level topology.</param>
        /// <param name="heat">The hive's belief about the player, or null to ignore heat.</param>
        /// <param name="route">Alesa's recommended route, player first; may be null or empty.</param>
        /// <param name="playerNode">Node the player is nearest to.</param>
        /// <param name="now">Current time, for the recent-use penalty.</param>
        public AmbushCandidate Best(MazeGraph graph, HiveHeatMap heat, IReadOnlyList<int> route,
            int playerNode, float now)
        {
            var all = Score(graph, heat, route, playerNode, now);
            var best = AmbushCandidate.None;
            foreach (var c in all)
                if (c.Score > best.Score) best = c;
            return best;
        }

        /// <summary>Every viable candidate with its score. Exposed so the debug view can draw them.</summary>
        public List<AmbushCandidate> Score(MazeGraph graph, HiveHeatMap heat, IReadOnlyList<int> route,
            int playerNode, float now)
        {
            var results = new List<AmbushCandidate>();
            if (graph == null || graph.Count == 0) return results;
            if ((uint)playerNode >= (uint)graph.Count) return results;

            // One Dijkstra flood gives the walking distance to every node at once, which is far
            // cheaper than pathfinding to each candidate separately.
            var field = GraphSearch.Dijkstra(graph, playerNode, MazeGraph.WalkingCost, MaxDistance);

            // Where the player currently is along the route, so we can tell ahead from behind.
            // Everything before this index is ground they have already covered.
            int playerIndexOnRoute = IndexOf(route, playerNode);

            for (int node = 0; node < graph.Count; node++)
            {
                if (node == playerNode) continue;
                if (!field.Reached(node)) continue;              // unreachable, or past MaxDistance

                float distance = field.Cost[node];
                if (distance < MinDistance) continue;            // they would watch it assemble

                if (IsUnusable != null && IsUnusable(node)) continue;

                // Ahead only counts when we know where the player is on the route AND the node
                // comes later. A node on the route behind them is worthless: they just left it.
                int nodeIndexOnRoute = IndexOf(route, node);
                bool onRoute = nodeIndexOnRoute >= 0
                               && playerIndexOnRoute >= 0
                               && nodeIndexOnRoute > playerIndexOnRoute;

                float score = 0f;
                if (onRoute) score += RouteWeight;
                if (heat != null) score += HeatWeight * heat.HeatAt(node);
                score -= DistanceWeight * Math.Abs(distance - IdealDistance);
                score -= RecentUseWeight * RecentUse(node, now);

                results.Add(new AmbushCandidate(node, score, distance, onRoute));
            }

            return results;
        }

        /// <summary>Position of a node in the route, or -1. Routes are short, so a scan is fine.</summary>
        private static int IndexOf(IReadOnlyList<int> route, int node)
        {
            if (route == null) return -1;
            for (int i = 0; i < route.Count; i++)
                if (route[i] == node) return i;
            return -1;
        }

        /// <summary>Forgets every staging site. Used on scene reload.</summary>
        public void Clear() => _lastUsed.Clear();
    }
}
